using FluentAssertions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Events;
using MealMate.Domain.CollaborativeCampaigns.Exceptions;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignLifecycleTests
{
    [Fact]
    public void NewCampaign_DefaultsTo_Draft()
    {
        var campaign = new Campaign();
        campaign.Status.Should().Be(CampaignStatus.Draft);
    }

    [Theory]
    [InlineData(CampaignStatus.OpenForJoining, true)]
    [InlineData(CampaignStatus.Cancelled, true)]
    [InlineData(CampaignStatus.Active, false)]
    [InlineData(CampaignStatus.Scheduled, false)]
    [InlineData(CampaignStatus.JoiningClosed, false)]
    [InlineData(CampaignStatus.UnderReview, false)]
    [InlineData(CampaignStatus.NeedsChanges, false)]
    [InlineData(CampaignStatus.Stopped, false)]
    [InlineData(CampaignStatus.Ended, false)]
    public void DraftCampaign_TransitionValidation(CampaignStatus target, bool shouldSucceed)
    {
        var campaign = new Campaign();
        campaign.CanTransitionTo(target).IsSuccess.Should().Be(shouldSucceed);
    }

    [Fact]
    public void TransitionTo_Throws_WhenInvalid()
    {
        var campaign = new Campaign();
        var act = () => campaign.TransitionTo(CampaignStatus.Active, "actor-1", "Direct activation");
        act.Should().Throw<InvalidCampaignStateTransitionException>()
            .WithMessage("*Draft*Active*");
    }

    [Fact]
    public void ValidTransitions_UpdateStatus_AndEmitDomainEvent()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor-admin", "Opened for restaurant enrollment");

        campaign.Status.Should().Be(CampaignStatus.OpenForJoining);
        campaign.DomainEvents.Should().ContainSingle(e => e is CampaignStatusChangedDomainEvent);

        var domainEvent = (CampaignStatusChangedDomainEvent)campaign.DomainEvents.Single();
        domainEvent.OldStatus.Should().Be(CampaignStatus.Draft);
        domainEvent.NewStatus.Should().Be(CampaignStatus.OpenForJoining);
        domainEvent.ActorId.Should().Be("actor-admin");
        domainEvent.Reason.Should().Be("Opened for restaurant enrollment");
    }

    [Fact]
    public void OpenForJoining_CanTransitionTo_JoiningClosedOrCancelled()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");

        campaign.CanTransitionTo(CampaignStatus.JoiningClosed).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Cancelled).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Active).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void JoiningClosed_CanTransitionTo_UnderReviewOrCancelled()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");
        campaign.TransitionTo(CampaignStatus.JoiningClosed, "actor", "Closed");

        campaign.CanTransitionTo(CampaignStatus.UnderReview).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Cancelled).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Scheduled).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void UnderReview_CanTransitionTo_NeedsChanges_Scheduled_OrCancelled()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");
        campaign.TransitionTo(CampaignStatus.JoiningClosed, "actor", "Closed");
        campaign.TransitionTo(CampaignStatus.UnderReview, "actor", "Reviewing");

        campaign.CanTransitionTo(CampaignStatus.NeedsChanges).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Scheduled).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Cancelled).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Active).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void NeedsChanges_CanTransitionBackTo_UnderReviewOrCancelled()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");
        campaign.TransitionTo(CampaignStatus.JoiningClosed, "actor", "Closed");
        campaign.TransitionTo(CampaignStatus.UnderReview, "actor", "Reviewing");
        campaign.TransitionTo(CampaignStatus.NeedsChanges, "actor", "Margin too low");

        campaign.CanTransitionTo(CampaignStatus.UnderReview).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Cancelled).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Scheduled).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Scheduled_CanTransitionTo_ActiveOrCancelled()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");
        campaign.TransitionTo(CampaignStatus.JoiningClosed, "actor", "Closed");
        campaign.TransitionTo(CampaignStatus.UnderReview, "actor", "Reviewing");
        campaign.TransitionTo(CampaignStatus.Scheduled, "actor", "Approved");

        campaign.CanTransitionTo(CampaignStatus.Active).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Cancelled).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Draft).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Active_CanTransitionTo_StoppedOrEnded_NeverCancelled()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");
        campaign.TransitionTo(CampaignStatus.JoiningClosed, "actor", "Closed");
        campaign.TransitionTo(CampaignStatus.UnderReview, "actor", "Reviewing");
        campaign.TransitionTo(CampaignStatus.Scheduled, "actor", "Approved");
        campaign.TransitionTo(CampaignStatus.Active, "actor", "Live");

        campaign.CanTransitionTo(CampaignStatus.Stopped).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Ended).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Cancelled).IsSuccess.Should().BeFalse();
        campaign.CanTransitionTo(CampaignStatus.Draft).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Stopped_IsTerminalForNewSales_CanOnlyTransitionTo_Ended()
    {
        var campaign = new Campaign();
        campaign.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");
        campaign.TransitionTo(CampaignStatus.JoiningClosed, "actor", "Closed");
        campaign.TransitionTo(CampaignStatus.UnderReview, "actor", "Reviewing");
        campaign.TransitionTo(CampaignStatus.Scheduled, "actor", "Approved");
        campaign.TransitionTo(CampaignStatus.Active, "actor", "Live");
        campaign.TransitionTo(CampaignStatus.Stopped, "actor", "Admin stopped");

        campaign.CanTransitionTo(CampaignStatus.Ended).IsSuccess.Should().BeTrue();
        campaign.CanTransitionTo(CampaignStatus.Active).IsSuccess.Should().BeFalse();
        campaign.CanTransitionTo(CampaignStatus.Cancelled).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void EndedAndCancelled_AreStrictTerminalStates()
    {
        var campaignEnded = new Campaign();
        campaignEnded.TransitionTo(CampaignStatus.OpenForJoining, "actor", "Open");
        campaignEnded.TransitionTo(CampaignStatus.JoiningClosed, "actor", "Closed");
        campaignEnded.TransitionTo(CampaignStatus.UnderReview, "actor", "Reviewing");
        campaignEnded.TransitionTo(CampaignStatus.Scheduled, "actor", "Approved");
        campaignEnded.TransitionTo(CampaignStatus.Active, "actor", "Live");
        campaignEnded.TransitionTo(CampaignStatus.Ended, "actor", "Customer sales window closed");

        campaignEnded.CanTransitionTo(CampaignStatus.Active).IsSuccess.Should().BeFalse();
        campaignEnded.CanTransitionTo(CampaignStatus.Stopped).IsSuccess.Should().BeFalse();
        campaignEnded.CanTransitionTo(CampaignStatus.Draft).IsSuccess.Should().BeFalse();

        var campaignCancelled = new Campaign();
        campaignCancelled.TransitionTo(CampaignStatus.Cancelled, "actor", "Aborted");

        campaignCancelled.CanTransitionTo(CampaignStatus.Draft).IsSuccess.Should().BeFalse();
        campaignCancelled.CanTransitionTo(CampaignStatus.OpenForJoining).IsSuccess.Should().BeFalse();
    }
}
