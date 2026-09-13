using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Payments;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Tickets.Queries;

/// <summary>A row in the applicant's own list of enquiries.</summary>
public sealed record MyTicketListItemDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string CategoryName,
    TicketStatus Status,
    string StatusName,
    int ReplyCount,
    DateTime? LastReplyAtUtc,
    DateTime CreatedAtUtc);

/// <summary>A file the applicant may open: their own attachment, or one support sent them.</summary>
public sealed record MyTicketFileDto(Guid Id, string FileName, string ContentType, long SizeBytes);

public sealed record MyTicketDocumentDto(
    Guid Id,
    string Title,
    string? Description,
    IReadOnlyList<MyTicketFileDto> Files);

/// <summary>
/// One reply from support.
/// </summary>
/// <remarks>
/// There is deliberately no visibility flag here, and no author identity. This record can only
/// ever describe something already meant for the applicant — an internal note has no shape it
/// could take.
/// </remarks>
/// <param name="FromSupport">
/// True for support's side of the conversation, false for the applicant's own message. Which side,
/// not who — no administrator's name reaches the applicant.
/// </param>
/// <param name="EmailedAtUtc">
/// When a copy also went to their mailbox, or null when it was only published here. Shown so
/// somebody who never received an email can tell that the reply itself is not missing.
/// </param>
public sealed record MyTicketMessageDto(
    Guid Id,
    string? Body,
    bool FromSupport,
    DateTime CreatedAtUtc,
    DateTime? EmailedAtUtc,
    IReadOnlyList<MyTicketDocumentDto> Documents);

public sealed record MyTicketDetailsDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Description,
    string CategoryName,
    TicketStatus Status,
    string StatusName,
    DateTime CreatedAtUtc,
    /// <summary>False once the ticket is closed, which is the one status that ends the thread.</summary>
    bool CanReply,
    IReadOnlyList<MyTicketFileDto> Files,
    IReadOnlyList<MyTicketMessageDto> Messages);

public sealed record GetMyTicketsQuery : IRequest<IReadOnlyList<MyTicketListItemDto>>;

public sealed record GetMyTicketQuery(Guid TicketId) : IRequest<MyTicketDetailsDto>;

public sealed record GetMyTicketFileQuery(Guid TicketId, Guid FileId) : IRequest<FileDownload>;

