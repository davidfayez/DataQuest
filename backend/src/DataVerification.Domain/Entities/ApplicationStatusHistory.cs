using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// An append-only record of one status change. Merged with comments and file events to render the
/// chat-style activity timeline on both the applicant and admin sides.
/// </summary>
public class ApplicationStatusHistory : Entity
{
    public Guid ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    public ApplicationStatus FromStatus { get; set; }

    public ApplicationStatus ToStatus { get; set; }

    public ActorType ChangedByType { get; set; }

    public Guid? ChangedById { get; set; }

    public string? ChangedByName { get; set; }

    public string? Note { get; set; }
}
