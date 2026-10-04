using FluentAssertions;
using MealMate.Application.Features.AdminCoverage;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.AdminCoverage;

public sealed class AdminCoverageSnapshotTests
{
    [Fact]
    public async Task Snapshot_EmptyCountryReturnsEmptyListsAndUnavailablePercentage()
    {
        await using var db = CreateDb();
        var (country, tenant, scope) = await SeedCountryScope(db, AccessScopeType.Country);
        var handler = CreateHandler(db, tenant.Id, scope.Id);

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Regions.Should().BeEmpty();
        result.Value.Restaurants.Should().BeEmpty();
        result.Value.Links.Should().BeEmpty();
        result.Value.Metrics.ActiveAreaCount.Should().Be(0);
        result.Value.Metrics.FullyCoveredAreaPercent.Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_SuperAdminCanExplicitlyReadKuwaitWithoutPermissionOrTenantClaims()
    {
        await using var db = CreateDb();
        var (country, tenant, scope) = await SeedCountryScope(db, AccessScopeType.Country);
        var handler = CreateHandler(db, tenant.Id, scope.Id, superAdmin: true);

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Filters.CountryCode.Should().Be("KW");
        result.Value.Regions.Should().BeEmpty();
    }

    [Fact]
    public async Task Snapshot_RegionAndRestaurantConstraintsHideOtherAreasAndLinksBeforeMetrics()
    {
        await using var db = CreateDb();
        var (country, tenant, _) = await SeedCountryScope(db, AccessScopeType.Country);
        var governorate = NewRegion(country.Id, RegionType.Governorate, null, "Hawalli");
        var allowedArea = NewRegion(country.Id, RegionType.Area, governorate.Id, "Salmiya");
        var allowedBlock = NewRegion(country.Id, RegionType.Block, allowedArea.Id, "Block 1");
        var hiddenArea = NewRegion(country.Id, RegionType.Area, governorate.Id, "Jabriya");
        var allowedUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "coverage-active", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
        var allowedRestaurant = NewRestaurant(allowedUser.Id, country.Id, allowedArea.Id, "Allowed restaurant");
        var otherUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "coverage-other", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
        var otherRestaurant = NewRestaurant(otherUser.Id, country.Id, hiddenArea.Id, "Other restaurant");
        var allowedLink = NewLink(allowedRestaurant.Id, allowedArea.Id);
        var duplicateLink = NewLink(allowedRestaurant.Id, allowedArea.Id);
        var hiddenLink = NewLink(allowedRestaurant.Id, hiddenArea.Id);
        var otherLink = NewLink(otherRestaurant.Id, hiddenArea.Id);
        var scope = new AccessScope
        {
            Name = "Salmiya operations",
            ScopeType = AccessScopeType.Region,
            CountryId = country.Id,
            TenantId = tenant.Id,
            RegionId = allowedArea.Id,
            RestaurantId = allowedRestaurant.Id,
            IsActive = true,
        };
        db.Regions.AddRange(governorate, allowedArea, allowedBlock, hiddenArea);
        db.Users.AddRange(allowedUser, otherUser);
        db.RestaurantProfiles.AddRange(allowedRestaurant, otherRestaurant);
        db.RestaurantServiceRegions.AddRange(allowedLink, duplicateLink, hiddenLink, otherLink);
        db.AccessScopes.Add(scope);
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, tenant.Id, scope.Id);

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Restaurants.Select(item => item.Id).Should().Equal(allowedRestaurant.Id);
        result.Value.Regions.Select(item => item.Id).Should().Contain(allowedArea.Id).And.Contain(allowedBlock.Id).And.NotContain(hiddenArea.Id);
        result.Value.Links.Should().OnlyContain(link => link.RegionId == allowedArea.Id);
        result.Value.Links.Should().HaveCount(2);
        result.Value.Restaurants.Single().OperationallyActive.Should().BeTrue();
        result.Value.Restaurants.Single().AccountActive.Should().BeTrue();
        result.Value.Metrics.ActiveAreaCount.Should().Be(1);
        result.Value.Metrics.FullyCoveredAreaCount.Should().Be(1);
        result.Value.Metrics.SingleRestaurantAreaCount.Should().Be(1);
    }

