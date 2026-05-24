using AutoApiEngine.ServiceAbstraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    public class MySqlDatabaseManagementService : IDatabaseManagementService
    {
        private readonly ILogger<MySqlDatabaseManagementService> _logger;
        private readonly string _mySqlConnection;
        private readonly IConfiguration _configuration;

        public MySqlDatabaseManagementService(ILogger<MySqlDatabaseManagementService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _mySqlConnection = configuration.GetConnectionString("MySqlConnection")
                ?? "Server=localhost;Port=3307;Uid=root;Pwd=root;";
        }

        public async Task<string> CreateDatabaseAsync(string databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default)
        {
            await using var connection = new MySqlConnection(_mySqlConnection);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
                connection);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            return databaseName;
        }

        public async Task<DatabaseStatsResult> GetDatabaseStatsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default)
        {
            var result = new DatabaseStatsResult();
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            await using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @db AND table_type = 'BASE TABLE'",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                result.TablesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @db AND routine_type = 'FUNCTION'",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                result.FunctionsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @db AND routine_type = 'PROCEDURE'",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                result.StoredProceduresCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await using (var cmd = new MySqlCommand(
                "SELECT IFNULL(SUM(data_length + index_length), 0) FROM information_schema.tables WHERE table_schema = @db",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                var val = await cmd.ExecuteScalarAsync(cancellationToken);
                result.DatabaseSizeBytes = val is DBNull or null ? 0L : Convert.ToInt64(val);
            }

            return result;
        }

        public async Task BackupAsync(
            DatabaseEngine engine,
            string backupPath,
            string databaseName,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var (user, password, server, port) = ParseConnectionString(_mySqlConnection);
            string containerName = _configuration["MySql:ContainerName"] ?? "mysql";

            progress?.Report(new DatabaseProgress { Percentage = 5, Message = "Initializing backup" });

            // Determine strategy: host mysqldump or docker exec
            bool useDocker = ShouldUseDocker();

            var tempPath = backupPath + ".part";

            try
            {
                if (!useDocker)
                {
                    try
                    {
                        progress?.Report(new DatabaseProgress { Percentage = 10, Message = "Running mysqldump" });
                        await RunHostMysqldump(server, port, user, password, databaseName, tempPath, cancellationToken);
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex.InnerException is System.ComponentModel.Win32Exception)
                    {
                        _logger.LogInformation("Host mysqldump not available, falling back to docker: {Message}", ex.Message);
                        useDocker = true;
                    }
                }

                if (useDocker)
                {
                    progress?.Report(new DatabaseProgress { Percentage = 10, Message = "Running mysqldump via Docker" });
                    await RunDockerMysqldump(containerName, user, password, databaseName, tempPath, cancellationToken);
                }

                progress?.Report(new DatabaseProgress { Percentage = 80, Message = "Writing backup file" });

                // Move temp file to final path
                File.Move(tempPath, backupPath, overwrite: true);

                // Validate
                var fileInfo = new FileInfo(backupPath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    throw new InvalidOperationException("mysqldump produced an empty file.");
                }

                _logger.LogInformation("Backup completed: {Path} ({Size} bytes)", backupPath, fileInfo.Length);
                progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Backup completed" });
            }
            catch
            {
                // Clean up partial files on failure
                TryDelete(tempPath);
                TryDelete(backupPath);
                throw;
            }
        }

        private async Task RunHostMysqldump(string server, string port, string user, string password, string databaseName, string outputPath, CancellationToken ct)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mysqldump",
                Arguments = $"-h {server} -P {port} -u {user} --single-transaction --routines --triggers {databaseName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["MYSQL_PWD"] = password;

            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start mysqldump process.");

            // Read stderr asynchronously to prevent deadlock
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await using (var fs = File.Create(outputPath))
            {
                await process.StandardOutput.BaseStream.CopyToAsync(fs, ct);
            }

            var stderr = await stderrTask;
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"mysqldump exited with code {process.ExitCode}: {stderr}");
            }
        }

        private async Task RunDockerMysqldump(string containerName, string user, string password, string databaseName, string outputPath, CancellationToken ct)
        {
            // Write credentials file (no BOM) and copy into container
            var credsContent = $"[client]\nuser={user}\npassword={password}\nhost=127.0.0.1\nport=3306\n";
            var hostTemp = Path.GetTempFileName();

            try
            {
                await File.WriteAllTextAsync(hostTemp, credsContent, new UTF8Encoding(false), ct);

                await RunProcessAsync("docker", $"cp \"{hostTemp}\" {containerName}:/tmp/backup_creds", ct);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"exec {containerName} mysqldump --defaults-extra-file=/tmp/backup_creds --single-transaction --routines --triggers {databaseName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start docker exec process.");

                var stderrTask = process.StandardError.ReadToEndAsync(ct);

                await using (var fs = File.Create(outputPath))
                {
                    await process.StandardOutput.BaseStream.CopyToAsync(fs, ct);
                }

                var stderr = await stderrTask;
                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"docker mysqldump exited with code {process.ExitCode}: {stderr}");
                }

                // Clean up credentials from container
                _ = Task.Run(() => RunProcessAsync("docker", $"exec {containerName} rm -f /tmp/backup_creds", CancellationToken.None));
            }
            finally
            {
                TryDelete(hostTemp);
            }
        }

        public async Task RestoreAsync(
            DatabaseEngine engine,
            string backupPath,
            DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var (user, password, server, port) = ParseConnectionString(databaseOptions.ConnectionString);
            string containerName = _configuration["MySql:ContainerName"] ?? "mysql";
            string dbName = databaseOptions.DatabaseName;

            var fileInfo = new FileInfo(backupPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
                throw new FileNotFoundException("Backup file not found or empty.", backupPath);

            long totalBytes = fileInfo.Length;

            progress?.Report(new DatabaseProgress { Percentage = 5, Message = "Initializing restore" });

            bool useDocker = ShouldUseDocker();

            if (!useDocker)
            {
                try
                {
                    progress?.Report(new DatabaseProgress { Percentage = 10, Message = "Restoring database" });
                    await RunHostMysqlRestore(server, port, user, password, dbName, backupPath, totalBytes, progress, cancellationToken);
                    progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Restore completed" });
                    return;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex.InnerException is System.ComponentModel.Win32Exception)
                {
                    _logger.LogInformation("Host mysql client not available, falling back to docker: {Message}", ex.Message);
                    useDocker = true;
                }
            }

            if (useDocker)
            {
                progress?.Report(new DatabaseProgress { Percentage = 10, Message = "Restoring database via Docker" });
                await RunDockerMysqlRestore(containerName, user, password, dbName, backupPath, totalBytes, progress, cancellationToken);
            }

            progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Restore completed" });
            _logger.LogInformation("Restore completed for database {Database}", dbName);
        }

        private async Task RunHostMysqlRestore(string server, string port, string user, string password, string dbName, string backupPath, long totalBytes, IProgress<DatabaseProgress>? progress, CancellationToken ct)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mysql",
                Arguments = $"-h {server} -P {port} -u {user} {dbName}",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["MYSQL_PWD"] = password;

            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start mysql process.");

            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await using (var fs = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[65536];
                long bytesRead = 0;
                int lastReportedPercent = 10;
                int read;

                while ((read = await fs.ReadAsync(buffer, ct)) > 0)
                {
                    await process.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;

                    if (totalBytes > 0)
                    {
                        int percent = (int)(10 + (80.0 * bytesRead / totalBytes));
                        if (percent > lastReportedPercent + 4) // report every ~5%
                        {
                            lastReportedPercent = percent;
                            progress?.Report(new DatabaseProgress { Percentage = percent, Message = $"Restoring... {percent}%" });
                        }
                    }
                }
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"mysql restore exited with code {process.ExitCode}: {stderr}");
            }
        }

        private async Task RunDockerMysqlRestore(string containerName, string user, string password, string dbName, string backupPath, long totalBytes, IProgress<DatabaseProgress>? progress, CancellationToken ct)
        {
            var credsContent = $"[client]\nuser={user}\npassword={password}\nhost=127.0.0.1\nport=3306\n";
            var hostTemp = Path.GetTempFileName();

            try
            {
                await File.WriteAllTextAsync(hostTemp, credsContent, new UTF8Encoding(false), ct);
                await RunProcessAsync("docker", $"cp \"{hostTemp}\" {containerName}:/tmp/restore_creds", ct);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"exec -i {containerName} mysql --defaults-extra-file=/tmp/restore_creds {dbName}",
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start docker exec process.");

                var stderrTask = process.StandardError.ReadToEndAsync(ct);

                await using (var fs = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[65536];
                    long bytesRead = 0;
                    int lastReportedPercent = 10;
                    int read;

                    while ((read = await fs.ReadAsync(buffer, ct)) > 0)
                    {
                        await process.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(0, read), ct);
                        bytesRead += read;

                        if (totalBytes > 0)
                        {
                            int percent = (int)(10 + (80.0 * bytesRead / totalBytes));
                            if (percent > lastReportedPercent + 4)
                            {
                                lastReportedPercent = percent;
                                progress?.Report(new DatabaseProgress { Percentage = percent, Message = $"Restoring... {percent}%" });
                            }
                        }
                    }
                }

                process.StandardInput.Close();
                await process.WaitForExitAsync(ct);
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"docker mysql restore exited with code {process.ExitCode}: {stderr}");
                }

                _ = Task.Run(() => RunProcessAsync("docker", $"exec {containerName} rm -f /tmp/restore_creds", CancellationToken.None));
            }
            finally
            {
                TryDelete(hostTemp);
            }
        }

        #region Helpers

        private (string user, string password, string server, string port) ParseConnectionString(string connectionString)
        {
            string user = "root", password = "root", server = "localhost", port = "3306";
            foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim().ToLowerInvariant();
                var value = kv[1].Trim();
                switch (key)
                {
                    case "uid" or "user id": user = value; break;
                    case "pwd" or "password": password = value; break;
                    case "server" or "data source": server = value; break;
                    case "port": port = value; break;
                }
            }
            return (user, password, server, port);
        }

        private bool ShouldUseDocker()
        {
            var cfg = _configuration["MySql:UseDocker"];
            if (!string.IsNullOrWhiteSpace(cfg) && bool.TryParse(cfg, out var val))
                return val;
            return false;
        }

        private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken ct)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{fileName} {arguments} failed (exit {process.ExitCode}): {stderr}");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        #endregion
    }
}