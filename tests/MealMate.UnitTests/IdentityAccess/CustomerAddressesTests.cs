using MealMate.Application.Common.Abstractions;
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

    private static Task<Result<CustomerAddressDto>> CreateAsync(
        Fixture fixture,
        Guid? regionId,
        string? area = null,
        Guid? blockRegionId = null)
        => fixture.Handler.Handle(new CreateCustomerAddressCommand(
            "المنزل",
            "قطعة 4، شارع 12",
            regionId,
            29.37m,
            48.12m,
            "453",
            true,
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
        return new Fixture(
            new CustomerAddressHandlers(db, user, Substitute.For<IAuditLogger>()),
            governorate.Id,
            area.Id,
            block.Id);
    }

    private sealed record Fixture(CustomerAddressHandlers Handler, Guid GovernorateId, Guid AreaId, Guid BlockId);
}
