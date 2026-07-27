using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Identity;

public sealed class AuthLoginChallengeService : IAuthLoginChallengeService
{
    private const int ChallengeLifetimeMinutes = 5;
    private const int MaxAttempts = 5;

    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public AuthLoginChallengeService(AppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<CreatedLoginChallenge> CreateAsync(
        CreateLoginChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        if (!LoginChallengeSteps.TryNormalize(request.NextStep, out var nextStep))
        {
            throw new ArgumentException($"Invalid login challenge step: {request.NextStep}", nameof(request));
        }

        var previous = await _db.AuthLoginChallenges
            .Where(x => x.UserId == request.UserId
                        && x.ConsumedAtUtc == null
                        && x.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var item in previous)
        {
            item.ConsumedAtUtc = now;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var challenge = new AuthLoginChallenge
        {
            UserId = request.UserId,
            TokenHash = HashToken(rawToken),
            NextStep = nextStep,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(ChallengeLifetimeMinutes),
            MaxAttempts = MaxAttempts,
            DeviceInfo = request.DeviceInfo,
            IpAddress = request.IpAddress,
        };

        _db.AuthLoginChallenges.Add(challenge);
        return new CreatedLoginChallenge(
            challenge.Id,
            rawToken,
            nextStep,
            request.UserId,
            challenge.ExpiresAtUtc);
    }

    public async Task<Result<ResolvedLoginChallenge>> ResolveAsync(
        string rawToken,
        string expectedNextStep,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge.");
        }

        var tokenHash = HashToken(rawToken);
        var challenge = await _db.AuthLoginChallenges
            .AsNoTracking()
            .Where(x => x.TokenHash == tokenHash)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (!LoginChallengeSteps.TryNormalize(expectedNextStep, out var expectedStep))
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge.");
        }

        if (challenge is null)
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge.");
        }

        if (!string.Equals(challenge.NextStep, expectedStep, StringComparison.Ordinal))
        {
            return Error.Validation(MessageCodes.Auth.TotpSetupNotAllowed, "Login challenge step mismatch.");
        }

        if (challenge.ConsumedAtUtc is not null)
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Login challenge already used.");
        }

        if (challenge.ExpiresAtUtc <= _clock.UtcNow)
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeExpired, "Login challenge expired.");
        }

        if (challenge.AttemptCount >= challenge.MaxAttempts)
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeTooManyAttempts, "Too many login challenge attempts.");
        }

        return new ResolvedLoginChallenge(
            challenge.Id,
            challenge.UserId,
            challenge.NextStep,
            challenge.AttemptCount,
            challenge.MaxAttempts,
            challenge.ExpiresAtUtc);
    }

    public async Task<Result> RecordFailedAttemptAsync(
        ResolvedLoginChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        if (UseCasUpdates())
        {
            var incremented = await _db.AuthLoginChallenges
                .Where(x => x.Id == challenge.Id
                            && x.ConsumedAtUtc == null
                            && x.AttemptCount == challenge.AttemptCount
                            && x.ExpiresAtUtc > _clock.UtcNow)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.AttemptCount, challenge.AttemptCount + 1),
                    cancellationToken);

            if (incremented == 0)
            {
                return Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge.");
            }

            var nextAttemptCount = challenge.AttemptCount + 1;
            if (nextAttemptCount >= challenge.MaxAttempts)
            {
                await _db.AuthLoginChallenges
                    .Where(x => x.Id == challenge.Id && x.ConsumedAtUtc == null)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(c => c.ConsumedAtUtc, _clock.UtcNow),
                        cancellationToken);
            }

            return Result.Success();
        }

        var tracked = await _db.AuthLoginChallenges
            .FirstOrDefaultAsync(x => x.Id == challenge.Id && x.ConsumedAtUtc == null, cancellationToken);
        if (tracked is null)
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge.");
        }

        tracked.AttemptCount++;
        if (tracked.AttemptCount >= tracked.MaxAttempts)
        {
            tracked.ConsumedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> TryConsumeAsync(
        ResolvedLoginChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        if (UseCasUpdates())
        {
            var consumed = await _db.AuthLoginChallenges
                .Where(x => x.Id == challenge.Id
                            && x.ConsumedAtUtc == null
                            && x.AttemptCount == challenge.AttemptCount
                            && x.ExpiresAtUtc > _clock.UtcNow
                            && x.AttemptCount < x.MaxAttempts)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.ConsumedAtUtc, _clock.UtcNow),
                    cancellationToken);

            return consumed == 0
                ? Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge.")
                : Result.Success();
        }

        var tracked = await _db.AuthLoginChallenges
            .FirstOrDefaultAsync(x => x.Id == challenge.Id && x.ConsumedAtUtc == null, cancellationToken);
        if (tracked is null || tracked.ExpiresAtUtc <= _clock.UtcNow)
        {
            return Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge.");
        }

        tracked.ConsumedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        if (UseCasUpdates())
        {
            await _db.AuthLoginChallenges
                .Where(x => x.UserId == userId && x.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.ConsumedAtUtc, now),
                    cancellationToken);
            return;
        }

        var active = await _db.AuthLoginChallenges
            .Where(x => x.UserId == userId && x.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var challenge in active)
        {
            challenge.ConsumedAtUtc = now;
        }

        if (active.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private bool UseCasUpdates() =>
        _db.Database.IsRelational()
        && _db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
