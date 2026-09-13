using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Common;

/// <summary>
/// Who performed an action. Carried into status history, the wallet ledger and the audit log so
/// every state change is attributable without the domain depending on ASP.NET identity types.
/// </summary>
/// <param name="Type">Whether an applicant, an admin, or the system itself acted.</param>
/// <param name="Id">Order id for applicants, admin user id for admins, null for the system.</param>
/// <param name="DisplayName">Human-readable label shown in timelines and the audit log.</param>
public readonly record struct Actor(ActorType Type, Guid? Id, string? DisplayName)
{
    public static Actor System() => new(ActorType.System, null, "System");

    public static Actor Applicant(Guid orderId, string? displayName = null) =>
        new(ActorType.Applicant, orderId, displayName);

    public static Actor Admin(Guid adminUserId, string? displayName = null) =>
        new(ActorType.Admin, adminUserId, displayName);
}
