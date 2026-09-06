using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class CustomerAddressesTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_accepts_area_region_id()
    {
        var fixture = await SeedAsync();
        var result = await CreateAsync(fixture, regionId: fixture.AreaId);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.AreaId, result.Value.RegionId);
        Assert.Equal("العاصمة", result.Value.Governorate);
        Assert.Equal("شرق", result.Value.Area);
    }

    [Fact]
    public async Task Create_returns_coverage_notice_and_serving_restaurants_for_address_region()
    {
        var fixture = await SeedAsync();
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "دايت ستيشن",
            TradeNameEn = "Diet Station",
            LegalNameAr = "شركة دايت ستيشن",
            LegalNameEn = "Diet Station Co",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        var serviceRegion = new RestaurantServiceRegion
        {
            RestaurantId = restaurant.Id,
            RegionId = fixture.AreaId,
            IsActive = true
        };
        fixture.Db.AddRange(restaurant, serviceRegion);
        await fixture.Db.SaveChangesAsync();

        var result = await CreateAsync(fixture, regionId: fixture.AreaId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.RestaurantCoverageNotice);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RestaurantCoverageNotice.Ar));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RestaurantCoverageNotice.En));
        Assert.NotNull(result.Value.ServingRestaurants);
        var item = Assert.Single(result.Value.ServingRestaurants);
        Assert.Equal(restaurant.Id, item.Id);
        Assert.Equal("دايت ستيشن", item.NameAr);
        Assert.Equal("Diet Station", item.NameEn);
    }

    [Fact]
    public async Task Create_returns_coverage_notice_with_empty_restaurants_when_no_coverage()
    {
        var fixture = await SeedAsync();
        var result = await CreateAsync(fixture, regionId: fixture.AreaId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.RestaurantCoverageNotice);
        Assert.NotNull(result.Value.ServingRestaurants);
        Assert.Empty(result.Value.ServingRestaurants);
    }

    [Fact]
    public async Task Update_returns_coverage_notice_and_serving_restaurants_for_address_region()
    {
        var fixture = await SeedAsync();
        var created = await CreateAsync(fixture, regionId: fixture.AreaId);
        Assert.True(created.IsSuccess);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "هيلثي كورنر",
            TradeNameEn = "Healthy Corner",
            LegalNameAr = "شركة هيلثي",
            LegalNameEn = "Healthy Co",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        var serviceRegion = new RestaurantServiceRegion
        {
            RestaurantId = restaurant.Id,
            RegionId = fixture.AreaId,
            IsActive = true
        };
        fixture.Db.AddRange(restaurant, serviceRegion);
        await fixture.Db.SaveChangesAsync();

        var updated = await fixture.Handler.Handle(new UpdateCustomerAddressCommand(
            created.Value.Id,
            "المنزل المحدث",
            "قطعة 4 شارع 12",
            fixture.AreaId,
            29.3759m,
            47.9774m,
            null,
            true,
            true,
            "العاصمة",
            "شرق",
            "4",
            "12",
            null,
            "25",
            null,
            null,
            null), CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.NotNull(updated.Value.RestaurantCoverageNotice);
        Assert.NotNull(updated.Value.ServingRestaurants);
        var item = Assert.Single(updated.Value.ServingRestaurants);
        Assert.Equal(restaurant.Id, item.Id);
        Assert.Equal("هيلثي كورنر", item.NameAr);
        Assert.Equal("Healthy Corner", item.NameEn);
    }

    [Fact]
    public async Task Create_rejects_missing_coordinates()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "المنزل",
            "قطعة 4",
            fixture.AreaId,
            null,
            null,
            null,
            true,
            true,
            "العاصمة",
            "شرق",
            "4",
            "12",
            null,
            "25",
            null,
            null,
            null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessageCodes.Validation.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public async Task Create_rejects_coordinates_outside_kuwait()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "المنزل",
            "قطعة 4",
            fixture.AreaId,
            40m,
            30m,
            null,
            true,
            true,
            "العاصمة",
            "شرق",
            "4",
            "12",
            null,
            "25",
            null,
            null,
            null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessageCodes.Validation.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public async Task Create_accepts_block_as_region_id()
    {
        var fixture = await SeedAsync();
        var result = await CreateAsync(fixture, regionId: fixture.BlockId);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.AreaId, result.Value.RegionId);
        Assert.Equal(fixture.BlockId, result.Value.BlockRegionId);
        Assert.Equal("4", result.Value.Block);
    }

    [Fact]
    public async Task Create_accepts_governorate_id_with_area_name()
    {
        var fixture = await SeedAsync();
        var result = await CreateAsync(fixture, regionId: fixture.GovernorateId, area: "شرق");

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.AreaId, result.Value.RegionId);
        Assert.Equal("شرق", result.Value.Area);
    }

    [Fact]
    public async Task Create_rejects_governorate_id_without_area()
    {
        var fixture = await SeedAsync();
        var result = await CreateAsync(fixture, regionId: fixture.GovernorateId);

        Assert.True(result.IsFailure);
        Assert.Equal("address.area_required", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Create_rejects_unknown_region_id()
    {
        var fixture = await SeedAsync();
        var result = await CreateAsync(fixture, regionId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("address.area_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Update_default_switch_activates_delivery_immediately_without_72_hours()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);

        var work = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: false, label: "العمل", line: "عنوان العمل");
        Assert.True(work.IsSuccess);

        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        profile.ActiveDeliveryAddressId = home.Value.Id;
        profile.ActiveDeliveryAddressLine = home.Value.AddressLine;
        profile.ActiveDeliveryRegionId = home.Value.RegionId;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Handler.Handle(new UpdateCustomerAddressCommand(
            work.Value.Id, "العمل", "عنوان العمل", fixture.AreaId, 29.37m, 48.12m, null, true, true,
            "العاصمة", "شرق", "4", "12", null, "25", null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsDefault);
        Assert.True(result.Value.IsDeliveryActive);
        Assert.Null(result.Value.DeliveryEffectiveFromUtc);
        Assert.Null(result.Value.DeliveryChangeNotice);

        profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(work.Value.Id, profile.ActiveDeliveryAddressId);
        Assert.Null(profile.PendingDeliveryAddressId);
        Assert.Null(profile.PendingDeliveryAddressEffectiveFromUtc);
    }

    [Fact]
    public async Task Update_rejects_address_when_active_subscription_restaurant_does_not_service_new_region()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);

        var area2 = new Region
        {
            CountryId = fixture.GovernorateId,
            ParentRegionId = fixture.GovernorateId,
            Type = RegionType.Area,
            Code = "KW-CAP-SALMIYA",
            NameAr = "السالمية",
            NameEn = "Salmiya",
            IsActive = true
        };
        fixture.Db.Regions.Add(area2);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم الكيتو",
            TradeNameEn = "Keto Restaurant",
            LegalNameAr = "شركة الكيتو",
            LegalNameEn = "Keto Co",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        var serviceRegion = new RestaurantServiceRegion
        {
            RestaurantId = restaurant.Id,
            RegionId = fixture.AreaId,
            IsActive = true
        };
        var subscription = new Subscription
        {
            CustomerUserId = fixture.CustomerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            StartDate = DateOnly.FromDateTime(Now),
            EndDate = DateOnly.FromDateTime(Now.AddDays(30)),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100, "KWD"),
            SelectedTier = RestaurantTier.Basic
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(Now.AddDays(1)),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            RestaurantId = restaurant.Id,
            DeliveryAddressId = home.Value.Id,
            DeliveryRegionId = fixture.AreaId
        };
        fixture.Db.AddRange(restaurant, serviceRegion, subscription, day);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Handler.Handle(new UpdateCustomerAddressCommand(
            home.Value.Id, "المنزل", "شارع سالم المبارك", area2.Id, 29.33m, 48.08m, null, true, true,
            "العاصمة", "السالمية", null, null, null, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("address.restaurant_does_not_service_region", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Update_syncs_upcoming_days_without_invoices_and_preserves_days_with_invoices()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم دايت",
            TradeNameEn = "Diet Rest",
            LegalNameAr = "شركة دايت",
            LegalNameEn = "Diet Co",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        var serviceRegion = new RestaurantServiceRegion
        {
            RestaurantId = restaurant.Id,
            RegionId = fixture.AreaId,
            IsActive = true
        };
        var subscription = new Subscription
        {
            CustomerUserId = fixture.CustomerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            StartDate = DateOnly.FromDateTime(Now),
            EndDate = DateOnly.FromDateTime(Now.AddDays(30)),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100, "KWD"),
            SelectedTier = RestaurantTier.Basic
        };

        var day1 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(Now),
            Status = SubscriptionDayStatus.Preparing,
            RestaurantId = restaurant.Id,
            DeliveryAddressId = home.Value.Id,
            DeliveryAddressLine = home.Value.AddressLine,
            DeliveryRegionId = fixture.AreaId
        };
        var invoice = new MealInvoice
        {
            SubscriptionDayId = day1.Id,
            RestaurantId = restaurant.Id,
            ServiceDate = day1.ServiceDate,
            InvoiceNumber = "INV-001",
            TotalBoxes = 1,
            SnapshotJson = "{}",
            BarcodeValue = "BC-001"
        };

        var day2 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(Now.AddDays(1)),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            RestaurantId = restaurant.Id,
            DeliveryAddressId = home.Value.Id,
            DeliveryAddressLine = home.Value.AddressLine,
            DeliveryRegionId = fixture.AreaId
        };

        fixture.Db.AddRange(restaurant, serviceRegion, subscription, day1, invoice, day2);
        await fixture.Db.SaveChangesAsync();

        var updateResult = await fixture.Handler.Handle(new UpdateCustomerAddressCommand(
            home.Value.Id, "المنزل المحدث", "شارع جديد 55", fixture.AreaId, 29.37m, 48.12m, null, true, true,
            "العاصمة", "شرق", "4", "55", null, "10", null, null, null), CancellationToken.None);

        Assert.True(updateResult.IsSuccess);

        var reloadedDay1 = await fixture.Db.SubscriptionDays.SingleAsync(x => x.Id == day1.Id);
        Assert.Equal(home.Value.AddressLine, reloadedDay1.DeliveryAddressLine);

        var reloadedDay2 = await fixture.Db.SubscriptionDays.SingleAsync(x => x.Id == day2.Id);
        Assert.Equal("شارع جديد 55", reloadedDay2.DeliveryAddressLine);
    }

    [Fact]
    public async Task Delete_rejects_active_delivery_address()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(home.Value.Id, profile.ActiveDeliveryAddressId);

        var result = await fixture.Handler.Handle(new DeleteCustomerAddressCommand(home.Value.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("address.active_delivery_cannot_delete", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.True(await fixture.Db.CustomerAddresses.AnyAsync(x => x.Id == home.Value.Id));
    }

    [Fact]
    public async Task Delete_rejects_default_even_when_active_id_is_stale()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        var work = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: false, label: "العمل", line: "عنوان العمل");
        Assert.True(work.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        // Simulate legacy/stale profile pointer so only IsDefault must protect the current default.
        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        profile.ActiveDeliveryAddressId = Guid.NewGuid();
        profile.ActiveDeliveryAddressLine = "عنوان قديم";
        await fixture.Db.SaveChangesAsync();

        var homeEntity = await fixture.Db.CustomerAddresses.SingleAsync(x => x.Id == home.Value.Id);
        Assert.True(homeEntity.IsDefault);

        var result = await fixture.Handler.Handle(new DeleteCustomerAddressCommand(home.Value.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("address.active_delivery_cannot_delete", result.Error.Code);
        Assert.True(await fixture.Db.CustomerAddresses.AnyAsync(x => x.Id == home.Value.Id));
    }

    [Fact]
    public async Task Delete_rejects_new_default_while_waiting_for_72h_activation()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        var work = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: false, label: "العمل", line: "عنوان العمل");
        Assert.True(work.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var switched = await fixture.Handler.Handle(new UpdateCustomerAddressCommand(
            work.Value.Id, "العمل", "عنوان العمل", fixture.AreaId, 29.37m, 48.12m, null, true, true,
            "العاصمة", "شرق", "4", "12", null, "25", null, null, null), CancellationToken.None);
        Assert.True(switched.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Handler.Handle(new DeleteCustomerAddressCommand(work.Value.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("address.active_delivery_cannot_delete", result.Error.Code);
    }

    [Fact]
    public async Task Delete_allows_non_active_address()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        var work = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: false, label: "العمل", line: "عنوان العمل");
        Assert.True(work.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        profile.ActiveDeliveryAddressId = home.Value.Id;
        profile.ActiveDeliveryAddressLine = home.Value.AddressLine;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Handler.Handle(new DeleteCustomerAddressCommand(work.Value.Id), CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.False(await fixture.Db.CustomerAddresses.AnyAsync(x => x.Id == work.Value.Id));
    }

    [Fact]
    public async Task Delete_pending_address_clears_pending_schedule()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        var work = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: false, label: "العمل", line: "عنوان العمل");
        Assert.True(work.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        profile.ActiveDeliveryAddressId = home.Value.Id;
        profile.ActiveDeliveryAddressLine = home.Value.AddressLine;
        profile.PendingDeliveryAddressId = work.Value.Id;
        profile.PendingDeliveryAddressEffectiveFromUtc = Now.AddHours(72);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Handler.Handle(new DeleteCustomerAddressCommand(work.Value.Id), CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Null(profile.PendingDeliveryAddressId);
        Assert.Null(profile.PendingDeliveryAddressEffectiveFromUtc);
        Assert.Equal(home.Value.Id, profile.ActiveDeliveryAddressId);
    }

    [Fact]
    public async Task Delete_allows_former_active_after_pending_applies()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        var work = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: false, label: "العمل", line: "عنوان العمل");
        Assert.True(work.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        var homeEntity = await fixture.Db.CustomerAddresses.SingleAsync(x => x.Id == home.Value.Id);
        var workEntity = await fixture.Db.CustomerAddresses.SingleAsync(x => x.Id == work.Value.Id);
        homeEntity.IsDefault = false;
        workEntity.IsDefault = true;
        profile.ActiveDeliveryAddressId = home.Value.Id;
        profile.ActiveDeliveryAddressLine = home.Value.AddressLine;
        profile.PendingDeliveryAddressId = work.Value.Id;
        profile.PendingDeliveryAddressEffectiveFromUtc = Now.AddHours(-1);
        await fixture.Db.SaveChangesAsync();

        await fixture.Delivery.TryApplyPendingAsync(profile, CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(work.Value.Id, profile.ActiveDeliveryAddressId);

        var result = await fixture.Handler.Handle(new DeleteCustomerAddressCommand(home.Value.Id), CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.False(await fixture.Db.CustomerAddresses.AnyAsync(x => x.Id == home.Value.Id));
    }

    private static Task<Result<CustomerAddressDto>> CreateAsync(
        Fixture fixture,
        Guid? regionId,
        string? area = null,
        Guid? blockRegionId = null,
        bool isDefault = true,
        string label = "المنزل",
        string line = "قطعة 4، شارع 12")
        => fixture.Handler.Handle(new CreateCustomerAddressCommand(
            label,
            line,
            regionId,
            29.37m,
            48.12m,
            "453",
            isDefault,
            true,
            "العاصمة",
            area,
            "4",
            "12",
            null,
            "25",
            null,
            null,
            blockRegionId), CancellationToken.None);

    private static async Task<Fixture> SeedAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var governorate = new Region
        {
            CountryId = country.Id, Type = RegionType.Governorate, Code = "KW-CAP",
            NameAr = "العاصمة", NameEn = "Capital", IsActive = true
        };
        var area = new Region
        {
            CountryId = country.Id, ParentRegionId = governorate.Id, Type = RegionType.Area,
            Code = "KW-CAP-SHARQ", NameAr = "شرق", NameEn = "Sharq", IsActive = true
        };
        var block = new Region
        {
            CountryId = country.Id, ParentRegionId = area.Id, Type = RegionType.Block,
            Code = "4", NameAr = "4", NameEn = "4", IsActive = true
        };
        db.AddRange(country, governorate, area, block, new CustomerProfile { UserId = customerId });
        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var delivery = new CustomerDeliveryAddressService(db, clock);
        return new Fixture(
            new CustomerAddressHandlers(db, user, Substitute.For<IAuditLogger>(), delivery, clock),
            delivery,
            db,
            customerId,
            governorate.Id,
            area.Id,
            block.Id);
    }

    private sealed record Fixture(
        CustomerAddressHandlers Handler,
        CustomerDeliveryAddressService Delivery,
        AppDbContext Db,
        Guid CustomerId,
        Guid GovernorateId,
        Guid AreaId,
        Guid BlockId);
}
