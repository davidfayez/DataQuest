using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications.Commands;

// ------------------------------------------------------------------- Update

/// <summary>Replaces an unpaid application's contents. Blocked server-side once paid.</summary>
public sealed record UpdateApplicationCommand(
    Guid ApplicationId,
    string AddressedTo,
    DateOnly? BirthDate,
    string? ApplicantEmail,
    string? ApplicantPhoneCountry,
    string? ApplicantPhoneCode,
    string? ApplicantPhoneNumber,
    IReadOnlyList<ApplicationNameInput> Names,
    Guid? TransactionTypeId,
    Guid? SubTransactionTypeId,
    Guid? VerificationAuthorityId,
    IReadOnlyList<ApplicationServiceInput> Services) : IRequest<ApplicationDetailsDto>;

public sealed class UpdateApplicationCommandValidator : AbstractValidator<UpdateApplicationCommand>
{
    public UpdateApplicationCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.AddressedTo).NotEmpty().MaximumLength(500);

        // A draft is saved step by step, so only what the applicant has reached is checked here.
        // Completeness is insisted on at submit, by the domain itself.
        RuleFor(c => c.BirthDate)
            .Must(date => date!.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            .When(c => c.BirthDate.HasValue)
            .WithMessage("The date of birth must be in the past.");

        // Optional on a draft, but anything actually entered has to be well formed — the phone's
        // prefix travels separately from its national part.
        RuleFor(c => c.ApplicantEmail)
            .EmailAddress()
            .MaximumLength(320)
            .When(c => !string.IsNullOrWhiteSpace(c.ApplicantEmail));

        RuleFor(c => c.ApplicantPhoneCountry)
            .Matches("^[A-Za-z]{2}$")
            .When(c => !string.IsNullOrWhiteSpace(c.ApplicantPhoneCountry))
            .WithMessage("'Applicant Phone Country' must be an ISO 3166-1 alpha-2 code.");

        RuleFor(c => c.ApplicantPhoneCode)
            .Matches(@"^\+\d{1,6}$")
            .When(c => !string.IsNullOrWhiteSpace(c.ApplicantPhoneCode))
            .WithMessage("'Applicant Phone Code' must be a dial prefix such as '+20'.");

        RuleFor(c => c.ApplicantPhoneNumber)
            .Matches(@"^\d{4,15}$")
            .WithMessage("'Applicant Phone Number' must be 4 to 15 digits, without the dial prefix.")
            .Must((command, _) => !string.IsNullOrWhiteSpace(command.ApplicantPhoneCountry)
                                  && !string.IsNullOrWhiteSpace(command.ApplicantPhoneCode))
            .WithMessage("A phone number must carry its country and dial code.")
            .When(c => !string.IsNullOrWhiteSpace(c.ApplicantPhoneNumber));

        RuleFor(c => c.Names)
            .Must(names => names.Any(n => n.LanguageType == NameLanguageType.Arabic))
            .When(c => c.Names.Count > 0)
            .WithMessage("An Arabic name is required.")
            .Must(names => names.Any(n => n.LanguageType == NameLanguageType.English))
            .When(c => c.Names.Count > 0)
            .WithMessage("An English name is required.");

        RuleForEach(c => c.Names).ChildRules(name =>
        {
            name.RuleFor(n => n.FirstName).NotEmpty().MaximumLength(100);
            name.RuleFor(n => n.LastName).NotEmpty().MaximumLength(100);
            name.RuleFor(n => n.MiddleName).MaximumLength(100);
        });


        // Services are priced against the chosen authority and sub-type, so they cannot be saved
        // before that part of the cascade exists.
        RuleFor(c => c.Services)
            .Must(_ => false)
            .When(c => c.Services.Count > 0
                       && (c.VerificationAuthorityId is null || c.SubTransactionTypeId is null))
            .WithMessage("Choose the verification cascade before adding services.");

        RuleForEach(c => c.Services).ChildRules(service =>
        {
            service.RuleFor(s => s.ServiceTypeId).NotEmpty();
            service.RuleFor(s => s.Quantity).GreaterThan(0);
            service.RuleFor(s => s.LanguageCode).NotEmpty().MaximumLength(10);
        });
    }
}

