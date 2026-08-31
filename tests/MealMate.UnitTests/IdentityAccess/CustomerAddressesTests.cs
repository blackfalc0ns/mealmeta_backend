using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
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
    public async Task Update_default_switch_schedules_delivery_after_72_hours()
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
            "العاصمة", "شرق", "4", "12", "25", null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsDefault);
        Assert.False(result.Value.IsDeliveryActive);
        Assert.Equal(Now.AddHours(72), result.Value.DeliveryEffectiveFromUtc);
        Assert.NotNull(result.Value.DeliveryChangeNotice);
        Assert.Contains("72", result.Value.DeliveryChangeNotice!.Ar);
        Assert.Contains("لن يُفعَّل", result.Value.DeliveryChangeNotice.Ar);
        Assert.Contains("72 hours", result.Value.DeliveryChangeNotice.En);
        Assert.Contains("will not become active", result.Value.DeliveryChangeNotice.En, StringComparison.OrdinalIgnoreCase);

        profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(home.Value.Id, profile.ActiveDeliveryAddressId);
        Assert.Equal(work.Value.Id, profile.PendingDeliveryAddressId);
        Assert.Equal(Now.AddHours(72), profile.PendingDeliveryAddressEffectiveFromUtc);
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
            "العاصمة", "شرق", "4", "12", "25", null, null, null), CancellationToken.None);
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
            new CustomerAddressHandlers(db, user, Substitute.For<IAuditLogger>(), delivery),
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
