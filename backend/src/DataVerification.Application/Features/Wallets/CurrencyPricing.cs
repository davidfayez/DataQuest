using DataVerification.Application.Common.Exceptions;
using DataVerification.Domain.Entities;

namespace DataVerification.Application.Features.Wallets;

/// <summary>
/// Prices applications in a currency other than the one they were created in, from the service
/// types' own prices in that currency — never by converting. A service not sold in the currency
/// (no price, or its price switched off) cannot be paid for in it.
/// </summary>
internal static class CurrencyPricing
{
    /// <summary>
    /// What the application would cost in <paramref name="currencyId"/>, or null when one of its
    /// services is not sold in that currency. Its own currency answers with its current total.
    /// Needs the lines loaded with their service types and prices.
    /// </summary>
    public static decimal? TotalIn(VerificationApplication application, Guid currencyId, Guid mainCurrencyId)
    {
        ArgumentNullException.ThrowIfNull(application);

        if ((application.CurrencyId ?? mainCurrencyId) == currencyId)
        {
            return application.TotalCost;
        }

        decimal total = 0m;
        foreach (var line in application.Services)
        {
            var serviceType = line.ServiceType;
            var price = serviceType?.FindActiveCost(currencyId);
            if (serviceType is null || price is null) return null;

            total += serviceType.CalculateLineTotal(line.Quantity, line.IsExpress, price.Cost, price.ExpressCost);
        }

        return total;
    }

    /// <summary>
    /// Prices every line again in <paramref name="currencyId"/> and moves the application to it.
    /// Refuses, naming the application, when a service is not sold in that currency.
    /// </summary>
    public static void Reprice(VerificationApplication application, Guid currencyId, string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var line in application.Services)
        {
            var serviceType = line.ServiceType;
            var price = serviceType?.FindActiveCost(currencyId);
            if (serviceType is null || price is null)
            {
                throw new ConflictException(
                    "payment.not_priced_in_currency",
                    $"Application {application.ApplicationNumber} cannot be paid in {currencyCode}: "
                    + $"'{serviceType?.NameEn ?? "a service"}' is not sold in that currency.");
            }

            line.PriceFrom(serviceType, price);
        }

        application.RecalculateTotal();
        application.CurrencyId = currencyId;
    }
}
