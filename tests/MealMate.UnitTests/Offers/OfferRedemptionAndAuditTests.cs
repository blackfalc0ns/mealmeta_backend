using FluentAssertions;
using MealMate.Domain.Offers;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferRedemptionAndAuditTests
{
    [Fact]
    public void OfferRedemption_InitializesWithRequiredValues()
    {
        var offerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        var redemption = new OfferRedemption
        {
            OfferId = offerId,
            CustomerId = customerId,
            SubscriptionId = subscriptionId,
            DiscountAppliedAmount = new Money(5.000m, "KWD"),
            PlatformFundedAmount = new Money(3.000m, "KWD"),
            RestaurantFundedAmount = new Money(2.000m, "KWD"),
            RedeemedAtUtc = DateTime.UtcNow
        };

        redemption.OfferId.Should().Be(offerId);
        redemption.DiscountAppliedAmount.Amount.Should().Be(5.000m);
        redemption.PlatformFundedAmount.Amount.Should().Be(3.000m);
        redemption.RestaurantFundedAmount.Amount.Should().Be(2.000m);
    }

    [Fact]
    public void OfferAuditLog_StoresDiffsAndReasons()
    {
        var log = new OfferAuditLog
        {
            OfferId = Guid.NewGuid(),
            ActorId = "admin-1",
            Action = "OfferStatusChanged",
            BeforeStateJson = "{\"Status\":\"Draft\"}",
            AfterStateJson = "{\"Status\":\"Scheduled\"}",
            JustificationReason = "Approved by marketing head"
        };

        log.ActorId.Should().Be("admin-1");
        log.JustificationReason.Should().Be("Approved by marketing head");
    }
}
