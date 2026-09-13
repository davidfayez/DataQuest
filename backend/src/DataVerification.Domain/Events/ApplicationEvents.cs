using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Events;

/// <summary>Raised by every legal status transition; drives audit logging and notifications.</summary>
public sealed record ApplicationStatusChangedEvent(
    Guid ApplicationId,
    ApplicationStatus FromStatus,
    ApplicationStatus ToStatus,
    Actor Actor,
    DateTime OccurredAtUtc) : IDomainEvent;

/// <summary>Raised once per application settled by a wallet payment.</summary>
public sealed record ApplicationPaidEvent(
    Guid ApplicationId,
    Guid OrderId,
    decimal Amount,
    DateTime OccurredAtUtc) : IDomainEvent;

/// <summary>
/// Raised when an admin posts a comment the applicant can read, so the notification pipeline can
/// email them without the review handler knowing about email at all.
/// </summary>
public sealed record UserVisibleCommentPostedEvent(
    Guid ApplicationId,
    Guid CommentId,
    DateTime OccurredAtUtc) : IDomainEvent;
