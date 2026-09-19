using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Payments.Queries;

/// <summary>
/// The payment methods the calling applicant may actually use: offered in the country their order
/// was set up for, accepting the currency being added (the order's main currency when none is
/// named), and complete enough to pay through.
/// </summary>
public sealed record GetOrderPaymentMethodsQuery(Guid? CurrencyId = null)
    : IRequest<IReadOnlyList<PaymentMethodOptionDto>>;

public sealed class GetOrderPaymentMethodsQueryHandler
    : IRequestHandler<GetOrderPaymentMethodsQuery, IReadOnlyList<PaymentMethodOptionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetOrderPaymentMethodsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<PaymentMethodOptionDto>> Handle(
        GetOrderPaymentMethodsQuery request,
        CancellationToken cancellationToken)
    {
        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.VerificationCountryId, o.CurrencyId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        // Before setup there is no country and no wallet, so there is nothing to pay into yet.
        if (order.VerificationCountryId is not { } countryId || order.CurrencyId is not { } mainCurrencyId)
        {
            return [];
        }

        return await LoadUsableMethodsAsync(countryId, request.CurrencyId ?? mainCurrencyId, cancellationToken);
    }

    /// <summary>
    /// The one place the "may this order use this method" rule lives, so the picker and the command
    /// that accepts a deposit cannot drift apart.
    /// </summary>
    internal static async Task<IReadOnlyList<PaymentMethodOptionDto>> LoadUsableMethodsAsync(
        IApplicationDbContext db,
        string? languageCode,
        Guid countryId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var methods = await db.PaymentMethods
            .AsNoTracking()
            .Include(method => method.Type)
            .Include(method => method.Accounts)
            .ThenInclude(account => account.Bank)
            // The documents the deposit form asks for, with their details and reference files.
            // Split, so the collections are not multiplied into one wide result set.
            .Include(method => method.RequiredFiles).ThenInclude(document => document.Fields)
                .ThenInclude(field => field.Options)
            .Include(method => method.RequiredFiles).ThenInclude(document => document.AllowedFileTypes)
            .Include(method => method.RequiredFiles).ThenInclude(document => document.Samples)
            .AsSplitQuery()
            .Where(method => method.IsActive
                && method.Type!.IsActive
                && method.CountryLinks.Any(link => link.CountryId == countryId)
                && method.CurrencyLinks.Any(link => link.CurrencyId == currencyId))
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.NameEn)
            .ToListAsync(cancellationToken);

        // IsUsable reads the account collection, so the half-configured check runs in memory
        // rather than as SQL.
        return methods
            .Where(method => method.Type is not null && method.IsUsable(method.Type))
            .Select(method => PaymentMethodOptionDto.From(method, languageCode))
            .ToList();
    }

    private Task<IReadOnlyList<PaymentMethodOptionDto>> LoadUsableMethodsAsync(
        Guid countryId,
        Guid currencyId,
        CancellationToken cancellationToken) =>
        LoadUsableMethodsAsync(_db, _currentUser.LanguageCode, countryId, currencyId, cancellationToken);
}
