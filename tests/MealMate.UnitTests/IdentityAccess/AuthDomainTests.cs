using FluentAssertions;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class PermissionCatalogTests
{
    [Fact]
    public void Catalog_contains_unique_codes()
    {
        var codes = PermissionCodes.Catalog.Select(x => x.Code).ToList();
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().Contain(PermissionCodes.RolesManage);
        codes.Should().Contain(PermissionCodes.AuditView);
    }

    [Fact]
    public void AppRoles_include_all_actors()
    {
        AppRoles.All.Should().Contain(new[]
        {
            AppRoles.SuperAdmin,
            AppRoles.Customer,
            AppRoles.RestaurantOwner,
            AppRoles.Driver,
            AppRoles.Influencer,
        });
    }
}

public class RefreshTokenEntityTests
{
    [Fact]
    public void IsActive_is_false_when_revoked_or_expired()
    {
        var token = new RefreshToken
        {
            UserId = Guid.NewGuid(),
            TokenHash = "abc",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
        };

        token.IsActive.Should().BeTrue();

        token.RevokedAtUtc = DateTime.UtcNow;
        token.IsActive.Should().BeFalse();
    }
}

public class AccountStatusRulesTests
{
    [Theory]
    [InlineData(AccountStatus.Active, true)]
    [InlineData(AccountStatus.Registered, true)]
    [InlineData(AccountStatus.PhonePending, true)]
    [InlineData(AccountStatus.Suspended, false)]
    [InlineData(AccountStatus.Closed, false)]
    public void Login_allowed_statuses(AccountStatus status, bool allowed)
    {
        var canLogin = status is not (AccountStatus.Suspended or AccountStatus.Closed);
        canLogin.Should().Be(allowed);
    }
}

public class RestaurantRegistrationStateTests
{
    [Theory]
    [InlineData(VerificationStatus.Draft, true)]
    [InlineData(VerificationStatus.NeedsChanges, true)]
    [InlineData(VerificationStatus.ResubmissionRequired, true)]
    [InlineData(VerificationStatus.Submitted, false)]
    [InlineData(VerificationStatus.Approved, false)]
    public void Editable_statuses(VerificationStatus status, bool editable)
    {
        var canEdit = status is VerificationStatus.Draft
            or VerificationStatus.NeedsChanges
            or VerificationStatus.ResubmissionRequired;
        canEdit.Should().Be(editable);
    }
}
