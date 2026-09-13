using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Orders.Commands;

/// <summary>
/// Locks the order's verification country and wallet currency, and records the person to contact
/// about it. Runs once — the wallet's ledger would otherwise mix denominations.
/// </summary>
public sealed record SetupOrderCommand(
    Guid VerificationCountryId,
    Guid CurrencyId,
    string ContactPersonName,
    string ContactPersonPhoneCountry,
    string ContactPersonPhoneCode,
    string ContactPersonPhoneNumber)
    : IRequest<SetupOrderResult>;

public sealed record SetupOrderResult(
    Guid VerificationCountryId,
    string CountryName,
    Guid CurrencyId,
    string CurrencyCode,
    decimal WalletBalance,
    string ContactPersonName,
    string ContactPersonPhone);

public sealed class SetupOrderCommandValidator : AbstractValidator<SetupOrderCommand>
{
    public SetupOrderCommandValidator()
    {
        RuleFor(c => c.VerificationCountryId).NotEmpty();
        RuleFor(c => c.CurrencyId).NotEmpty();

        RuleFor(c => c.ContactPersonName)
            .NotEmpty()
            .MaximumLength(200);

        // The alpha-2 code the caller picked in the dial-code list; it selects the flag on the way
        // back out, so it has to be a well-formed code rather than free text.
        RuleFor(c => c.ContactPersonPhoneCountry)
            .NotEmpty()
            .Matches("^[A-Za-z]{2}$")
            .WithMessage("'Contact Person Phone Country' must be an ISO 3166-1 alpha-2 code.");

        RuleFor(c => c.ContactPersonPhoneCode)
            .NotEmpty()
            .Matches(@"^\+\d{1,6}$")
            .WithMessage("'Contact Person Phone Code' must be a dial prefix such as '+20'.");

        // Digits only: the dial prefix is carried separately, and a national number is at most 15
        // digits including it (ITU-T E.164).
        RuleFor(c => c.ContactPersonPhoneNumber)
            .NotEmpty()
            .Matches(@"^\d{4,15}$")
            .WithMessage("'Contact Person Phone Number' must be 4 to 15 digits, without the dial prefix.");
    }
}

public sealed class SetupOrderCommandHandler : IRequestHandler<SetupOrderCommand, SetupOrderResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public SetupOrderCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<SetupOrderResult> Handle(
        SetupOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var order = await _db.Orders
            .Include(o => o.Wallet)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        // Setup is immutable once applications exist, otherwise their prices would be denominated
        // in a currency the order no longer holds.
        var hasApplications = await _db.Applications
            .AnyAsync(a => a.OrderId == orderId, cancellationToken);

        if (hasApplications)
        {
            throw new ConflictException(
                "order.setup_locked",
                "The verification country and currency cannot change once an application exists.");
        }

        var country = await _db.Countries
            .Include(c => c.CountryCurrencies)
            .FirstOrDefaultAsync(c => c.Id == request.VerificationCountryId && c.IsActive, cancellationToken)
            ?? throw new NotFoundException("Country", request.VerificationCountryId);

        var currency = await _db.Currencies
            .FirstOrDefaultAsync(c => c.Id == request.CurrencyId && c.IsActive, cancellationToken)
            ?? throw new NotFoundException("Currency", request.CurrencyId);

        // Order.CompleteSetup enforces the country/currency pairing and opens the wallet.
        var wallet = order.CompleteSetup(
            country,
            currency,
            request.ContactPersonName,
            request.ContactPersonPhoneCountry,
            request.ContactPersonPhoneCode,
            request.ContactPersonPhoneNumber);
        _db.Wallets.Add(wallet);

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Order.SetupCompleted",
            "Order",
            order.Id,
            new
            {
                country.Code,
                Currency = currency.Code,
                ContactPerson = order.ContactPersonName,
                ContactPhone = order.ContactPersonPhone,
            },
            cancellationToken);

        return new SetupOrderResult(
            country.Id,
            country.ResolveName(_currentUser.LanguageCode),
            currency.Id,
            currency.Code,
            wallet.Balance,
            order.ContactPersonName ?? string.Empty,
            order.ContactPersonPhone ?? string.Empty);
    }
}
