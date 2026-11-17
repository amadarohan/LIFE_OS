using System.Security.Cryptography;
using LifeOS.Application.Abstractions;

namespace LifeOS.Infrastructure.Services;

/// <summary>
/// Stores attachments under a configured root. Every path is validated to stay inside that root,
/// so a crafted file name cannot escape the storage directory.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Storage root path is required.", nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAttachmentAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var safeOriginal = SanitizeFileName(originalFileName);
        var extension = Path.GetExtension(safeOriginal);
        var storedName = $"{Guid.CreateVersion7():N}{extension}";

        // Date-based folders keep directory sizes manageable without a database lookup.
        var relativeFolder = Path.Combine(
            DateTime.UtcNow.ToString("yyyy"),
            DateTime.UtcNow.ToString("MM"));

        var relativePath = Path.Combine(relativeFolder, storedName);
        var absolutePath = GetAttachmentAbsolutePath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using (var target = new FileStream(
            absolutePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        return relativePath.Replace('\\', '/');
    }

    public Task<Stream> OpenAttachmentAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAttachmentAbsolutePath(relativePath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Attachment not found.", relativePath);
        }

        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return Task.FromResult(stream);
    }

    public Task DeleteAttachmentAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAttachmentAbsolutePath(relativePath);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a relative path inside the storage root and rejects anything that escapes it.
    /// This is the single choke point for path traversal protection.
    /// </summary>
    public string GetAttachmentAbsolutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Absolute paths are not allowed for attachments.");
        }

        var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));

        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escapes the storage root.");
        }

        return combined;
    }

    /// <summary>Strips directory components and invalid characters from a user-supplied name.</summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "file";
        }

        var nameOnly = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(nameOnly.Where(c => !invalid.Contains(c)).ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "file";
        }

        // Bound the length so the stored name plus extension stays within filesystem limits.
        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }

    /// <summary>Computes a SHA-256 checksum for integrity verification.</summary>
    public static async Task<string> ComputeChecksumAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var hash = await SHA256.HashDataAsync(content, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
