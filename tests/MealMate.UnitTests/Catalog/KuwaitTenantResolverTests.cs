using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.Catalog;

public sealed class KuwaitTenantResolverTests
{
    [Fact]
    public async Task ResolveAsync_RejectsUserWithoutTenantClaim()
    {
        await using var db = CreateDb();
        var user = CreateUser();
        user.TenantIds.Returns(Array.Empty<Guid>());

        var result = await new KuwaitTenantResolver(db, user).ResolveAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.tenant_required");
    }

    [Fact]
    public async Task ResolveAsync_RejectsMultipleTenantClaims()
    {
        await using var db = CreateDb();
        var user = CreateUser();
        user.TenantIds.Returns(new[] { Guid.NewGuid(), Guid.NewGuid() });

        var result = await new KuwaitTenantResolver(db, user).ResolveAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.single_tenant_required");
    }

    [Fact]
    public async Task ResolveAsync_RejectsTenantFromAnotherCountry()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "SA", NameAr = "السعودية", NameEn = "Saudi Arabia" };
        var tenant = new Tenant { Code = "TEN-SA", NameAr = "MealMate Saudi", NameEn = "MealMate Saudi", Country = country, Domain = "sa.mealmate.app", CurrencyCode = "SAR", TimeZoneId = "Asia/Riyadh", Status = TenantStatus.Active, IsActive = true };
        db.Countries.Add(country);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var user = CreateUser();
        user.TenantIds.Returns(new[] { tenant.Id });

        var result = await new KuwaitTenantResolver(db, user).ResolveAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.tenant_not_kuwait");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsActiveKuwaitTenant()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait" };
        var tenant = new Tenant { Code = "TEN-KW", NameAr = "ميل ميت الكويت", NameEn = "MealMate Kuwait", Country = country, Domain = "kw.mealmate.app", CurrencyCode = "KWD", TimeZoneId = "Asia/Kuwait", Status = TenantStatus.Active, IsActive = true };
        db.Countries.Add(country);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var user = CreateUser();
        user.TenantIds.Returns(new[] { tenant.Id });

        var result = await new KuwaitTenantResolver(db, user).ResolveAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(tenant.Id);
        result.Value.Country.Code.Should().Be("KW");
    }

    private static Persistence.AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static ICurrentUser CreateUser()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.IsInRole(Arg.Any<string>()).Returns(false);
        return user;
    }
}
