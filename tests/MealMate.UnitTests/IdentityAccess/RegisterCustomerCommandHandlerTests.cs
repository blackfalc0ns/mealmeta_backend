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
        id, email, "+96590000000", "Ahmed", UserType.Customer, AccountStatus.Registered,
        null, false, false, DateTime.UtcNow, null);

    [Fact]
    public async Task Register_creates_customer_with_role_and_profile_and_returns_next_step()
    {
        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var profiles = Substitute.For<DbSet<CustomerProfile>>();
        db.CustomerProfiles.Returns(profiles);
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var otp = Substitute.For<IOtpService>();
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var userId = Guid.NewGuid();
        var created = NewUser(userId, "ahmed@test.com");
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(created));
        users.AddToRoleAsync(created.Id, AppRoles.Customer, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new RegisterCustomerCommandHandler(users, db, audit, clock, otp);
        var result = await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "+96590000000", "Password123!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.NextStep.Should().Be("verify_phone_otp");

        await users.Received(1).CreateAsync(
            Arg.Is<CreateUserRequest>(r => r.UserType == UserType.Customer
                && r.Email == null
                && r.AccountStatus == AccountStatus.PhonePending),
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
        var db = Substitute.For<IAppDbContext>();
        var profiles = Substitute.For<DbSet<CustomerProfile>>();
        db.CustomerProfiles.Returns(profiles);
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var otp = Substitute.For<IOtpService>();
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var created = NewUser(Guid.NewGuid(), "x@test.com");
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(created));

        var handler = new RegisterCustomerCommandHandler(users, db, audit, clock, otp);
        await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "+96590000000", "Password123!"),
            CancellationToken.None);

        profiles.Received(1).Add(Arg.Is<CustomerProfile>(p => p.PreferredLanguage == "ar"));
    }

    [Fact]
    public async Task Register_propagates_duplicate_email_conflict()
    {
        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var otp = Substitute.For<IOtpService>();

        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Error.Conflict(MessageCodes.Auth.DuplicateEmail, "Email already registered."));

        var handler = new RegisterCustomerCommandHandler(users, db, audit, clock, otp);
        var result = await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "+96590000000", "Password123!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.DuplicateEmail);
    }

    [Fact]
    public async Task Register_does_not_create_an_email_identity()
    {
        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var profiles = Substitute.For<DbSet<CustomerProfile>>();
        db.CustomerProfiles.Returns(profiles);
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var otp = Substitute.For<IOtpService>();
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var created = NewUser(Guid.NewGuid(), "upper@test.com");
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(created));

        var handler = new RegisterCustomerCommandHandler(users, db, audit, clock, otp);
        await handler.Handle(
            new RegisterCustomerCommand("Ahmed", "+96590000000", "Password123!"),
            CancellationToken.None);

        await users.Received(1).CreateAsync(
            Arg.Is<CreateUserRequest>(r => r.Email == null),
            Arg.Any<CancellationToken>());
    }
}