    [Fact]
    public async Task Snapshot_SuspendedAccountsRemainVisibleButDoNotCountAsCoverage()
    {
        await using var db = CreateDb();
        var (country, tenant, scope) = await SeedCountryScope(db, AccessScopeType.Country);
        var governorate = NewRegion(country.Id, RegionType.Governorate, null, "Hawalli");
        var area = NewRegion(country.Id, RegionType.Area, governorate.Id, "Salmiya");
        var suspendedUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "coverage-suspended", AccountStatus = AccountStatus.Suspended, UserType = UserType.Restaurant };
        var suspendedRestaurant = NewRestaurant(suspendedUser.Id, country.Id, area.Id, "Suspended restaurant");
        db.Regions.AddRange(governorate, area);
        db.Users.Add(suspendedUser);
        db.RestaurantProfiles.Add(suspendedRestaurant);
        db.RestaurantServiceRegions.Add(NewLink(suspendedRestaurant.Id, area.Id));
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, tenant.Id, scope.Id);

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.Value.Restaurants.Single().AccountActive.Should().BeFalse();
        result.Value.Metrics.FullyCoveredAreaCount.Should().Be(0);
        result.Value.Metrics.UncoveredAreaCount.Should().Be(1);
    }

    [Fact]
    public async Task Snapshot_BlockScopeKeepsParentAreaContextWithoutClaimingAreaCoverage()
    {
        await using var db = CreateDb();
        var (country, tenant, _) = await SeedCountryScope(db, AccessScopeType.Country);
        var governorate = NewRegion(country.Id, RegionType.Governorate, null, "Hawalli");
        var area = NewRegion(country.Id, RegionType.Area, governorate.Id, "Salmiya");
        var block = NewRegion(country.Id, RegionType.Block, area.Id, "Block 1");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "coverage-block", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
        var restaurant = NewRestaurant(user.Id, country.Id, area.Id, "Area restaurant");
        var parentAreaLink = NewLink(restaurant.Id, area.Id);
        var scope = new AccessScope
        {
            Name = "Block operations",
            ScopeType = AccessScopeType.Region,
            CountryId = country.Id,
            TenantId = tenant.Id,
            RegionId = block.Id,
            IsActive = true,
        };
        db.Regions.AddRange(governorate, area, block);
        db.Users.Add(user);
        db.RestaurantProfiles.Add(restaurant);
        db.RestaurantServiceRegions.Add(parentAreaLink);
        db.AccessScopes.Add(scope);
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, tenant.Id, scope.Id);

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Regions.Should().Contain(region => region.Id == area.Id && region.IsScopeContextOnly);
        result.Value.Regions.Should().Contain(region => region.Id == block.Id && !region.IsScopeContextOnly);
        result.Value.Links.Should().ContainSingle(link => link.RegionId == area.Id);
        result.Value.Metrics.ActiveAreaCount.Should().Be(0);
        result.Value.Metrics.FullyCoveredAreaCount.Should().Be(0);
        result.Value.Metrics.FullyCoveredAreaPercent.Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_ExcludesArchivedAndSoftDeletedProfiles()
    {
        await using var db = CreateDb();
        var (country, tenant, scope) = await SeedCountryScope(db, AccessScopeType.Country);
        var archivedUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "coverage-archived", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
        var archived = NewRestaurant(archivedUser.Id, country.Id, null, "Archived");
        archived.IsArchived = true;
        var deletedUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "coverage-deleted", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
        var deleted = NewRestaurant(deletedUser.Id, country.Id, null, "Deleted");
        deleted.IsDeleted = true;
        db.Users.AddRange(archivedUser, deletedUser);
        db.RestaurantProfiles.AddRange(archived, deleted);
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, tenant.Id, scope.Id);

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.Value.Restaurants.Should().BeEmpty();
    }

    [Fact]
    public async Task Snapshot_RejectsRestaurantIdOutsideScope()
    {
        await using var db = CreateDb();
        var (country, tenant, scope) = await SeedCountryScope(db, AccessScopeType.Restaurant);
        scope.RestaurantId = Guid.NewGuid();
        db.AccessScopes.Update(scope);
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, tenant.Id, scope.Id);

        var result = await handler.Handle(Query(restaurantId: Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.Forbidden);
    }

    [Fact]
    public void Grant_IntersectsRestaurantAndRegionConstraintsOnOneScope()
    {
        var tenantId = Guid.NewGuid();
        var countryId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var scope = new AccessScope
        {
            ScopeType = AccessScopeType.Region,
            TenantId = tenantId,
            CountryId = countryId,
            RegionId = regionId,
            RestaurantId = restaurantId,
            IsActive = true,
        };

        var grant = CoverageScopeGrant.TryCreate(scope, tenantId, countryId);

        grant.Should().NotBeNull();
        grant!.IsCountryWide.Should().BeFalse();
        grant.RegionId.Should().Be(regionId);
        grant.RestaurantId.Should().Be(restaurantId);
    }

    [Fact]
    public void Grant_DoesNotTurnRestaurantScopeWithCountryIdIntoCountryWideAccess()
    {
        var tenantId = Guid.NewGuid();
        var countryId = Guid.NewGuid();
        var scope = new AccessScope
        {
            ScopeType = AccessScopeType.Restaurant,
            TenantId = tenantId,
            CountryId = countryId,
            RestaurantId = Guid.NewGuid(),
            IsActive = true,
        };

        var grant = CoverageScopeGrant.TryCreate(scope, tenantId, countryId);

        grant.Should().NotBeNull();
        grant!.IsCountryWide.Should().BeFalse();
        grant.RestaurantId.Should().NotBeNull();
    }

    [Fact]
    public void Grant_RejectsForeignAndUnsupportedScopes()
    {
        var tenantId = Guid.NewGuid();
        var countryId = Guid.NewGuid();
        var foreignCountry = new AccessScope { ScopeType = AccessScopeType.Country, CountryId = Guid.NewGuid(), IsActive = true };
        var unsupported = new AccessScope
        {
            ScopeType = AccessScopeType.Entity,
            EntityId = Guid.NewGuid(),
            EntityType = "UnsupportedEntity",
            IsActive = true,
        };

        CoverageScopeGrant.TryCreate(foreignCountry, tenantId, countryId).Should().BeNull();
        CoverageScopeGrant.TryCreate(unsupported, tenantId, countryId).Should().BeNull();
    }

    [Fact]
    public void Grant_EntityTenantScopeIsCountryWideOnlyForTheResolvedTenant()
    {
        var tenantId = Guid.NewGuid();
        var countryId = Guid.NewGuid();
        var scope = new AccessScope
        {
            ScopeType = AccessScopeType.Entity,
            EntityType = "Tenant",
            EntityId = tenantId,
            CountryId = countryId,
            IsActive = true,
        };

        CoverageScopeGrant.TryCreate(scope, tenantId, countryId)!.IsCountryWide.Should().BeTrue();
        CoverageScopeGrant.TryCreate(scope, Guid.NewGuid(), countryId).Should().BeNull();
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static async Task<(Country Country, Tenant Tenant, AccessScope Scope)> SeedCountryScope(AppDbContext db, AccessScopeType type)
    {
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "KW", IsActive = true };
        var tenant = new Tenant
        {
            Code = "TEN-KW",
            NameAr = "الكويت",
            NameEn = "Kuwait",
            CountryId = country.Id,
            Country = country,
            Domain = "mealmate.example",
            CurrencyCode = "KWD",
            TimeZoneId = "Asia/Kuwait",
            Status = TenantStatus.Active,
            IsActive = true,
        };
        var scope = new AccessScope
        {
            Name = "Coverage",
            ScopeType = type,
            CountryId = country.Id,
            TenantId = tenant.Id,
            RestaurantId = type == AccessScopeType.Restaurant ? Guid.NewGuid() : null,
            IsActive = true,
        };
        db.Countries.Add(country);
        db.Tenants.Add(tenant);
        db.AccessScopes.Add(scope);
        await db.SaveChangesAsync();
        return (country, tenant, scope);
    }

    private static GetAdminCoverageSnapshotQueryHandler CreateHandler(AppDbContext db, Guid tenantId, Guid scopeId, bool superAdmin = false)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.NewGuid());
        user.UserType.Returns(UserType.Admin);
        user.Roles.Returns(superAdmin ? [AppRoles.SuperAdmin] : [AppRoles.CountryAdmin]);
        user.Permissions.Returns(superAdmin ? [] : [PermissionCodes.RestaurantRegView]);
        user.AccessScopeIds.Returns(superAdmin ? [] : [scopeId]);
        user.TenantIds.Returns(superAdmin ? [] : [tenantId]);
        user.IsInRole(AppRoles.SuperAdmin).Returns(superAdmin);
        user.HasPermission(PermissionCodes.RestaurantRegView).Returns(!superAdmin);
        var tenantResolver = new KuwaitTenantResolver(db, user);
        var access = new AdminCoverageAccess(db, user, tenantResolver);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        return new GetAdminCoverageSnapshotQueryHandler(db, access, clock);
    }

    private static GetAdminCoverageSnapshotQuery Query(Guid? restaurantId = null, string countryCode = "KW")
        => new(countryCode, null, restaurantId, new DateOnly(2026, 10, 3), "");

    private static Region NewRegion(Guid countryId, RegionType type, Guid? parentId, string name)
        => new()
        {
            CountryId = countryId,
            Type = type,
            ParentRegionId = parentId,
            Code = Guid.NewGuid().ToString("N"),
            NameAr = name,
            NameEn = name,
            IsActive = true,
        };

    private static RestaurantProfile NewRestaurant(Guid userId, Guid? countryId, Guid? primaryRegionId, string name)
        => new()
        {
            UserId = userId,
            CountryId = countryId,
            PrimaryRegionId = primaryRegionId,
            TradeNameAr = name,
            TradeNameEn = name,
            LegalNameAr = name,
            LegalNameEn = name,
            IsOperationallyActive = true,
        };

    private static RestaurantServiceRegion NewLink(Guid restaurantId, Guid regionId)
        => new()
        {
            RestaurantId = restaurantId,
            RegionId = regionId,
            IsActive = true,
            DeliveryWindowStart = new TimeOnly(22, 0),
            DeliveryWindowEnd = new TimeOnly(2, 0),
        };
    [Fact]
    public async Task Snapshot_AreaGrantUsesAncestorGovernorateServiceLinkOnlyForAuthorizedAreaMetrics()
    {
        await using var db = CreateDb();
        var (country, tenant, _) = await SeedCountryScope(db, AccessScopeType.Country);
        var governorate = NewRegion(country.Id, RegionType.Governorate, null, "Hawalli");
        var area = NewRegion(country.Id, RegionType.Area, governorate.Id, "Salmiya");
        var siblingArea = NewRegion(country.Id, RegionType.Area, governorate.Id, "Jabriya");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "coverage-ancestor-link", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
        var restaurant = NewRestaurant(user.Id, country.Id, area.Id, "Governorate linked restaurant");
        var governorateLink = NewLink(restaurant.Id, governorate.Id);
        var scope = new AccessScope
        {
            Name = "Salmiya operations",
            ScopeType = AccessScopeType.Region,
            CountryId = country.Id,
            TenantId = tenant.Id,
            RegionId = area.Id,
            IsActive = true,
        };
        db.Regions.AddRange(governorate, area, siblingArea);
        db.Users.Add(user);
        db.RestaurantProfiles.Add(restaurant);
        db.RestaurantServiceRegions.Add(governorateLink);
        db.AccessScopes.Add(scope);
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, tenant.Id, scope.Id);

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Restaurants.Should().ContainSingle(item => item.Id == restaurant.Id);
        result.Value.Regions.Should().Contain(region => region.Id == governorate.Id && region.IsScopeContextOnly);
        result.Value.Regions.Should().Contain(region => region.Id == area.Id && !region.IsScopeContextOnly);
        result.Value.Regions.Should().NotContain(region => region.Id == siblingArea.Id);
        result.Value.Links.Should().ContainSingle(link => link.RegionId == governorate.Id);
        result.Value.Metrics.ActiveAreaCount.Should().Be(1);
        result.Value.Metrics.FullyCoveredAreaCount.Should().Be(1);
    }
}


