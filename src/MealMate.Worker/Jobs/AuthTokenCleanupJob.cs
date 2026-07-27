using MealMate.Application.Common.Abstractions;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class AuthTokenCleanupJob : ScheduledJob
{
    private const int RefreshTokenRetentionDays = 30;
    private const int OtpChallengeRetentionDays = 7;
    private const int AuthLoginChallengeRetentionDays = 7;

    private readonly ILogger<AuthTokenCleanupJob> _logger;

    public AuthTokenCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthTokenCleanupJob> logger)
        : base(scopeFactory, logger)
        => _logger = logger;

    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    protected override string JobName => nameof(AuthTokenCleanupJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var now = clock.UtcNow;

        var expiredRefreshTokens = await db.RefreshTokens
            .Where(x => x.ExpiresAtUtc < now.AddDays(-RefreshTokenRetentionDays))
            .ExecuteDeleteAsync(cancellationToken);

        var expiredOtpChallenges = await db.OtpChallenges
            .Where(x => x.ExpiresAtUtc < now.AddDays(-OtpChallengeRetentionDays))
            .ExecuteDeleteAsync(cancellationToken);

        var expiredLoginChallenges = await db.AuthLoginChallenges
            .Where(x => x.ExpiresAtUtc < now.AddDays(-AuthLoginChallengeRetentionDays))
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation(
            "{JobName} cleaned up {RefreshTokens} expired refresh tokens, {OtpChallenges} expired OTP challenges, and {LoginChallenges} expired login challenges.",
            JobName, expiredRefreshTokens, expiredOtpChallenges, expiredLoginChallenges);
    }
}
