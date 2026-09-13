using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// The wallet's two guarantees: the balance never goes negative, and it always reconciles with
/// the ledger — the ledger being the record an audit would actually be run against.
/// </summary>
public class WalletLedgerTests
{
    private static readonly Actor Applicant = Actor.Applicant(Guid.NewGuid(), "Applicant");
    private static readonly Actor Admin = Actor.Admin(Guid.NewGuid(), "Finance");

    private static Wallet NewWallet() => new()
    {
        OrderId = Guid.NewGuid(),
        CurrencyId = Guid.NewGuid(),
    };

    [Fact]
    public void NewWallet_StartsEmpty()
    {
        var wallet = NewWallet();

        wallet.Balance.Should().Be(0m);
        wallet.LedgerBalance.Should().Be(0m);
    }

    [Fact]
    public void Credit_IncreasesBalanceAndRecordsTheEntry()
    {
        var wallet = NewWallet();

        var transaction = wallet.Credit(1000m, WalletTransactionType.TopUp, Admin);

        wallet.Balance.Should().Be(1000m);
        transaction.Type.Should().Be(WalletTransactionType.TopUp);
        transaction.Amount.Should().Be(1000m);
        transaction.BalanceAfter.Should().Be(1000m);
        transaction.PerformedByType.Should().Be(ActorType.Admin);
    }

    [Fact]
    public void Debit_DecreasesBalanceAndReferencesThePaidApplications()
    {
        var wallet = NewWallet();
        wallet.Credit(3000m, WalletTransactionType.TopUp, Admin);
        var paidApplications = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var transaction = wallet.Debit(2250m, Applicant, paidApplications);

        wallet.Balance.Should().Be(750m);
        transaction.Type.Should().Be(WalletTransactionType.Payment);
        transaction.BalanceAfter.Should().Be(750m);
        transaction.ReferenceApplicationIds.Should().BeEquivalentTo(paidApplications);
    }

    [Fact]
    public void Debit_BeyondTheBalance_IsRejectedAndLeavesTheWalletUntouched()
    {
        var wallet = NewWallet();
        wallet.Credit(500m, WalletTransactionType.TopUp, Admin);

        var act = () => wallet.Debit(500.01m, Applicant);

        act.Should().Throw<InsufficientFundsException>()
            .Which.Should().Match<InsufficientFundsException>(e =>
                e.Required == 500.01m && e.Available == 500m);

        wallet.Balance.Should().Be(500m);
        wallet.Transactions.Should().HaveCount(1);
    }

    [Fact]
    public void Refund_ReturnsTheMoneyAndLeavesAnAuditableEntry()
    {
        var wallet = NewWallet();
        wallet.Credit(1000m, WalletTransactionType.TopUp, Admin);
        var applicationId = Guid.NewGuid();
        wallet.Debit(750m, Applicant, [applicationId]);

        var refund = wallet.Credit(
            750m,
            WalletTransactionType.Refund,
            Admin,
            [applicationId],
            "Refunded before review started");

        wallet.Balance.Should().Be(1000m);
        refund.Type.Should().Be(WalletTransactionType.Refund);
        refund.ReferenceApplicationIds.Should().ContainSingle().Which.Should().Be(applicationId);
        refund.Note.Should().Be("Refunded before review started");
    }

    [Fact]
    public void LedgerAlwaysReconcilesWithTheBalance()
    {
        var wallet = NewWallet();

        wallet.Credit(5000m, WalletTransactionType.TopUp, Admin);
        wallet.Debit(1200m, Applicant, [Guid.NewGuid()]);
        wallet.Debit(800m, Applicant, [Guid.NewGuid(), Guid.NewGuid()]);
        wallet.Credit(1200m, WalletTransactionType.Refund, Admin, [Guid.NewGuid()]);
        wallet.Credit(250m, WalletTransactionType.TopUp, Admin);

        wallet.Transactions.Should().HaveCount(5);
        wallet.LedgerBalance.Should().Be(wallet.Balance);
        wallet.Balance.Should().Be(4450m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveAmounts_AreRejected(decimal amount)
    {
        var wallet = NewWallet();

        var credit = () => wallet.Credit(amount, WalletTransactionType.TopUp, Admin);
        var debit = () => wallet.Debit(amount, Applicant);

        credit.Should().Throw<DomainException>().Which.Code.Should().Be("wallet.invalid_amount");
        debit.Should().Throw<DomainException>().Which.Code.Should().Be("wallet.invalid_amount");
    }

    [Theory]
    [InlineData(WalletTransactionType.Payment)]
    [InlineData(WalletTransactionType.Withdrawal)]
    public void Credit_RefusesAnyEntryThatTakesMoneyOut(WalletTransactionType type)
    {
        var wallet = NewWallet();

        var act = () => wallet.Credit(100m, type, Admin);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("wallet.invalid_transaction_type");
    }

    [Theory]
    [InlineData(WalletTransactionType.TopUp)]
    [InlineData(WalletTransactionType.Refund)]
    [InlineData(WalletTransactionType.WithdrawalReversal)]
    public void Debit_RefusesAnyEntryThatPutsMoneyIn(WalletTransactionType type)
    {
        var wallet = NewWallet();
        wallet.Credit(500m, WalletTransactionType.TopUp, Admin);

        var act = () => wallet.Debit(100m, Applicant, null, null, type);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("wallet.invalid_transaction_type");
    }

    [Fact]
    public void WithdrawalHold_TakesTheMoneyOutOfTheSpendableBalance()
    {
        var wallet = NewWallet();
        wallet.Credit(1000m, WalletTransactionType.TopUp, Admin);

        var hold = wallet.Debit(
            400m,
            Applicant,
            null,
            "Withdrawal requested",
            WalletTransactionType.Withdrawal);

        wallet.Balance.Should().Be(600m);
        hold.Type.Should().Be(WalletTransactionType.Withdrawal);
        hold.BalanceAfter.Should().Be(600m);

        // The whole point of holding: the held money cannot also be spent on applications.
        var overspend = () => wallet.Debit(700m, Applicant, [Guid.NewGuid()]);
        overspend.Should().Throw<InsufficientFundsException>();
    }

    [Fact]
    public void RejectedWithdrawal_ReturnsTheHeldFundsAndReconciles()
    {
        var wallet = NewWallet();
        wallet.Credit(1000m, WalletTransactionType.TopUp, Admin);
        wallet.Debit(400m, Applicant, null, null, WalletTransactionType.Withdrawal);

        var reversal = wallet.Credit(
            400m,
            WalletTransactionType.WithdrawalReversal,
            Admin,
            null,
            "Withdrawal request rejected");

        wallet.Balance.Should().Be(1000m);
        wallet.LedgerBalance.Should().Be(wallet.Balance);
        reversal.Type.Should().Be(WalletTransactionType.WithdrawalReversal);
    }
}
