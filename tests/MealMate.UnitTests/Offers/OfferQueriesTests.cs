using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferQueriesTests
{
    [Fact]
    public void OfferDto_ExposesPersistedNavigationScope()
    {
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var offer = new Offer { NameAr = "عرض", NameEn = "Offer" };
        offer.EligiblePrograms.Add(new OfferProgram { ProgramId = programId });
        offer.EligibleBundles.Add(new OfferBundle { BundleId = bundleId });
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = restaurantId });
        var dto = MealMate.Application.Features.Offers.Commands.CreateOfferDraftCommandHandler.MapOfferDto(offer);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dto));
        json.RootElement.GetProperty("EligibleProgramIds")[0].GetGuid().Should().Be(programId);
        json.RootElement.GetProperty("EligibleBundleIds")[0].GetGuid().Should().Be(bundleId);
        json.RootElement.GetProperty("EligibleRestaurantIds")[0].GetGuid().Should().Be(restaurantId);
        json.RootElement.GetProperty("SourceType").GetString().Should().Be("Offer");
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task GetOfferByIdQuery_ReturnsOffer_WhenExists()
    {
        using var db = CreateDbContext();
        var offer = new Offer
        {
            NameAr = "عرض خاص",
            NameEn = "Special Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(10)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var handler = new GetOfferByIdQueryHandler(db);
        var result = await handler.Handle(new GetOfferByIdQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(offer.Id);
        result.Value.NameEn.Should().Be("Special Offer");
    }

    [Fact]
    public async Task GetOffersListQuery_ReturnsPagedResult_WithFiltering()
    {
        using var db = CreateDbContext();
        db.Offers.Add(new Offer
        {
            NameAr = "عرض 1",
            NameEn = "Active Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        });
        db.Offers.Add(new Offer
        {
            NameAr = "عرض 2",
            NameEn = "Draft Offer",
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        });
        await db.SaveChangesAsync();

        var handler = new GetOffersListQueryHandler(db);
        var result = await handler.Handle(new GetOffersListQuery(OfferStatus.Active, new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        result.Value.Items[0].NameEn.Should().Be("Active Offer");
    }

    [Fact]
    public async Task GetCustomerAvailableOffersQuery_FiltersByEligibility()
    {
        using var db = CreateDbContext();
        var activeOffer = new Offer
        {
            NameAr = "عرض متاح",
            NameEn = "Available Offer",
            Status = OfferStatus.Active,
            CustomerSegment = CustomerSegmentFilter.All,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            PerCustomerLimit = 2
        };
        db.Offers.Add(activeOffer);
        await db.SaveChangesAsync();

        var engine = new OfferEligibilityEngine();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new GetCustomerAvailableOffersQueryHandler(db, engine, clock);
        var query = new GetCustomerAvailableOffersQuery(
            CustomerId: Guid.NewGuid(),
            CartAmount: 50.000m,
            DurationDays: 20,
            DeliveryGovernorate: "Hawalli");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].NameEn.Should().Be("Available Offer");
    }

    [Fact]
    public void OfferDto_PurchaseMode_DirectPurchaseWithoutPromoCode_CouponOnlyWithPromoCode()
    {
        var direct = MealMate.Application.Features.Offers.Commands.CreateOfferDraftCommandHandler.MapOfferDto(new Offer
        {
            NameAr = "عرض مباشر",
            NameEn = "Direct Offer",
            Status = OfferStatus.Active
        });
        direct.PurchaseMode.Should().Be(MealMate.Application.Features.Offers.DTOs.PurchaseModes.DirectPurchase);

        var coupon = MealMate.Application.Features.Offers.Commands.CreateOfferDraftCommandHandler.MapOfferDto(new Offer
        {
            NameAr = "كوبون",
            NameEn = "Coupon Offer",
            Status = OfferStatus.Active,
            PromoCode = "SAVE20"
        });
        coupon.PurchaseMode.Should().Be(MealMate.Application.Features.Offers.DTOs.PurchaseModes.CouponOnly);
    }

    [Fact]
    public async Task GetCustomerAvailableOffersQuery_ClassifiesPurchaseModes_AndCampaignProjectionsStayCouponOnly()
    {
        using var db = CreateDbContext();

        var directOffer = new Offer
        {
            NameAr = "عرض مباشر",
            NameEn = "Direct Offer",
            Status = OfferStatus.Active,
            CustomerSegment = CustomerSegmentFilter.All,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        };
        var couponOffer = new Offer
        {
            NameAr = "كوبون",
            NameEn = "Coupon Offer",
            Status = OfferStatus.Active,
            CustomerSegment = CustomerSegmentFilter.All,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            PromoCode = "SAVE20"
        };
        db.Offers.AddRange(directOffer, couponOffer);

        var campaign = new MealMate.Domain.CollaborativeCampaigns.Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = MealMate.Domain.Enums.CampaignStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            TotalDiscountRate = MealMate.Domain.ValueObjects.Percentage.FromFraction(0.20m),
            MealMateShareRate = MealMate.Domain.ValueObjects.Percentage.FromFraction(0.10m),
            RestaurantShareRate = MealMate.Domain.ValueObjects.Percentage.FromFraction(0.10m)
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var handler = new GetCustomerAvailableOffersQueryHandler(db, new OfferEligibilityEngine(), Sub_clock());
        var result = await handler.Handle(new GetCustomerAvailableOffersQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var direct = result.Value!.Single(o => o.Id == directOffer.Id);
        direct.PurchaseMode.Should().Be(MealMate.Application.Features.Offers.DTOs.PurchaseModes.DirectPurchase);

        var coupon = result.Value!.Single(o => o.Id == couponOffer.Id);
        coupon.PurchaseMode.Should().Be(MealMate.Application.Features.Offers.DTOs.PurchaseModes.CouponOnly);

        // Campaign projection: id is the campaign id (not an offer id) → never DirectPurchase.
        var campaignProjection = result.Value!.Single(o => o.Id == campaign.Id);
        campaignProjection.PurchaseMode.Should().Be(MealMate.Application.Features.Offers.DTOs.PurchaseModes.CouponOnly);
    }

    private static IDateTimeProvider Sub_clock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return clock;
    }

    [Fact]
    public async Task CustomerDetails_ReturnsScopeAndServerClock_WithoutConsumingCoupon()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
        var programId = Guid.NewGuid();
        var offer = new Offer
        {
            NameAr = "عرض", NameEn = "Offer", Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1), EndsAtUtc = now.AddDays(1), PerCustomerLimit = 1,
            PromoCode = "SAVE10"
        };
        offer.EligiblePrograms.Add(new OfferProgram { ProgramId = programId });
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var available = new GetCustomerAvailableOffersQueryHandler(db, new OfferEligibilityEngine(), clock);
        var handler = new GetCustomerOfferDetailsQueryHandler(available, clock);

        var result = await handler.Handle(new GetCustomerOfferDetailsQuery(offer.Id, Guid.NewGuid(), ProgramId: programId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Offer.Id.Should().Be(offer.Id);
        result.Value.Offer.EligibleProgramIds.Should().ContainSingle().Which.Should().Be(programId);
        result.Value.ServerNowUtc.Should().Be(now);
        db.OfferRedemptions.Should().BeEmpty();
        (await db.Offers.SingleAsync()).CurrentUsageCount.Should().Be(0);

        var wrongProgram = await handler.Handle(new GetCustomerOfferDetailsQuery(offer.Id, Guid.NewGuid(), ProgramId: Guid.NewGuid()), CancellationToken.None);
        wrongProgram.IsFailure.Should().BeTrue();
        wrongProgram.Error.Type.Should().Be(ErrorType.NotFound);
        offer.Status = OfferStatus.Draft;
        await db.SaveChangesAsync();
        var inactive = await handler.Handle(new GetCustomerOfferDetailsQuery(offer.Id, Guid.NewGuid()), CancellationToken.None);
        inactive.IsFailure.Should().BeTrue();
        inactive.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetCustomerAvailableOffersQuery_IncludesTargetedOffers_WhenNoPackageSelected()
    {
        using var db = CreateDbContext();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var offer = new Offer
        {
            NameAr = "عرض مخصص",
            NameEn = "Targeted Offer",
            Status = OfferStatus.Active,
            CustomerSegment = CustomerSegmentFilter.All,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            PerCustomerLimit = 1,
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = programId,
                    BundleId = bundleId,
                    Tier = MealMate.Domain.Enums.RestaurantTier.Platinum,
                    AllowedDurationDays = [26]
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var engine = new OfferEligibilityEngine();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new GetCustomerAvailableOffersQueryHandler(db, engine, clock);
        // Customer opens offers hub without selecting any package
        var query = new GetCustomerAvailableOffersQuery(CustomerId: Guid.NewGuid());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(o => o.Id == offer.Id);
    }

    [Fact]
    public async Task CustomerDetails_ReturnsTargetedOffer_WhenNavigatedDirectlyWithoutPackageSelection()
    {
        using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var offer = new Offer
        {
            NameAr = "عرض مخصص",
            NameEn = "Targeted Offer",
            Status = OfferStatus.Active,
            CustomerSegment = CustomerSegmentFilter.All,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
            PerCustomerLimit = 1,
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = programId,
                    BundleId = bundleId,
                    Tier = MealMate.Domain.Enums.RestaurantTier.Platinum,
                    AllowedDurationDays = [26]
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var available = new GetCustomerAvailableOffersQueryHandler(db, new OfferEligibilityEngine(), clock);
        var handler = new GetCustomerOfferDetailsQueryHandler(available, clock);

        // Deep link without specifying package context
        var result = await handler.Handle(new GetCustomerOfferDetailsQuery(offer.Id, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Offer.Id.Should().Be(offer.Id);
    }
}
