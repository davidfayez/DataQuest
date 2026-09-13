using System.Text.Json;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Infrastructure;

/// <summary>
/// Vets every file on every multipart request before it reaches a controller.
///
/// The upload commands each perform their own checks, and those stay — this is a second, uniform
/// gate so a new endpoint that forgets to validate is still covered, and so a hostile file is
/// rejected before any handler, model binder or storage provider has touched it. Rejections are
/// returned as ProblemDetails with a stable <c>code</c>, matching the rest of the API.
/// </summary>
public sealed class UploadSecurityMiddleware
{
    /// <summary>
    /// Extensions that must never appear anywhere in the name, not merely at the end.
    /// <c>invoice.php.pdf</c> passes an extension check but is a well-known way to get a file
    /// executed by a misconfigured server that dispatches on the first extension it recognises.
    /// </summary>
    private static readonly string[] DangerousExtensions =
    [
        ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".scr", ".ps1", ".psm1", ".vbs", ".js",
        ".jar", ".sh", ".php", ".phtml", ".asp", ".aspx", ".jsp", ".jspx", ".cgi", ".pl", ".py",
        ".rb", ".htaccess", ".config", ".svg", ".html", ".htm", ".xhtml",
    ];

    private const int MaxFileNameLength = 200;

    private readonly RequestDelegate _next;
    private readonly ILogger<UploadSecurityMiddleware> _logger;

    public UploadSecurityMiddleware(RequestDelegate next, ILogger<UploadSecurityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IFileTypeValidator typeValidator,
        IUploadContentScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.HasFormContentType || !IsMultipart(context.Request.ContentType))
        {
            await _next(context);
            return;
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // A malformed or oversized multipart body never reaches a handler.
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "file.malformed_request",
                "The upload could not be read.");
            return;
        }

        foreach (var file in form.Files)
        {
            var failure = await InspectAsync(file, typeValidator, scanner, context.RequestAborted);
            if (failure is null) continue;

            _logger.LogWarning(
                "Rejected upload {FileName} ({Length} bytes) on {Path}: {Code}.",
                file.FileName,
                file.Length,
                context.Request.Path,
                failure.Code);

            await WriteProblemAsync(context, failure.Status, failure.Code, failure.Detail);
            return;
        }

        await _next(context);
    }

    private static async Task<UploadFailure?> InspectAsync(
        IFormFile file,
        IFileTypeValidator typeValidator,
        IUploadContentScanner scanner,
        CancellationToken cancellationToken)
    {
        var name = file.FileName ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxFileNameLength)
        {
            return new(StatusCodes.Status400BadRequest, "file.invalid_name", "The file name is missing or too long.");
        }

        // A name is metadata, not a path. Anything that could be read as one is refused outright.
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal)
            || name.Contains(':') || name.Any(char.IsControl))
        {
            return new(StatusCodes.Status400BadRequest, "file.invalid_name", "The file name contains illegal characters.");
        }

        var extension = Path.GetExtension(name).ToLowerInvariant();

        if (!ApplicationFile.IsAllowedExtension(extension))
        {
            return new(
                StatusCodes.Status415UnsupportedMediaType,
                "file.unsupported_type",
                "Only PDF, JPG, JPEG and PNG files are accepted.");
        }

        var lowerName = name.ToLowerInvariant();
        foreach (var dangerous in DangerousExtensions)
        {
            if (lowerName.Contains(dangerous + ".", StringComparison.Ordinal))
            {
                return new(
                    StatusCodes.Status415UnsupportedMediaType,
                    "file.double_extension",
                    "The file name carries a second, executable extension.");
            }
        }

        if (file.Length <= 0)
        {
            return new(StatusCodes.Status400BadRequest, "file.empty", "The uploaded file is empty.");
        }

        if (file.Length > ApplicationFile.MaxFileSizeBytes)
        {
            return new(
                StatusCodes.Status413PayloadTooLarge,
                "file.too_large",
                $"Files must be {ApplicationFile.MaxFileSizeBytes / (1024 * 1024)} MB or smaller.");
        }

        // Buffered so both the signature check and the content scan can read it, and so the
        // controller still receives an untouched stream afterwards.
        await using var buffer = new MemoryStream();
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;

        var contentType = await typeValidator.DetectAllowedContentTypeAsync(buffer, name, cancellationToken);
        if (contentType is null)
        {
            return new(
                StatusCodes.Status415UnsupportedMediaType,
                "file.unsupported_type",
                "The file's contents do not match its extension.");
        }

        var threat = await scanner.FindThreatAsync(buffer, contentType, cancellationToken);
        if (threat is not null)
        {
            return new(
                StatusCodes.Status422UnprocessableEntity,
                threat,
                "The file contains active content and was rejected.");
        }

        return null;
    }

    private static bool IsMultipart(string? contentType) =>
        contentType?.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string code,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Upload rejected",
            Detail = detail,
            Type = $"https://tools.ietf.org/html/rfc9110#section-15.5.{status - 400 + 1}",
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            context.RequestAborted);
    }

    private sealed record UploadFailure(int Status, string Code, string Detail);
}
