using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// The rules that decide what may leave the platform on a support ticket.
///
/// These matter more than most: a ticket thread mixes notes support write to each other with
/// answers meant for a member of the public, and the difference between them is one flag.
/// </summary>
public sealed class TicketActionTests
{
    private static TicketAction Action(
        CommentVisibility visibility = CommentVisibility.ForUser,
        string? body = "Here is the answer you asked for.") =>
        new() { TicketId = Guid.NewGuid(), Visibility = visibility, Body = body };

    [Fact]
    public void APublicReplyWithWordsCanBeSent()
    {
        Action().CanBeSentToSender.Should().BeTrue();
    }

    [Fact]
    public void AnInternalNoteCanNeverBeSent()
    {
        Action(CommentVisibility.Internal).CanBeSentToSender.Should().BeFalse();
    }

    [Fact]
    public void AnInternalNoteCannotBeMarkedAsSentEither()
    {
        // The guard is on the domain object, not only on the handler: no future code path gets to
        // record an internal note as delivered to the person outside.
        var action = Action(CommentVisibility.Internal);

        var recording = () => action.MarkNotified("someone@example.com", DateTime.UtcNow);

        recording.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ticket_action.not_sendable");
    }

    [Fact]
    public void AnEmptyActionHasNothingWorthSending()
    {
        // An email carrying neither words nor documents is noise arriving in somebody's inbox.
        Action(body: null).CanBeSentToSender.Should().BeFalse();
        Action(body: "   ").CanBeSentToSender.Should().BeFalse();
    }

    [Fact]
    public void AnActionCarryingOnlyDocumentsIsStillWorthSending()
    {
        var action = Action(body: null);
        action.Documents.Add(new TicketActionDocument
        {
            TicketActionId = action.Id,
            Title = "Your certificate",
        });

        action.CanBeSentToSender.Should().BeTrue();
    }

    [Fact]
    public void SendingIsRecordedAgainstTheAddressItReached()
    {
        var action = Action();
        var when = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

        action.MarkNotified("nadia@example.com", when);

        action.WasNotified.Should().BeTrue();
        action.NotifiedAtUtc.Should().Be(when);
        // Kept on the row so the trail survives the sender later changing their address.
        action.NotifiedEmail.Should().Be("nadia@example.com");
    }
}

public sealed class TicketLinkTests
{
    private static Ticket Ticket() => new()
    {
        TicketNumber = "TKT-2608-ABC123",
        Name = "Nadia Hassan",
        Email = "nadia@example.com",
        Subject = "A question",
        Description = "Something needs looking at.",
    };

    [Fact]
    public void ATicketOpensAtPendingWithNothingLinked()
    {
        var ticket = Ticket();

        ticket.Status.Should().Be(TicketStatus.Pending);
        ticket.OrderId.Should().BeNull();
        ticket.ApplicationId.Should().BeNull();
        ticket.AssignedToAdminUserId.Should().BeNull();
    }

    [Fact]
    public void LinkingSetsBothTheOrderAndItsApplication()
    {
        var ticket = Ticket();
        var orderId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        ticket.LinkTo(orderId, applicationId);

        ticket.OrderId.Should().Be(orderId);
        ticket.ApplicationId.Should().Be(applicationId);
    }

    [Fact]
    public void DroppingTheOrderDropsTheApplicationWithIt()
    {
        // An application without the order it belongs to is a link support cannot navigate.
        var ticket = Ticket();
        ticket.LinkTo(Guid.NewGuid(), Guid.NewGuid());

        ticket.LinkTo(null, null);

        ticket.OrderId.Should().BeNull();
        ticket.ApplicationId.Should().BeNull();
    }

    [Fact]
    public void AnApplicationCannotBeLinkedWithoutItsOrder()
    {
        var ticket = Ticket();

        var linking = () => ticket.LinkTo(null, Guid.NewGuid());

        linking.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ticket.application_without_order");
    }

    [Theory]
    [InlineData("+20", "1005551234", "+201005551234")]
    [InlineData(null, null, null)]
    [InlineData("+20", "  ", null)]
    public void TheFullNumberIsComposedFromItsParts(string? code, string? number, string? expected)
    {
        var ticket = Ticket();
        ticket.PhoneCountryCode = code;
        ticket.PhoneNumber = number;

        ticket.FullPhoneNumber.Should().Be(expected);
    }
}
