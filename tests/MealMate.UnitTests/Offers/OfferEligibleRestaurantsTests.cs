using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferEligibleRestaurantsTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static IDateTimeProvider UtcClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return clock;
    }

    private static Offer ActiveOffer(string nameAr = "عرض", string nameEn = "Offer")
    {
        return new Offer
        {
            NameAr = nameAr,
            NameEn = nameEn,
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
        };
    }

    private static RestaurantProfile Restaurant(string nameEn, string nameAr, bool operational = true, bool approved = true)
    {
        return new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = $"شركة {nameAr}",
            LegalNameEn = $"Legal {nameEn}",
            TradeNameAr = nameAr,
            TradeNameEn = nameEn,
            VerificationStatus = approved ? VerificationStatus.Approved : VerificationStatus.Draft,
            IsOperationallyActive = operational,
        };
    }

    private static Document LogoDocument(Guid restaurantId, string storageKey)
    {
        return new Document
        {
            OwnerType = DocumentOwnerType.Restaurant,
            OwnerId = restaurantId,
            DocumentType = DocumentType.ProfileImage,
            FileName = "logo.png",
            ContentType = "image/png",
            StoragePath = storageKey,
            SizeBytes = 10,
        };
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_ExplicitList_ReturnsExactlyThoseRestaurants_WithNamesAndLogos()
    {
        using var db = CreateDbContext();
        var withLogo = Restaurant("Alpha Restaurant", "ألفا");
        var withoutLogo = Restaurant("Beta Restaurant", "بيتا");
        db.RestaurantProfiles.AddRange(withLogo, withoutLogo);
        await db.SaveChangesAsync();
        db.Documents.Add(LogoDocument(withLogo.Id, "uploads/restaurant/logos/alpha.png"));
        var offer = ActiveOffer("عرض اختيار المطاعم", "Restaurant Choice");
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = withLogo.Id });
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = withoutLogo.Id });
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var result = await new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock())
            .Handle(new GetOfferEligibleRestaurantsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OfferId.Should().Be(offer.Id);
        result.Value.AllRestaurants.Should().BeFalse();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Restaurants.Select(x => x.RestaurantId).Should().BeEquivalentTo(new[] { withLogo.Id, withoutLogo.Id });
        result.Value.DisplayNameAr.Should().Be("عرض اختيار المطاعم");
        result.Value.DisplayNameEn.Should().Be("Restaurant Choice");
        var logoRow = result.Value.Restaurants.Single(x => x.RestaurantId == withLogo.Id);
        logoRow.NameEn.Should().Be("Alpha Restaurant");
        logoRow.NameAr.Should().Be("ألفا");
        logoRow.LogoUrl.Should().Be(CatalogImageStorage.ToContentUrl("uploads/restaurant/logos/alpha.png"));
        result.Value.Restaurants.Single(x => x.RestaurantId == withoutLogo.Id).LogoUrl.Should().BeNull();
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_ExplicitList_SkipsSoftDeletedRestaurants_AndUnknownIds()
    {
        using var db = CreateDbContext();
        var live = Restaurant("Live Restaurant", "حي");
        var deleted = Restaurant("Deleted Restaurant", "محذوف");
        db.RestaurantProfiles.AddRange(live, deleted);
        await db.SaveChangesAsync();
        deleted.IsDeleted = true;
        await db.SaveChangesAsync();
        var offer = ActiveOffer();
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = live.Id });
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = deleted.Id });
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = Guid.NewGuid() });
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var result = await new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock())
            .Handle(new GetOfferEligibleRestaurantsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Restaurants.Single().RestaurantId.Should().Be(live.Id);
        result.Value.Restaurants.Single().NameEn.Should().Be("Live Restaurant");
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_ExplicitList_IncludesNonOperationalRestaurant()
    {
        using var db = CreateDbContext();
        var paused = Restaurant("Paused Restaurant", "موقوف", operational: false, approved: false);
        db.RestaurantProfiles.Add(paused);
        await db.SaveChangesAsync();
        var offer = ActiveOffer();
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = paused.Id });
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var result = await new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock())
            .Handle(new GetOfferEligibleRestaurantsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Restaurants.Single().RestaurantId.Should().Be(paused.Id);
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_ExplicitList_OrdersByNameOrdinal()
    {
        using var db = CreateDbContext();
        var ten = Restaurant("restaurant-10", "مطعم ١٠");
        var two = Restaurant("restaurant-2", "مطعم ٢");
        db.RestaurantProfiles.AddRange(ten, two);
        await db.SaveChangesAsync();
        var offer = ActiveOffer();
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = ten.Id });
        offer.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = two.Id });
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var result = await new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock())
            .Handle(new GetOfferEligibleRestaurantsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Restaurants.Select(x => x.NameEn).Should().ContainInConsecutiveOrder("restaurant-10", "restaurant-2");
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_AllRestaurantsOffer_ReturnsStorefrontLiveSet_ExcludesNotApprovedAndNotOperational()
    {
        using var db = CreateDbContext();
        var liveOne = Restaurant("Aurora Restaurant", "أورورا");
        var liveTwo = Restaurant("Zest Restaurant", "زست");
        var waitingReview = Restaurant("Waiting Restaurant", "بالمراجعة", approved: false);
        var paused = Restaurant("Paused Restaurant", "موقوف", operational: false);
        db.RestaurantProfiles.AddRange(liveOne, liveTwo, waitingReview, paused);
        await db.SaveChangesAsync();
        var offer = ActiveOffer("عرض عام", "Global Offer");
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var result = await new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock())
            .Handle(new GetOfferEligibleRestaurantsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllRestaurants.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.TotalCount.Should().Be(result.Value.Restaurants.Count);
        result.Value.Restaurants.Select(x => x.RestaurantId).Should().BeEquivalentTo(new[] { liveOne.Id, liveTwo.Id });
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_AllRestaurantsOffer_CapsAt500WithDeterministicOrder()
    {
        using var db = CreateDbContext();
        var capped = new List<RestaurantProfile>(502);
        for (var i = 0; i < 502; i++)
        {
            capped.Add(Restaurant($"Rest-{i:000}", $"مطعم {i:000}"));
        }
        db.RestaurantProfiles.AddRange(capped);
        await db.SaveChangesAsync();
        var excluded = capped.Skip(500).ToList();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var result = await new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock())
            .Handle(new GetOfferEligibleRestaurantsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllRestaurants.Should().BeTrue();
        result.Value.TotalCount.Should().Be(500);
        result.Value.Restaurants.First().NameEn.Should().Be("Rest-000");
        result.Value.Restaurants.Last().NameEn.Should().Be("Rest-499");
        result.Value.Restaurants.Select(x => x.RestaurantId).Should().NotContain(excluded.Select(x => x.Id));
        result.Value.Restaurants.Select(x => x.NameEn)
            .Should().Equal(result.Value.Restaurants.Select(x => x.NameEn).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_UnknownOrInactiveOrExpiredOffer_FailsWithCustomerUnavailableCode()
    {
        using var db = CreateDbContext();
        var draft = new Offer
        {
            NameAr = "عرض مسودة",
            NameEn = "Draft Offer",
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
        };
        var expired = new Offer
        {
            NameAr = "عرض منتهي",
            NameEn = "Expired Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-30),
            EndsAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        db.Offers.Add(draft);
        db.Offers.Add(expired);
        await db.SaveChangesAsync();

        var handler = new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock());
        var unknown = await handler.Handle(new GetOfferEligibleRestaurantsQuery(Guid.NewGuid()), CancellationToken.None);
        var inactive = await handler.Handle(new GetOfferEligibleRestaurantsQuery(draft.Id), CancellationToken.None);
        var expiredResult = await handler.Handle(new GetOfferEligibleRestaurantsQuery(expired.Id), CancellationToken.None);

        unknown.IsFailure.Should().BeTrue();
        unknown.Error!.Type.Should().Be(ErrorType.NotFound);
        unknown.Error.Code.Should().Be("offers.customer.unavailable");
        inactive.IsFailure.Should().BeTrue();
        inactive.Error!.Type.Should().Be(ErrorType.NotFound);
        inactive.Error.Code.Should().Be("offers.customer.unavailable");
        expiredResult.IsFailure.Should().BeTrue();
        expiredResult.Error!.Type.Should().Be(ErrorType.NotFound);
        expiredResult.Error.Code.Should().Be("offers.customer.unavailable");
    }

    [Fact]
    public async Task GetOfferEligibleRestaurants_AllRestaurantsOffer_IncludesApprovalLogoBranchFallback()
    {
        using var db = CreateDbContext();
        var live = Restaurant("Fallback Logo Restaurant", "شعار بديل");
        db.RestaurantProfiles.Add(live);
        await db.SaveChangesAsync();
        var registration = new RestaurantRegistration
        {
            ResultingRestaurantId = live.Id,
        };
        var branch = new RestaurantBranch
        {
            RestaurantRegistrationId = registration.Id,
            BranchCode = "BR-1",
            NameAr = "فرع",
            NameEn = "Branch",
            AddressAr = "العنوان",
            AddressEn = "Address",
            GovernorateAr = "الكويت",
            GovernorateEn = "Kuwait",
            ContactPhone = "+96500000000",
            IsActive = true,
            IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/fallback.png",
        };
        db.RestaurantBranches.Add(branch);
        db.RestaurantRegistrations.Add(registration);
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var result = await new GetOfferEligibleRestaurantsQueryHandler(db, UtcClock())
            .Handle(new GetOfferEligibleRestaurantsQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Restaurants.Single().LogoUrl.Should().Be(CatalogImageStorage.ToContentUrl("uploads/restaurant/branches/fallback.png"));
    }
}
