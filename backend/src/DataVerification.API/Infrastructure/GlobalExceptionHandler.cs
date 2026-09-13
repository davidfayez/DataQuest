using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Features.Orders.Commands;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace DataVerification.API.Infrastructure;

/// <summary>
/// Translates the exception vocabulary of the lower layers into RFC 7807 responses. Every problem
/// carries a stable <c>code</c> so front ends localize the message rather than parsing English,
/// and stack traces are never serialized regardless of environment.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = Map(exception);

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception on {Path}.", httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation(
                "Request on {Path} rejected: {Code} — {Detail}",
                httpContext.Request.Path,
                problem.Extensions.TryGetValue("code", out var code) ? code : "unknown",
                problem.Detail);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException validation => Build(
            StatusCodes.Status400BadRequest,
            "Validation failed",
            "One or more fields are invalid.",
            "request.validation_failed",
            extensions: new Dictionary<string, object?> { ["errors"] = validation.Errors }),

        InvalidCredentialsException => Build(
            StatusCodes.Status401Unauthorized,
            "Authentication failed",
            // Deliberately generic: it must not reveal whether the account exists.
            "The credentials provided are not valid.",
            "auth.invalid_credentials"),

        ForbiddenAccessException forbidden => Build(
            StatusCodes.Status403Forbidden,
            "Forbidden",
            forbidden.Message,
            "request.forbidden"),

        NotFoundException notFound => Build(
            StatusCodes.Status404NotFound,
            "Not found",
            notFound.Message,
            "request.not_found"),

        ConflictException conflict => Build(
            StatusCodes.Status409Conflict,
            "Conflict",
            conflict.Message,
            conflict.Code),

        // Insufficient funds is semantically "well-formed but unprocessable", and the front end
        // shows a top-up prompt rather than a generic error, so it gets its own status.
        InsufficientFundsException funds => Build(
            StatusCodes.Status422UnprocessableEntity,
            "Insufficient funds",
            funds.Message,
            funds.Code,
            extensions: new Dictionary<string, object?>
            {
                ["required"] = funds.Required,
                ["available"] = funds.Available,
            }),

        DomainException domain => Build(
            StatusCodes.Status409Conflict,
            "Business rule violated",
            domain.Message,
            domain.Code),

        // The database still has the row but the bytes are gone from storage (a restored database
        // paired with a wiped disk, say). A 404 with a specific code lets the client say so plainly
        // instead of surfacing an opaque "server error".
        FileNotFoundException or DirectoryNotFoundException => Build(
            StatusCodes.Status404NotFound,
            "File unavailable",
            "The stored file is no longer available.",
            "file.not_found"),

        // 499 is nginx's "client closed request"; ASP.NET defines no constant for it.
        OperationCanceledException => Build(
            499,
            "Request cancelled",
            "The request was cancelled by the client.",
            "request.cancelled"),

        _ => Build(
            StatusCodes.Status500InternalServerError,
            "Server error",
            "An unexpected error occurred. Quote the trace id when contacting support.",
            "server.unexpected_error"),
    };

    private static ProblemDetails Build(
        int status,
        string title,
        string detail,
        string code,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };

        problem.Extensions["code"] = code;

        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[key] = value;
        }

        return problem;
    }
}
