using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// A ticket is a conversation, and these are the rules about who may add to it and what may be
/// sent onward. Both sides write here now, so "public" no longer implies "written by support".
/// </summary>
public sealed class TicketConversationTests
{
    private static Ticket Ticket(TicketStatus status = TicketStatus.Pending) => new()
    {
        TicketNumber = "TKT-2608-ABC123",
        Name = "Nadia Hassan",
        Email = "nadia@example.com",
        Subject = "A question",
        Description = "Something needs looking at.",
        Status = status,
    };

    private static TicketAction FromApplicant(string body = "One more detail.") => new()
    {
        TicketId = Guid.NewGuid(),
        AuthorType = ActorType.Applicant,
        Visibility = CommentVisibility.ForUser,
        Body = body,
    };

    [Theory]
    [InlineData(TicketStatus.Pending)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Answered)]
    public void AnOpenTicketAcceptsReplies(TicketStatus status)
    {
        // Answered included on purpose: an answer that did not help is exactly when somebody
        // needs to say so.
        Ticket(status).AcceptsReplies.Should().BeTrue();
    }

    [Fact]
    public void AClosedTicketDoesNot()
    {
        Ticket(TicketStatus.Closed).AcceptsReplies.Should().BeFalse();
    }

    [Fact]
    public void AnActionIsAttributedToSupportUnlessSaidOtherwise()
    {
        // Every action predating applicant replies was written by support, and rows written then
        // carry no author type of their own.
        var action = new TicketAction { TicketId = Guid.NewGuid() };

        action.AuthorType.Should().Be(ActorType.Admin);
        action.IsFromApplicant.Should().BeFalse();
    }

    [Fact]
    public void TheApplicantsOwnMessageIsNeverEmailedBackToThem()
    {
        // It is public, and it carries words — but mailing somebody their own message is not a
        // reply, so the notify path must refuse it.
        var action = FromApplicant();

        action.Visibility.Should().Be(CommentVisibility.ForUser);
        action.CanBeSentToSender.Should().BeFalse();
    }

    [Fact]
    public void RecordingTheApplicantsMessageAsSentIsRefused()
    {
        var action = FromApplicant();

        var recording = () => action.MarkNotified("nadia@example.com", DateTime.UtcNow);

        recording.Should().Throw<DataVerification.Domain.Common.DomainException>()
            .Which.Code.Should().Be("ticket_action.not_sendable");
    }

    [Fact]
    public void SupportsOwnReplyCanStillBeSent()
    {
        var action = new TicketAction
        {
            TicketId = Guid.NewGuid(),
            AuthorType = ActorType.Admin,
            Visibility = CommentVisibility.ForUser,
            Body = "Here is the answer.",
        };

        action.CanBeSentToSender.Should().BeTrue();
    }

    [Fact]
    public void AnInternalNoteIsStillUnsendableWhoeverWroteIt()
    {
        var action = new TicketAction
        {
            TicketId = Guid.NewGuid(),
            AuthorType = ActorType.Admin,
            Visibility = CommentVisibility.Internal,
            Body = "Checked storage.",
        };

        action.CanBeSentToSender.Should().BeFalse();
    }
}
