using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// An enquiry raised through the public "contact us" form.
///
/// Deliberately standalone rather than hung off an order: whoever writes in may not have an account,
/// may not remember which order they are asking about, or may be asking before they buy anything.
/// An administrator can link it to an order and an application afterwards — see
/// <see cref="OrderId"/> — which is the point at which the enquiry stops being anonymous.
/// </summary>
public class Ticket : Entity
{
    /// <summary>Human-facing reference, <c>TKT-YYMM-XXXXXX</c>. Quoted in every email about it.</summary>
    public required string TicketNumber { get; set; }

    public Guid TicketCategoryId { get; set; }

    public TicketCategory? Category { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.Pending;

    // -- Who wrote in -------------------------------------------------------
    // Captured as plain text, not a foreign key: the sender is whoever filled the form in, and
    // holding their own words is what lets support answer someone with no account at all.

    public required string Name { get; set; }

    public required string Email { get; set; }

    /// <summary>Dialling code including the leading "+", kept apart so the number stays portable.</summary>
    public string? PhoneCountryCode { get; set; }

    public string? PhoneNumber { get; set; }

    public required string Subject { get; set; }

    public required string Description { get; set; }

    /// <summary>The language the form was filled in, so replies go back in the same one.</summary>
    public string LanguageCode { get; set; } = "en";

    // -- What support did with it ------------------------------------------

    /// <summary>The support user carrying this ticket, or null while it is unassigned.</summary>
    public Guid? AssignedToAdminUserId { get; set; }

    public AdminUser? AssignedTo { get; set; }

    public DateTime? AssignedAtUtc { get; set; }

    /// <summary>Set once someone recognises which order the enquiry is about.</summary>
    public Guid? OrderId { get; set; }

    public Order? Order { get; set; }

    /// <summary>
    /// Set once the enquiry is narrowed to one application. Only ever an application belonging to
    /// <see cref="OrderId"/>; clearing the order clears this too, so the pair can never disagree.
    /// </summary>
    public Guid? ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    /// <summary>What the sender attached to the original enquiry.</summary>
    public ICollection<TicketFile> Files { get; set; } = [];

    /// <summary>Everything support has since recorded against it, oldest first.</summary>
    public ICollection<TicketAction> Actions { get; set; } = [];

    /// <summary>
    /// Whether the person who raised this may still write on it.
    ///
    /// Closed is the one status that ends the conversation. Anything else — waiting, being looked
    /// at, already answered — is a thread somebody may reasonably have more to say on.
    /// </summary>
    public bool AcceptsReplies => Status != TicketStatus.Closed;

    /// <summary>The full international number, or null when none was given.</summary>
    public string? FullPhoneNumber => string.IsNullOrWhiteSpace(PhoneNumber)
        ? null
        : $"{PhoneCountryCode}{PhoneNumber}".Trim();

    /// <summary>
    /// Links the ticket to an order, and to one of that order's applications.
    ///
    /// Both are set together because an application without its order is a link support cannot
    /// navigate, and an application belonging to a different order is simply wrong. The caller has
    /// already checked the application belongs to the order; this keeps the pair consistent
    /// afterwards — dropping the order drops the application with it.
    /// </summary>
    public void LinkTo(Guid? orderId, Guid? applicationId)
    {
        if (orderId is null && applicationId is not null)
        {
            throw new DomainException(
                "ticket.application_without_order",
                "An application cannot be linked without the order it belongs to.");
        }

        OrderId = orderId;
        ApplicationId = orderId is null ? null : applicationId;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// The subject list the contact form offers, managed from the admin panel.
///
/// A lookup rather than a fixed enum so support can reshape their own intake — adding "Refunds" or
/// retiring a category is data, not a deployment.
/// </summary>
public class TicketCategory : LocalizedLookup
{
    /// <summary>Ascending display order in the form's dropdown; ties fall back to the name.</summary>
    public int SortOrder { get; set; }

    public ICollection<Ticket> Tickets { get; set; } = [];
}

/// <summary>
/// A file the sender attached to their enquiry. As everywhere else on the platform, only the
/// metadata is in SQL; the bytes go through IFileStorage.
/// </summary>
public class TicketFile : Entity
{
    /// <summary>The same ceiling the applicant's other uploads carry.</summary>
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    /// <summary>Enough to show a problem from a few angles, and no more.</summary>
    public const int MaxFilesPerTicket = 5;

    public Guid TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    /// <summary>Set when the file belongs to a document on a reply rather than to the enquiry itself.</summary>
    public Guid? TicketActionDocumentId { get; set; }

    public TicketActionDocument? Document { get; set; }

    public required string FileName { get; set; }

    /// <summary>Provider-relative path; never handed to a client, who downloads through a scoped endpoint.</summary>
    public required string StoragePath { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public string? UploadedByName { get; set; }
}

/// <summary>
/// Something an administrator recorded against a ticket: a note to the team, an answer to the
/// sender, or documents attached to either.
/// </summary>
/// <remarks>
/// <see cref="Visibility"/> is the whole safety story of this feature. An internal action is
/// support talking among themselves; it must never reach the sender's mailbox, which is why
/// <see cref="CanBeSentToSender"/> gates notification here in the domain rather than in a form.
/// </remarks>
public class TicketAction : Entity
{
    public Guid TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    /// <summary>
    /// Which side of the conversation this came from.
    ///
    /// Defaults to <see cref="ActorType.Admin"/> because support wrote every entry before
    /// applicants could reply, and rows written then carry no author type of their own.
    /// </summary>
    public ActorType AuthorType { get; set; } = ActorType.Admin;

    public Guid? AuthorId { get; set; }

    public string? AuthorName { get; set; }

    public CommentVisibility Visibility { get; set; } = CommentVisibility.ForUser;

    /// <summary>What the administrator wrote. Optional when the action is only carrying documents.</summary>
    public string? Body { get; set; }

    /// <summary>The status the ticket moved to in the same breath, or null when it did not move.</summary>
    public TicketStatus? ChangedStatusTo { get; set; }

    /// <summary>When this action was emailed to the sender, or null if it never was.</summary>
    public DateTime? NotifiedAtUtc { get; set; }

    /// <summary>The address it went to, kept so the trail survives the sender changing theirs.</summary>
    public string? NotifiedEmail { get; set; }

    public ICollection<TicketActionDocument> Documents { get; set; } = [];

    /// <summary>True once this action has been emailed; a second send would be a duplicate.</summary>
    public bool WasNotified => NotifiedAtUtc is not null;

    /// <summary>
    /// Whether this action may be emailed to the person who raised the ticket.
    ///
    /// Internal actions never can. Nor can an empty one — an email carrying neither words nor
    /// documents is noise arriving in someone's inbox. Nor can the applicant's own message: mailing
    /// somebody their own words back is not a reply.
    /// </summary>
    public bool CanBeSentToSender =>
        Visibility == CommentVisibility.ForUser
        && AuthorType == ActorType.Admin
        && (!string.IsNullOrWhiteSpace(Body) || Documents.Count > 0);

    /// <summary>True when the applicant wrote this rather than support.</summary>
    public bool IsFromApplicant => AuthorType == ActorType.Applicant;

    /// <summary>
    /// Records that this action reached the sender. Refuses on an action that may not be sent, so
    /// no code path can mark an internal note as delivered to the person outside.
    /// </summary>
    public void MarkNotified(string email, DateTime whenUtc)
    {
        if (!CanBeSentToSender)
        {
            throw new DomainException(
                "ticket_action.not_sendable",
                "This action cannot be sent to the person who raised the ticket.");
        }

        NotifiedAtUtc = whenUtc;
        NotifiedEmail = email;
        UpdatedAtUtc = whenUtc;
    }
}

/// <summary>
/// A titled, described bundle of files on a reply — the "document" component support fills in when
/// they attach something, so the sender receives a named thing rather than a bare filename.
/// </summary>
public class TicketActionDocument : Entity
{
    public Guid TicketActionId { get; set; }

    public TicketAction? Action { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Display order within the action, lowest first.</summary>
    public int SortOrder { get; set; }

    public ICollection<TicketFile> Files { get; set; } = [];
}
