using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public sealed class AdminRestaurantAccountsTests
{
    [Fact]
    public async Task List_includes_verified_restaurant_account_without_profile()
    {
        var userId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);
        var users = Substitute.For<IUserAccountService>();
        users.ListUsersByTypeAsync(UserType.Restaurant, Arg.Any<CancellationToken>())
            .Returns([
                new UserAccountDto(
                    userId,
                    "owner@restaurant.test",
                    "0500000000",
                    "New Restaurant Owner",
                    UserType.Restaurant,
                    AccountStatus.Registered,
                    null,
                    true,
                    false,
                    createdAt,
                    null),
            ]);

        await using var db = CreateDbContext();
        var handler = new ListAdminRestaurantsQueryHandler(db, users);

        var result = await handler.Handle(
            new ListAdminRestaurantsQuery(Page: 1, PageSize: 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle();

        var account = result.Value.Items[0];
        account.Id.Should().Be(userId);
        account.UserId.Should().Be(userId);
        account.ProfileId.Should().BeNull();
        account.RegistrationId.Should().BeNull();
        account.TradeNameEn.Should().Be("New Restaurant Owner");
        account.OwnerName.Should().Be("New Restaurant Owner");
        account.ContactEmail.Should().Be("owner@restaurant.test");
        account.VerificationStatus.Should().Be(VerificationStatus.Draft);
        account.IsOperationallyActive.Should().BeFalse();

        (await db.RestaurantProfiles.CountAsync()).Should().Be(0);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
