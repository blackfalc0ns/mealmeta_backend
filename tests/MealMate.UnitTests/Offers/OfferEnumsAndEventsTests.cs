using FluentAssertions;
using MealMate.Domain.Common;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Offers.Events;
using MealMate.Domain.Offers.Exceptions;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferEnumsAndEventsTests
{
    [Fact]
    public void OfferEnums_ContainExpectedValues()
    {
        ((int)OfferStatus.Draft).Should().Be(1);
        ((int)OfferStatus.Active).Should().Be(3);
        ((int)OfferStatus.Stopped).Should().Be(5);
        ((int)OfferStatus.Ended).Should().Be(6);
        ((int)OfferStatus.Cancelled).Should().Be(7);

        ((int)OfferFundingMode.Shared).Should().Be(3);
    }

    [Fact]
    public void OfferStatusChangedDomainEvent_SetsPropertiesCorrectly()
    {
        var offerId = Guid.NewGuid();
        var evt = new OfferStatusChangedDomainEvent(offerId, OfferStatus.Draft, OfferStatus.Scheduled, "admin-1", "Approved");

        evt.OfferId.Should().Be(offerId);
        evt.OldStatus.Should().Be(OfferStatus.Draft);
        evt.NewStatus.Should().Be(OfferStatus.Scheduled);
        evt.ActorId.Should().Be("admin-1");
        evt.Reason.Should().Be("Approved");
        evt.Should().BeAssignableTo<DomainEvent>();
    }

    [Fact]
    public void InvalidOfferStateTransitionException_HasDescriptiveMessage()
    {
        var ex = new InvalidOfferStateTransitionException(OfferStatus.Draft, OfferStatus.Active);
        ex.Message.Should().Contain("Draft").And.Contain("Active");
    }
}
