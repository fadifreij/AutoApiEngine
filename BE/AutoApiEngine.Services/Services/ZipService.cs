using AutoApiEngine.ServiceAbstraction;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace AutoApiEngine.Services.Services;

public class ZipService : IZipService
{
    public Task<string> ZipWithPasswordAsync(
        string sourceFilePath,
        string password,
        string? outputZipPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            throw new ArgumentNullException(nameof(sourceFilePath));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentNullException(nameof(password));

        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source file not found.", sourceFilePath);

        outputZipPath ??= Path.ChangeExtension(sourceFilePath, ".zip");

        cancellationToken.ThrowIfCancellationRequested();

        using var zipStream = new FileStream(outputZipPath, FileMode.Create);
        using var zipOutput = new ZipOutputStream(zipStream);
        
        zipOutput.SetLevel(9); // Maximum compression
        zipOutput.Password = password;
        // Use AES-256 encryption (requires 7-Zip, WinRAR, or compatible tool to extract)
        zipOutput.UseZip64 = UseZip64.Dynamic;

        var fileInfo = new FileInfo(sourceFilePath);
        var entry = new ZipEntry(fileInfo.Name)
        {
            DateTime = fileInfo.LastWriteTime,
            Size = fileInfo.Length
            // Note: Not using AESKeySize for Windows Explorer compatibility
            // Windows built-in zip only supports legacy ZipCrypto encryption
            // For AES-256, users would need 7-Zip or WinRAR to extract
        };
        
        zipOutput.PutNextEntry(entry);
        
        using (var inputStream = File.OpenRead(sourceFilePath))
        {
            StreamUtils.Copy(inputStream, zipOutput, new byte[4096]);
        }
        
        zipOutput.CloseEntry();
        zipOutput.Finish();

        return Task.FromResult(outputZipPath);
    }

