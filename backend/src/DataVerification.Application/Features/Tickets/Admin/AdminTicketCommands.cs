using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Tickets.Commands;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Application.Features.Tickets.Admin;

/// <summary>Hands a ticket to a support user, or takes it back when the id is null.</summary>
public sealed record AssignTicketCommand(Guid TicketId, Guid? AdminUserId)
    : IRequest<TicketDetailsDto>;

/// <summary>
/// Links a ticket to an order, and optionally to one of that order's applications.
///
/// Both travel in one command because they are one decision: an application belongs to an order,
/// and letting them be set separately would allow a ticket to point at an application whose order
/// it does not name.
/// </summary>
public sealed record LinkTicketCommand(Guid TicketId, Guid? OrderId, Guid? ApplicationId)
    : IRequest<TicketDetailsDto>;

public sealed record ChangeTicketStatusCommand(Guid TicketId, TicketStatus Status)
    : IRequest<TicketDetailsDto>;

/// <summary>One document on a reply, as it arrives from the form.</summary>
/// <param name="IsInternal">
/// True to keep it with the team's note rather than sending it to the person who wrote in. The
/// default is false, because a document attached to a reply is almost always something being sent.
/// </param>
public sealed record TicketDocumentInput(
    string Title,
    string? Description,
    IReadOnlyList<TicketFileUpload> Files,
    bool IsInternal = false);

/// <summary>
/// Records what support did on a ticket: an answer for the person who wrote in, a note for the
/// team, or both at once.
/// </summary>
/// <remarks>
/// Both in one request rather than two, because they are one thought — "here is what I told them,
/// and here is what the rest of us need to know". Two separate posts would let the reply land while
/// the note failed, leaving the ticket answered with no record of why.
///
/// Each becomes its own action, so the visibility rule stays exactly as strict as it was: an
/// internal note is a row the applicant's queries never select, not a field on a shared row that
/// something might forget to strip.
/// </remarks>
/// <param name="ReplyToSender">
/// What the applicant reads. Visible in their account as soon as it is saved; emailing a copy is a
/// separate step — see <see cref="NotifyTicketActionCommand"/>.
/// </param>
/// <param name="InternalNote">What the team reads. Never shown to the applicant, never emailed.</param>
public sealed record CreateTicketActionCommand(
    Guid TicketId,
    string? ReplyToSender,
    string? InternalNote,
    TicketStatus? ChangeStatusTo,
    IReadOnlyList<TicketDocumentInput> Documents) : IRequest<TicketDetailsDto>;

/// <summary>Emails a public action to the person who raised the ticket.</summary>
public sealed record NotifyTicketActionCommand(Guid TicketId, Guid ActionId)
    : IRequest<TicketDetailsDto>;

public sealed class CreateTicketActionCommandValidator : AbstractValidator<CreateTicketActionCommand>
{
    public CreateTicketActionCommandValidator()
    {
        RuleFor(c => c.ReplyToSender).MaximumLength(5000);
        RuleFor(c => c.InternalNote).MaximumLength(5000);
        RuleFor(c => c.ChangeStatusTo).IsInEnum().When(c => c.ChangeStatusTo is not null);

        // A submission with neither words nor documents records nothing.
        RuleFor(c => c)
            .Must(command => !string.IsNullOrWhiteSpace(command.ReplyToSender)
                || !string.IsNullOrWhiteSpace(command.InternalNote)
                || command.Documents.Count > 0)
            .WithMessage("Write a reply, a note, or attach a document.");

        RuleForEach(c => c.Documents).ChildRules(document =>
        {
            document.RuleFor(d => d.Title)
                .NotEmpty().WithMessage("Give the document a title.")
                .MaximumLength(200);

            document.RuleFor(d => d.Description).MaximumLength(2000);

            document.RuleFor(d => d.Files)
                .Must(files => files.Count > 0)
                .WithMessage("A document needs at least one file.")
                .Must(files => files.Count <= TicketFile.MaxFilesPerTicket)
                .WithMessage($"Attach at most {TicketFile.MaxFilesPerTicket} files per document.");
        });
    }
}

