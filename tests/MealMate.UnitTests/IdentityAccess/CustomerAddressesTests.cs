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
    public async Task Create_rejects_missing_coordinates_when_region_has_no_centroid()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "المنزل",
            "قطعة 4",
            null,
            null,
            null,
            null,
            true,
            true,
            null,
            null,
            null,
            null,
            null,
            null,
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
    public async Task Create_default_rejects_and_does_not_save_when_scheduled_subscription_restaurant_does_not_service_region()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);

        var area2 = new Region
        {
            CountryId = fixture.GovernorateId,
            ParentRegionId = fixture.GovernorateId,
            Type = RegionType.Area,
            Code = "KW-CAP-FARWANIYA",
            NameAr = "الفروانية",
            NameEn = "Farwaniya",
            IsActive = true
        };
        fixture.Db.Regions.Add(area2);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم العاصمة",
            TradeNameEn = "Capital Rest",
            LegalNameAr = "شركة العاصمة",
            LegalNameEn = "Capital Co",
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
            Status = SubscriptionStatus.Scheduled, // Scheduled subscription!
            StartDate = DateOnly.FromDateTime(Now.AddDays(4)),
            EndDate = DateOnly.FromDateTime(Now.AddDays(34)),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100, "KWD"),
            SelectedTier = RestaurantTier.Basic,
            RegionId = fixture.AreaId
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(Now.AddDays(4)),
            Status = SubscriptionDayStatus.Open,
            RestaurantId = null, // Not yet selected by customer!
            DeliveryAddressId = home.Value.Id,
            DeliveryRegionId = fixture.AreaId
        };
        var snapshot = new MealMate.Domain.CalendarMeals.SubscriptionDayMealSnapshot
        {
            SubscriptionId = subscription.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            RestaurantId = restaurant.Id, // Frozen checkout hold restaurant
            ProgramId = subscription.ProgramId,
            MenuPlanVersionId = Guid.NewGuid(),
            RequiredSlotsJson = "[]",
            MealsJson = "[]"
        };
        fixture.Db.AddRange(restaurant, serviceRegion, subscription, day, snapshot);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "عنوان جديد", "قطعة 1 شارع 2", area2.Id, 29.30m, 48.00m, null, true, true,
            "الفروانية", "الفروانية", "1", "2", null, "10", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("address.restaurant_does_not_service_region", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);

        // Verify the address was NOT saved in database!
        Assert.False(await fixture.Db.CustomerAddresses.AnyAsync(x => x.RegionId == area2.Id));
    }

    [Fact]
    public async Task Update_default_rejects_and_does_not_change_default_when_scheduled_subscription_restaurant_does_not_service_region()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);

        var area2 = new Region
        {
            CountryId = fixture.GovernorateId,
            ParentRegionId = fixture.GovernorateId,
            Type = RegionType.Area,
            Code = "KW-CAP-JAHRA",
            NameAr = "الجهراء",
            NameEn = "Jahra",
            IsActive = true
        };
        fixture.Db.Regions.Add(area2);
        await fixture.Db.SaveChangesAsync();

        // Add second address as non-default first
        var work = await fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "العمل", "الجهراء قطعة 5", area2.Id, 29.33m, 47.67m, null, false, true,
            "الجهراء", "الجهراء", "5", "10", null, "1", null, null, null), CancellationToken.None);
        Assert.True(work.IsSuccess);
        Assert.False(work.Value.IsDefault);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم الصحي",
            TradeNameEn = "Healthy Rest",
            LegalNameAr = "شركة الصحي",
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
        var subscription = new Subscription
        {
            CustomerUserId = fixture.CustomerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Scheduled,
            StartDate = DateOnly.FromDateTime(Now.AddDays(4)),
            EndDate = DateOnly.FromDateTime(Now.AddDays(34)),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100, "KWD"),
            SelectedTier = RestaurantTier.Basic,
            RegionId = fixture.AreaId
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(Now.AddDays(4)),
            Status = SubscriptionDayStatus.Open,
            RestaurantId = null,
            DeliveryAddressId = home.Value.Id,
            DeliveryRegionId = fixture.AreaId
        };
        var poolRestaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            RestaurantId = restaurant.Id,
            ProgramId = subscription.ProgramId,
            BundleId = subscription.BundleId,
            RegionId = fixture.AreaId,
            Tier = RestaurantTier.Basic,
            NameAr = restaurant.TradeNameAr,
            NameEn = restaurant.TradeNameEn,
            IsInitiallySelected = true,
            OfferingSnapshotJson = "{}",
            CapturedAtUtc = Now
        };
        fixture.Db.AddRange(restaurant, serviceRegion, subscription, day, poolRestaurant);
        await fixture.Db.SaveChangesAsync();

        // Now attempt to set work address as default
        var result = await fixture.Handler.Handle(new UpdateCustomerAddressCommand(
            work.Value.Id, "العمل", "الجهراء قطعة 5", area2.Id, 29.33m, 47.67m, null, true, true,
            "الجهراء", "الجهراء", "5", "10", null, "1", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("address.restaurant_does_not_service_region", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);

        // Verify work address was NOT set to default in database!
        var workInDb = await fixture.Db.CustomerAddresses.SingleAsync(x => x.Id == work.Value.Id);
        Assert.False(workInDb.IsDefault);

        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(home.Value.Id, profile.ActiveDeliveryAddressId);
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

    [Fact]
    public async Task Create_manual_from_list_without_coordinates_auto_populates_region_centroid()
    {
        var fixture = await SeedAsync();
        var command = new CreateCustomerAddressCommand(
            "المنزل",
            "السالمية، شارع 2",
            fixture.SalmiyaAreaId,
            null,
            null,
            null,
            true,
            true,
            "حولي",
            "السالمية",
            null,
            "شارع 2",
            null,
            "10",
            null,
            null);

        var result = await fixture.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(29.3400m, result.Value.Latitude);
        Assert.Equal(48.0800m, result.Value.Longitude);
    }

    [Fact]
    public async Task Create_manual_from_list_with_block_without_coordinates_auto_populates_block_centroid()
    {
        var fixture = await SeedAsync();
        var command = new CreateCustomerAddressCommand(
            "المنزل",
            "شرق، قطعة 4",
            fixture.AreaId,
            null,
            null,
            null,
            true,
            true,
            "العاصمة",
            "شرق",
            "4",
            "شارع 1",
            null,
            "10",
            null,
            null,
            fixture.BlockId);

        var result = await fixture.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(29.3785m, result.Value.Latitude);
        Assert.Equal(47.9910m, result.Value.Longitude);
    }

    [Fact]
    public async Task Create_manual_without_coordinates_and_without_region_fails_validation()
    {
        var validator = new CustomerAddressCommandValidator();
        var command = new CreateCustomerAddressCommand(
            "المنزل",
            "شارع بدون منطقة",
            null,
            null,
            null,
            null,
            true,
            true,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == MessageCodes.Validation.RequiredField);
    }

    [Fact]
    public async Task Update_manual_from_list_without_coordinates_auto_populates_new_region_centroid()
    {
        var fixture = await SeedAsync();
        var created = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(created.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var updateCmd = new UpdateCustomerAddressCommand(
            created.Value.Id,
            "المنزل الجديد",
            "السالمية شارع 10",
            fixture.SalmiyaAreaId,
            null,
            null,
            null,
            true,
            true,
            "حولي",
            "السالمية",
            null,
            "شارع 10",
            null,
            "15",
            null,
            null);

        var updateResult = await fixture.Handler.Handle(updateCmd, CancellationToken.None);

        Assert.True(updateResult.IsSuccess, $"{updateResult.Error?.Code}: {updateResult.Error?.Description}");
        Assert.Equal(fixture.SalmiyaAreaId, updateResult.Value.RegionId);
        Assert.Equal(29.3400m, updateResult.Value.Latitude);
        Assert.Equal(48.0800m, updateResult.Value.Longitude);
    }

    [Fact]
    public async Task Preview_with_active_subscription_returns_subscription_pool_restaurants_and_validates_coverage()
    {
        var fixture = await SeedAsync();
        var restA = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), TradeNameAr = "مطعم باقة أ", TradeNameEn = "Pool Rest A",
            LegalNameAr = "شركة مطعم باقة أ", LegalNameEn = "Pool Rest A Co",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true
        };
        var restB = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), TradeNameAr = "مطعم باقة ب", TradeNameEn = "Pool Rest B",
            LegalNameAr = "شركة مطعم باقة ب", LegalNameEn = "Pool Rest B Co",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true
        };
        var unrelatedRest = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), TradeNameAr = "مطعم عام", TradeNameEn = "General Rest",
            LegalNameAr = "شركة مطعم عام", LegalNameEn = "General Rest Co",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true
        };
        var srA = new RestaurantServiceRegion { RestaurantId = restA.Id, RegionId = fixture.SalmiyaAreaId, IsActive = true };
        var srB = new RestaurantServiceRegion { RestaurantId = restB.Id, RegionId = fixture.AreaId, IsActive = true };
        var srUnrelated = new RestaurantServiceRegion { RestaurantId = unrelatedRest.Id, RegionId = fixture.SalmiyaAreaId, IsActive = true };

        var sub = new Subscription
        {
            CustomerUserId = fixture.CustomerId, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active, StartDate = DateOnly.FromDateTime(Now), EndDate = DateOnly.FromDateTime(Now.AddDays(30)),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100, "KWD"), RegionId = fixture.AreaId
        };
        var poolSnapshot = new MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = sub.Id, RestaurantCount = 2
        };
        var snapA = new MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = poolSnapshot.Id, SubscriptionId = sub.Id, RestaurantId = restA.Id,
            ProgramId = sub.ProgramId, BundleId = sub.BundleId, RegionId = fixture.AreaId,
            NameAr = restA.TradeNameAr, NameEn = restA.TradeNameEn
        };
        var snapB = new MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = poolSnapshot.Id, SubscriptionId = sub.Id, RestaurantId = restB.Id,
            ProgramId = sub.ProgramId, BundleId = sub.BundleId, RegionId = fixture.AreaId,
            NameAr = restB.TradeNameAr, NameEn = restB.TradeNameEn
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = sub.Id, ServiceDate = DateOnly.FromDateTime(Now.AddDays(1)),
            Status = SubscriptionDayStatus.Open, RestaurantId = restA.Id, DeliveryRegionId = fixture.AreaId
        };
        fixture.Db.AddRange(restA, restB, unrelatedRest, srA, srB, srUnrelated, sub, poolSnapshot, snapA, snapB, day);
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            RegionId: fixture.SalmiyaAreaId,
            IsDefault: true), CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.True(preview.Value.CanSetAsDefault);
        Assert.True(preview.Value.IsCurrentSubscriptionRestaurantCovered);
        Assert.True(preview.Value.HasActiveSubscription);
        Assert.Single(preview.Value.ServingRestaurants);
        Assert.Equal(restA.Id, preview.Value.ServingRestaurants[0].Id);
    }

    [Fact]
    public async Task Preview_with_uncovered_subscription_restaurant_returns_canSetAsDefault_false_and_uncovered_name()
    {
        var fixture = await SeedAsync();
        var restA = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), TradeNameAr = "مطعم العاصمة المغلق", TradeNameEn = "Capital Rest",
            LegalNameAr = "شركة مطعم العاصمة المغلق", LegalNameEn = "Capital Rest Co",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true
        };
        var srA = new RestaurantServiceRegion { RestaurantId = restA.Id, RegionId = fixture.AreaId, IsActive = true };

        var sub = new Subscription
        {
            CustomerUserId = fixture.CustomerId, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active, StartDate = DateOnly.FromDateTime(Now), EndDate = DateOnly.FromDateTime(Now.AddDays(30)),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100, "KWD"), RegionId = fixture.AreaId
        };
        var poolSnapshot = new MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = sub.Id, RestaurantCount = 1
        };
        var snapA = new MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = poolSnapshot.Id, SubscriptionId = sub.Id, RestaurantId = restA.Id,
            ProgramId = sub.ProgramId, BundleId = sub.BundleId, RegionId = fixture.AreaId,
            NameAr = restA.TradeNameAr, NameEn = restA.TradeNameEn
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = sub.Id, ServiceDate = DateOnly.FromDateTime(Now.AddDays(1)),
            Status = SubscriptionDayStatus.Open, RestaurantId = restA.Id, DeliveryRegionId = fixture.AreaId
        };
        fixture.Db.AddRange(restA, srA, sub, poolSnapshot, snapA, day);
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            RegionId: fixture.SalmiyaAreaId,
            IsDefault: true), CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.False(preview.Value.CanSetAsDefault);
        Assert.False(preview.Value.IsCurrentSubscriptionRestaurantCovered);
        Assert.Equal(restA.TradeNameAr, preview.Value.UncoveredRestaurantNameAr);
        Assert.NotNull(preview.Value.ConflictMessage);
    }

    [Fact]
    public async Task Preview_by_existing_address_id_checks_coverage()
    {
        var fixture = await SeedAsync();
        var created = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(created.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            AddressId: created.Value.Id), CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.Equal(fixture.AreaId, preview.Value.ResolvedRegionId);
    }

    [Fact]
    public async Task Preview_paginates_serving_restaurants_with_metadata()
    {
        var fixture = await SeedAsync();
        var restaurants = await SeedCoveredRestaurantsAsync(fixture, "مطعم ألف", "مطعم باء", "مطعم جيم");

        var firstPage = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            RegionId: fixture.AreaId, Page: 1, PageSize: 2), CancellationToken.None);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value.ServingRestaurants.Count);
        Assert.Equal(restaurants[0].Id, firstPage.Value.ServingRestaurants[0].Id);
        Assert.Equal(restaurants[1].Id, firstPage.Value.ServingRestaurants[1].Id);
        Assert.NotNull(firstPage.Value.Pagination);
        Assert.Equal(3, firstPage.Value.Pagination!.TotalCount);
        Assert.Equal(1, firstPage.Value.Pagination.Page);
        Assert.Equal(2, firstPage.Value.Pagination.PageSize);
        Assert.Equal(2, firstPage.Value.Pagination.TotalPages);
        Assert.True(firstPage.Value.Pagination.HasNextPage);
        Assert.False(firstPage.Value.Pagination.HasPreviousPage);

        var secondPage = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            RegionId: fixture.AreaId, Page: 2, PageSize: 2), CancellationToken.None);
        Assert.True(secondPage.IsSuccess);
        var item = Assert.Single(secondPage.Value.ServingRestaurants);
        Assert.Equal(restaurants[2].Id, item.Id);
        Assert.NotNull(secondPage.Value.Pagination);
        Assert.Equal(2, secondPage.Value.Pagination!.Page);
        Assert.False(secondPage.Value.Pagination.HasNextPage);
        Assert.True(secondPage.Value.Pagination.HasPreviousPage);
    }

    [Fact]
    public async Task Preview_without_paging_keeps_legacy_shape_and_full_list()
    {
        var fixture = await SeedAsync();
        await SeedCoveredRestaurantsAsync(fixture, "مطعم ألف", "مطعم باء", "مطعم جيم");

        var preview = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            RegionId: fixture.AreaId), CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.Equal(3, preview.Value.ServingRestaurants.Count);
        Assert.Null(preview.Value.Pagination);
    }

    [Fact]
    public async Task Preview_paging_opts_in_with_page_size_only_and_defaults_page()
    {
        var fixture = await SeedAsync();
        await SeedCoveredRestaurantsAsync(fixture, "مطعم ألف", "مطعم باء", "مطعم جيم");

        var preview = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            RegionId: fixture.AreaId, PageSize: 2), CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value.ServingRestaurants.Count);
        Assert.NotNull(preview.Value.Pagination);
        Assert.Equal(1, preview.Value.Pagination!.Page);
        Assert.Equal(2, preview.Value.Pagination.PageSize);
        Assert.True(preview.Value.Pagination.HasNextPage);
    }

    [Fact]
    public async Task Preview_page_beyond_range_returns_empty_items_with_metadata()
    {
        var fixture = await SeedAsync();
        await SeedCoveredRestaurantsAsync(fixture, "مطعم ألف", "مطعم باء", "مطعم جيم");

        var preview = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            RegionId: fixture.AreaId, Page: 5, PageSize: 2), CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.Empty(preview.Value.ServingRestaurants);
        Assert.NotNull(preview.Value.Pagination);
        Assert.Equal(3, preview.Value.Pagination!.TotalCount);
        Assert.Equal(5, preview.Value.Pagination.Page);
        Assert.Equal(2, preview.Value.Pagination.TotalPages);
        Assert.False(preview.Value.Pagination.HasNextPage);
        Assert.True(preview.Value.Pagination.HasPreviousPage);
    }

    [Fact]
    public async Task Preview_by_existing_address_id_paginates_serving_restaurants()
    {
        var fixture = await SeedAsync();
        var created = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(created.IsSuccess);
        await SeedCoveredRestaurantsAsync(fixture, "مطعم ألف", "مطعم باء", "مطعم جيم");

        var preview = await fixture.Handler.Handle(new PreviewCustomerAddressCoverageCommand(
            AddressId: created.Value.Id, Page: 2, PageSize: 2), CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.Equal(fixture.AreaId, preview.Value.ResolvedRegionId);
        var item = Assert.Single(preview.Value.ServingRestaurants);
        Assert.Equal("مطعم جيم", item.NameAr);
        Assert.NotNull(preview.Value.Pagination);
        Assert.Equal(3, preview.Value.Pagination!.TotalCount);
    }

    [Fact]
    public async Task SetDefault_sets_existing_address_as_default_when_covered()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        var work = await fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "العمل", "شرق قطعة 4 شارع 1", fixture.AreaId, 29.3780m, 47.9900m, null, false, true,
            "العاصمة", "شرق", "4", "1", null, "5", null, null, null), CancellationToken.None);
        Assert.True(work.IsSuccess);
        await fixture.Db.SaveChangesAsync();

        var setDefault = await fixture.Handler.Handle(new SetDefaultCustomerAddressCommand(work.Value.Id), CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        Assert.True(setDefault.IsSuccess);
        Assert.True(setDefault.Value.IsDefault);
        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(work.Value.Id, profile.ActiveDeliveryAddressId);
    }

    [Fact]
    public async Task SetDefault_rejects_when_subscription_restaurant_does_not_service_region()
    {
        var fixture = await SeedAsync();
        var home = await CreateAsync(fixture, regionId: fixture.AreaId, isDefault: true);
        Assert.True(home.IsSuccess);
        var work = await fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "العمل", "السالمية شارع 1", fixture.SalmiyaAreaId, 29.3400m, 48.0800m, null, false, true,
            "حولي", "السالمية", "1", "1", null, "5", null, null, null), CancellationToken.None);
        Assert.True(work.IsSuccess);

        var restA = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), TradeNameAr = "مطعم غير مغطي", TradeNameEn = "Non Serving Rest",
            LegalNameAr = "شركة مطعم غير مغطي", LegalNameEn = "Non Serving Rest Co",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true
        };
        var srA = new RestaurantServiceRegion { RestaurantId = restA.Id, RegionId = fixture.AreaId, IsActive = true };
        var sub = new Subscription
        {
            CustomerUserId = fixture.CustomerId, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active, StartDate = DateOnly.FromDateTime(Now), EndDate = DateOnly.FromDateTime(Now.AddDays(30)),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100, "KWD"), RegionId = fixture.AreaId
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = sub.Id, ServiceDate = DateOnly.FromDateTime(Now.AddDays(1)),
            Status = SubscriptionDayStatus.Open, RestaurantId = restA.Id, DeliveryRegionId = fixture.AreaId
        };
        fixture.Db.AddRange(restA, srA, sub, day);
        await fixture.Db.SaveChangesAsync();

        var setDefault = await fixture.Handler.Handle(new SetDefaultCustomerAddressCommand(work.Value.Id), CancellationToken.None);

        Assert.True(setDefault.IsFailure);
        Assert.Equal("address.restaurant_does_not_service_region", setDefault.Error.Code);
        var workEntity = await fixture.Db.CustomerAddresses.SingleAsync(x => x.Id == work.Value.Id);
        Assert.False(workEntity.IsDefault);
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

    private static async Task<List<RestaurantProfile>> SeedCoveredRestaurantsAsync(Fixture fixture, params string[] tradeNamesAr)
    {
        var restaurants = tradeNamesAr.Select(n => new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = n,
            TradeNameEn = n + " En",
            LegalNameAr = n + " شركة",
            LegalNameEn = n + " Co",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        }).ToList();
        foreach (var restaurant in restaurants)
        {
            fixture.Db.Add(new RestaurantServiceRegion
            {
                RestaurantId = restaurant.Id,
                RegionId = fixture.AreaId,
                IsActive = true
            });
        }
        fixture.Db.AddRange(restaurants);
        await fixture.Db.SaveChangesAsync();
        return restaurants;
    }

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
            Code = "KW-CAP-SHARQ", NameAr = "شرق", NameEn = "Sharq", IsActive = true,
            CentroidLatitude = 29.3780m, CentroidLongitude = 47.9900m
        };
        var salmiya = new Region
        {
            CountryId = country.Id, ParentRegionId = governorate.Id, Type = RegionType.Area,
            Code = "KW-HAW-SALMIYA", NameAr = "السالمية", NameEn = "Salmiya", IsActive = true,
            CentroidLatitude = 29.3400m, CentroidLongitude = 48.0800m
        };
        var block = new Region
        {
            CountryId = country.Id, ParentRegionId = area.Id, Type = RegionType.Block,
            Code = "4", NameAr = "4", NameEn = "4", IsActive = true,
            CentroidLatitude = 29.3785m, CentroidLongitude = 47.9910m
        };
        db.AddRange(country, governorate, area, salmiya, block, new CustomerProfile { UserId = customerId });
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
            block.Id,
            salmiya.Id);
    }

    private sealed record Fixture(
        CustomerAddressHandlers Handler,
        CustomerDeliveryAddressService Delivery,
        AppDbContext Db,
        Guid CustomerId,
        Guid GovernorateId,
        Guid AreaId,
        Guid BlockId,
        Guid SalmiyaAreaId);
}
