using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A message on the application thread. <see cref="Visibility"/> decides whether the applicant may
/// ever see it; <c>Internal</c> comments are filtered out in the query layer for applicant
/// endpoints, never merely hidden in the UI.
/// </summary>
public class ApplicationComment : Entity
{
    public Guid ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    public ActorType AuthorType { get; set; }

    public Guid? AuthorId { get; set; }

    public string? AuthorName { get; set; }

    public CommentVisibility Visibility { get; set; } = CommentVisibility.ForUser;

    public required string Body { get; set; }

    /// <summary>
    /// Applicants can only ever write user-visible comments. Enforced here so no command handler
    /// can accidentally let an applicant author an internal note.
    /// </summary>
    public static ApplicationComment FromApplicant(Guid applicationId, Actor actor, string body)
    {
        if (actor.Type != ActorType.Applicant)
        {
            throw new DomainException("comment.invalid_author", "Expected an applicant author.");
        }

        return new ApplicationComment
        {
            ApplicationId = applicationId,
            AuthorType = ActorType.Applicant,
            AuthorId = actor.Id,
            AuthorName = actor.DisplayName,
            Visibility = CommentVisibility.ForUser,
            Body = body,
        };
    }

    public static ApplicationComment FromAdmin(
        Guid applicationId,
        Actor actor,
        string body,
        CommentVisibility visibility)
    {
        if (actor.Type != ActorType.Admin)
        {
            throw new DomainException("comment.invalid_author", "Expected an admin author.");
        }

        return new ApplicationComment
        {
            ApplicationId = applicationId,
            AuthorType = ActorType.Admin,
            AuthorId = actor.Id,
            AuthorName = actor.DisplayName,
            Visibility = visibility,
            Body = body,
        };
    }
}