public sealed class UpdateApplicationCommandHandler
    : IRequestHandler<UpdateApplicationCommand, ApplicationDetailsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationWriteService _writeService;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public UpdateApplicationCommandHandler(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _writeService = writeService;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<ApplicationDetailsDto> Handle(
        UpdateApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var application = await _writeService.RequireOwnedAsync(
            request.ApplicationId,
            orderId,
            cancellationToken,
            includeChildren: true);

        // The rule lives in the domain; the handler only surfaces it. Throws
        // application.not_editable, which the API maps to 409.
        application.EnsureEditable();

        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.VerificationCountryId, o.CurrencyId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        if (order.VerificationCountryId is null || order.CurrencyId is null)
        {
            throw new ConflictException("order.setup_incomplete", "Order setup is not complete.");
        }

        // Only a completed cascade can be validated, and only then can services be priced. A draft
        // saved before that point simply stores what it has.
        // Treated as absent when empty as well as null: an omitted Guid binds to Guid.Empty on
        // some client shapes, and that is a missing step, not a real id.
        var hasCascade = request.TransactionTypeId is { } tx && tx != Guid.Empty
                         && request.SubTransactionTypeId is { } sub && sub != Guid.Empty
                         && request.VerificationAuthorityId is { } auth && auth != Guid.Empty;

        if (hasCascade)
        {
            await _writeService.ValidateCascadeAsync(
                order.VerificationCountryId.Value,
                request.TransactionTypeId!.Value,
                request.SubTransactionTypeId!.Value,
                request.VerificationAuthorityId!.Value,
                cancellationToken);
        }

        application.AddressedTo = request.AddressedTo.Trim();
        application.BirthDate = request.BirthDate;
        application.TransactionTypeId = request.TransactionTypeId;
        application.SubTransactionTypeId = request.SubTransactionTypeId;
        application.VerificationAuthorityId = request.VerificationAuthorityId;

        ApplicationWriteService.ApplyApplicantContact(
            application,
            request.ApplicantEmail,
            request.ApplicantPhoneCountry,
            request.ApplicantPhoneCode,
            request.ApplicantPhoneNumber);

        _writeService.ApplyNames(application, request.Names);

        if (hasCascade)
        {
            await _writeService.ApplyServicesAsync(
                application,
                request.Services,
                request.VerificationAuthorityId!.Value,
                request.SubTransactionTypeId!.Value,
                order.CurrencyId.Value,
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.Updated",
            "Application",
            application.Id,
            new { application.ApplicationNumber, application.TotalCost },
            cancellationToken);

        return await ApplicationDetailsProjection.LoadAsync(
            _db,
            _writeService,
            application.Id,
            orderId,
            _currentUser.LanguageCode,
            cancellationToken);
    }
}

// ------------------------------------------------------------------- Delete

public sealed record DeleteApplicationCommand(Guid ApplicationId) : IRequest;

public sealed class DeleteApplicationCommandHandler : IRequestHandler<DeleteApplicationCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationWriteService _writeService;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _auditLogger;

    public DeleteApplicationCommandHandler(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditLogger auditLogger)
    {
        _db = db;
        _writeService = writeService;
        _currentUser = currentUser;
        _clock = clock;
        _auditLogger = auditLogger;
    }

    public async Task Handle(DeleteApplicationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var application = await _writeService.RequireOwnedAsync(
            request.ApplicationId,
            orderId,
            cancellationToken);

        // Soft delete, so a removed draft still leaves an auditable trail.
        application.SoftDelete(_clock.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.Deleted",
            "Application",
            application.Id,
            new { application.ApplicationNumber },
            cancellationToken);
    }
}

// ------------------------------------------------------------------- Submit

/// <summary>Draft → PendingPayment, once every mandatory file is attached.</summary>
public sealed record SubmitApplicationCommand(Guid ApplicationId) : IRequest<ApplicationDetailsDto>;

public sealed class SubmitApplicationCommandHandler
    : IRequestHandler<SubmitApplicationCommand, ApplicationDetailsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationWriteService _writeService;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public SubmitApplicationCommandHandler(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _writeService = writeService;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<ApplicationDetailsDto> Handle(
        SubmitApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var application = await _writeService.RequireOwnedAsync(
            request.ApplicationId,
            orderId,
            cancellationToken,
            includeChildren: true);

        var requiredFiles = await _writeService.GetRequiredFileStatusAsync(
            application,
            _currentUser.LanguageCode,
            cancellationToken);

        if (!ApplicationDetailsProjection.AllMandatoryFilesPresent(requiredFiles))
        {
            var missing = requiredFiles
                .Where(f => f.IsMandatory && !f.IsSatisfied)
                .Select(f => f.Name)
                .Distinct();

            throw new ConflictException(
                "application.mandatory_files_missing",
                $"Upload every mandatory file before submitting: {string.Join(", ", missing)}.");
        }

        // A document's custom fields are part of the evidence, so an incomplete one blocks
        // submission exactly as a missing upload does.
        var incompleteFields = requiredFiles
            .Where(f => !f.AreFieldsComplete)
            .Select(f => f.Name)
            .Distinct()
            .ToList();

        if (incompleteFields.Count > 0)
        {
            throw new ConflictException(
                "application.document_fields_incomplete",
                $"Complete the details for: {string.Join(", ", incompleteFields)}.");
        }

        // Rejects anything that is not still a Draft, via the domain state machine.
        application.Submit(_currentUser.ToActor());

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.Submitted",
            "Application",
            application.Id,
            new { application.ApplicationNumber, application.TotalCost },
            cancellationToken);

        return await ApplicationDetailsProjection.LoadAsync(
            _db,
            _writeService,
            application.Id,
            orderId,
            _currentUser.LanguageCode,
            cancellationToken);
    }
}
