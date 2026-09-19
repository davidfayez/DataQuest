using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications.Commands;

/// <summary>Creates a Draft application. Prices and totals are always computed server-side.</summary>
public sealed record CreateApplicationCommand(
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

public sealed class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
{
    public CreateApplicationCommandValidator()
    {
        // A draft is saved step by step, so only what the applicant has actually reached is
        // checked here. Completeness is insisted on at submit, by the domain itself.
        RuleFor(c => c.AddressedTo).NotEmpty().MaximumLength(500);

        RuleFor(c => c.BirthDate)
            .Must(date => date!.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            .When(c => c.BirthDate.HasValue)
            .WithMessage("The date of birth must be in the past.");

        // Contact details are optional on a draft too, but anything actually entered has to be
        // well formed — the phone's prefix travels separately from its national part.
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

        // Services are priced against the chosen authority and sub-type, so they cannot be saved
        // before that part of the cascade exists.
        RuleFor(c => c.Services)
            .Must(_ => false)
            .When(c => c.Services.Count > 0
                       && (c.VerificationAuthorityId is null || c.SubTransactionTypeId is null))
            .WithMessage("Choose the verification cascade before adding services.");

        RuleForEach(c => c.Names).ChildRules(name =>
        {
            // First and last are mandatory in both scripts; the middle name is optional.
            name.RuleFor(n => n.FirstName).NotEmpty().MaximumLength(100);
            name.RuleFor(n => n.LastName).NotEmpty().MaximumLength(100);
            name.RuleFor(n => n.MiddleName).MaximumLength(100);
        });


        RuleForEach(c => c.Services).ChildRules(service =>
        {
            service.RuleFor(s => s.ServiceTypeId).NotEmpty();
            service.RuleFor(s => s.Quantity).GreaterThan(0);
            service.RuleFor(s => s.LanguageCode).NotEmpty().MaximumLength(10);
        });
    }
}

public sealed class CreateApplicationCommandHandler
    : IRequestHandler<CreateApplicationCommand, ApplicationDetailsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationWriteService _writeService;
    private readonly IApplicationNumberGenerator _numberGenerator;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public CreateApplicationCommandHandler(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        IApplicationNumberGenerator numberGenerator,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _writeService = writeService;
        _numberGenerator = numberGenerator;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<ApplicationDetailsDto> Handle(
        CreateApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        if (order.VerificationCountryId is null || order.CurrencyId is null)
        {
            throw new ConflictException(
                "order.setup_incomplete",
                "Choose a verification country and currency before creating an application.");
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

        var application = new VerificationApplication
        {
            OrderId = orderId,
            ApplicationNumber = await _numberGenerator.GenerateUniqueAsync(cancellationToken),
            AddressedTo = request.AddressedTo.Trim(),
            BirthDate = request.BirthDate,
            TransactionTypeId = request.TransactionTypeId,
            SubTransactionTypeId = request.SubTransactionTypeId,
            VerificationAuthorityId = request.VerificationAuthorityId,
            // Priced in the order's main currency; paying from another balance prices it again.
            CurrencyId = order.CurrencyId,
        };

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

        _db.Applications.Add(application);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.Created",
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
