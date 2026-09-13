namespace DataVerification.Application.Common.Interfaces;

/// <summary>
/// Where uploaded bytes live. The local-disk implementation is the v1 default; the interface is
/// deliberately narrow so S3 or Azure Blob can be dropped in without touching feature code.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Persists the stream and returns the provider-relative path recorded on the file row.
    /// The path is never exposed to clients — downloads go through an order-scoped endpoint.
    /// </summary>
    /// <param name="scopeId">
    /// The service line (or other owner) the upload belongs to. It becomes part of the stored file
    /// name, so a file on disk can be traced back to what it was attached to.
    /// </param>
    Task<string> SaveAsync(
        Stream content,
        string relativeDirectory,
        string fileName,
        string? scopeId = null,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}

/// <summary>Generates the human-facing application reference.</summary>
public interface IApplicationNumberGenerator
{
    Task<string> GenerateUniqueAsync(CancellationToken cancellationToken = default);
}

/// <summary>Generates the human-facing support ticket reference.</summary>
public interface ITicketNumberGenerator
{
    Task<string> GenerateUniqueAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates an upload's real type. Extensions are trivially forged, so the platform sniffs the
/// leading bytes and refuses anything whose signature does not match an allowed type.
/// </summary>
public interface IFileTypeValidator
{
    /// <summary>
    /// Returns the canonical content type when the stream's signature matches an allowed format,
    /// or null when it does not. The stream position is restored before returning.
    /// </summary>
    Task<string?> DetectAllowedContentTypeAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same check, narrowed to the formats one required document accepts. Codes come from
    /// <c>DocumentFileTypes</c>; null means there is no per-document rule and only the
    /// platform-wide list applies. A document can narrow what is accepted, never widen it.
    /// </summary>
    Task<string?> DetectAllowedContentTypeAsync(
        Stream content,
        string fileName,
        IReadOnlyList<string>? allowedFileTypes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inspects an upload's whole body for content that would execute rather than merely display.
/// A matching file signature only proves how the bytes start; this is what checks the rest.
/// </summary>
public interface IUploadContentScanner
{
    /// <summary>
    /// Returns a machine-readable reason when the content is unsafe, or null when it is clean.
    /// The stream position is restored before returning.
    /// </summary>
    Task<string?> FindThreatAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);
}
