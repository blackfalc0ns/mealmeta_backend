using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferRedemptionsQueryTests
{
    private readonly AppDbContext _context;

    public OfferRedemptionsQueryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task GetOfferRedemptionsQuery_ReturnsSummaryAndPagedItems()
    {
        var offer = new Offer
        {
            NameAr = "عرض ترويجي",
            NameEn = "Promo Offer",
            Status = OfferStatus.Active,
            Type = OfferType.StandardDiscount,
            DiscountType = DiscountType.Percentage,
            FundingMode = OfferFundingMode.Shared,
            DiscountRate = Percentage.FromFraction(0.20m),
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(10)
        };
        _context.Offers.Add(offer);

        var r1 = new OfferRedemption
        {
            OfferId = offer.Id,
            CustomerId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(),
            DiscountAppliedAmount = new Money(10.000m, "KWD"),
            PlatformFundedAmount = new Money(6.000m, "KWD"),
            RestaurantFundedAmount = new Money(4.000m, "KWD"),
            RedeemedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var r2 = new OfferRedemption
        {
            OfferId = offer.Id,
            CustomerId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(),
            DiscountAppliedAmount = new Money(15.000m, "KWD"),
            PlatformFundedAmount = new Money(9.000m, "KWD"),
            RestaurantFundedAmount = new Money(6.000m, "KWD"),
            RedeemedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        _context.OfferRedemptions.AddRange(r1, r2);
        await _context.SaveChangesAsync();

        var handler = new GetOfferRedemptionsQueryHandler(_context);

        var result = await handler.Handle(new GetOfferRedemptionsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OfferId.Should().Be(offer.Id);
        result.Value.TotalRedemptionsCount.Should().Be(2);
        result.Value.TotalDiscountAppliedKwd.Should().Be(25.000m);
        result.Value.TotalPlatformFundedKwd.Should().Be(15.000m);
        result.Value.TotalRestaurantFundedKwd.Should().Be(10.000m);
        result.Value.Redemptions.Items.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetOfferRedemptionsQuery_ReturnsNotFound_WhenOfferDoesNotExist()
    {
        var handler = new GetOfferRedemptionsQueryHandler(_context);

        var result = await handler.Handle(new GetOfferRedemptionsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }
}
