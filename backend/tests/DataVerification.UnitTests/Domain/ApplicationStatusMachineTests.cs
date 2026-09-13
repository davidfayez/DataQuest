using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// Covers the lifecycle rules the whole product depends on: what may follow what, and which
/// operations stay available once money has changed hands.
/// </summary>
public class ApplicationStatusMachineTests
{
    private static readonly Actor Applicant = Actor.Applicant(Guid.NewGuid(), "Applicant");
    private static readonly Actor Admin = Actor.Admin(Guid.NewGuid(), "Reviewer");

    /// <summary>
    /// A complete application. These tests are about the status machine, so the application is
    /// built ready to submit — <see cref="VerificationApplication.EnsureReadyToSubmit"/> has its
    /// own coverage, and an incomplete draft would fail here for an unrelated reason.
    /// </summary>
    private static VerificationApplication NewApplication()
    {
        var application = new VerificationApplication
        {
            OrderId = Guid.NewGuid(),
            ApplicationNumber = "APP-0001",
            AddressedTo = "الطلب موجه إلي",
            BirthDate = new DateOnly(1990, 5, 17),
            ApplicantEmail = "ahmed@example.com",
            ApplicantPhoneCountry = "EG",
            ApplicantPhoneCode = "+20",
            ApplicantPhoneNumber = "1005550101",
            TransactionTypeId = Guid.NewGuid(),
            SubTransactionTypeId = Guid.NewGuid(),
            VerificationAuthorityId = Guid.NewGuid(),
        };

        application.Names.Add(new ApplicationName
        {
            LanguageType = NameLanguageType.Arabic,
            FirstName = "أحمد",
            LastName = "علي",
        });
        application.Names.Add(new ApplicationName
        {
            LanguageType = NameLanguageType.English,
            FirstName = "Ahmed",
            LastName = "Ali",
        });
        application.Services.Add(new ApplicationService
        {
            ServiceTypeId = Guid.NewGuid(),
            Quantity = 1,
            LanguageCode = "en",
        });

        return application;
    }

    /// <summary>Drives an application to a target status through legal transitions only.</summary>
    private static VerificationApplication ApplicationAt(ApplicationStatus status)
    {
        var application = NewApplication();
        if (status == ApplicationStatus.Draft) return application;

        application.Submit(Applicant);
        if (status == ApplicationStatus.PendingPayment) return application;

        application.MarkPaid(Applicant, DateTime.UtcNow);
        if (status == ApplicationStatus.Pending) return application;

        if (status == ApplicationStatus.Refunded)
        {
            application.Refund(Applicant);
            return application;
        }

        application.TransitionTo(ApplicationStatus.InProgress, Admin);
        if (status == ApplicationStatus.InProgress) return application;

        if (status == ApplicationStatus.MissedInfo)
        {
            application.TransitionTo(ApplicationStatus.MissedInfo, Admin);
            return application;
        }

        application.TransitionTo(status, Admin);
        return application;
    }

    [Fact]
    public void NewApplication_StartsAsDraft()
    {
        NewApplication().Status.Should().Be(ApplicationStatus.Draft);
    }

    [Fact]
    public void HappyPath_RunsDraftToSuccess()
    {
        var application = NewApplication();

        application.Submit(Applicant);
        application.Status.Should().Be(ApplicationStatus.PendingPayment);

        application.MarkPaid(Applicant, DateTime.UtcNow);
        application.Status.Should().Be(ApplicationStatus.Pending);
        application.PaidAtUtc.Should().NotBeNull();

        application.TransitionTo(ApplicationStatus.InProgress, Admin);
        application.TransitionTo(ApplicationStatus.MissedInfo, Admin);
        application.TransitionTo(ApplicationStatus.InProgress, Applicant);
        application.TransitionTo(ApplicationStatus.Success, Admin);

        application.Status.Should().Be(ApplicationStatus.Success);
    }

