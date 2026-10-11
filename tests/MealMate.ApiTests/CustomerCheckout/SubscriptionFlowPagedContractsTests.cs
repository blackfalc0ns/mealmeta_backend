using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using System.Text.Json;

namespace MealMate.ApiTests.CustomerCheckout;

/// <summary>
/// Contract tests for the additive subscription-flow enhancements: paged envelopes for
/// programs/bundles/tier restaurants/summary restaurants, the mobile design theme, and
/// the coupon display on the order summary. The legacy shapes must keep working.
/// </summary>
public sealed class SubscriptionFlowPagedContractsTests
{
    [Fact]
    public async Task Programs_WithoutPagingParams_ReturnsLegacyBareList()
    {
        await using var db = CreateDb();
        SeedVisibleProgram(db, "P1", sortOrder: 1);
        SeedVisibleProgram(db, "P2", sortOrder: 2);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None));
        var items = Assert.IsAssignableFrom<IReadOnlyList<CustomerProgramCardDto>>(result.Value);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task Programs_WithPagingParams_ReturnsPagedEnvelopeWithMetadata()
    {
        await using var db = CreateDb();
        SeedVisibleProgram(db, "P1", sortOrder: 1);
        SeedVisibleProgram(db, "P2", sortOrder: 2);
        SeedVisibleProgram(db, "P3", sortOrder: 3);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None, page: 2, pageSize: 2));
        var paged = Assert.IsType<PagedResult<CustomerProgramCardDto>>(result.Value);

        Assert.Single(paged.Items);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(2, paged.Page);
        Assert.Equal(2, paged.PageSize);
        Assert.Equal(2, paged.TotalPages);
        Assert.False(paged.HasNextPage);
        Assert.True(paged.HasPreviousPage);
        Assert.Equal("P3", paged.Items[0].Code);
    }

    [Fact]
    public async Task Programs_UnionOfPagesCoversVisibleSetWithoutDuplicates()
    {
        await using var db = CreateDb();
        foreach (var i in Enumerable.Range(1, 5))
            SeedVisibleProgram(db, $"P{i}", sortOrder: i);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var collected = new List<Guid>();
        const int pageSize = 2;
        for (var page = 1; page <= 3; page++)
        {
            var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None, page, pageSize));
            var paged = Assert.IsType<PagedResult<CustomerProgramCardDto>>(result.Value);
            collected.AddRange(paged.Items.Select(x => x.ProgramId));
        }

        Assert.Equal(5, collected.Count);
        Assert.Equal(5, collected.Distinct().Count());
    }

    [Fact]
    public async Task Programs_PaginationNormalizesOutOfRangeValues()
    {
        await using var db = CreateDb();
        SeedVisibleProgram(db, "P1", sortOrder: 1);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None, page: -5, pageSize: 5000));
        var paged = Assert.IsType<PagedResult<CustomerProgramCardDto>>(result.Value);

        Assert.Equal(1, paged.Page);
        Assert.Equal(100, paged.PageSize);
    }

    [Fact]
    public async Task Programs_PageBeyondEnd_ReturnsEmptyItemsWithRealTotalCount()
    {
        await using var db = CreateDb();
        SeedVisibleProgram(db, "P1", sortOrder: 1);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None, page: 9, pageSize: 2));
        var paged = Assert.IsType<PagedResult<CustomerProgramCardDto>>(result.Value);

        Assert.Empty(paged.Items);
        Assert.Equal(1, paged.TotalCount);
        Assert.False(paged.HasNextPage);
    }

    [Fact]
    public async Task Programs_PagedTotalCount_KeepsActiveUnavailablePrograms()
    {
        await using var db = CreateDb();
        SeedVisibleProgram(db, "P_VISIBLE", sortOrder: 1);
        // Active unavailable programs stay visible with a disabled availability hint.
        db.Programs.Add(new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P_HIDDEN", NameAr = "مخفي", NameEn = "Hidden",
            Status = ProgramStatus.Active, IsActive = true, SortOrder = 2,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None, page: 1, pageSize: 20));
        var paged = Assert.IsType<PagedResult<CustomerProgramCardDto>>(result.Value);

        Assert.Equal(2, paged.TotalCount);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal("P_VISIBLE", paged.Items[0].Code);
    }

    [Fact]
    public async Task Bundles_WithoutPaging_KeepsLegacyShapeWithoutPagination()
    {
        await using var db = CreateDb();
        var (program, bundles) = SeedBundles(db, bundleCount: 2);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id, CancellationToken.None));
        var dto = Assert.IsType<CustomerProgramBundlesDto>(result.Value);

        Assert.Equal(2, dto.Bundles.Count);
        Assert.Null(dto.Pagination);
    }

    [Fact]
    public async Task Bundles_WithPaging_SlicesCardsButKeepsFullSetTotals()
    {
        await using var db = CreateDb();
        var (program, bundles) = SeedBundles(db, bundleCount: 3);
        // Make the third bundle the most selected; slicing must not change how badges are computed.
        var customerId = Guid.NewGuid();
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = customerId, ProgramId = program.Id, BundleId = bundles[2].Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 26, Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 20), EndDate = new DateOnly(2026, 9, 20), PaidAmount = new Money(100m, "KWD"),
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var pagedResult = Assert.IsType<OkObjectResult>(
            await controller.Bundles(program.Id, CancellationToken.None, page: 1, pageSize: 2));
        var paged = Assert.IsType<CustomerProgramBundlesDto>(pagedResult.Value);

        Assert.Equal(2, paged.Bundles.Count);
        Assert.NotNull(paged.Pagination);
        Assert.Equal(3, paged.Pagination!.TotalCount);
        Assert.Equal(1, paged.Pagination.Page);
        Assert.Equal(2, paged.Pagination.PageSize);
        Assert.Equal(2, paged.Pagination.TotalPages);
        Assert.True(paged.Pagination.HasNextPage);
        Assert.False(paged.Pagination.HasPreviousPage);
        Assert.DoesNotContain(paged.Bundles, x => x.IsMostSelected);

        // The unpaged contract still reports the badge and per-bundle restaurant counts unchanged.
        var legacyResult = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id, CancellationToken.None));
        var legacy = Assert.IsType<CustomerProgramBundlesDto>(legacyResult.Value);
        var mostSelected = Assert.Single(legacy.Bundles, x => x.IsMostSelected);
        Assert.Equal(bundles[2].Id, mostSelected.BundleId);
        Assert.Equal(paged.Pagination.TotalCount, legacy.Bundles.Count);
        foreach (var card in paged.Bundles)
            Assert.Equal(legacy.Bundles.Single(x => x.BundleId == card.BundleId).RestaurantCount, card.RestaurantCount);
    }

    [Fact]
    public async Task TierRestaurants_WithPaging_ReturnsPagedEnvelopeAndUnionCoversSet()
    {
        await using var db = CreateDb();
        var (program, bundle, restaurantIds) = SeedClassifiedRestaurants(db, restaurantCount: 3);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, restaurantIds.ToArray());

        var firstPage = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic, null, CancellationToken.None, page: 1, pageSize: 2));
        var paged = Assert.IsType<PagedResult<CustomerRestaurantBrowseCardDto>>(firstPage.Value);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(2, paged.TotalPages);
        Assert.True(paged.HasNextPage);

        var secondPage = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic, null, CancellationToken.None, page: 2, pageSize: 2));
        var paged2 = Assert.IsType<PagedResult<CustomerRestaurantBrowseCardDto>>(secondPage.Value);
        Assert.Single(paged2.Items);

        var unionIds = paged.Items.Concat(paged2.Items).Select(x => x.RestaurantId).ToList();
        Assert.Equal(3, unionIds.Distinct().Count());
        Assert.Equal(restaurantIds.ToHashSet(), unionIds.ToHashSet());
        Assert.All(paged.Items.Concat(paged2.Items), x => Assert.Equal(RestaurantTier.Basic, x.Tier));
    }

    [Fact]
    public async Task TierRestaurants_WithoutPaging_KeepsLegacyBareList()
    {
        await using var db = CreateDb();
        var (program, bundle, restaurantIds) = SeedClassifiedRestaurants(db, restaurantCount: 2);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, restaurantIds.ToArray());

        var result = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic, null, CancellationToken.None));
        var cards = Assert.IsAssignableFrom<IReadOnlyList<CustomerRestaurantBrowseCardDto>>(result.Value);
        Assert.Equal(2, cards.Count);
    }

    [Fact]
    public async Task Tiers_IncludeSavedThemeDesignChoice()
    {
        await using var db = CreateDb();
        var (program, bundle, restaurantIds, sender, clock) = SeedTierCatalog(db);
        SavePricingSettings(db, settings => settings.Theme = new TierThemeSettings { DesignId = (int)TierThemeDesign.Aurora });
        await db.SaveChangesAsync();

        var schedule = new SubscriptionScheduleService(clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, restaurantIds.ToArray());

        var result = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, 26, false, CancellationToken.None));
        var tiers = Assert.IsAssignableFrom<IReadOnlyList<CustomerTierOptionDto>>(result.Value);
        Assert.NotEmpty(tiers);
        Assert.All(tiers, x =>
        {
            Assert.NotNull(x.Theme);
            Assert.Equal((int)TierThemeDesign.Aurora, x.Theme!.DesignId);
            Assert.Equal("Aurora", x.Theme.DesignName);
        });
    }

    [Fact]
    public async Task Tiers_UnknownStoredDesignNumber_FallsBackToNullTheme()
    {
        await using var db = CreateDb();
        var (program, bundle, restaurantIds, sender, clock) = SeedTierCatalog(db);
        // A design removed from a newer backend catalog: reads fall back to no theme, no crash.
        SavePricingSettings(db, settings => settings.Theme = new TierThemeSettings { DesignId = 99, DesignName = "Ghost" });
        await db.SaveChangesAsync();

        var schedule = new SubscriptionScheduleService(clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, restaurantIds.ToArray());

        var result = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, 26, false, CancellationToken.None));
        var tiers = Assert.IsAssignableFrom<IReadOnlyList<CustomerTierOptionDto>>(result.Value);
        Assert.NotEmpty(tiers);
        Assert.All(tiers, x => Assert.Null(x.Theme));
    }

    [Fact]
    public async Task Tiers_ThemeIsNullWhenNoDesignChoiceSaved()
    {
        await using var db = CreateDb();
        var (program, bundle, restaurantIds, sender, clock) = SeedTierCatalog(db);
        SavePricingSettings(db, _ => { });
        await db.SaveChangesAsync();

        var schedule = new SubscriptionScheduleService(clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, restaurantIds.ToArray());

        var result = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, 26, false, CancellationToken.None));
        var tiers = Assert.IsAssignableFrom<IReadOnlyList<CustomerTierOptionDto>>(result.Value);
        Assert.NotEmpty(tiers);
        Assert.All(tiers, x => Assert.Null(x.Theme));
    }

    [Fact]
    public async Task Summary_WithRestaurantsPaging_MetadataAndSlicingDoNotChangeTotals()
    {
        await using var db = CreateDb();
        var context = await SeedSummaryContextAsync(db, restaurantCount: 3);
        var sender = SummaryPricingSenderStub(grossFinalPrice: 234m);
        var schedule = new SubscriptionScheduleService(context.Clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, context.RestaurantIds.ToArray());

        var pagedRequest = new SubscriptionSummaryRequest(
            context.ProgramId, context.BundleId, RestaurantTier.Basic, 26, schedule.GetKuwaitToday(),
            RestaurantsPage: 1, RestaurantsPageSize: 2);
        var pagedResult = Assert.IsType<OkObjectResult>(await controller.Summary(pagedRequest, CancellationToken.None));
        var paged = Assert.IsType<SubscriptionOrderSummaryDto>(pagedResult.Value);

        Assert.Equal(3, paged.RestaurantCount);
        Assert.Equal(2, paged.Restaurants!.Count);
        Assert.NotNull(paged.RestaurantsPagination);
        Assert.Equal(3, paged.RestaurantsPagination!.TotalCount);
        Assert.Equal(1, paged.RestaurantsPagination.Page);
        Assert.Equal(2, paged.RestaurantsPagination.PageSize);
        Assert.Equal(2, paged.RestaurantsPagination.TotalPages);
        Assert.True(paged.RestaurantsPagination.HasNextPage);
        Assert.True(paged.CanCheckout);
        Assert.True(paged.FinalPrice > 0m);

        var page2 = Assert.IsType<OkObjectResult>(await controller.Summary(
            new SubscriptionSummaryRequest(
                context.ProgramId, context.BundleId, RestaurantTier.Basic, 26, schedule.GetKuwaitToday(),
                RestaurantsPage: 2, RestaurantsPageSize: 2), CancellationToken.None));
        var page2Dto = Assert.IsType<SubscriptionOrderSummaryDto>(page2.Value);
        Assert.Single(page2Dto.Restaurants!);
        Assert.False(page2Dto.RestaurantsPagination!.HasNextPage);
        Assert.True(page2Dto.RestaurantsPagination.HasPreviousPage);

        // Totals do not depend on the page window.
        Assert.Equal(paged.RestaurantCount, page2Dto.RestaurantCount);
        Assert.Equal(paged.RestaurantRepetition!.Limit, page2Dto.RestaurantRepetition!.Limit);
        Assert.Equal(paged.FinalPrice, page2Dto.FinalPrice);
    }

    [Fact]
    public async Task Summary_WithoutRestaurantsPaging_OmitsMetadataAndReturnsFullList()
    {
        await using var db = CreateDb();
        var context = await SeedSummaryContextAsync(db, restaurantCount: 2);
        var sender = SummaryPricingSenderStub(grossFinalPrice: 234m);
        var schedule = new SubscriptionScheduleService(context.Clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, context.RestaurantIds.ToArray());

        var request = new SubscriptionSummaryRequest(
            context.ProgramId, context.BundleId, RestaurantTier.Basic, 26, schedule.GetKuwaitToday());
        var result = Assert.IsType<OkObjectResult>(await controller.Summary(request, CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);

        Assert.Null(summary.RestaurantsPagination);
        Assert.Equal(2, summary.Restaurants!.Count);
        Assert.Null(summary.Coupon);
    }

    [Fact]
    public async Task Summary_WithValidCoupon_ShowsDiscountedPriceAndCouponDetails()
    {
        await using var db = CreateDb();
        var context = await SeedSummaryContextAsync(db, restaurantCount: 1);
        var offer = SeedActiveCoupon(db, code: "WELCOME10", discountAmountKwd: 10m);
        await db.SaveChangesAsync();
        var sender = SummaryPricingSenderStub(grossFinalPrice: 234m, promoDiscount: 10m);
        var schedule = new SubscriptionScheduleService(context.Clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, context.RestaurantIds.ToArray());

        var request = new SubscriptionSummaryRequest(
            context.ProgramId, context.BundleId, RestaurantTier.Basic, 26, schedule.GetKuwaitToday(),
            PromoCode: " welcome10 ");
        var result = Assert.IsType<OkObjectResult>(await controller.Summary(request, CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);

        Assert.NotNull(summary.Coupon);
        Assert.Equal("WELCOME10", summary.Coupon!.Code);
        Assert.Equal("valid", summary.Coupon.Status);
        Assert.Equal(offer.Id, summary.Coupon.OfferId);
        Assert.Equal(10m, summary.Coupon.DiscountAmount);
        Assert.Equal(224m, summary.FinalPrice);
        Assert.Equal(10m, summary.CampaignDiscount);

        // The summary is a read-only evaluation.
        Assert.Empty(await db.OfferRedemptions.ToListAsync());
        var storedOffer = await db.Offers.SingleAsync(x => x.Id == offer.Id);
        Assert.Equal(0, storedOffer.CurrentUsageCount);
    }

    [Fact]
    public async Task Summary_WithRejectedCoupon_ReturnsUndiscountedPriceAndReason()
    {
        await using var db = CreateDb();
        var context = await SeedSummaryContextAsync(db, restaurantCount: 1);
        var offer = SeedActiveCoupon(db, code: "OLD10", discountAmountKwd: 10m);
        offer.EndsAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        var sender = SummaryPricingSenderStub(grossFinalPrice: 234m, promoDiscount: 10m);
        var schedule = new SubscriptionScheduleService(context.Clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, context.RestaurantIds.ToArray());

        var request = new SubscriptionSummaryRequest(
            context.ProgramId, context.BundleId, RestaurantTier.Basic, 26, schedule.GetKuwaitToday(),
            PromoCode: "OLD10");
        var result = Assert.IsType<OkObjectResult>(await controller.Summary(request, CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);

        Assert.NotNull(summary.Coupon);
        Assert.Equal("expired", summary.Coupon!.Status);
        Assert.Equal(0m, summary.Coupon.DiscountAmount);
        Assert.False(string.IsNullOrWhiteSpace(summary.Coupon.MessageAr));
        Assert.False(string.IsNullOrWhiteSpace(summary.Coupon.MessageEn));
        Assert.Equal(234m, summary.FinalPrice);
        Assert.Equal(0m, summary.CampaignDiscount);
    }

    [Fact]
    public async Task Summary_WithUnknownCoupon_ReportsNotFoundStatus()    {
        await using var db = CreateDb();
        var context = await SeedSummaryContextAsync(db, restaurantCount: 1);
        var sender = SummaryPricingSenderStub(grossFinalPrice: 234m, promoDiscount: 10m);
        var schedule = new SubscriptionScheduleService(context.Clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, context.RestaurantIds.ToArray());

        var request = new SubscriptionSummaryRequest(
            context.ProgramId, context.BundleId, RestaurantTier.Basic, 26, schedule.GetKuwaitToday(),
            PromoCode: "NOPE");
        var result = Assert.IsType<OkObjectResult>(await controller.Summary(request, CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);

        Assert.Equal("not_found", summary.Coupon!.Status);
        Assert.Equal(234m, summary.FinalPrice);
    }

    [Fact]
    public async Task Readiness_AddressIncludesLocalizedRegionNamesFromCatalog()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_ADDR_NAMES", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_ADDR_NAMES", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var governorate = new MealMate.Domain.Foundation.Region
        {
            Type = MealMate.Domain.Enums.RegionType.Governorate, Code = "GOV-HW",
            NameAr = "حولي", NameEn = "Hawalli", IsActive = true, CountryId = Guid.NewGuid(),
        };
        var area = new MealMate.Domain.Foundation.Region
        {
            Type = MealMate.Domain.Enums.RegionType.Area, Code = "AREA-SLM",
            ParentRegionId = governorate.Id, NameAr = "السالمية", NameEn = "Salmiya", IsActive = true, CountryId = governorate.CountryId,
        };
        var block = new MealMate.Domain.Foundation.Region
        {
            Type = MealMate.Domain.Enums.RegionType.Block, Code = "BLK-4",
            ParentRegionId = area.Id, NameAr = "قطعة 4", NameEn = "Block 4", IsActive = true, CountryId = governorate.CountryId,
        };
        db.AddRange(program, bundle, governorate, area, block);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        var userId = currentUser.UserId!.Value;
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = userId,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
            DietaryPreferencesCompletedAtUtc = DateTime.UtcNow,
        });
        // Stored snapshot text stays Arabic (as captured at creation); localized names come from the catalog.
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId, Label = "المنزل", AddressLine = "حولي, السالمية, قطعة 4",
            Governorate = "حولي", Area = "السالمية", Block = "قطعة 4",
            RegionId = area.Id, BlockRegionId = block.Id, IsDefault = true,
        });
        await db.SaveChangesAsync();
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == userId);
        profile.ActiveDeliveryAddressId = (await db.CustomerAddresses.SingleAsync(x => x.UserId == userId)).Id;
        profile.ActiveDeliveryRegionId = area.Id;
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        var address = readiness.Address!;

        Assert.Equal("حولي", address.Governorate);
        Assert.Equal("السالمية", address.Area);
        Assert.Equal("قطعة 4", address.Block);
        Assert.Equal("حولي", address.GovernorateNameAr);
        Assert.Equal("Hawalli", address.GovernorateNameEn);
        Assert.Equal("السالمية", address.AreaNameAr);
        Assert.Equal("Salmiya", address.AreaNameEn);
        Assert.Equal("قطعة 4", address.BlockNameAr);
        Assert.Equal("Block 4", address.BlockNameEn);
    }

    [Fact]
    public async Task Summary_WithValidCouponAndRestaurantsPaging_BothContractsCoexist()
    {
        await using var db = CreateDb();
        var context = await SeedSummaryContextAsync(db, restaurantCount: 3);
        SeedActiveCoupon(db, code: "WELCOME10", discountAmountKwd: 10m);
        await db.SaveChangesAsync();
        var sender = SummaryPricingSenderStub(grossFinalPrice: 234m, promoDiscount: 10m);
        var schedule = new SubscriptionScheduleService(context.Clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, context.RestaurantIds.ToArray());

        var request = new SubscriptionSummaryRequest(
            context.ProgramId, context.BundleId, RestaurantTier.Basic, 26, schedule.GetKuwaitToday(),
            RestaurantsPage: 1, RestaurantsPageSize: 2, PromoCode: "WELCOME10");
        var result = Assert.IsType<OkObjectResult>(await controller.Summary(request, CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);

        Assert.Equal("valid", summary.Coupon!.Status);
        Assert.Equal(224m, summary.FinalPrice);
        Assert.Equal(2, summary.Restaurants!.Count);
        Assert.Equal(3, summary.RestaurantsPagination!.TotalCount);
    }

    // ---- helpers ----

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static IWebHostEnvironment CreateTestWebHostEnvironment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return environment;
    }

    private static CustomerSubscriptionFlowController CreateController(AppDbContext db, out IPaymentGatewayService payment,
        out ICurrentUser currentUser, ISender? sender = null, ISubscriptionScheduleService? schedule = null)
    {
        payment = Substitute.For<IPaymentGatewayService>();
        currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Customer);
        var httpContext = new DefaultHttpContext();
        if (sender is not null)
            httpContext.RequestServices = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var slots = new BundleSlotRequirementService(db);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        var resolvedSchedule = schedule ?? new SubscriptionScheduleService(clock);
        var tiers = new RestaurantTierClassificationService(db, clock);
        var readiness = new SubscriptionCheckoutReadinessService(
            db, new AllowedSubscriptionDurationService(db), tiers,
            new CustomerDeliveryAddressService(db, clock),
            new CustomerIndividualSubscriptionGate(db));
        return new CustomerSubscriptionFlowController(db, currentUser,
            eligibility, Substitute.For<IMenuPlanResolutionService>(),
            resolvedSchedule, slots, payment,
            new AllowedSubscriptionDurationService(db), tiers, readiness,
            new CustomerIndividualSubscriptionGate(db),
            new RestaurantDiversificationCalculator(),
            new ConfigurationBuilder().Build(),
            CreateTestWebHostEnvironment(), preparation: CustomerSubscriptionFlowControllerTests.CreatePreparedCatalogFixture(db))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async Task<Guid> SeedCustomerDeliveryCoverageAsync(AppDbContext db, ICurrentUser currentUser, params Guid[] restaurantIds)
    {
        var userId = currentUser.UserId!.Value;
        var regionId = Guid.NewGuid();
        if (!await db.CustomerProfiles.AnyAsync(x => x.UserId == userId))
        {
            db.CustomerProfiles.Add(new CustomerProfile
            {
                UserId = userId,
                AllergiesCompletedAtUtc = DateTime.UtcNow,
                DietaryPreferencesCompletedAtUtc = DateTime.UtcNow,
            });
        }

        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "المنزل",
            AddressLine = "السالمية",
            RegionId = regionId,
            IsDefault = true,
            Latitude = 29.34m,
            Longitude = 48.08m,
        };
        db.CustomerAddresses.Add(address);
        foreach (var restaurantId in restaurantIds.Distinct())
        {
            db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
            {
                RestaurantId = restaurantId,
                RegionId = regionId,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync();
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == userId);
        profile.ActiveDeliveryAddressId = address.Id;
        profile.ActiveDeliveryRegionId = regionId;
        profile.ActiveDeliveryAddressLine = address.AddressLine;
        profile.AllergiesCompletedAtUtc ??= DateTime.UtcNow;
        profile.DietaryPreferencesCompletedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return address.Id;
    }

    private static void SeedVisibleProgram(AppDbContext db, string code, int sortOrder)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = code, NameAr = "برنامج " + code, NameEn = code,
            Status = ProgramStatus.Active, IsActive = true, SortOrder = sortOrder,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_" + code, NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم " + code, TradeNameEn = code, LegalNameAr = "مطعم", LegalNameEn = code,
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
    }

    private static (MealMate.Domain.ProgramsSubscriptions.Program Program, List<Bundle> Bundles) SeedBundles(AppDbContext db, int bundleCount)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_BUNDLES", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, restaurant);
        var bundles = new List<Bundle>();
        for (var i = 1; i <= bundleCount; i++)
        {
            var bundle = new Bundle
            {
                ProgramId = program.Id, Program = program, Code = $"BND_{i}", NameAr = $"باقة {i}", NameEn = $"Bundle {i}",
                MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true, SortOrder = i,
            };
            bundles.Add(bundle);
            db.Add(bundle);
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
            });
        }
        return (program, bundles);
    }

    private static (MealMate.Domain.ProgramsSubscriptions.Program Program, Bundle Bundle, List<Guid> RestaurantIds)
        SeedClassifiedRestaurants(AppDbContext db, int restaurantCount)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_PAGED_BROWSE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_PAGED_BROWSE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var run = new ClassificationRun
        {
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            RestaurantCount = restaurantCount, ConfidenceLevel = "95", RunAtUtc = DateTime.UtcNow,
        };
        db.AddRange(program, bundle, run);
        var restaurantIds = new List<Guid>();
        for (var i = 1; i <= restaurantCount; i++)
        {
            var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
            {
                TradeNameAr = $"مطعم {i}", TradeNameEn = $"Restaurant {i}", LegalNameAr = $"مطعم {i}", LegalNameEn = $"Restaurant {i}",
                VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
            };
            restaurantIds.Add(restaurant.Id);
            db.Add(restaurant);
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
                ReferencePrice26 = new Money(52m, "KWD"),
                ContractedSupplyPrice26 = new Money(40m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
            db.RestaurantClassifications.Add(new RestaurantClassification
            {
                ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = restaurant.Id,
                ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
                Tier = RestaurantTier.Basic,
            });
        }
        return (program, bundle, restaurantIds);
    }

    private static (MealMate.Domain.ProgramsSubscriptions.Program Program, Bundle Bundle, List<Guid> RestaurantIds,
        ISender Sender, IDateTimeProvider Clock) SeedTierCatalog(AppDbContext db)
    {
        var (program, bundle, restaurantIds) = SeedClassifiedRestaurants(db, restaurantCount: 2);
        IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog =
        [
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Basic, "سيلكت", "Select",
                [new(6, 66, 11), new(12, 120, 10), new(26, 234, 9)]),
        ];
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(catalog));
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<PreviewCustomerPricingQuery>(0);
                return Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                    9m, query.DurationDays, DurationRatesPct: new Dictionary<int, decimal> { [query.DurationDays] = 0m },
                    GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m,
                    FridayDays: query.FridayDays, FridayRatePct: 0m)));
            });
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return (program, bundle, restaurantIds, sender, clock);
    }

    private static void SavePricingSettings(AppDbContext db, Action<SubscriptionPricingSettingsDocument> configure)
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        configure(settings);
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(settings, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
    }

    private static Offer SeedActiveCoupon(AppDbContext db, string code, decimal discountAmountKwd)
    {
        var offer = new Offer
        {
            NameAr = "عرض ترحيبي", NameEn = "Welcome offer",
            Status = OfferStatus.Active,
            DiscountType = DiscountType.FixedAmount,
            DiscountAmount = new Money(discountAmountKwd, "KWD"),
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
            PromoCode = code,
            PerCustomerLimit = 1,
            // A promo-code offer must allow stacking to pass the eligibility engine's
            // promo-code rule — same requirement as the existing pricing preview contract.
            AllowPromoCodeStacking = true,
        };
        db.Offers.Add(offer);
        return offer;
    }

    private static async Task<(Guid ProgramId, Guid BundleId, List<Guid> RestaurantIds, IDateTimeProvider Clock)>
        SeedSummaryContextAsync(AppDbContext db, int restaurantCount)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_COUPON_SUMMARY", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_COUPON_SUMMARY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        var restaurantIds = new List<Guid>();
        for (var i = 0; i < restaurantCount; i++)
        {
            var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
            {
                TradeNameAr = $"مطعم {i + 1}", TradeNameEn = $"Restaurant {i + 1}", LegalNameAr = $"مطعم {i + 1}", LegalNameEn = $"Restaurant {i + 1}",
                VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
            };
            restaurantIds.Add(restaurant.Id);
            db.Add(restaurant);
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
                ReferencePrice26 = new Money(104m, "KWD"),
                ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
        }
        SavePricingSettings(db, _ => { });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return (program.Id, bundle.Id, restaurantIds, clock);
    }

    private static ISender SummaryPricingSenderStub(decimal grossFinalPrice, decimal promoDiscount = 0m)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<PreviewCustomerPricingQuery>(0);
                var hasPromo = !string.IsNullOrWhiteSpace(query.PromoCode);
                return Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                    9m, query.DurationDays,
                    CampaignDiscountKwd: hasPromo ? promoDiscount : 0m,
                    DurationRatesPct: new Dictionary<int, decimal> { [query.DurationDays] = 0m },
                    GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m,
                    FridayDays: query.FridayDays, FridayRatePct: 0m)));
            });
        return sender;
    }
}
