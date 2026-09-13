using DataVerification.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataVerification.Infrastructure.Storage;

/// <summary>Bound from the <c>Storage</c> configuration section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "LocalDisk";

    /// <summary>Root directory for uploads, resolved relative to the content root when not absolute.</summary>
    public string RootPath { get; set; } = "storage";

    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } = [".pdf", ".jpg", ".jpeg", ".png"];
}

/// <summary>
/// Stores uploads on the local filesystem under <c>{root}/orders/{orderId}/applications/{appId}/</c>.
/// Paths are treated as untrusted: every resolved path is checked to be inside the configured root
/// so a crafted file name cannot escape it.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalDiskFileStorage> _logger;

    public LocalDiskFileStorage(
        IOptions<StorageOptions> options,
        ILogger<LocalDiskFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);

        Directory.CreateDirectory(_rootPath);
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Stream content,
        string relativeDirectory,
        string fileName,
        string? scopeId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Named "{original}_{scope}_{timestamp}{ext}" so a file on disk is traceable, but the
        // original part is sanitised first: the client's name is untrusted input and must never
        // be able to introduce a separator, a traversal sequence or a second extension.
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var storedName = StoredFileName.Build(fileName, scopeId, extension, DateTime.UtcNow);
        var relativePath = Path.Combine(relativeDirectory, storedName).Replace('\\', '/');

        var absolutePath = ResolveWithinRoot(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // Two uploads of the same name, for the same scope, within the same millisecond would
        // otherwise overwrite each other.
        var attempt = 1;
        while (File.Exists(absolutePath))
        {
            storedName = StoredFileName.Build(fileName, scopeId, extension, DateTime.UtcNow, attempt++);
            relativePath = Path.Combine(relativeDirectory, storedName).Replace('\\', '/');
            absolutePath = ResolveWithinRoot(relativePath);
        }

        await using (var target = File.Create(absolutePath))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        _logger.LogInformation("Stored upload at {RelativePath}.", relativePath);
        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = ResolveWithinRoot(storagePath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("The stored file no longer exists.", storagePath);
        }

        Stream stream = File.OpenRead(absolutePath);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolveWithinRoot(storagePath)));

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = ResolveWithinRoot(storagePath);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a relative path against the storage root and refuses anything that escapes it.
    /// This is the last line of defence against path traversal reaching the filesystem.
    /// </summary>
    private string ResolveWithinRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A storage path is required.", nameof(relativePath));
        }

        var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var normalizedRoot = Path.GetFullPath(_rootPath);

        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "The resolved storage path lies outside the configured storage root.");
        }

        return combined;
    }
}
