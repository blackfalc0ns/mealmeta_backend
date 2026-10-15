using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Infrastructure.Email;
using MealMate.Infrastructure.Services;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MealMate.IntegrationTests.IdentityAccess;

public sealed class FamilyFirstLoginPersistenceTests : IAsyncLifetime
{
    private readonly MealMate.Testing.PostgresTestDatabase _container = new();
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Theory]
    [InlineData("active")]
    [InlineData("removed")]
    [InlineData("cancelled")]
    [InlineData("disabled")]
    [InlineData("soft_deleted")]
    public async Task Pending_setup_denies_session_issue_and_refresh_in_real_database(string state)
    {
        await using var db = CreateDb();
        var (user, access, group) = await SeedAsync(db);
        access.RemovedAtUtc = state == "removed" ? Now : null;
        access.HasSeparateAccount = state != "disabled";
        access.IsDeleted = state == "soft_deleted";
        group.Status = state == "cancelled" ? FamilyGroupStatus.Cancelled : FamilyGroupStatus.Active;
        var rawRefresh = "previous-refresh-token";
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawRefresh))),
            CreatedAtUtc = Now.AddMinutes(-1), ExpiresAtUtc = Now.AddDays(1) });
        await db.SaveChangesAsync();
        var users = Accounts(user);
        var clock = Clock();
        var sessions = Sessions(db, users, clock);
        var flow = new FamilyMemberFirstLoginService(db, users, Substitute.For<IOtpService>(),
            new AuthLoginChallengeService(db, clock), sessions, Substitute.For<IAuditLogger>(), db);
        Assert.True(await flow.IsPendingSetupAsync(user.Id));

        var issue = await sessions.IssueSessionAsync(user.Id);
        Assert.True(issue.IsFailure);
        Assert.Equal("family.password_setup_required", issue.Error.Code);
        var refresh = await sessions.RefreshSessionAsync(rawRefresh);

        Assert.True(refresh.IsFailure);
        Assert.Equal("family.password_setup_required", refresh.Error.Code);
        Assert.Equal(1, await db.RefreshTokens.CountAsync());
        Assert.Null((await db.Users.SingleAsync(x => x.Id == user.Id)).LastLoginAtUtc);
    }

    [Fact]
    public async Task Configured_fixed_phone_code_advances_production_first_login_without_a_message_provider()
    {
        await using var db = CreateDb();
        var (user, _, _) = await SeedAsync(db);
        var users = Accounts(user);
        var clock = Clock();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Otp:WhatsAppFixedCode"] = "123456" }).Build();
        var delivery = new RoutingOtpDeliveryService(null!,
            new LoggingOtpDeliveryService(NullLogger<LoggingOtpDeliveryService>.Instance),
            new SilentOtpDeliveryService(), environment, NullLogger<RoutingOtpDeliveryService>.Instance, configuration);
        var otp = new OtpService(db, delivery, clock, NullLogger<OtpService>.Instance,
            Options.Create(new OtpOptions { WhatsAppFixedCode = "123456" }));
        var flow = new FamilyMemberFirstLoginService(db, users, otp, new AuthLoginChallengeService(db, clock),
            Sessions(db, users, clock), Substitute.For<IAuditLogger>(), db,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));

        var login = await flow.BeginAsync(user, [AppRoles.Customer], null, null);
        Assert.True(login.IsSuccess);
        Assert.False(login.Value.IsAuthenticated);
        var verified = await flow.VerifyOtpAsync(login.Value.LoginChallengeToken!, "123456", null, null);

        Assert.True(verified.IsSuccess);
        Assert.Equal("family_set_password", verified.Value.NextStep);
        Assert.False(verified.Value.IsAuthenticated);
        await using var verification = CreateDb();
        Assert.NotNull((await verification.OtpChallenges.SingleAsync()).ConsumedAtUtc);
        Assert.Empty(await verification.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Missing_production_phone_provider_returns_failure_without_login_challenge_or_session()
    {
        await using var db = CreateDb();
        var (user, _, _) = await SeedAsync(db);
        var users = Accounts(user);
        var clock = Clock();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var delivery = new RoutingOtpDeliveryService(null!,
            new LoggingOtpDeliveryService(NullLogger<LoggingOtpDeliveryService>.Instance),
            new SilentOtpDeliveryService(), environment, NullLogger<RoutingOtpDeliveryService>.Instance,
            new ConfigurationBuilder().Build());
        var otp = new OtpService(db, delivery, clock, NullLogger<OtpService>.Instance);
        var flow = new FamilyMemberFirstLoginService(db, users, otp, new AuthLoginChallengeService(db, clock),
            Sessions(db, users, clock), Substitute.For<IAuditLogger>(), db,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));

        var result = await flow.BeginAsync(user, [AppRoles.Customer], null, null);

        Assert.True(result.IsFailure);
        await using var verification = CreateDb();
        Assert.Empty(await verification.AuthLoginChallenges.ToListAsync());
        Assert.Empty(await verification.RefreshTokens.ToListAsync());
        Assert.Empty(await verification.OtpChallenges.Where(x => x.ConsumedAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task Failed_otp_attempts_are_committed_and_exhaust_the_login_challenge()
    {
        await using var db = CreateDb();
        var (user, _, _) = await SeedAsync(db);
        var users = Accounts(user);
        var clock = Clock();
        var challenges = new AuthLoginChallengeService(db, clock);
        var challenge = await challenges.CreateAsync(new CreateLoginChallengeRequest(
            user.Id, LoginChallengeSteps.FamilyVerifyOtp, null, null));
        await db.SaveChangesAsync();
        var otp = Substitute.For<IOtpService>();
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.OtpInvalid));
        var flow = new FamilyMemberFirstLoginService(db, users, otp, challenges,
            Sessions(db, users, clock), Substitute.For<IAuditLogger>(), db,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.True((await flow.VerifyOtpAsync(challenge.RawToken, "000000", null, null)).IsFailure);

        await using var verification = CreateDb();
        var persisted = await verification.AuthLoginChallenges.SingleAsync(x => x.Id == challenge.ChallengeId);
        Assert.Equal(5, persisted.AttemptCount);
        Assert.NotNull(persisted.ConsumedAtUtc);
        Assert.False((await challenges.ResolveAsync(challenge.RawToken, LoginChallengeSteps.FamilyVerifyOtp)).IsSuccess);
        Assert.Empty(await verification.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Successful_otp_transition_persists_new_setup_challenge_before_releasing_transaction()
    {
        await using var db = CreateDb();
        var (user, _, _) = await SeedAsync(db);
        var users = Accounts(user);
        var clock = Clock();
        var challenges = new AuthLoginChallengeService(db, clock);
        var challenge = await challenges.CreateAsync(new CreateLoginChallengeRequest(
            user.Id, LoginChallengeSteps.FamilyVerifyOtp, null, null));
        await db.SaveChangesAsync();
        var otp = Substitute.For<IOtpService>();
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        var flow = new FamilyMemberFirstLoginService(db, users, otp, challenges,
            Sessions(db, users, clock), Substitute.For<IAuditLogger>(), db,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));

        var result = await flow.VerifyOtpAsync(challenge.RawToken, "123456", null, null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsAuthenticated);
        await using var verification = CreateDb();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.Value.LoginChallengeToken!)));
        var setup = await verification.AuthLoginChallenges.SingleOrDefaultAsync(x => x.TokenHash == tokenHash);
        Assert.NotNull(setup);
        Assert.Equal(LoginChallengeSteps.FamilySetPassword, setup.NextStep);
        Assert.Null(setup.ConsumedAtUtc);
        Assert.NotNull((await verification.AuthLoginChallenges.SingleAsync(x => x.Id == challenge.ChallengeId)).ConsumedAtUtc);
        Assert.Empty(await verification.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Rejected_password_preserves_verified_setup_challenge_for_retry_in_real_transaction()
    {
        await using var db = CreateDb();
        var (user, access, _) = await SeedAsync(db);
        var users = Accounts(user);
        users.GeneratePasswordResetTokenAsync(user.Id, Arg.Any<CancellationToken>()).Returns("identity-reset-token");
        users.ResetPasswordAsync(user.Id, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.PasswordTooWeak));
        var clock = Clock();
        var challenges = new AuthLoginChallengeService(db, clock);
        var challenge = await challenges.CreateAsync(new CreateLoginChallengeRequest(
            user.Id, LoginChallengeSteps.FamilySetPassword, null, null));
        await db.SaveChangesAsync();
        var sessions = Sessions(db, users, clock);
        var flow = new FamilyMemberFirstLoginService(db, users, Substitute.For<IOtpService>(), challenges,
            sessions, Substitute.For<IAuditLogger>(), db, new PostgresApplicationTransactionExecutor(db),
            new PostgresApplicationLockManager(db));

        var result = await flow.SetPasswordAsync(challenge.RawToken, "weak", null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(MessageCodes.Auth.PasswordTooWeak, result.Error.Code);
        await using var verification = CreateDb();
        Assert.True((await verification.FamilyMemberAccesses.SingleAsync(x => x.Id == access.Id)).RequiresPasswordSetup);
        Assert.Null((await verification.AuthLoginChallenges.SingleAsync(x => x.Id == challenge.ChallengeId)).ConsumedAtUtc);
        Assert.Empty(await verification.RefreshTokens.ToListAsync());
        Assert.True((await challenges.ResolveAsync(challenge.RawToken, LoginChallengeSteps.FamilySetPassword)).IsSuccess);
    }

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_container.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());

    private static IDateTimeProvider Clock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private static IUserAccountService Accounts(UserAccountDto user)
    {
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        users.GetRolesAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new[] { AppRoles.Customer });
        users.GetPermissionsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        return users;
    }

    private static AuthSessionService Sessions(AppDbContext db, IUserAccountService users, IDateTimeProvider clock)
    {
        var tokens = Substitute.For<ITokenService>();
        tokens.CreateAccessToken(Arg.Any<TokenUser>()).Returns(new AccessToken("test-access-token", Now.AddMinutes(15)));
        tokens.GenerateRefreshToken().Returns("test-new-refresh-token");
        return new(users, tokens, db, clock, Options.Create(new JwtOptions
        { Issuer = "tests", Audience = "tests", SecretKey = "01234567890123456789012345678901" }));
    }

    private static async Task<(UserAccountDto User, FamilyMemberAccess Access, FamilyGroup Group)> SeedAsync(AppDbContext db)
    {
        var identity = new ApplicationUser { Id = Guid.NewGuid(), UserType = UserType.Customer,
            AccountStatus = AccountStatus.Active, PhoneNumber = "+96550000001", CreatedAtUtc = Now };
        var group = new FamilyGroup { Name = "Test family", ManagerCustomerUserId = Guid.NewGuid(), Status = FamilyGroupStatus.Active };
        // The manager is persisted too, so the test follows the real foreign-key contract.
        db.Users.Add(new ApplicationUser { Id = group.ManagerCustomerUserId, UserType = UserType.Customer,
            AccountStatus = AccountStatus.Active, CreatedAtUtc = Now });
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Test member",
            Role = FamilyMemberRole.Member, RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        group.Members.Add(member);
        var access = new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = identity.Id,
            HasSeparateAccount = true, RequiresPasswordSetup = true };
        db.AddRange(identity, group, access);
        await db.SaveChangesAsync();
        var user = new UserAccountDto(identity.Id, null, identity.PhoneNumber, null, UserType.Customer,
            AccountStatus.Active, null, false, true, Now, null, false);
        return (user, access, group);
    }
}
