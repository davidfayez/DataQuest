using DataVerification.Domain.Enums;

namespace DataVerification.Application.Features.Review;

/// <summary>What a timeline entry represents. Drives the icon and layout in the chat UI.</summary>
public enum TimelineEntryKind
{
    Comment = 0,
    StatusChange = 1,
    FileUploaded = 2,
    ResultAttached = 3,
}

/// <summary>
/// One entry in the merged activity feed. Comments, status changes and file events share this
/// shape so the UI can render a single chronological thread.
/// </summary>
public sealed record TimelineEntryDto(
    Guid Id,
    TimelineEntryKind Kind,
    string KindName,
    ActorType AuthorType,
    string? AuthorName,
    DateTime CreatedAtUtc,
    string? Body,
    CommentVisibility? Visibility,
    ApplicationStatus? FromStatus,
    ApplicationStatus? ToStatus,
    string? FileName,
    Guid? FileId);

public sealed record CommentDto(
    Guid Id,
    ActorType AuthorType,
    string? AuthorName,
    CommentVisibility Visibility,
    string Body,
    DateTime CreatedAtUtc);

/// <summary>A deliverable the admin attached once verification succeeded.</summary>
public sealed record ResultFileDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime UploadedAtUtc,
    string DownloadUrl,
    /// <summary>
    /// What to call the file once saved. A result has no required document behind it, so it is
    /// named for the application, the order and the name the reviewer attached it under.
    /// </summary>
    string DownloadName);

/// <summary>Row in the admin applications queue.</summary>
public sealed record AdminApplicationListItemDto(
    Guid Id,
    string ApplicationNumber,
    string AddressedTo,
    ApplicationStatus Status,
    string StatusName,
    bool IsPaid,
    decimal TotalCost,
    string CurrencyCode,
    string OrderNumber,
    string OrderEmail,
    string CountryName,
    string AuthorityName,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    int UnreadUserComments);
