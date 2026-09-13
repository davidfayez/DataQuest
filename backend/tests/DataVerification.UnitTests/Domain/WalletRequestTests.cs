using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// A wallet request has exactly one decision in it. Everything money-related hangs off that
/// guarantee: an approval that ran twice would credit a wallet twice, and a cancel accepted after
/// a rejection would release held funds a second time.
/// </summary>
public class WalletRequestTests
{
    private static readonly Actor Applicant = Actor.Applicant(Guid.NewGuid(), "NEN408125993");
    private static readonly Actor Admin = Actor.Admin(Guid.NewGuid(), "Finance");

    private static WalletRequest NewRequest(WalletRequestType type = WalletRequestType.Deposit) => new()
    {
        OrderId = Guid.NewGuid(),
        WalletId = Guid.NewGuid(),
        Type = type,
        Amount = 250m,
    };

    [Fact]
    public void NewRequest_IsPendingAndCancellable()
    {
        var request = NewRequest();

        request.Status.Should().Be(WalletRequestStatus.Pending);
        request.IsPending.Should().BeTrue();
        request.CanCancel.Should().BeTrue();
        request.ReviewedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Approve_RecordsTheReviewerAndWhatTheyConfirmed()
    {
        var request = NewRequest();

        request.Approve(Admin, 90m, " REF-9 ", "Transfer confirmed");

        request.Status.Should().Be(WalletRequestStatus.Approved);
        request.ReviewerNote.Should().Be("Transfer confirmed");
        request.ReviewedByAdminUserId.Should().Be(Admin.Id);
        request.ReviewedByName.Should().Be("Finance");
        request.ReviewedAtUtc.Should().NotBeNull();
        request.CanCancel.Should().BeFalse();

        request.ConfirmedAmount.Should().Be(90m);
        // Trimmed, so a pasted reference matches the one typed by hand.
        request.ConfirmedReference.Should().Be("REF-9");
    }

    [Fact]
    public void Approve_KeepsTheClaimAndTheConfirmationApart()
    {
        // The applicant said 100; only 90 arrived. Both halves have to stay readable.
        var request = NewRequest();
        request.Amount = 100m;

        request.Approve(Admin, 90m, "REF-9");

        request.Amount.Should().Be(100m);
        request.ConfirmedAmount.Should().Be(90m);
        request.EffectiveAmount.Should().Be(90m, "the reviewer's figure is what moved");
    }

    [Fact]
    public void EffectiveAmount_FallsBackToTheClaimWhileUndecided()
    {
        var request = NewRequest();

        request.EffectiveAmount.Should().Be(request.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Approve_RefusesAnAmountThatIsNotPositive(decimal confirmed)
    {
        var request = NewRequest();

        var approve = () => request.Approve(Admin, confirmed, "REF-9");

        approve.Should().Throw<DomainException>()
            .Which.Code.Should().Be("wallet_request.invalid_confirmed_amount");
        request.Status.Should().Be(WalletRequestStatus.Pending, "a refused decision moves nothing");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Approve_RefusesABlankReference(string reference)
    {
        var request = NewRequest();

        var approve = () => request.Approve(Admin, 100m, reference);

        approve.Should().Throw<DomainException>()
            .Which.Code.Should().Be("wallet_request.confirmed_reference_required");
        request.Status.Should().Be(WalletRequestStatus.Pending);
    }

    [Fact]
    public void Reject_KeepsNoReference_SoTheReceiptStaysFree()
    {
        // A refused claim must not burn a reference the applicant will quote correctly next time.
        var request = NewRequest();

        request.Reject(Admin, 100m, "Not on the statement");

        request.Status.Should().Be(WalletRequestStatus.Rejected);
        request.ConfirmedAmount.Should().Be(100m, "what the reviewer looked at is still worth keeping");
        request.ConfirmedReference.Should().BeNull();
    }

    [Fact]
    public void Cancel_ByTheApplicant_LeavesTheReviewerBlank()
    {
        var request = NewRequest(WalletRequestType.Withdrawal);

        request.Cancel(Applicant);

        request.Status.Should().Be(WalletRequestStatus.Cancelled);
        request.ReviewedByAdminUserId.Should().BeNull();
        request.ReviewedByName.Should().BeNull();
        // Still stamped, because "when was this called off" is the question a ledger raises.
        request.ReviewedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void SecondDecision_IsRefusedSoMoneyCannotMoveTwice()
    {
        var request = NewRequest();
        request.Approve(Admin, 100m, "REF-1");

        var again = () => request.Approve(Admin, 100m, "REF-2");
        var reject = () => request.Reject(Admin, 100m, "changed my mind");
        var cancel = () => request.Cancel(Applicant);

        again.Should().Throw<DomainException>().Which.Code.Should().Be("wallet_request.not_pending");
        reject.Should().Throw<DomainException>().Which.Code.Should().Be("wallet_request.not_pending");
        cancel.Should().Throw<DomainException>().Which.Code.Should().Be("wallet_request.not_pending");

        request.Status.Should().Be(WalletRequestStatus.Approved);
    }

    [Fact]
    public void CancelledRequest_CannotBeApprovedLater()
    {
        var request = NewRequest(WalletRequestType.Withdrawal);
        request.Cancel(Applicant);

        var approve = () => request.Approve(Admin, 100m, "REF-1");

        approve.Should().Throw<DomainException>()
            .Which.Code.Should().Be("wallet_request.not_pending");
    }
}
