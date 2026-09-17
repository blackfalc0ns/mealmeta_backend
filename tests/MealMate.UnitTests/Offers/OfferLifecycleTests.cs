using FluentAssertions;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Offers.Events;
using MealMate.Domain.Offers.Exceptions;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferLifecycleTests
{
    [Fact]
    public void NewOffer_DefaultsToDraft()
    {
        var offer = new Offer();
        offer.Status.Should().Be(OfferStatus.Draft);
    }

    [Theory]
    [InlineData(OfferStatus.Scheduled, true)]
    [InlineData(OfferStatus.Cancelled, true)]
    [InlineData(OfferStatus.Active, false)]
    [InlineData(OfferStatus.Paused, false)]
    [InlineData(OfferStatus.Stopped, false)]
    [InlineData(OfferStatus.Ended, false)]
    public void DraftOffer_TransitionValidation(OfferStatus target, bool expectedSuccess)
    {
        var offer = new Offer();
        offer.CanTransitionTo(target).IsSuccess.Should().Be(expectedSuccess);
    }

    [Fact]
    public void TransitionTo_Throws_WhenInvalid()
    {
        var offer = new Offer();
        var act = () => offer.TransitionTo(OfferStatus.Active, "admin-user", "Direct launch");
        act.Should().Throw<InvalidOfferStateTransitionException>();
    }

    [Fact]
    public void ValidTransitions_UpdateStatus_AndEmitDomainEvent()
    {
        var offer = new Offer();
        offer.TransitionTo(OfferStatus.Scheduled, "admin-1", "Schedule launch");

        offer.Status.Should().Be(OfferStatus.Scheduled);
        offer.DomainEvents.Should().ContainSingle(e => e is OfferStatusChangedDomainEvent);

        var evt = (OfferStatusChangedDomainEvent)offer.DomainEvents.Single();
        evt.OldStatus.Should().Be(OfferStatus.Draft);
        evt.NewStatus.Should().Be(OfferStatus.Scheduled);
        evt.ActorId.Should().Be("admin-1");
        evt.Reason.Should().Be("Schedule launch");
    }

    [Fact]
    public void Stopped_IsTerminalForNewRedemptions_CanOnlyTransitionToEnded()
    {
        var offer = new Offer();
        offer.TransitionTo(OfferStatus.Scheduled, "admin", "Scheduled");
        offer.TransitionTo(OfferStatus.Active, "admin", "Live");
        offer.TransitionTo(OfferStatus.Stopped, "admin", "Halted");

        offer.CanTransitionTo(OfferStatus.Active).IsSuccess.Should().BeFalse();
        offer.CanTransitionTo(OfferStatus.Cancelled).IsSuccess.Should().BeFalse();
        offer.CanTransitionTo(OfferStatus.Ended).IsSuccess.Should().BeTrue();
    }
}
