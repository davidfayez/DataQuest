using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;

namespace DataVerification.Application.Features.Tickets;

/// <summary>One selectable subject on the public contact form.</summary>
public sealed record TicketCategoryDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    int SortOrder,
    bool IsActive)
{
    public static TicketCategoryDto From(TicketCategory category, string? language)
    {
        ArgumentNullException.ThrowIfNull(category);

        return new TicketCategoryDto(
            category.Id,
            category.ResolveName(language),
            category.NameAr,
            category.NameEn,
            category.SortOrder,
            category.IsActive);
    }
}

/// <summary>
/// What the sender is told once their enquiry lands: the reference, and nothing else.
///
/// Deliberately thin. The form is open to anyone, so echoing the stored record back would let a
/// stranger confirm what the platform now holds about the address they typed.
/// </summary>
public sealed record TicketSubmittedDto(string TicketNumber, string Email);

/// <summary>A row in the support queue.</summary>
public sealed record TicketListItemDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Name,
    string Email,
    string CategoryName,
    TicketStatus Status,
    string StatusName,
    string? AssignedToName,
    string? OrderNumber,
    string? ApplicationNumber,
    int FileCount,
    int ActionCount,
    /// <summary>When support last answered the sender — not merely when a note was written.</summary>
    DateTime? LastRepliedAtUtc,
    DateTime CreatedAtUtc);

/// <summary>A file on a ticket. The storage path never leaves the server.</summary>
public sealed record TicketFileDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc)
{
    public static TicketFileDto From(TicketFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new TicketFileDto(
            file.Id,
            file.FileName,
            file.ContentType,
            file.SizeBytes,
            file.CreatedAtUtc);
    }
}

/// <summary>A titled bundle of files attached to a reply.</summary>
public sealed record TicketActionDocumentDto(
    Guid Id,
    string Title,
    string? Description,
    IReadOnlyList<TicketFileDto> Files);

/// <summary>
/// One entry on the ticket's thread.
/// </summary>
/// <param name="IsInternal">
/// True for a note the sender must never see. The applicant-facing surfaces never receive these
/// rows at all — they are filtered in the query, not hidden in the UI.
/// </param>
/// <param name="CanNotify">
/// Whether this action may still be emailed to the sender: it has to be public, carry something,
/// and not have been sent already.
/// </param>
public sealed record TicketActionDto(
    Guid Id,
    string? Body,
    bool IsInternal,
    /// <summary>True when the applicant wrote it, so the thread can show both sides.</summary>
    bool IsFromApplicant,
    TicketStatus? ChangedStatusTo,
    string? ChangedStatusToName,
    string? AuthorName,
    DateTime CreatedAtUtc,
    DateTime? NotifiedAtUtc,
    string? NotifiedEmail,
    bool CanNotify,
    IReadOnlyList<TicketActionDocumentDto> Documents)
{
    public static TicketActionDto From(TicketAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new TicketActionDto(
            action.Id,
            action.Body,
            action.Visibility == CommentVisibility.Internal,
            action.IsFromApplicant,
            action.ChangedStatusTo,
            action.ChangedStatusTo?.ToString(),
            action.AuthorName,
            action.CreatedAtUtc,
            action.NotifiedAtUtc,
            action.NotifiedEmail,
            action.CanBeSentToSender && !action.WasNotified,
            action.Documents
                .OrderBy(document => document.SortOrder)
                .ThenBy(document => document.CreatedAtUtc)
                .Select(document => new TicketActionDocumentDto(
                    document.Id,
                    document.Title,
                    document.Description,
                    document.Files.Select(TicketFileDto.From).ToList()))
                .ToList());
    }
}

/// <summary>Everything the ticket detail page shows.</summary>
public sealed record TicketDetailsDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Description,
    string Name,
    string Email,
    string? PhoneCountryCode,
    string? PhoneNumber,
    string? FullPhone,
    string LanguageCode,
    Guid TicketCategoryId,
    string CategoryName,
    TicketStatus Status,
    string StatusName,
    Guid? AssignedToAdminUserId,
    string? AssignedToName,
    DateTime? AssignedAtUtc,
    Guid? OrderId,
    string? OrderNumber,
    Guid? ApplicationId,
    string? ApplicationNumber,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyList<TicketFileDto> Files,
    IReadOnlyList<TicketActionDto> Actions);

/// <summary>An admin user a ticket can be handed to.</summary>
public sealed record TicketAssigneeDto(Guid Id, string FullName, string Email, int OpenTickets);

/// <summary>An order offered by the "link to order" picker.</summary>
public sealed record TicketOrderOptionDto(Guid Id, string OrderNumber, string Email);

/// <summary>An application offered by the picker, once an order has been chosen.</summary>
public sealed record TicketApplicationOptionDto(
    Guid Id,
    string ApplicationNumber,
    string StatusName,
    string? AddressedTo);
