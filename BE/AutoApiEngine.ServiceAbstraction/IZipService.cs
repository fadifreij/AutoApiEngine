namespace AutoApiEngine.ServiceAbstraction;

public interface IZipService
{
    /// <summary>
    /// Creates a password-protected zip archive from a source file using AES-256 encryption.
    /// </summary>
    /// <param name="sourceFilePath">Path to the file to compress.</param>
    /// <param name="password">Password for encryption.</param>
    /// <param name="outputZipPath">Optional output path. Defaults to source path with .zip extension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the created zip file.</returns>
    Task<string> ZipWithPasswordAsync(
        string sourceFilePath,
        string password,
        string? outputZipPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a password-protected zip archive.
    /// </summary>
    /// <param name="zipFilePath">Path to the zip file.</param>
    /// <param name="password">Password for decryption.</param>
    /// <param name="extractPath">Optional extraction directory. Defaults to zip file directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the first extracted file (typically the backup file).</returns>
    Task<string> UnzipWithPasswordAsync(
        string zipFilePath,
        string password,
        string? extractPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a zip archive without password protection.
    /// </summary>
    /// <param name="sourceFilePath">Path to the file to compress.</param>
    /// <param name="outputZipPath">Optional output path. Defaults to source path with .zip extension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the created zip file.</returns>
    Task<string> ZipAsync(
        string sourceFilePath,
        string? outputZipPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a zip archive without password.
    /// </summary>
    /// <param name="zipFilePath">Path to the zip file.</param>
    /// <param name="extractPath">Optional extraction directory. Defaults to zip file directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the first extracted file.</returns>
    Task<string> UnzipAsync(
        string zipFilePath,
        string? extractPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a zip file is password-protected.
    /// </summary>
    /// <param name="zipFilePath">Path to the zip file.</param>
    /// <returns>True if the zip requires a password.</returns>
    bool IsPasswordProtected(string zipFilePath);

    /// <summary>
    /// Attempts to extract a zip file, trying with password first if provided, then without.
    /// </summary>
    /// <param name="zipFilePath">Path to the zip file.</param>
    /// <param name="password">Optional password to try first.</param>
    /// <param name="extractPath">Optional extraction directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the first extracted file.</returns>
    Task<string> ExtractAsync(
        string zipFilePath,
        string? password = null,
        string? extractPath = null,
        CancellationToken cancellationToken = default);
}
