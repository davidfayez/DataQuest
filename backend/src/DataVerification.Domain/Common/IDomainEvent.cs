namespace DataVerification.Domain.Common;

/// <summary>
/// A fact that has already happened inside the domain. The Domain project stays free of any
/// messaging dependency; the Application layer wraps these in MediatR notifications and dispatches
/// them after the transaction commits, so handlers must not assume they can veto the change.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredAtUtc { get; }
}
