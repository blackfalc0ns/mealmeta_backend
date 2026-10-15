using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class FamilyMemberFirstLoginServiceTests
{
    [Fact]
    public async Task Pending_family_login_sends_otp_and_returns_challenge_without_session()
    {
        await using var fx = await Fixture.Create();

        var result = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], "device", "127.0.0.1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAuthenticated.Should().BeFalse();
        result.Value.AccessToken.Should().BeNull();
        result.Value.NextStep.Should().Be(LoginChallengeSteps.FamilyVerifyOtp);
        result.Value.LoginChallengeToken.Should().NotBeNullOrWhiteSpace();
        await fx.Otp.Received(1).SendAsync(Arg.Is<OtpSendRequest>(x => x.UserId == fx.User.Id
            && x.Destination == fx.User.PhoneNumber && x.Purpose == OtpPurposes.FamilyMemberFirstLogin), Arg.Any<CancellationToken>());
        await fx.Sessions.DidNotReceive().IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(FamilyGroupStatus.Draft)]
    [InlineData(FamilyGroupStatus.CheckoutPending)]
    [InlineData(FamilyGroupStatus.Active)]
    public async Task Pending_family_login_is_available_before_and_after_checkout(FamilyGroupStatus status)
    {
        await using var fx = await Fixture.Create();
        (await fx.Db.FamilyGroups.SingleAsync()).Status = status;
        await fx.Db.SaveChangesAsync();

        var result = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAuthenticated.Should().BeFalse();
        result.Value.NextStep.Should().Be(LoginChallengeSteps.FamilyVerifyOtp);
    }

    [Fact]
    public async Task Family_first_login_resend_rotates_challenge_and_uses_family_purpose()
    {
        await using var fx = await Fixture.Create();
        var first = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], null, null);
        await fx.Db.SaveChangesAsync();

        var resent = await fx.Service.ResendOtpAsync(first.Value.LoginChallengeToken!, "device", "127.0.0.1");

        resent.IsSuccess.Should().BeTrue();
        resent.Value.IsAuthenticated.Should().BeFalse();
        resent.Value.NextStep.Should().Be(LoginChallengeSteps.FamilyVerifyOtp);
        resent.Value.LoginChallengeToken.Should().NotBe(first.Value.LoginChallengeToken);
        (await fx.Service.VerifyOtpAsync(first.Value.LoginChallengeToken!, "123456", null, null)).IsFailure.Should().BeTrue();
        await fx.Otp.Received(2).SendAsync(Arg.Is<OtpSendRequest>(x => x.UserId == fx.User.Id
            && x.Purpose == OtpPurposes.FamilyMemberFirstLogin), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_family_first_login_otp_never_yields_password_setup_or_session()
    {
        await using var fx = await Fixture.Create();
        var started = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], null, null, CancellationToken.None);
        fx.Otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation("auth.otp_invalid"));

        var result = await fx.Service.VerifyOtpAsync(started.Value.LoginChallengeToken!, "000000", null, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await fx.Sessions.DidNotReceive().IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Normal_customer_login_enters_first_login_flow_without_issuing_tokens()
    {
        await using var fx = await Fixture.Create(phoneConfirmed: false);
        fx.Users.FindByPhoneAsync(fx.User.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(fx.User);
        fx.Users.VerifyPasswordOrDummyAsync(fx.User.Id, "temporary", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CustomerLoginCommandHandler(fx.Users, fx.Sessions, Substitute.For<IAuditLogger>(),
            Substitute.For<IUnitOfWork>(), fx.Service);

        var result = await handler.Handle(new CustomerLoginCommand(null, fx.User.PhoneNumber, "temporary"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NextStep.Should().Be(LoginChallengeSteps.FamilyVerifyOtp);
        result.Value.AccessToken.Should().BeNull();
        await fx.Sessions.DidNotReceive().IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Paused_or_removed_member_cannot_verify_an_already_issued_otp_challenge()
    {
        await using var fx = await Fixture.Create();
        var started = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], null, null);
        await fx.Db.SaveChangesAsync();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync();
        access.IsPaused = true;
        await fx.Db.SaveChangesAsync();

        var result = await fx.Service.VerifyOtpAsync(started.Value.LoginChallengeToken!, "123456", null, null);

        result.IsFailure.Should().BeTrue();
        await fx.Otp.DidNotReceive().VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>());
        await fx.Sessions.DidNotReceive().IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Verified_otp_allows_one_password_setup_and_only_then_issues_session()
    {
        await using var fx = await Fixture.Create();
        fx.Otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        fx.Users.SetPhoneConfirmedAsync(fx.User.Id, true, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        fx.Users.GetRolesAsync(fx.User.Id, Arg.Any<CancellationToken>()).Returns([AppRoles.Customer]);
        fx.Users.GeneratePasswordResetTokenAsync(fx.User.Id, Arg.Any<CancellationToken>()).Returns("reset-token");
        fx.Users.ResetPasswordAsync(fx.User.Id, "reset-token", "FreshPass123", Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        fx.Users.SetHasPasswordAsync(fx.User.Id, true, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        fx.Sessions.IssueSessionAsync(fx.User.Id, "device", "127.0.0.1", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(fx.User.Id, null, fx.User.PhoneNumber, fx.User.FullName,
                UserType.Customer, AccountStatus.Active, null, [AppRoles.Customer], [],
                new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)))));
        fx.Sessions.RevokeAllUserTokensAsync(fx.User.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var otpChallenge = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], "device", "127.0.0.1");
        await fx.Db.SaveChangesAsync();
        var passwordChallenge = await fx.Service.VerifyOtpAsync(otpChallenge.Value.LoginChallengeToken!, "123456", "device", "127.0.0.1");
        await fx.Db.SaveChangesAsync();
        passwordChallenge.IsSuccess.Should().BeTrue();
        passwordChallenge.Value.NextStep.Should().Be(LoginChallengeSteps.FamilySetPassword);
        passwordChallenge.Value.AccessToken.Should().BeNull();
        await fx.Sessions.DidNotReceive().IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        var otpReplay = await fx.Service.VerifyOtpAsync(otpChallenge.Value.LoginChallengeToken!, "123456", null, null);
        otpReplay.IsFailure.Should().BeTrue();

        var completed = await fx.Service.SetPasswordAsync(passwordChallenge.Value.LoginChallengeToken!, "FreshPass123", "device", "127.0.0.1");

        completed.IsSuccess.Should().BeTrue();
        completed.Value.AccessToken.Should().Be("access");
        (await fx.Db.FamilyMemberAccesses.SingleAsync()).RequiresPasswordSetup.Should().BeFalse();
        await fx.Sessions.Received(1).IssueSessionAsync(fx.User.Id, "device", "127.0.0.1", Arg.Any<CancellationToken>());

        var replay = await fx.Service.SetPasswordAsync(passwordChallenge.Value.LoginChallengeToken!, "AnotherPass123", null, null);
        replay.IsFailure.Should().BeTrue();
        await fx.Sessions.Received(1).IssueSessionAsync(fx.User.Id, "device", "127.0.0.1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expired_otp_challenge_cannot_advance_to_password_setup()
    {
        await using var fx = await Fixture.Create();
        var started = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], null, null);
        await fx.Db.SaveChangesAsync();
        fx.Clock.UtcNow.Returns(new DateTime(2026, 10, 14, 12, 6, 0, DateTimeKind.Utc));

        var result = await fx.Service.VerifyOtpAsync(started.Value.LoginChallengeToken!, "123456", null, null);

        result.IsFailure.Should().BeTrue();
        await fx.Sessions.DidNotReceive().IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pending_flag_blocks_login_even_after_family_member_is_removed_or_group_is_inactive()
    {
        await using var fx = await Fixture.Create();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync();
        access.RemovedAtUtc = DateTime.UtcNow;
        var member = await fx.Db.FamilyMembers.SingleAsync();
        member.IsDeleted = true;
        var group = await fx.Db.FamilyGroups.SingleAsync();
        group.Status = FamilyGroupStatus.Cancelled;
        await fx.Db.SaveChangesAsync();

        (await fx.Service.IsPendingSetupAsync(fx.User.Id)).Should().BeTrue();

        var login = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], null, null);
        login.IsFailure.Should().BeTrue();
        await fx.Sessions.DidNotReceive().IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Historical_setup_recovery_clears_gate_only_for_inaccessible_membership()
    {
        await using var fx = await Fixture.Create();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync();
        access.RemovedAtUtc = DateTime.UtcNow;
        await fx.Db.SaveChangesAsync();

        (await fx.Service.IsPendingSetupAsync(fx.User.Id)).Should().BeTrue();
        (await fx.Service.IsViablePendingSetupAsync(fx.User.Id)).Should().BeFalse();
        (await fx.Service.CompleteHistoricalSetupRecoveryAsync(fx.User.Id)).IsSuccess.Should().BeTrue();
        (await fx.Db.FamilyMemberAccesses.SingleAsync()).RequiresPasswordSetup.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Disabled_or_deleted_setup_can_be_recovered(bool hasSeparateAccount, bool isDeleted)
    {
        await using var fx = await Fixture.Create();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync();
        access.HasSeparateAccount = hasSeparateAccount;
        access.IsDeleted = isDeleted;
        await fx.Db.SaveChangesAsync();

        (await fx.Service.IsViablePendingSetupAsync(fx.User.Id)).Should().BeFalse();
        (await fx.Service.CompleteHistoricalSetupRecoveryAsync(fx.User.Id)).IsSuccess.Should().BeTrue();
        (await fx.Service.IsPendingSetupAsync(fx.User.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Recovery_rechecks_viability_after_otp_before_replacing_password()
    {
        await using var fx = await Fixture.Create();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync();
        access.IsPaused = true;
        await fx.Db.SaveChangesAsync();
        fx.Users.FindByPhoneAsync(fx.User.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(fx.User);
        fx.Otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            access.IsPaused = false;
            await fx.Db.SaveChangesAsync();
            return Result.Success();
        });
        var handler = new CustomerResetPasswordCommandHandler(fx.Users, fx.Otp, fx.Sessions,
            new AuthLoginChallengeService(fx.Db, fx.Clock), Substitute.For<IAuditLogger>(), fx.UnitOfWork, fx.Service);

        var result = await handler.Handle(new CustomerResetPasswordCommand(fx.User.PhoneNumber!, "123456", "FreshPass123"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await fx.Users.DidNotReceive().ResetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await fx.Service.IsPendingSetupAsync(fx.User.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Verified_recovery_replaces_password_before_retiring_obsolete_setup_gate()
    {
        await using var fx = await Fixture.Create();
        (await fx.Db.FamilyMemberAccesses.SingleAsync()).RemovedAtUtc = DateTime.UtcNow;
        await fx.Db.SaveChangesAsync();
        fx.Users.FindByPhoneAsync(fx.User.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(fx.User);
        fx.Users.GeneratePasswordResetTokenAsync(fx.User.Id, Arg.Any<CancellationToken>()).Returns("reset-token");
        fx.Users.ResetPasswordAsync(fx.User.Id, "reset-token", "FreshPass123", Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var handler = new CustomerResetPasswordCommandHandler(fx.Users, fx.Otp, fx.Sessions,
            new AuthLoginChallengeService(fx.Db, fx.Clock), Substitute.For<IAuditLogger>(), fx.UnitOfWork, fx.Service);

        var result = await handler.Handle(new CustomerResetPasswordCommand(fx.User.PhoneNumber!, "123456", "FreshPass123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await fx.Otp.Received(1).VerifyAsync(Arg.Is<OtpVerifyRequest>(request => request.Purpose == OtpPurposes.PasswordReset), Arg.Any<CancellationToken>());
        await fx.Users.Received(1).ResetPasswordAsync(fx.User.Id, "reset-token", "FreshPass123", Arg.Any<CancellationToken>());
        (await fx.Service.IsPendingSetupAsync(fx.User.Id)).Should().BeFalse();
        await fx.Sessions.Received(1).RevokeAllUserTokensAsync(fx.User.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Completing_current_onboarding_clears_obsolete_historical_gates()
    {
        await using var fx = await Fixture.Create();
        var historicalGroup = new FamilyGroup { Name = "History", Status = FamilyGroupStatus.Cancelled,
            ManagerCustomerUserId = Guid.NewGuid() };
        var historicalMember = new FamilyMember { FamilyGroupId = historicalGroup.Id,
            Role = FamilyMemberRole.Member, DisplayName = "Historical member",
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        historicalGroup.Members.Add(historicalMember);
        fx.Db.AddRange(historicalGroup, new FamilyMemberAccess { FamilyMemberId = historicalMember.Id,
            LinkedCustomerUserId = fx.User.Id, RequiresPasswordSetup = true, HasSeparateAccount = true,
            RemovedAtUtc = DateTime.UtcNow });
        await fx.Db.SaveChangesAsync();
        fx.Users.GeneratePasswordResetTokenAsync(fx.User.Id, Arg.Any<CancellationToken>()).Returns("reset-token");
        fx.Users.ResetPasswordAsync(fx.User.Id, "reset-token", "FreshPass123", Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        fx.Sessions.IssueSessionAsync(fx.User.Id, null, null, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(fx.User.Id, null, fx.User.PhoneNumber, fx.User.FullName,
                UserType.Customer, AccountStatus.Active, null, [AppRoles.Customer], [],
                new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)))));
        var started = await fx.Service.BeginAsync(fx.User, [AppRoles.Customer], null, null);
        var verified = await fx.Service.VerifyOtpAsync(started.Value.LoginChallengeToken!, "123456", null, null);

        var completed = await fx.Service.SetPasswordAsync(verified.Value.LoginChallengeToken!, "FreshPass123", null, null);

        completed.IsSuccess.Should().BeTrue();
        (await fx.Service.IsPendingSetupAsync(fx.User.Id)).Should().BeFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public FamilyMemberFirstLoginService Service { get; }
        public IOtpService Otp { get; }
        public IAuthSessionService Sessions { get; }
        public IUserAccountService Users { get; }
        public IUnitOfWork UnitOfWork { get; }
        public IDateTimeProvider Clock { get; }
        public UserAccountDto User { get; }

        private Fixture(AppDbContext db, FamilyMemberFirstLoginService service, IOtpService otp,
            IAuthSessionService sessions, IUserAccountService users, IUnitOfWork unitOfWork,
            IDateTimeProvider clock, UserAccountDto user)
        {
            Db = db;
            Service = service;
            Otp = otp;
            Sessions = sessions;
            Users = users;
            UnitOfWork = unitOfWork;
            Clock = clock;
            User = user;
        }

        public static async Task<Fixture> Create(bool phoneConfirmed = true)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
            var userId = Guid.NewGuid();
            var group = new FamilyGroup { Name = "Group", Status = FamilyGroupStatus.Active, ManagerCustomerUserId = Guid.NewGuid() };
        var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member, DisplayName = "Member",
            PhoneNumber = "+96550000001", RelationshipCode = "son", NutritionalGoalCode = "balanced" };
            group.Members.Add(member);
            db.AddRange(group, new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = userId,
                HasSeparateAccount = true, RequiresPasswordSetup = true });
            await db.SaveChangesAsync();

            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc));
            clock.TodayUtc.Returns(new DateOnly(2026, 10, 14));
            var challengeService = new AuthLoginChallengeService(db, clock);
            var otp = Substitute.For<IOtpService>();
            otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
            otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
            var sessions = Substitute.For<IAuthSessionService>();
            var users = Substitute.For<IUserAccountService>();
            var user = new UserAccountDto(userId, null, "+96550000001", "Member", UserType.Customer,
                AccountStatus.Active, null, false, phoneConfirmed, DateTime.UtcNow, null, HasSetPassword: false);
            users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
            var unitOfWork = Substitute.For<IUnitOfWork>();
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(call => db.SaveChangesAsync(call.Arg<CancellationToken>()));
            var service = new FamilyMemberFirstLoginService(db, users, otp, challengeService, sessions,
                Substitute.For<IAuditLogger>(), unitOfWork);
            return new Fixture(db, service, otp, sessions, users, unitOfWork, clock, user);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
