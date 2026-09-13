using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Tickets.Commands;

/// <summary>
/// The applicant writing back on their own ticket.
/// </summary>
/// <remarks>
/// A support thread that only one side can write on is not a conversation — it forces somebody who
/// has one more detail to add to open a second ticket, which support then has to reconcile with the
/// first. The one status that ends it is Closed.
/// </remarks>
public sealed record ReplyToMyTicketCommand(
    Guid TicketId,
    string? Body,
    IReadOnlyList<TicketFileUpload> Files) : IRequest<Unit>;

public sealed class ReplyToMyTicketCommandValidator : AbstractValidator<ReplyToMyTicketCommand>
{
    public ReplyToMyTicketCommandValidator()
    {
        RuleFor(c => c.Body).MaximumLength(5000);

        RuleFor(c => c)
            .Must(command => !string.IsNullOrWhiteSpace(command.Body) || command.Files.Count > 0)
            .WithMessage("Write a message, or attach a file.");

        RuleFor(c => c.Files)
            .Must(files => files.Count <= TicketFile.MaxFilesPerTicket)
            .WithMessage($"Attach at most {TicketFile.MaxFilesPerTicket} files.");
    }
}

public sealed class ReplyToMyTicketCommandHandler : IRequestHandler<ReplyToMyTicketCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;

    public ReplyToMyTicketCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IAuditLogger auditLogger,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _typeValidator = typeValidator;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Unit> Handle(ReplyToMyTicketCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        // The same rule the applicant's read queries use: a ticket carrying their order's id.
        var ticket = await _db.Tickets
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.TicketId && candidate.OrderId == orderId,
                cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        if (!ticket.AcceptsReplies)
        {
            throw new ConflictException(
                "ticket.closed",
                "This ticket has been closed. Start a new one and we will pick it up from there.");
        }

        var action = new TicketAction
        {
            TicketId = ticket.Id,
            AuthorType = ActorType.Applicant,
            AuthorId = orderId,
            AuthorName = ticket.Name,
            // Their own words are never internal, and never emailed back to them.
            Visibility = CommentVisibility.ForUser,
            Body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim(),
        };

        var stored = new List<TicketFile>();

        try
        {
            if (request.Files.Count > 0)
            {
                var document = new TicketActionDocument
                {
                    TicketActionId = action.Id,
                    // Titled from the ticket rather than asking: the applicant is attaching
                    // evidence to a message, not filing a named document.
                    Title = ticket.TicketNumber,
                    SortOrder = 0,
                };

                foreach (var upload in request.Files)
                {
                    var file = await StoreAsync(ticket, document, upload, cancellationToken);
                    document.Files.Add(file);
                    stored.Add(file);
                }

                action.Documents.Add(document);
            }

            _db.TicketActions.Add(action);

            // An answered ticket that has been written on again is waiting once more. It goes back
            // to whoever was carrying it rather than to the unassigned queue, so a reply does not
            // quietly take the ticket off the person already dealing with it.
            if (ticket.Status == TicketStatus.Answered)
            {
                ticket.Status = ticket.AssignedToAdminUserId is null
                    ? TicketStatus.Pending
                    : TicketStatus.InProgress;
            }

            ticket.UpdatedAtUtc = _clock.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Bytes are written before the row; a failed save must not leave them orphaned.
            foreach (var file in stored)
            {
                await _storage.DeleteAsync(file.StoragePath, cancellationToken);
            }

            throw;
        }

        await _auditLogger.LogAsync(
            "Ticket.ApplicantReplied",
            nameof(Ticket),
            ticket.Id,
            new { ticket.TicketNumber, Files = stored.Count },
            cancellationToken);

        return Unit.Value;
    }

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
            UploadedByName = ticket.Name,
        };
    }
}
