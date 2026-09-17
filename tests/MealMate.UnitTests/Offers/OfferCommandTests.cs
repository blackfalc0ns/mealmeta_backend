using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferCommandTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task CreateOfferDraftCommandHandler_CreatesValidDraft()
    {
        using var db = CreateDbContext();
        var handler = new CreateOfferDraftCommandHandler(db);

        var command = new CreateOfferDraftCommand(
            NameAr: "عرض تجريبي",
            NameEn: "Promo Offer",
            Type: OfferType.StandardDiscount,
            DiscountType: DiscountType.Percentage,
            FundingMode: OfferFundingMode.PlatformFull,
            DiscountRate: 0.20m,
            DiscountAmount: null,
            PlatformShareRate: 0.20m,
            RestaurantShareRate: 0m,
            StartsAtUtc: DateTime.UtcNow.AddDays(1),
            EndsAtUtc: DateTime.UtcNow.AddDays(10));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Status.Should().Be(OfferStatus.Draft);
        result.Value.NameEn.Should().Be("Promo Offer");
    }

    [Fact]
    public async Task TransitionOfferStatusCommandHandler_TransitionsAndLogsAudit()
    {
        using var db = CreateDbContext();
        var offer = new Offer
        {
            NameAr = "عرض تجريبي",
            NameEn = "Test Offer",
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(1),
            EndsAtUtc = DateTime.UtcNow.AddDays(10)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var handler = new TransitionOfferStatusCommandHandler(db);
        var command = new TransitionOfferStatusCommand(offer.Id, OfferStatus.Scheduled, "admin-1", "Approved for schedule");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(OfferStatus.Scheduled);

        var logs = await db.OfferAuditLogs.Where(l => l.OfferId == offer.Id).ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Action.Should().Be("OfferStatusChanged");
        logs[0].JustificationReason.Should().Be("Approved for schedule");
    }

    [Fact]
    public async Task RedeemOfferCommandHandler_AppliesDiscountAndUpdatesUsage()
    {
        using var db = CreateDbContext();
        var offer = new Offer
        {
            NameAr = "خصم 20%",
            NameEn = "20% Off",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            DiscountType = DiscountType.Percentage,
            DiscountRate = MealMate.Domain.ValueObjects.Percentage.FromFraction(0.20m),
            FundingMode = OfferFundingMode.PlatformFull,
            PerCustomerLimit = 1
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var engine = new OfferEligibilityEngine();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new RedeemOfferCommandHandler(db, engine, clock);
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        var command = new RedeemOfferCommand(
            OfferId: offer.Id,
            CustomerId: customerId,
            SubscriptionId: subscriptionId,
            CartAmount: 100.000m,
            DurationDays: 26,
            DeliveryGovernorate: "Hawalli");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DiscountAppliedAmount.Should().Be(20.000m);

        offer.CurrentUsageCount.Should().Be(1);
        var redemptions = await db.OfferRedemptions.Where(r => r.OfferId == offer.Id).ToListAsync();
        redemptions.Should().ContainSingle();
    }
}
