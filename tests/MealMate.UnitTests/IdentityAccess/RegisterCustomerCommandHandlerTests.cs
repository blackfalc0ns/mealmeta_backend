using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class RegisterCustomerCommandHandlerTests
{
    private static UserAccountDto NewUser(Guid id, string email) => new(
        id, email, null, "Ahmed", UserType.Customer, AccountStatus.Active,
        null, false, false, DateTime.UtcNow, null);

    private static AuthSessionDto Session(UserAccountDto u) => new(
        u.Id, u.Email, null, u.FullName, u.UserType, u.AccountStatus, null,
        ["Customer"], Array.Empty<string>(),
        new AuthTokensDto("tok", DateTime.UtcNow.AddMinutes(15), "ref", DateTime.UtcNow.AddDays(1)));

    [Fact]
    public async Task Register_creates_customer_with_role_and_profile_and_returns_next_step()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var db = Substitute.For<IAppDbContext>();
        var profiles = Substitute.For<DbSet<CustomerProfile>>();
        db.CustomerProfiles.Returns(profiles);
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var created = NewUser(userId, "ahmed@test.com");
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(created));
        users.AddToRoleAsync(created.Id, AppRoles.Customer, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        sessions.IssueSessionAsync(created.Id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Session(created)));

        var handler = new RegisterCustomerCommandHandler(users, sessions, db, audit, clock);
        var result = await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "ahmed@test.com", null, "Password123!", "ar"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NextStep.Should().Be("choose_subscription");
        result.Value.UserType.Should().Be(UserType.Customer);

        await users.Received(1).CreateAsync(
            Arg.Is<CreateUserRequest>(r => r.UserType == UserType.Customer
                && r.Email == "ahmed@test.com"
                && r.AccountStatus == AccountStatus.Active),
            Arg.Any<CancellationToken>());
        await users.Received(1).AddToRoleAsync(created.Id, AppRoles.Customer, Arg.Any<CancellationToken>());
        profiles.Received(1).Add(Arg.Is<CustomerProfile>(p =>
            p.UserId == created.Id && p.PreferredLanguage == "ar" && p.OnboardingCompleted == false));
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "CustomerRegistered"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_defaults_preferred_language_to_ar_when_not_provided()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var db = Substitute.For<IAppDbContext>();
        var profiles = Substitute.For<DbSet<CustomerProfile>>();
        db.CustomerProfiles.Returns(profiles);
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var created = NewUser(Guid.NewGuid(), "x@test.com");
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(created));
        sessions.IssueSessionAsync(created.Id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Session(created)));

        var handler = new RegisterCustomerCommandHandler(users, sessions, db, audit, clock);
        await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "x@test.com", null, "Password123!"),
            CancellationToken.None);

        profiles.Received(1).Add(Arg.Is<CustomerProfile>(p => p.PreferredLanguage == "ar"));
    }

    [Fact]
    public async Task Register_propagates_duplicate_email_conflict()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var db = Substitute.For<IAppDbContext>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();

        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Error.Conflict(MessageCodes.Auth.DuplicateEmail, "Email already registered."));

        var handler = new RegisterCustomerCommandHandler(users, sessions, db, audit, clock);
        var result = await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "dup@test.com", null, "Password123!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.DuplicateEmail);
        await sessions.DidNotReceive().IssueSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_lowercases_email_before_create()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var db = Substitute.For<IAppDbContext>();
        var profiles = Substitute.For<DbSet<CustomerProfile>>();
        db.CustomerProfiles.Returns(profiles);
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var created = NewUser(Guid.NewGuid(), "upper@test.com");
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(created));
        sessions.IssueSessionAsync(created.Id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Session(created)));

        var handler = new RegisterCustomerCommandHandler(users, sessions, db, audit, clock);
        await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "UPPER@TEST.COM", null, "Password123!"),
            CancellationToken.None);

        await users.Received(1).CreateAsync(
            Arg.Is<CreateUserRequest>(r => r.Email == "upper@test.com"),
            Arg.Any<CancellationToken>());
    }
}
