using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Application.Features.IdentityAccess.Influencer;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class InfluencerCustomerLoginTests
{
    [Fact]
    public async Task CustomerLogin_allows_and_auto_heals_influencer_user_type()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var userId = Guid.NewGuid();
        var phone = "+96551111111";
        var password = "Password123!";

        var influencerUser = new UserAccountDto(
            userId,
            "influencer@test.com",
            phone,
            "Influencer User",
            UserType.Influencer, // Legacy account with UserType.Influencer
            AccountStatus.Active,
            null,
            true,
            false,
            DateTime.UtcNow,
            null);

        users.FindByPhoneAsync(phone, Arg.Any<CancellationToken>()).Returns(influencerUser);
        users.VerifyPasswordOrDummyAsync(userId, password, Arg.Any<CancellationToken>()).Returns(true);
        users.IsLockedOutAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        var sessionDto = new AuthSessionDto(
            userId,
            "influencer@test.com",
            phone,
            "Influencer User",
            UserType.Customer,
            AccountStatus.Active,
            null,
            [AppRoles.Customer, AppRoles.Influencer],
            [],
            new AuthTokensDto("access_token", DateTime.UtcNow.AddMinutes(15), "refresh_token", DateTime.UtcNow.AddDays(14)));

        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(sessionDto);

        var handler = new CustomerLoginCommandHandler(users, sessions, audit, unitOfWork);
        var result = await handler.Handle(
            new CustomerLoginCommand(null, phone, password),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.AccessToken.Should().Be("access_token");

        // Verify auto-heal happened
        await users.Received(1).SetUserTypeAsync(userId, UserType.Customer, Arg.Any<CancellationToken>());
        await users.Received(1).AddToRoleAsync(userId, AppRoles.Customer, Arg.Any<CancellationToken>());
        await users.Received(1).AddToRoleAsync(userId, AppRoles.Influencer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CustomerLogin_allows_normal_customer()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var userId = Guid.NewGuid();
        var phone = "+96552222222";
        var password = "Password123!";

        var customerUser = new UserAccountDto(
            userId,
            "customer@test.com",
            phone,
            "Customer User",
            UserType.Customer,
            AccountStatus.Active,
            null,
            true,
            false,
            DateTime.UtcNow,
            null);

        users.FindByPhoneAsync(phone, Arg.Any<CancellationToken>()).Returns(customerUser);
        users.VerifyPasswordOrDummyAsync(userId, password, Arg.Any<CancellationToken>()).Returns(true);
        users.IsLockedOutAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        var sessionDto = new AuthSessionDto(
            userId,
            "customer@test.com",
            phone,
            "Customer User",
            UserType.Customer,
            AccountStatus.Active,
            null,
            [AppRoles.Customer],
            [],
            new AuthTokensDto("access_token", DateTime.UtcNow.AddMinutes(15), "refresh_token", DateTime.UtcNow.AddDays(14)));

        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(sessionDto);

        var handler = new CustomerLoginCommandHandler(users, sessions, audit, unitOfWork);
        var result = await handler.Handle(
            new CustomerLoginCommand(null, phone, password),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access_token");
        // Should NOT trigger auto-heal on already Customer accounts
        await users.DidNotReceive().SetUserTypeAsync(userId, Arg.Any<UserType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteCustomer_preserves_customer_user_type_and_adds_influencer_role()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var customerId = Guid.NewGuid();
        var customerAccount = new UserAccountDto(
            customerId,
            "promote@test.com",
            "+96553333333",
            "Promoted Customer",
            UserType.Customer,
            AccountStatus.Active,
            null,
            true,
            false,
            DateTime.UtcNow,
            null);

        users.FindByIdAsync(customerId, Arg.Any<CancellationToken>()).Returns(customerAccount);
        users.AddToRoleAsync(customerId, AppRoles.Influencer, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var service = new InfluencerAdminService(db, users, currentUser, audit, clock);
        var command = new PromoteCustomerToInfluencerCommand(
            customerId,
            "Influencer Display",
            "INFCODE",
            10.0m,
            true);

        var result = await service.PromoteCustomerAsync(command, CancellationToken.None);
        await db.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.InfluencerCode.Should().Be("INFCODE");

        // Verify that SetUserTypeAsync to Influencer was NEVER called
        await users.DidNotReceive().SetUserTypeAsync(customerId, UserType.Influencer, Arg.Any<CancellationToken>());

        // Verify role added
        await users.Received(1).AddToRoleAsync(customerId, AppRoles.Influencer, Arg.Any<CancellationToken>());

        // Verify profile created in db
        var profile = await db.InfluencerProfiles.FirstOrDefaultAsync(x => x.UserId == customerId);
        profile.Should().NotBeNull();
        profile!.InfluencerCode.Should().Be("INFCODE");
        profile.IsActive.Should().BeTrue();
    }
}
