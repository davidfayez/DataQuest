using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// Append-only trail of everything that changes money or state: status transitions, comments,
/// payments, refunds and lookup edits. Written by the pipeline, never by hand in a handler.
/// </summary>
public class AuditLogEntry : Entity
{
    /// <summary>Verb-ish identifier, e.g. <c>Application.StatusChanged</c> or <c>Wallet.Credited</c>.</summary>
    public required string Action { get; set; }

    /// <summary>Entity type the action targeted, e.g. <c>Application</c>.</summary>
    public required string EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public ActorType ActorType { get; set; }

    public Guid? ActorId { get; set; }

    public string? ActorName { get; set; }

    /// <summary>JSON snapshot of the relevant before/after values.</summary>
    public string? Data { get; set; }

    public string? IpAddress { get; set; }
}
