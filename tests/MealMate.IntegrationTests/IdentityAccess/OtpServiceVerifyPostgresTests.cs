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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.IdentityAccess;

public sealed class OtpServiceVerifyPostgresTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException(
                "Docker is required for OtpServiceVerifyPostgresTests. " +
                "Start Docker Desktop or the Docker daemon and re-run the IdentityAccess integration tests.",
                ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Verify_correct_code_consumes_challenge_and_increments_attempt_count()
    {
        const string code = "482910";
        await using var db = await CreateDbContextAsync(_container.GetConnectionString());
        var challengeId = await SeedChallengeAsync(db, "pg-verify@test.com", code);

        var svc = CreateService(db);
        var result = await svc.VerifyAsync(
            new OtpVerifyRequest("pg-verify@test.com", OtpPurposes.VerifyEmail, code),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var stored = await db.OtpChallenges.AsNoTracking().SingleAsync(x => x.Id == challengeId);
        stored.ConsumedAtUtc.Should().NotBeNull();
        stored.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Verify_wrong_code_increments_attempt_count_without_consuming()
    {
        const string code = "482910";
        await using var db = await CreateDbContextAsync(_container.GetConnectionString());
        var challengeId = await SeedChallengeAsync(db, "pg-wrong@test.com", code);

        var svc = CreateService(db);
        var result = await svc.VerifyAsync(
            new OtpVerifyRequest("pg-wrong@test.com", OtpPurposes.VerifyEmail, "000000"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.OtpInvalid);

        var stored = await db.OtpChallenges.AsNoTracking().SingleAsync(x => x.Id == challengeId);
        stored.ConsumedAtUtc.Should().BeNull();
        stored.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Verify_concurrent_correct_code_attempts_allow_exactly_one_success()
    {
        const string code = "482910";
        var connectionString = _container.GetConnectionString();
        await using var setupDb = await CreateDbContextAsync(connectionString);
        var challengeId = await SeedChallengeAsync(setupDb, "pg-race@test.com", code);

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        async Task<Result> VerifyOnceAsync()
        {
            await using var db = await CreateDbContextAsync(connectionString);
            var svc = CreateService(db);
            ready.Signal();
            ready.Wait();
            start.Wait();
            return await svc.VerifyAsync(
                new OtpVerifyRequest("pg-race@test.com", OtpPurposes.VerifyEmail, code),
                CancellationToken.None);
        }

        var first = VerifyOnceAsync();
        var second = VerifyOnceAsync();
        start.Set();
        var results = await Task.WhenAll(first, second);

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Count(r => r.IsFailure).Should().Be(1);
        results.Single(r => r.IsFailure).Error.Code.Should().Be(MessageCodes.Auth.OtpInvalid);

        await using var verifyDb = await CreateDbContextAsync(connectionString);
        var stored = await verifyDb.OtpChallenges.AsNoTracking().SingleAsync(x => x.Id == challengeId);
        stored.ConsumedAtUtc.Should().NotBeNull();
        stored.AttemptCount.Should().Be(1);
    }

    private static OtpService CreateService(AppDbContext db)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new OtpService(
            db,
            Substitute.For<IOtpDeliveryService>(),
            clock,
            NullLogger<OtpService>.Instance);
    }

    private static async Task<Guid> SeedChallengeAsync(AppDbContext db, string destination, string code)
    {
        var challenge = new OtpChallenge
        {
            Destination = destination.Trim().ToLowerInvariant(),
            Channel = "Email",
            Purpose = OtpPurposes.VerifyEmail,
            CodeHash = Hash(code),
            CreatedAtUtc = Now,
            ExpiresAtUtc = Now.AddMinutes(10),
            MaxAttempts = 5,
        };

        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();
        return challenge.Id;
    }

    private static async Task<AppDbContext> CreateDbContextAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        await db.Database.MigrateAsync();
        return db;
    }

    private static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
