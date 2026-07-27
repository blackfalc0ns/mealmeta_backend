using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class OtpServiceTests
{
    private static AppDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static OtpService CreateService(
        AppDbContext db,
        DateTime now,
        out IOtpDeliveryService delivery,
        string? fixedCode = null)
    {
        delivery = Substitute.For<IOtpDeliveryService>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return new OtpService(
            db,
            delivery,
            clock,
            NullLogger<OtpService>.Instance,
            Options.Create(new OtpOptions { FixedCode = fixedCode }));
    }

    // Sending an OTP creates a challenge and consumes any previous active ones
    // for the same destination+purpose so only the latest code is valid.
    [Fact]
    public async Task Send_uses_configured_fixed_code()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        var svc = CreateService(db, now, out var delivery, "123456");

        var result = await svc.SendAsync(
            new OtpSendRequest("fixed@test.com", "Email", OtpPurposes.VerifyEmail, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await delivery.Received(1).DeliverAsync(
            "Email",
            "fixed@test.com",
            "123456",
            OtpPurposes.VerifyEmail,
            Arg.Any<CancellationToken>());
        var challenge = await db.OtpChallenges.AsNoTracking().SingleAsync();
        challenge.CodeHash.Should().Be(Hash("123456"));
    }

    [Fact]
    public async Task Send_creates_challenge_and_consumes_previous_active_ones()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.OtpChallenges.Add(new OtpChallenge
        {
            Destination = "a@test.com",
            Channel = "Email",
            Purpose = OtpPurposes.VerifyEmail,
            CodeHash = Hash("111111"),
            CreatedAtUtc = now.AddMinutes(-2),
            ExpiresAtUtc = now.AddMinutes(8),
            MaxAttempts = 5,
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, now, out var delivery);
        var result = await svc.SendAsync(
            new OtpSendRequest("A@Test.COM", "Email", OtpPurposes.VerifyEmail, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await delivery.Received(1).DeliverAsync(
            "Email", "a@test.com", Arg.Any<string>(), OtpPurposes.VerifyEmail, Arg.Any<CancellationToken>());

        db.OtpChallenges.Should().HaveCount(2);
        db.OtpChallenges.Where(x => x.ConsumedAtUtc != null).Should().HaveCount(1);
        db.OtpChallenges.Where(x => x.ConsumedAtUtc == null).Should().HaveCount(1);
    }

    [Fact]
    public async Task Send_rate_limits_after_five_per_hour()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        for (var i = 0; i < 5; i++)
        {
            db.OtpChallenges.Add(new OtpChallenge
            {
                Destination = "b@test.com",
                Channel = "Email",
                Purpose = OtpPurposes.PasswordReset,
                CodeHash = Hash(i.ToString("D6")),
                CreatedAtUtc = now.AddMinutes(-30 + i),
                ExpiresAtUtc = now.AddMinutes(10),
                MaxAttempts = 5,
            });
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db, now, out var delivery);
        var result = await svc.SendAsync(
            new OtpSendRequest("b@test.com", "Email", OtpPurposes.PasswordReset, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.OtpSendLimitExceeded);
        await delivery.DidNotReceive().DeliverAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_after_one_hour_window_is_allowed_again()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        for (var i = 0; i < 5; i++)
        {
            db.OtpChallenges.Add(new OtpChallenge
            {
                Destination = "c@test.com",
                Channel = "Email",
                Purpose = OtpPurposes.PasswordReset,
                CodeHash = Hash(i.ToString("D6")),
                CreatedAtUtc = now.AddHours(-2),
                ExpiresAtUtc = now.AddHours(-1).AddMinutes(10),
                MaxAttempts = 5,
            });
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db, now, out var delivery);
        var result = await svc.SendAsync(
            new OtpSendRequest("c@test.com", "Email", OtpPurposes.PasswordReset, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await delivery.Received(1).DeliverAsync(
            "Email", "c@test.com", Arg.Any<string>(), OtpPurposes.PasswordReset, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Verify_with_correct_code_persists_consumption_immediately()
    {
        var now = DateTime.UtcNow;
        var databaseName = Guid.NewGuid().ToString();
        await using var db = CreateDbContext(databaseName);
        const string code = "482910";
        var challenge = new OtpChallenge
        {
            Destination = "d@test.com",
            Channel = "Email",
            Purpose = OtpPurposes.VerifyEmail,
            CodeHash = Hash(code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            MaxAttempts = 5,
        };
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();
        var challengeId = challenge.Id;

        var svc = CreateService(db, now, out _);
        var result = await svc.VerifyAsync(
            new OtpVerifyRequest("D@Test.COM", OtpPurposes.VerifyEmail, code), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var freshDb = CreateDbContext(databaseName);
        var stored = await freshDb.OtpChallenges.AsNoTracking().SingleAsync(x => x.Id == challengeId);
        stored.ConsumedAtUtc.Should().NotBeNull();
        stored.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Reset_password_failure_after_successful_otp_verify_rejects_reuse()
    {
        var now = DateTime.UtcNow;
        var databaseName = Guid.NewGuid().ToString();
        await using var db = CreateDbContext(databaseName);
        const string code = "482910";
        var userId = Guid.NewGuid();
        db.OtpChallenges.Add(new OtpChallenge
        {
            Destination = "reset@test.com",
            Channel = "Email",
            Purpose = OtpPurposes.PasswordReset,
            CodeHash = Hash(code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            MaxAttempts = 5,
        });
        await db.SaveChangesAsync();

        var otp = CreateService(db, now, out _);
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();

        users.FindByEmailAsync("reset@test.com", Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "reset@test.com", null, "A", UserType.Customer, AccountStatus.Active,
            null, true, false, now, null));
        users.GeneratePasswordResetTokenAsync(userId, Arg.Any<CancellationToken>()).Returns("reset-token");
        users.ResetPasswordAsync(userId, "reset-token", "weak", Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.PasswordTooWeak, "Password too weak."));

        var handler = new ResetPasswordCommandHandler(
            users,
            otp,
            sessions,
            Substitute.For<IAuthLoginChallengeService>(),
            audit,
            Substitute.For<IUnitOfWork>());
        var resetResult = await handler.Handle(
            new ResetPasswordCommand("reset@test.com", code, "weak"),
            CancellationToken.None);

        resetResult.IsFailure.Should().BeTrue();
        resetResult.Error.Code.Should().Be(MessageCodes.Auth.PasswordTooWeak);

        await using var freshDb = CreateDbContext(databaseName);
        var stored = await freshDb.OtpChallenges.AsNoTracking().SingleAsync();
        stored.ConsumedAtUtc.Should().NotBeNull();

        var otpRetry = CreateService(freshDb, now, out _);
        var reuse = await otpRetry.VerifyAsync(
            new OtpVerifyRequest("reset@test.com", OtpPurposes.PasswordReset, code),
            CancellationToken.None);

        reuse.IsFailure.Should().BeTrue();
        reuse.Error.Code.Should().Be(MessageCodes.Auth.OtpInvalid);
    }

    [Fact]
    public async Task Verify_with_wrong_code_increments_attempts_and_returns_invalid()
    {
        var now = DateTime.UtcNow;
        var databaseName = Guid.NewGuid().ToString();
        await using var db = CreateDbContext(databaseName);
        var challenge = new OtpChallenge
        {
            Destination = "e@test.com",
            Channel = "Email",
            Purpose = OtpPurposes.VerifyEmail,
            CodeHash = Hash("111111"),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            MaxAttempts = 5,
        };
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();
        var challengeId = challenge.Id;

        var svc = CreateService(db, now, out _);
        var result = await svc.VerifyAsync(
            new OtpVerifyRequest("e@test.com", OtpPurposes.VerifyEmail, "999999"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.OtpInvalid);

        await using var freshDb = CreateDbContext(databaseName);
        var stored = await freshDb.OtpChallenges.AsNoTracking().SingleAsync(x => x.Id == challengeId);
        stored.AttemptCount.Should().Be(1);
        stored.ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Verify_blocks_after_max_attempts()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.OtpChallenges.Add(new OtpChallenge
        {
            Destination = "f@test.com",
            Channel = "Email",
            Purpose = OtpPurposes.VerifyEmail,
            CodeHash = Hash("111111"),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            MaxAttempts = 2,
            AttemptCount = 2,
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, now, out _);
        var result = await svc.VerifyAsync(
            new OtpVerifyRequest("f@test.com", OtpPurposes.VerifyEmail, "111111"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.OtpTooManyAttempts);
    }

    [Fact]
    public async Task Verify_rejects_expired_code()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.OtpChallenges.Add(new OtpChallenge
        {
            Destination = "g@test.com",
            Channel = "Email",
            Purpose = OtpPurposes.VerifyEmail,
            CodeHash = Hash("111111"),
            CreatedAtUtc = now.AddMinutes(-11),
            ExpiresAtUtc = now.AddMinutes(-1),
            MaxAttempts = 5,
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, now, out _);
        var result = await svc.VerifyAsync(
            new OtpVerifyRequest("g@test.com", OtpPurposes.VerifyEmail, "111111"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.OtpExpired);
    }

    [Fact]
    public async Task Verify_with_no_active_challenge_returns_invalid()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();

        var svc = CreateService(db, now, out _);
        var result = await svc.VerifyAsync(
            new OtpVerifyRequest("h@test.com", OtpPurposes.VerifyEmail, "111111"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.OtpInvalid);
    }

    [Fact]
    public async Task Send_marks_challenge_consumed_when_delivery_fails()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        var svc = CreateService(db, now, out var delivery);
        delivery
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("smtp down")));

        var result = await svc.SendAsync(
            new OtpSendRequest("fail@test.com", "Email", OtpPurposes.VerifyEmail, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        db.OtpChallenges.Should().ContainSingle();
        (await db.OtpChallenges.AsNoTracking().SingleAsync()).ConsumedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Send_normalizes_purpose_case()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        var svc = CreateService(db, now, out var delivery);

        var result = await svc.SendAsync(
            new OtpSendRequest("case@test.com", "Email", "verifyemail", Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.OtpChallenges.Single().Purpose.Should().Be(OtpPurposes.VerifyEmail);
        await delivery.Received(1).DeliverAsync(
            "Email", "case@test.com", Arg.Any<string>(), OtpPurposes.VerifyEmail, Arg.Any<CancellationToken>());
    }

    private static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
