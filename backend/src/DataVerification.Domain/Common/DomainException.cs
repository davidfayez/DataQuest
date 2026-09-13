namespace DataVerification.Domain.Common;

/// <summary>
/// Raised when an operation would break an invariant the domain guarantees — an illegal status
/// transition, a negative wallet balance, express on a non-express service, and so on.
/// <see cref="Code"/> is a stable machine-readable identifier the API surfaces in ProblemDetails
/// so front ends can localize the message instead of parsing English text.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    public DomainException(string message) : this("domain.rule_violated", message) { }

    public DomainException() : this("domain.rule_violated", "A domain rule was violated.") { }

    public DomainException(string message, Exception innerException)
        : base(message, innerException) => Code = "domain.rule_violated";

    public string Code { get; } = "domain.rule_violated";
}
