using FluentValidation.Results;

namespace DataVerification.Application.Common.Exceptions;

/// <summary>Aggregates FluentValidation failures into the 400 ProblemDetails the API returns.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException() : base("One or more validation failures occurred.") =>
        Errors = new Dictionary<string, string[]>();

    public ValidationException(IEnumerable<ValidationFailure> failures) : this() =>
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(group => group.Key, group => group.ToArray());

    public ValidationException(string message) : base(message) =>
        Errors = new Dictionary<string, string[]>();

    public ValidationException(string message, Exception innerException)
        : base(message, innerException) => Errors = new Dictionary<string, string[]>();

    public IDictionary<string, string[]> Errors { get; }
}

/// <summary>The requested resource does not exist, or the caller may not know that it does.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException() : base("The requested resource was not found.") { }

    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException) { }

    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.") { }
}

/// <summary>
/// The caller is authenticated but not allowed to touch this resource — most often an order
/// reaching for another order's data. Surfaced as 403 without revealing whether it exists.
/// </summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException() : base("You are not allowed to access this resource.") { }

    public ForbiddenAccessException(string message) : base(message) { }

    public ForbiddenAccessException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>The request conflicts with the resource's current state. Surfaced as 409.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException() : base("The request conflicts with the current state of the resource.") =>
        Code = "request.conflict";

    public ConflictException(string code, string message) : base(message) => Code = code;

    public ConflictException(string message) : base(message) => Code = "request.conflict";

    public ConflictException(string message, Exception innerException)
        : base(message, innerException) => Code = "request.conflict";

    public string Code { get; } = "request.conflict";
}
