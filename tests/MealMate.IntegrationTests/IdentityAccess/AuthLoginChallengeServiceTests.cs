using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class AuthLoginChallengeServiceTests
{
    private static AppDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static AuthLoginChallengeService CreateService(AppDbContext db, DateTime now)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return new AuthLoginChallengeService(db, clock);
    }

    [Fact]
    public async Task Create_persists_hashed_token_and_invalidates_previous_active_challenges()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.AuthLoginChallenges.Add(new AuthLoginChallenge
        {
            UserId = userId,
            TokenHash = "OLD",
            NextStep = LoginChallengeSteps.EnrollTotp,
            CreatedAtUtc = now.AddMinutes(-1),
            ExpiresAtUtc = now.AddMinutes(4),
            MaxAttempts = 5,
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, now);
        var created = await svc.CreateAsync(
            new CreateLoginChallengeRequest(userId, LoginChallengeSteps.EnrollTotp, "device", "127.0.0.1"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        created.RawToken.Should().NotBeNullOrWhiteSpace();
        created.NextStep.Should().Be(LoginChallengeSteps.EnrollTotp);
        created.ExpiresAtUtc.Should().Be(now.AddMinutes(5));

        var challenges = await db.AuthLoginChallenges.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToListAsync();
        challenges.Should().HaveCount(2);
        challenges[0].ConsumedAtUtc.Should().NotBeNull();
        challenges[1].TokenHash.Should().Be(HashToken(created.RawToken));
        challenges[1].DeviceInfo.Should().Be("device");
    }

    [Fact]
    public async Task Resolve_rejects_expired_consumed_and_wrong_step_challenges()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var svc = CreateService(db, now);
        var created = await svc.CreateAsync(
            new CreateLoginChallengeRequest(userId, LoginChallengeSteps.VerifyTotp, null, null),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var wrongStep = await svc.ResolveAsync(created.RawToken, LoginChallengeSteps.EnrollTotp, CancellationToken.None);
        wrongStep.IsFailure.Should().BeTrue();
        wrongStep.Error.Code.Should().Be(MessageCodes.Auth.TotpSetupNotAllowed);

        var consumed = await svc.TryConsumeAsync(
            new ResolvedLoginChallenge(created.ChallengeId, userId, LoginChallengeSteps.VerifyTotp, 0, 5, now.AddMinutes(5)),
            CancellationToken.None);
        consumed.IsSuccess.Should().BeTrue();

        var reused = await svc.ResolveAsync(created.RawToken, LoginChallengeSteps.VerifyTotp, CancellationToken.None);
        reused.IsFailure.Should().BeTrue();
        reused.Error.Code.Should().Be(MessageCodes.Auth.TotpChallengeInvalid);
    }

    [Fact]
    public async Task RecordFailedAttempt_consumes_challenge_at_max_attempts()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var challenge = new AuthLoginChallenge
        {
            UserId = userId,
            TokenHash = HashToken("token"),
            NextStep = LoginChallengeSteps.VerifyTotp,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(5),
            AttemptCount = 4,
            MaxAttempts = 5,
        };
        db.AuthLoginChallenges.Add(challenge);
        await db.SaveChangesAsync();

        var svc = CreateService(db, now);
        var resolved = new ResolvedLoginChallenge(challenge.Id, userId, LoginChallengeSteps.VerifyTotp, 4, 5, now.AddMinutes(5));
        var result = await svc.RecordFailedAttemptAsync(resolved, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var stored = await db.AuthLoginChallenges.AsNoTracking().SingleAsync();
        stored.AttemptCount.Should().Be(5);
        stored.ConsumedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task InvalidateAllForUser_marks_active_challenges_consumed()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.AuthLoginChallenges.AddRange(
            new AuthLoginChallenge
            {
                UserId = userId,
                TokenHash = HashToken("active"),
                NextStep = LoginChallengeSteps.EnrollTotp,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(5),
                MaxAttempts = 5,
            },
            new AuthLoginChallenge
            {
                UserId = userId,
                TokenHash = HashToken("consumed"),
                NextStep = LoginChallengeSteps.VerifyTotp,
                CreatedAtUtc = now.AddMinutes(-10),
                ExpiresAtUtc = now.AddMinutes(-5),
                MaxAttempts = 5,
                ConsumedAtUtc = now.AddMinutes(-6),
            },
            new AuthLoginChallenge
            {
                UserId = Guid.NewGuid(),
                TokenHash = HashToken("other-user"),
                NextStep = LoginChallengeSteps.VerifyTotp,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(5),
                MaxAttempts = 5,
            });
        await db.SaveChangesAsync();

        var svc = CreateService(db, now);
        await svc.InvalidateAllForUserAsync(userId, CancellationToken.None);

        var challenges = await db.AuthLoginChallenges.AsNoTracking().OrderBy(x => x.TokenHash).ToListAsync();
        challenges.Single(x => x.TokenHash == HashToken("active")).ConsumedAtUtc.Should().NotBeNull();
        challenges.Single(x => x.TokenHash == HashToken("consumed")).ConsumedAtUtc.Should().Be(now.AddMinutes(-6));
        challenges.Single(x => x.TokenHash == HashToken("other-user")).ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_rejects_unknown_expected_step()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var svc = CreateService(db, now);
        var created = await svc.CreateAsync(
            new CreateLoginChallengeRequest(userId, LoginChallengeSteps.VerifyTotp, null, null),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var result = await svc.ResolveAsync(created.RawToken, "invalid_step", CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.TotpChallengeInvalid);
    }

    [Fact]
    public async Task TryConsume_allows_only_one_concurrent_winner()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        await using var seedDb = CreateDbContext(dbName);
        var seedSvc = CreateService(seedDb, now);
        var created = await seedSvc.CreateAsync(
            new CreateLoginChallengeRequest(userId, LoginChallengeSteps.EnrollTotp, null, null),
            CancellationToken.None);
        await seedDb.SaveChangesAsync();

        var resolved = new ResolvedLoginChallenge(
            created.ChallengeId,
            userId,
            LoginChallengeSteps.EnrollTotp,
            0,
            5,
            now.AddMinutes(5));

        async Task<Result> ConsumeOnceAsync()
        {
            await using var db = CreateDbContext(dbName);
            var svc = CreateService(db, now);
            var result = await svc.TryConsumeAsync(resolved, CancellationToken.None);
            if (result.IsSuccess)
            {
                await db.SaveChangesAsync();
            }

            return result;
        }

        var results = await Task.WhenAll(ConsumeOnceAsync(), ConsumeOnceAsync());
        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Count(r => r.IsFailure).Should().Be(1);

        await using var verifyDb = CreateDbContext(dbName);
        var stored = await verifyDb.AuthLoginChallenges.AsNoTracking().SingleAsync();
        stored.ConsumedAtUtc.Should().NotBeNull();
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
