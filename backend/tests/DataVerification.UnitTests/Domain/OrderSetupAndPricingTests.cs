using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using FluentAssertions;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// Order setup locks the wallet currency, and service pricing is always derived from the stored
/// configuration — the two rules that stop a client from choosing what it pays.
/// </summary>
public class OrderSetupAndPricingTests
{
    private static (Country Country, Currency Egp, Currency Usd) BuildEgypt()
    {
        var egp = new Currency { Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", Symbol = "ج.م" };
        var usd = new Currency { Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", Symbol = "$" };
        var country = new Country { Code = "EG", NameAr = "مصر", NameEn = "Egypt" };

        country.CountryCurrencies.Add(new CountryCurrency { CountryId = country.Id, CurrencyId = egp.Id });

        return (country, egp, usd);
    }

    private static Order NewOrder() => new()
    {
        Email = "applicant@example.com",
        OrderNumber = "ABCDEFGH2345",
        PasswordHash = "hash",
    };

    /// <summary>Setup also captures the contact person; these are the values every test passes.</summary>
    private static Wallet CompleteSetup(Order order, Country country, Currency currency) =>
        order.CompleteSetup(country, currency, "  Mona Fahmy ", "eg", "+20", " 1001234567 ");

    [Fact]
    public void CompleteSetup_LocksTheCountryCurrencyAndOpensAWallet()
    {
        var (country, egp, _) = BuildEgypt();
        var order = NewOrder();

        var wallet = CompleteSetup(order, country, egp);

        order.IsSetupComplete.Should().BeTrue();
        order.VerificationCountryId.Should().Be(country.Id);
        order.CurrencyId.Should().Be(egp.Id);
        wallet.CurrencyId.Should().Be(egp.Id);
        wallet.Balance.Should().Be(0m);
    }

    [Fact]
    public void CompleteSetup_StoresTheContactPersonTrimmedAndJoinsThePhoneForDisplay()
    {
        var (country, egp, _) = BuildEgypt();
        var order = NewOrder();

        CompleteSetup(order, country, egp);

        order.ContactPersonName.Should().Be("Mona Fahmy");
        // The alpha-2 code is normalised, so the flag lookup never depends on how it was typed.
        order.ContactPersonPhoneCountry.Should().Be("EG");
        order.ContactPersonPhoneCode.Should().Be("+20");
        order.ContactPersonPhoneNumber.Should().Be("1001234567");
        order.ContactPersonPhone.Should().Be("+201001234567");
    }

    [Fact]
    public void ContactPersonPhone_IsNullBeforeSetupHasRun()
    {
        NewOrder().ContactPersonPhone.Should().BeNull();
    }

    [Fact]
    public void CompleteSetup_RejectsACurrencyTheCountryDoesNotOffer()
    {
        var (country, _, usd) = BuildEgypt();
        var order = NewOrder();

        var act = () => CompleteSetup(order, country, usd);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("order.currency_not_available_in_country");
        order.IsSetupComplete.Should().BeFalse();
    }

    [Fact]
    public void CompleteSetup_RunsOnlyOnce()
    {
        var (country, egp, _) = BuildEgypt();
        var order = NewOrder();
        CompleteSetup(order, country, egp);

        var act = () => CompleteSetup(order, country, egp);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("order.setup_already_completed");
    }

    [Fact]
    public void FailedLogins_LockTheOrderOnceTheThresholdIsReached()
    {
        var order = NewOrder();
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            order.RegisterFailedLogin(now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        order.IsLockedOut(now).Should().BeFalse();

        order.RegisterFailedLogin(now, maxAttempts: 5, TimeSpan.FromMinutes(15));

        order.IsLockedOut(now).Should().BeTrue();
        order.IsLockedOut(now.AddMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void SuccessfulLogin_ClearsTheLockout()
    {
        var order = NewOrder();
        var now = DateTime.UtcNow;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            order.RegisterFailedLogin(now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        order.RegisterSuccessfulLogin(now);

        order.IsLockedOut(now).Should().BeFalse();
        order.FailedLoginAttempts.Should().Be(0);
        order.LastLoginAtUtc.Should().Be(now);
    }

    private static ServiceType NewServiceType(bool enableExpress) => new()
    {
        NameAr = "التحقق مع التصديق",
        NameEn = "Attested Verification",
        Cost = 1500m,
        EnableExpress = enableExpress,
        ExpressCost = 600m,
    };

    /// <summary>The per-currency price row that authoritative pricing is now derived from.</summary>
    private static ServiceTypeCost NewCost() => new() { Cost = 1500m, ExpressCost = 600m };

    [Fact]
    public void LineTotal_MultipliesTheBaseCostByQuantity()
    {
        NewServiceType(enableExpress: false)
            .CalculateLineTotal(quantity: 3, isExpress: false, unitCost: 1500m, expressCost: 600m)
            .Should().Be(4500m);
    }

    [Fact]
    public void LineTotal_AddsTheExpressSurchargePerUnit()
    {
        NewServiceType(enableExpress: true)
            .CalculateLineTotal(quantity: 2, isExpress: true, unitCost: 1500m, expressCost: 600m)
            .Should().Be(4200m);
    }

    [Fact]
    public void Express_OnAServiceThatDoesNotOfferIt_IsRejected()
    {
        var act = () => NewServiceType(enableExpress: false)
            .CalculateLineTotal(1, isExpress: true, unitCost: 1500m, expressCost: 600m);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("service.express_not_available");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void NonPositiveQuantities_AreRejected(int quantity)
    {
        var act = () => NewServiceType(enableExpress: true)
            .CalculateLineTotal(quantity, isExpress: false, unitCost: 1500m, expressCost: 600m);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("service.invalid_quantity");
    }

    [Fact]
    public void PriceFrom_SnapshotsTheCostsOntoTheLine()
    {
        var serviceType = NewServiceType(enableExpress: true);
        var line = new ApplicationService { LanguageCode = "ar", Quantity = 2, IsExpress = true };

        line.PriceFrom(serviceType, NewCost());

        line.UnitCost.Should().Be(1500m);
        line.ExpressCost.Should().Be(600m);
        line.LineTotal.Should().Be(4200m);
    }

    [Fact]
    public void PriceFrom_ZeroesTheExpressCostWhenExpressWasNotRequested()
    {
        var line = new ApplicationService { LanguageCode = "en", Quantity = 1, IsExpress = false };

        line.PriceFrom(NewServiceType(enableExpress: true), NewCost());

        line.ExpressCost.Should().Be(0m);
        line.LineTotal.Should().Be(1500m);
    }

    [Fact]
    public void ApplicantsCannotAuthorInternalComments()
    {
        var act = () => ApplicationComment.FromApplicant(
            Guid.NewGuid(),
            Actor.Admin(Guid.NewGuid(), "Reviewer"),
            "should not be possible");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("comment.invalid_author");
    }
}