public sealed class AdminTicketCommandHandlers :
    IRequestHandler<AssignTicketCommand, TicketDetailsDto>,
    IRequestHandler<LinkTicketCommand, TicketDetailsDto>,
    IRequestHandler<ChangeTicketStatusCommand, TicketDetailsDto>,
    IRequestHandler<CreateTicketActionCommand, TicketDetailsDto>,
    IRequestHandler<NotifyTicketActionCommand, TicketDetailsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IEmailTemplateRenderer _templates;
    private readonly IEmailSender _emailSender;
    private readonly IEmailRouting _emailRouting;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AdminTicketCommandHandlers> _logger;

    public AdminTicketCommandHandlers(
        IApplicationDbContext db,
        ISender sender,
        ICurrentUser currentUser,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IEmailTemplateRenderer templates,
        IEmailSender emailSender,
        IEmailRouting emailRouting,
        IAuditLogger auditLogger,
        IDateTimeProvider clock,
        ILogger<AdminTicketCommandHandlers> logger)
    {
        _db = db;
        _sender = sender;
        _currentUser = currentUser;
        _storage = storage;
        _typeValidator = typeValidator;
        _templates = templates;
        _emailSender = emailSender;
        _emailRouting = emailRouting;
        _auditLogger = auditLogger;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TicketDetailsDto> Handle(
        AssignTicketCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await RequireTicketAsync(request.TicketId, cancellationToken);

        if (request.AdminUserId is { } adminUserId)
        {
            await EnsureCanCarryTicketsAsync(adminUserId, cancellationToken);

            ticket.AssignedToAdminUserId = adminUserId;
            ticket.AssignedAtUtc = _clock.UtcNow;

            // Picking it up is what makes it "in progress"; leaving it Pending would keep it in the
            // unassigned queue's mental model while somebody is already working on it.
            if (ticket.Status == TicketStatus.Pending)
            {
                ticket.Status = TicketStatus.InProgress;
            }
        }
        else
        {
            ticket.AssignedToAdminUserId = null;
            ticket.AssignedAtUtc = null;
        }

        ticket.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Ticket.Assigned",
            nameof(Ticket),
            ticket.Id,
            new { ticket.TicketNumber, request.AdminUserId },
            cancellationToken);

        return await DetailsAsync(ticket.Id, cancellationToken);
    }

    public async Task<TicketDetailsDto> Handle(
        LinkTicketCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await RequireTicketAsync(request.TicketId, cancellationToken);

        if (request.OrderId is { } orderId)
        {
            var orderExists = await _db.Orders
                .AnyAsync(order => order.Id == orderId, cancellationToken);

            if (!orderExists)
            {
                throw new NotFoundException(nameof(Order), orderId);
            }

            // An application from a different order would show support the wrong case entirely.
            if (request.ApplicationId is { } applicationId)
            {
                var belongs = await _db.Applications.AnyAsync(
                    application => application.Id == applicationId && application.OrderId == orderId,
                    cancellationToken);

                if (!belongs)
                {
                    throw new ConflictException(
                        "ticket.application_not_in_order",
                        "That application does not belong to the chosen order.");
                }
            }
        }

        ticket.LinkTo(request.OrderId, request.ApplicationId);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Ticket.Linked",
            nameof(Ticket),
            ticket.Id,
            new { ticket.TicketNumber, ticket.OrderId, ticket.ApplicationId },
            cancellationToken);

        return await DetailsAsync(ticket.Id, cancellationToken);
    }

    public async Task<TicketDetailsDto> Handle(
        ChangeTicketStatusCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await RequireTicketAsync(request.TicketId, cancellationToken);
        var previous = ticket.Status;

        ticket.Status = request.Status;
        ticket.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Ticket.StatusChanged",
            nameof(Ticket),
            ticket.Id,
            new { ticket.TicketNumber, From = previous.ToString(), To = request.Status.ToString() },
            cancellationToken);

        return await DetailsAsync(ticket.Id, cancellationToken);
    }

    public async Task<TicketDetailsDto> Handle(
        CreateTicketActionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await RequireTicketAsync(request.TicketId, cancellationToken);

        var stored = new List<TicketFile>();

        try
        {
            // One action per audience. Documents follow the audience they were marked for, so a
            // file meant for the team cannot ride along on the reply that leaves the platform.
            var reply = await BuildActionAsync(
                ticket,
                CommentVisibility.ForUser,
                request.ReplyToSender,
                request.Documents.Where(document => !document.IsInternal).ToList(),
                request.ChangeStatusTo,
                stored,
                cancellationToken);

            var note = await BuildActionAsync(
                ticket,
                CommentVisibility.Internal,
                request.InternalNote,
                request.Documents.Where(document => document.IsInternal).ToList(),
                // The status change belongs to the reply where there is one, so a thread does not
                // claim the move twice.
                reply is null ? request.ChangeStatusTo : null,
                stored,
                cancellationToken);

            if (reply is null && note is null)
            {
                throw new ConflictException(
                    "ticket_action.empty",
                    "Write a reply, a note, or attach a document.");
            }

            if (reply is not null)
            {
                _db.TicketActions.Add(reply);
            }

            if (note is not null)
            {
                _db.TicketActions.Add(note);
            }

            if (request.ChangeStatusTo is { } status)
            {
                ticket.Status = status;
            }
            else if (reply is not null
                && ticket.Status is TicketStatus.Pending or TicketStatus.InProgress)
            {
                // Writing an answer for the sender is answering them — they can read it in their
                // account as soon as it is saved, whether or not a copy is also emailed. A note to
                // the team moves nothing, and a status set explicitly above wins.
                ticket.Status = TicketStatus.Answered;
            }

            ticket.UpdatedAtUtc = _clock.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                "Ticket.ActionCreated",
                nameof(Ticket),
                ticket.Id,
                new
                {
                    ticket.TicketNumber,
                    RepliedToSender = reply is not null,
                    NotedInternally = note is not null,
                    Documents = request.Documents.Count,
                    StatusChangedTo = request.ChangeStatusTo?.ToString(),
                },
                cancellationToken);
        }
        catch
        {
            // Bytes are written before the rows; a failed save must not leave them orphaned.
            foreach (var file in stored)
            {
                await _storage.DeleteAsync(file.StoragePath, cancellationToken);
            }

            throw;
        }

        return await DetailsAsync(ticket.Id, cancellationToken);
    }

    public async Task<TicketDetailsDto> Handle(
        NotifyTicketActionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await _db.Tickets
            .Include(t => t.Category)
            .Include(t => t.Actions.Where(action => action.Id == request.ActionId))
            .ThenInclude(action => action.Documents)
            .ThenInclude(document => document.Files)
            .FirstOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        var action = ticket.Actions.FirstOrDefault(a => a.Id == request.ActionId)
            ?? throw new NotFoundException(nameof(TicketAction), request.ActionId);

        // The rule that matters most in this feature: an internal note is support talking among
        // themselves, and no request may put it in the sender's inbox. Checked here, at the
        // boundary, and again in the domain when the send is recorded.
        if (action.Visibility == CommentVisibility.Internal)
        {
            throw new ConflictException(
                "ticket_action.internal",
                "An internal note cannot be sent to the person who raised the ticket.");
        }

        if (!action.CanBeSentToSender)
        {
            throw new ConflictException(
                "ticket_action.nothing_to_send",
                "This action has nothing in it to send.");
        }

        if (action.WasNotified)
        {
            throw new ConflictException(
                "ticket_action.already_notified",
                "This reply has already been emailed.");
        }

        // The files travel with the email. Whoever wrote in has no account and no portal to sign
        // in to, so a document they cannot open is a document they never received.
        var attachments = await ReadAttachmentsAsync(action, cancellationToken);

        var message = _templates.RenderTicketReply(
            ticket.Email,
            ticket.TicketNumber,
            ticket.Name,
            ticket.Subject,
            action.Body,
            action.Documents
                .OrderBy(document => document.SortOrder)
                .Select(document => new TicketReplyAttachment(
                    document.Title,
                    document.Description,
                    document.Files.Count))
                .ToList(),
            ticket.LanguageCode);

        message = message with { Attachments = attachments };
        message = await _emailRouting.ApplyAsync(EmailType.ContactUs, message, cancellationToken);

        var delivery = await _emailSender.SendAsync(message, cancellationToken);

        // The one place in this feature where the result has to be read. Everywhere else email is
        // a courtesy alongside something that already happened; here the send *is* what happened,
        // an administrator pressed a button for it, and the outcome is written down: recording
        // MarkNotified regardless would stamp the reply "emailed", show the sender "also emailed
        // to you" for a message their mailbox never received, and clear it from the queue's
        // not-emailed count — three places all agreeing on something untrue.
        //
        // The sender never throws, so nothing else has been undone; refusing here leaves the reply
        // exactly as it was, still sendable once the delivery problem is fixed.
        if (!delivery.Delivered)
        {
            _logger.LogError(
                "Notify failed for ticket {TicketNumber} action {ActionId}: {Status} {Detail}",
                ticket.TicketNumber,
                action.Id,
                delivery.Status,
                delivery.Detail);

            throw new ConflictException(
                "ticket_action.not_emailed",
                delivery.Detail is { Length: > 0 } detail
                    ? $"The email was not sent: {detail}"
                    : "The email was not sent. Check the delivery settings and try again.");
        }

        action.MarkNotified(ticket.Email, _clock.UtcNow);

        // Normally already Answered by the time the email goes out — the reply moved it when it
        // was written. This stays for the case where somebody moved the ticket back afterwards.
        // Not applied to a closed ticket: someone who deliberately closed it has not reopened it.
        if (ticket.Status is TicketStatus.Pending or TicketStatus.InProgress)
        {
            ticket.Status = TicketStatus.Answered;
        }

        ticket.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Ticket.ActionNotified",
            nameof(Ticket),
            ticket.Id,
            new { ticket.TicketNumber, ActionId = action.Id, ticket.Email },
            cancellationToken);

        _logger.LogInformation(
            "Emailed action {ActionId} on ticket {TicketNumber}.", action.Id, ticket.TicketNumber);

        return await DetailsAsync(ticket.Id, cancellationToken);
    }

    /// <summary>
    /// Builds one action for one audience, or null when there is nothing to record for it.
    /// </summary>
    private async Task<TicketAction?> BuildActionAsync(
        Ticket ticket,
        CommentVisibility visibility,
        string? body,
        IReadOnlyList<TicketDocumentInput> documents,
        TicketStatus? changedStatusTo,
        List<TicketFile> stored,
        CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(body) ? null : body.Trim();

        if (text is null && documents.Count == 0)
        {
            return null;
        }

        var action = new TicketAction
        {
            TicketId = ticket.Id,
            AuthorId = _currentUser.AdminUserId,
            AuthorName = _currentUser.DisplayName,
            Visibility = visibility,
            Body = text,
            ChangedStatusTo = changedStatusTo,
        };

        var order = 0;

        foreach (var input in documents)
        {
            var document = new TicketActionDocument
            {
                TicketActionId = action.Id,
                Title = input.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(input.Description)
                    ? null
                    : input.Description.Trim(),
                SortOrder = order++,
            };

            foreach (var upload in input.Files)
            {
                var file = await StoreAsync(ticket, document, upload, cancellationToken);
                document.Files.Add(file);
                stored.Add(file);
            }

            action.Documents.Add(document);
        }

        return action;
    }

    /// <summary>
    /// Reads every file on the action into memory so it can be attached.
    /// </summary>
    /// <remarks>
    /// Refuses rather than silently dropping files once the total passes the ceiling: a reply that
    /// arrives missing half its documents looks delivered to support and looks broken to the person
    /// reading it, and neither of them finds out. Splitting it into two replies is the fix.
    /// </remarks>
    private async Task<List<EmailAttachment>> ReadAttachmentsAsync(
        TicketAction action,
        CancellationToken cancellationToken)
    {
        var attachments = new List<EmailAttachment>();
        var total = 0L;

        foreach (var document in action.Documents.OrderBy(document => document.SortOrder))
        {
            foreach (var file in document.Files)
            {
                total += file.SizeBytes;

                if (total > EmailAttachment.MaxTotalBytes)
                {
                    throw new ConflictException(
                        "ticket_action.attachments_too_large",
                        "The documents on this reply are too large to email together. "
                        + "Send them as separate replies.");
                }

                await using var content = await _storage.OpenReadAsync(file.StoragePath, cancellationToken);
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);

                attachments.Add(new EmailAttachment(file.FileName, file.ContentType, buffer.ToArray()));
            }
        }

        return attachments;
    }

    /// <summary>
    /// Refuses an assignee who could not open the ticket you just gave them. Without this, a ticket
    /// can be assigned into a black hole and look handled while nobody can see it.
    /// </summary>
    private async Task EnsureCanCarryTicketsAsync(Guid adminUserId, CancellationToken cancellationToken)
    {
        var isActive = await _db.AdminUsers
            .AsNoTracking()
            .Where(candidate => candidate.Id == adminUserId)
            .Select(candidate => (bool?)candidate.IsActive)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(AdminUser), adminUserId);

        // Asked as a question rather than projected as a list: the grant can come from a role or
        // from a direct assignment, and EF translates the two Any() clauses where it cannot
        // translate a projection that concatenates them.
        var canView = await _db.AdminUsers
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == adminUserId
                    && (candidate.UserRoles.Any(userRole => userRole.Role!.RolePermissions
                            .Any(rolePermission =>
                                rolePermission.Permission!.Name == Permissions.TicketsView))
                        || candidate.UserPermissions.Any(direct =>
                            direct.Permission!.Name == Permissions.TicketsView)),
                cancellationToken);

        if (!isActive || !canView)
        {
            throw new ConflictException(
                "ticket.assignee_cannot_view",
                "That user cannot open the support queue, so a ticket assigned to them would go unseen.");
        }
    }

    private async Task<Ticket> RequireTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        await _db.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == ticketId, cancellationToken)
        ?? throw new NotFoundException(nameof(Ticket), ticketId);

    private Task<TicketDetailsDto> DetailsAsync(Guid ticketId, CancellationToken cancellationToken) =>
        _sender.Send(new GetTicketQuery(ticketId), cancellationToken);

    private async Task<TicketFile> StoreAsync(
        Ticket ticket,
        TicketActionDocument document,
        TicketFileUpload upload,
        CancellationToken cancellationToken)
    {
        if (upload.SizeBytes > TicketFile.MaxFileSizeBytes)
        {
            throw new ConflictException(
                "file.too_large",
                $"Each file must be {TicketFile.MaxFileSizeBytes / (1024 * 1024)} MB or smaller.");
        }

        var contentType = await _typeValidator.DetectAllowedContentTypeAsync(
            upload.Content,
            upload.FileName,
            cancellationToken)
            ?? throw new ConflictException(
                "file.unsupported_type",
                "Only PDF, JPG, JPEG and PNG files are accepted.");

        var storagePath = await _storage.SaveAsync(
            upload.Content,
            "tickets",
            upload.FileName,
            ticket.Id.ToString("N"),
            cancellationToken);

        return new TicketFile
        {
            TicketId = ticket.Id,
            TicketActionDocumentId = document.Id,
            FileName = Path.GetFileName(upload.FileName),
            ContentType = contentType,
            StoragePath = storagePath,
            SizeBytes = upload.SizeBytes,
            UploadedByName = _currentUser.DisplayName,
        };
    }
}