    [Fact]
    public void EveryTransition_AppendsHistoryWithActor()
    {
        var application = NewApplication();

        application.Submit(Applicant);
        application.MarkPaid(Applicant, DateTime.UtcNow);

        application.StatusHistory.Should().HaveCount(2);

        var latest = application.StatusHistory.Last();
        latest.FromStatus.Should().Be(ApplicationStatus.PendingPayment);
        latest.ToStatus.Should().Be(ApplicationStatus.Pending);
        latest.ChangedByType.Should().Be(ActorType.Applicant);
        latest.ChangedById.Should().Be(Applicant.Id);
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Pending)]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Success)]
    [InlineData(ApplicationStatus.PendingPayment, ApplicationStatus.InProgress)]
    [InlineData(ApplicationStatus.Pending, ApplicationStatus.Success)]
    [InlineData(ApplicationStatus.InProgress, ApplicationStatus.Refunded)]
    [InlineData(ApplicationStatus.MissedInfo, ApplicationStatus.Success)]
    [InlineData(ApplicationStatus.Success, ApplicationStatus.InProgress)]
    [InlineData(ApplicationStatus.Failed, ApplicationStatus.InProgress)]
    [InlineData(ApplicationStatus.Refunded, ApplicationStatus.Pending)]
    public void IllegalTransitions_AreRejected(ApplicationStatus from, ApplicationStatus to)
    {
        var application = ApplicationAt(from);

        var act = () => application.TransitionTo(to, Admin);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("application.illegal_status_transition");
    }

    [Fact]
    public void TransitionToSameStatus_IsRejected()
    {
        var application = ApplicationAt(ApplicationStatus.InProgress);

        var act = () => application.TransitionTo(ApplicationStatus.InProgress, Admin);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("application.status_unchanged");
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft, true)]
    [InlineData(ApplicationStatus.PendingPayment, true)]
    [InlineData(ApplicationStatus.Pending, false)]
    [InlineData(ApplicationStatus.InProgress, false)]
    [InlineData(ApplicationStatus.MissedInfo, false)]
    [InlineData(ApplicationStatus.Success, false)]
    [InlineData(ApplicationStatus.Failed, false)]
    [InlineData(ApplicationStatus.Refunded, false)]
    public void EditAndDelete_AreAllowedOnlyBeforePayment(ApplicationStatus status, bool expected)
    {
        var application = ApplicationAt(status);

        application.CanEdit().Should().Be(expected);
        application.CanDelete().Should().Be(expected);
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft, false)]
    [InlineData(ApplicationStatus.PendingPayment, false)]
    [InlineData(ApplicationStatus.Pending, true)]
    [InlineData(ApplicationStatus.InProgress, false)]
    [InlineData(ApplicationStatus.MissedInfo, false)]
    [InlineData(ApplicationStatus.Success, false)]
    [InlineData(ApplicationStatus.Failed, false)]
    [InlineData(ApplicationStatus.Refunded, false)]
    public void Refund_IsAllowedOnlyWhilePending(ApplicationStatus status, bool expected)
    {
        var application = ApplicationAt(status);

        application.CanRefund().Should().Be(expected);
    }

    [Fact]
    public void EnsureEditable_ExplainsWhyAPaidApplicationIsLocked()
    {
        var application = ApplicationAt(ApplicationStatus.InProgress);

        var act = application.EnsureEditable;

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("application.not_editable");
    }

    [Fact]
    public void MarkPaid_RequiresTheApplicationToBeAwaitingPayment()
    {
        var application = NewApplication();

        var act = () => application.MarkPaid(Applicant, DateTime.UtcNow);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("application.not_awaiting_payment");
    }

    [Fact]
    public void Refund_OnAnInProgressApplication_IsRejected()
    {
        var application = ApplicationAt(ApplicationStatus.InProgress);

        var act = () => application.Refund(Admin);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("application.not_refundable");
    }

    [Fact]
    public void SoftDelete_IsBlockedOncePaid()
    {
        var application = ApplicationAt(ApplicationStatus.Pending);

        var act = () => application.SoftDelete(DateTime.UtcNow);

        act.Should().Throw<DomainException>();
        application.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void RecalculateTotal_SumsTheServiceLines()
    {
        var application = NewApplication();
        application.Services.Add(new ApplicationService { LanguageCode = "en", LineTotal = 750m });
        application.Services.Add(new ApplicationService { LanguageCode = "ar", LineTotal = 2100m });

        application.RecalculateTotal().Should().Be(2850m);
        application.TotalCost.Should().Be(2850m);
    }
    [Fact]
    public void Submit_OnAPartiallyFilledDraft_IsRejected()
    {
        // A draft saved from the first wizard step carries only the addressee.
        var application = new VerificationApplication
        {
            OrderId = Guid.NewGuid(),
            ApplicationNumber = "APP-0002",
            AddressedTo = "Ministry of Education",
        };

        var act = () => application.Submit(Applicant);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("application.incomplete");
        application.Status.Should().Be(ApplicationStatus.Draft);
    }

    [Fact]
    public void FindMissingRequiredFields_NamesEveryStepStillOutstanding()
    {
        var application = new VerificationApplication
        {
            OrderId = Guid.NewGuid(),
            ApplicationNumber = "APP-0003",
            AddressedTo = "Ministry of Education",
        };

        application.FindMissingRequiredFields().Should().BeEquivalentTo(
        [
            nameof(VerificationApplication.BirthDate),
            nameof(VerificationApplication.ApplicantEmail),
            nameof(VerificationApplication.ApplicantPhone),
            nameof(VerificationApplication.Names),
            nameof(VerificationApplication.TransactionTypeId),
            nameof(VerificationApplication.SubTransactionTypeId),
            nameof(VerificationApplication.VerificationAuthorityId),
            nameof(VerificationApplication.Services),
        ]);
    }

    [Fact]
    public void FindMissingRequiredFields_IsEmptyOnceEveryStepIsAnswered()
    {
        NewApplication().FindMissingRequiredFields().Should().BeEmpty();
    }

    [Fact]
    public void ApplicantPhone_JoinsTheDialCodeToTheNationalNumber()
    {
        NewApplication().ApplicantPhone.Should().Be("+201005550101");
    }

    [Theory]
    // The applicant's own contact details are as mandatory at submit as the date of birth is.
    [InlineData(null, "1005550101", nameof(VerificationApplication.ApplicantEmail))]
    [InlineData("   ", "1005550101", nameof(VerificationApplication.ApplicantEmail))]
    [InlineData("ahmed@example.com", null, nameof(VerificationApplication.ApplicantPhone))]
    [InlineData("ahmed@example.com", "  ", nameof(VerificationApplication.ApplicantPhone))]
    public void ASubmitIsRefusedWhileTheApplicantsContactDetailsAreBlank(
        string? email,
        string? phoneNumber,
        string expectedMissing)
    {
        var application = NewApplication();
        application.ApplicantEmail = email;
        application.ApplicantPhoneNumber = phoneNumber;

        application.FindMissingRequiredFields().Should().Contain(expectedMissing);

        var act = () => application.Submit(Applicant);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("application.incomplete");
    }
}