public sealed class MyTicketQueryHandlers :
    IRequestHandler<GetMyTicketsQuery, IReadOnlyList<MyTicketListItemDto>>,
    IRequestHandler<GetMyTicketQuery, MyTicketDetailsDto>,
    IRequestHandler<GetMyTicketFileQuery, FileDownload>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;

    public MyTicketQueryHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    public async Task<IReadOnlyList<MyTicketListItemDto>> Handle(
        GetMyTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var tickets = await MineOnly(RequireOrder())
            .Include(ticket => ticket.Category)
            .OrderByDescending(ticket => ticket.CreatedAtUtc)
            .Select(ticket => new
            {
                Ticket = ticket,
                // Support's replies only — the applicant's own messages are not replies to them,
                // and counting their own words back would read as an answer that never came.
                Replies = ticket.Actions.Count(action =>
                    action.Visibility == CommentVisibility.ForUser
                    && action.AuthorType != ActorType.Applicant),
                LastReply = ticket.Actions
                    .Where(action => action.Visibility == CommentVisibility.ForUser
                        && action.AuthorType != ActorType.Applicant)
                    .Max(action => (DateTime?)action.CreatedAtUtc),
            })
            .ToListAsync(cancellationToken);

        return tickets
            .Select(row => new MyTicketListItemDto(
                row.Ticket.Id,
                row.Ticket.TicketNumber,
                row.Ticket.Subject,
                row.Ticket.Category?.ResolveName(_currentUser.LanguageCode) ?? string.Empty,
                row.Ticket.Status,
                row.Ticket.Status.ToString(),
                row.Replies,
                row.LastReply,
                row.Ticket.CreatedAtUtc))
            .ToList();
    }

    public async Task<MyTicketDetailsDto> Handle(
        GetMyTicketQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await MineOnly(RequireOrder())
            .Include(t => t.Category)
            .Include(t => t.Files)
            // Filtered in the query, not after loading: an internal note must never be read into
            // memory on an applicant's request, let alone mapped and then trimmed.
            .Include(t => t.Actions.Where(action => action.Visibility == CommentVisibility.ForUser))
            .ThenInclude(action => action.Documents)
            .ThenInclude(document => document.Files)
            .FirstOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        return new MyTicketDetailsDto(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Description,
            ticket.Category?.ResolveName(_currentUser.LanguageCode) ?? string.Empty,
            ticket.Status,
            ticket.Status.ToString(),
            ticket.CreatedAtUtc,
            ticket.AcceptsReplies,
            ticket.Files
                .Where(file => file.TicketActionDocumentId is null)
                .OrderBy(file => file.CreatedAtUtc)
                .Select(ToFileDto)
                .ToList(),
            ticket.Actions
                .OrderBy(action => action.CreatedAtUtc)
                .Select(action => new MyTicketMessageDto(
                    action.Id,
                    action.Body,
                    !action.IsFromApplicant,
                    action.CreatedAtUtc,
                    action.NotifiedAtUtc,
                    action.Documents
                        .OrderBy(document => document.SortOrder)
                        .Select(document => new MyTicketDocumentDto(
                            document.Id,
                            document.Title,
                            document.Description,
                            document.Files.Select(ToFileDto).ToList()))
                        .ToList()))
                .ToList());
    }

    public async Task<FileDownload> Handle(
        GetMyTicketFileQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await MineOnly(RequireOrder())
            .Include(t => t.Files)
            .Include(t => t.Actions)
            .ThenInclude(action => action.Documents)
            .FirstOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        var file = ticket.Files.FirstOrDefault(candidate => candidate.Id == request.FileId)
            ?? throw new NotFoundException(nameof(TicketFile), request.FileId);

        // Their own attachment is always readable. A file on a reply is readable when the reply
        // was addressed to them — the same rule the thread itself follows. A document on an
        // internal note never becomes theirs, emailed or not.
        if (file.TicketActionDocumentId is { } documentId)
        {
            var action = ticket.Actions.FirstOrDefault(candidate =>
                candidate.Documents.Any(document => document.Id == documentId));

            var readable = action is not null && action.Visibility == CommentVisibility.ForUser;

            if (!readable)
            {
                // 404 rather than 403: an applicant has no business learning that a file they may
                // not read exists at all.
                throw new NotFoundException(nameof(TicketFile), request.FileId);
            }
        }

        var content = await _storage.OpenReadAsync(file.StoragePath, cancellationToken);
        return new FileDownload(content, file.ContentType, file.FileName);
    }

    private static MyTicketFileDto ToFileDto(TicketFile file) =>
        new(file.Id, file.FileName, file.ContentType, file.SizeBytes);

    /// <summary>
    /// The tickets this order may read: the ones carrying its id, and nothing else.
    /// </summary>
    /// <remarks>
    /// A ticket carries this order's id because it was raised while signed in — the link is taken
    /// from the token, which the sender cannot assert for themselves — or because an administrator
    /// attributed it here. Both are authoritative.
    ///
    /// It used to also match an unattributed ticket whose address equalled the order's, to catch
    /// someone who wrote in before signing in. That address is typed into a public form and never
    /// verified, so it proved nothing: anyone who knew an applicant's email could put a ticket, and
    /// every reply support wrote on it, into that applicant's portal. Reuniting a genuine
    /// before-signing-in enquiry with its order is support's job now — one click on the ticket's
    /// Linked records — and that decision is recorded rather than inferred.
    /// </remarks>
    private IQueryable<Ticket> MineOnly(Guid orderId) =>
        _db.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.OrderId == orderId);

    private Guid RequireOrder() =>
        _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");
}
