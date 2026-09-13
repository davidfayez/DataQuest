using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Application.Features.Tickets.Commands;

/// <summary>One attachment as it arrives from the contact form, before it is written to storage.</summary>
public sealed record TicketFileUpload(string FileName, long SizeBytes, Stream Content);

/// <summary>
/// Raises a support ticket from the public contact form.
/// </summary>
/// <remarks>
/// Open to anyone, signed in or not, which shapes the rules below: the sender's own words are
/// trusted for name and phone, but nothing they send decides status, assignment or which order the
/// enquiry belongs to. Those are support's to set afterwards.
/// </remarks>
public sealed record CreateTicketCommand(
    Guid TicketCategoryId,
    string Name,
    string Email,
    string? PhoneCountryCode,
    string? PhoneNumber,
    string Subject,
    string Description,
    IReadOnlyList<TicketFileUpload> Files) : IRequest<TicketSubmittedDto>;

public sealed class CreateTicketCommandValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketCommandValidator()
    {
        RuleFor(c => c.TicketCategoryId).NotEmpty().WithMessage("Choose what your message is about.");

        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Enter your name.")
            .MaximumLength(200);

        RuleFor(c => c.Email)
            .NotEmpty().WithMessage("Enter your email address.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("That is not a valid email address.");

        RuleFor(c => c.Subject)
            .NotEmpty().WithMessage("Enter a subject.")
            .MaximumLength(300);

        RuleFor(c => c.Description)
            .NotEmpty().WithMessage("Describe what you need help with.")
            .MaximumLength(5000);

        RuleFor(c => c.PhoneCountryCode)
            .MaximumLength(8)
            .Matches(@"^\+?\d{1,6}$").When(c => !string.IsNullOrWhiteSpace(c.PhoneCountryCode))
            .WithMessage("That is not a valid dialling code.");

        RuleFor(c => c.PhoneNumber)
            .MaximumLength(32)
            .Matches(@"^[\d\s\-()]{4,}$").When(c => !string.IsNullOrWhiteSpace(c.PhoneNumber))
            .WithMessage("That is not a valid phone number.");

        // A dialling code on its own says nothing, and a number without one cannot be called back.
        RuleFor(c => c.PhoneCountryCode)
            .NotEmpty().When(c => !string.IsNullOrWhiteSpace(c.PhoneNumber))
            .WithMessage("Choose the country code for that number.");

        RuleFor(c => c.Files)
            .Must(files => files.Count <= TicketFile.MaxFilesPerTicket)
            .WithMessage($"Attach at most {TicketFile.MaxFilesPerTicket} files.");
    }
}

public sealed class CreateTicketCommandHandler : IRequestHandler<CreateTicketCommand, TicketSubmittedDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ITicketNumberGenerator _numbers;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailTemplateRenderer _templates;
    private readonly IEmailSender _emailSender;
    private readonly IEmailRouting _emailRouting;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<CreateTicketCommandHandler> _logger;

    public CreateTicketCommandHandler(
        IApplicationDbContext db,
        ITicketNumberGenerator numbers,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        ICurrentUser currentUser,
        IEmailTemplateRenderer templates,
        IEmailSender emailSender,
        IEmailRouting emailRouting,
        IAuditLogger auditLogger,
        ILogger<CreateTicketCommandHandler> logger)
    {
        _db = db;
        _numbers = numbers;
        _storage = storage;
        _typeValidator = typeValidator;
        _currentUser = currentUser;
        _templates = templates;
        _emailSender = emailSender;
        _emailRouting = emailRouting;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<TicketSubmittedDto> Handle(
        CreateTicketCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Only an active category, and re-checked here rather than trusted from the dropdown: the
        // form is public, so the posted id is an assertion, not a fact.
        var category = await _db.TicketCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == request.TicketCategoryId && c.IsActive,
                cancellationToken)
            ?? throw new NotFoundException(nameof(TicketCategory), request.TicketCategoryId);

        var ticket = new Ticket
        {
            TicketNumber = await _numbers.GenerateUniqueAsync(cancellationToken),
            TicketCategoryId = category.Id,
            Status = TicketStatus.Pending,
            Name = request.Name.Trim(),
            Email = request.Email.Trim(),
            PhoneCountryCode = Trim(request.PhoneCountryCode),
            PhoneNumber = Trim(request.PhoneNumber),
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            LanguageCode = _currentUser.LanguageCode,
        };

        // A signed-in applicant's enquiry starts out already linked to their order. Taken from the
        // token rather than the form — this is the one link the sender cannot assert for themselves.
        if (_currentUser.OrderId is { } orderId)
        {
            var orderExists = await _db.Orders
                .AsNoTracking()
                .AnyAsync(order => order.Id == orderId, cancellationToken);

            if (orderExists)
            {
                ticket.OrderId = orderId;
            }
        }

        foreach (var upload in request.Files)
        {
            ticket.Files.Add(await StoreAsync(ticket, upload, cancellationToken));
        }

        _db.Tickets.Add(ticket);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Bytes are written before the row, so a rejected save must not leave them orphaned.
            foreach (var file in ticket.Files)
            {
                await _storage.DeleteAsync(file.StoragePath, cancellationToken);
            }

            throw;
        }

        await _auditLogger.LogAsync(
            "Ticket.Created",
            nameof(Ticket),
            ticket.Id,
            new
            {
                ticket.TicketNumber,
                Category = category.NameEn,
                ticket.Email,
                Files = ticket.Files.Count,
                LinkedOrder = ticket.OrderId,
            },
            cancellationToken);

        await SendReceiptAsync(ticket, category, cancellationToken);

        _logger.LogInformation("Ticket {TicketNumber} raised from the contact form.", ticket.TicketNumber);

        return new TicketSubmittedDto(ticket.TicketNumber, ticket.Email);
    }

    /// <summary>
    /// Confirms receipt to the sender. The ticket is already committed, so a delivery failure must
    /// not undo it — the sender can always be answered from the queue regardless.
    /// </summary>
    private async Task SendReceiptAsync(
        Ticket ticket,
        TicketCategory category,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = _templates.RenderTicketCreated(
                ticket.Email,
                ticket.TicketNumber,
                ticket.Name,
                category.ResolveName(ticket.LanguageCode),
                ticket.Subject,
                ticket.LanguageCode);

            message = await _emailRouting.ApplyAsync(EmailType.ContactUs, message, cancellationToken);
            await _emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ticket {TicketNumber} was raised but its receipt email could not be sent.",
                ticket.TicketNumber);
        }
    }

    private async Task<TicketFile> StoreAsync(
        Ticket ticket,
        TicketFileUpload upload,
        CancellationToken cancellationToken)
    {
        if (upload.SizeBytes > TicketFile.MaxFileSizeBytes)
        {
            throw new ConflictException(
                "file.too_large",
                $"Each file must be {TicketFile.MaxFileSizeBytes / (1024 * 1024)} MB or smaller.");
        }

        // An extension is not evidence of anything; the signature has to agree with it.
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
            FileName = Path.GetFileName(upload.FileName),
            ContentType = contentType,
            StoragePath = storagePath,
            SizeBytes = upload.SizeBytes,
            UploadedByName = ticket.Name,
        };
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