    public Task<string> UnzipWithPasswordAsync(
        string zipFilePath,
        string password,
        string? extractPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipFilePath))
            throw new ArgumentNullException(nameof(zipFilePath));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentNullException(nameof(password));

        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("Zip file not found.", zipFilePath);

        extractPath ??= Path.Combine(
            Path.GetDirectoryName(zipFilePath) ?? ".",
            Path.GetFileNameWithoutExtension(zipFilePath));

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(extractPath);

        using var zipStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        using var zipFile = new ZipFile(zipStream);
        
        zipFile.Password = password;
        
        foreach (ZipEntry entry in zipFile)
        {
            if (!entry.IsFile) continue;
            
            cancellationToken.ThrowIfCancellationRequested();
            
            // Prevent directory traversal attacks
            var entryFileName = SanitizeEntryName(entry.Name);
            var fullPath = Path.Combine(extractPath, entryFileName);
            var directoryName = Path.GetDirectoryName(fullPath);
            
            if (!string.IsNullOrEmpty(directoryName))
                Directory.CreateDirectory(directoryName);
            
            using var inputStream = zipFile.GetInputStream(entry);
            using var outputStream = File.Create(fullPath);
            StreamUtils.Copy(inputStream, outputStream, new byte[4096]);
        }

        return Task.FromResult(GetFirstExtractedFile(extractPath));
    }

    public Task<string> ZipAsync(
        string sourceFilePath,
        string? outputZipPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            throw new ArgumentNullException(nameof(sourceFilePath));

        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source file not found.", sourceFilePath);

        outputZipPath ??= Path.ChangeExtension(sourceFilePath, ".zip");

        cancellationToken.ThrowIfCancellationRequested();

        using var zipStream = new FileStream(outputZipPath, FileMode.Create);
        using var zipOutput = new ZipOutputStream(zipStream);
        
        zipOutput.SetLevel(9); // Maximum compression

        var fileInfo = new FileInfo(sourceFilePath);
        var entry = new ZipEntry(fileInfo.Name)
        {
            DateTime = fileInfo.LastWriteTime,
            Size = fileInfo.Length
        };
        
        zipOutput.PutNextEntry(entry);
        
        using (var inputStream = File.OpenRead(sourceFilePath))
        {
            StreamUtils.Copy(inputStream, zipOutput, new byte[4096]);
        }
        
        zipOutput.CloseEntry();

        return Task.FromResult(outputZipPath);
    }

    public Task<string> UnzipAsync(
        string zipFilePath,
        string? extractPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipFilePath))
            throw new ArgumentNullException(nameof(zipFilePath));

        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("Zip file not found.", zipFilePath);

        extractPath ??= Path.Combine(
            Path.GetDirectoryName(zipFilePath) ?? ".",
            Path.GetFileNameWithoutExtension(zipFilePath));

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(extractPath);

        using var zipStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        using var zipFile = new ZipFile(zipStream);
        
        foreach (ZipEntry entry in zipFile)
        {
            if (!entry.IsFile) continue;
            
            cancellationToken.ThrowIfCancellationRequested();
            
            // Prevent directory traversal attacks
            var entryFileName = SanitizeEntryName(entry.Name);
            var fullPath = Path.Combine(extractPath, entryFileName);
            var directoryName = Path.GetDirectoryName(fullPath);
            
            if (!string.IsNullOrEmpty(directoryName))
                Directory.CreateDirectory(directoryName);
            
            using var inputStream = zipFile.GetInputStream(entry);
            using var outputStream = File.Create(fullPath);
            StreamUtils.Copy(inputStream, outputStream, new byte[4096]);
        }

        return Task.FromResult(GetFirstExtractedFile(extractPath));
    }

    public bool IsPasswordProtected(string zipFilePath)
    {
        if (string.IsNullOrWhiteSpace(zipFilePath))
            throw new ArgumentNullException(nameof(zipFilePath));

        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("Zip file not found.", zipFilePath);

        using var zipStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        using var zipFile = new ZipFile(zipStream);
        
        foreach (ZipEntry entry in zipFile)
        {
            if (entry.IsCrypted)
                return true;
        }
        
        return false;
    }

    public async Task<string> ExtractAsync(
        string zipFilePath,
        string? password = null,
        string? extractPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipFilePath))
            throw new ArgumentNullException(nameof(zipFilePath));

        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("Zip file not found.", zipFilePath);

        extractPath ??= Path.Combine(
            Path.GetDirectoryName(zipFilePath) ?? ".",
            Path.GetFileNameWithoutExtension(zipFilePath));

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(extractPath);

        bool isProtected = IsPasswordProtected(zipFilePath);

        if (isProtected)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("The zip file is password-protected but no password was provided.");

            return await UnzipWithPasswordAsync(zipFilePath, password, extractPath, cancellationToken);
        }

        return await UnzipAsync(zipFilePath, extractPath, cancellationToken);
    }

    /// <summary>
    /// Sanitizes entry names to prevent directory traversal attacks.
    /// </summary>
    private static string SanitizeEntryName(string entryName)
    {
        // Remove leading slashes and ../ sequences
        var sanitized = entryName.Replace('\\', '/');
        while (sanitized.StartsWith("/") || sanitized.StartsWith("../"))
        {
            if (sanitized.StartsWith("/"))
                sanitized = sanitized[1..];
            if (sanitized.StartsWith("../"))
                sanitized = sanitized[3..];
        }
        
        // Remove any remaining ../ in the path
        sanitized = sanitized.Replace("../", "");
        
        return sanitized;
    }

    private static string GetFirstExtractedFile(string extractPath)
    {
        var backupExtensions = new[] { ".bak", ".sql", ".dump", ".gz" };
        
        // First, look for backup files specifically
        var backupFile = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories)
            .FirstOrDefault(f => backupExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        if (backupFile != null)
            return backupFile;

        // Fall back to any file
        var files = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
        return files.FirstOrDefault() ?? extractPath;
    }
}
