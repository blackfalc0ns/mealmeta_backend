using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MealMate.Persistence.Identity;

public sealed class OtpService : IOtpService
{
    private const int MaxSendsPerHour = 5;
    private const int MaxSerializationRetries = 3;

    private readonly AppDbContext _db;
    private readonly IOtpDeliveryService _delivery;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<OtpService> _logger;
    private readonly OtpOptions _options;

    public OtpService(
        AppDbContext db,
        IOtpDeliveryService delivery,
        IDateTimeProvider clock,
        ILogger<OtpService> logger,
        IOptions<OtpOptions>? options = null)
    {
        _db = db;
        _delivery = delivery;
        _clock = clock;
        _logger = logger;
        _options = options?.Value ?? new OtpOptions();
    }

    public async Task<Result> SendAsync(OtpSendRequest request, CancellationToken cancellationToken = default)
    {
        var destination = NormalizeDestination(request.Destination);
        var purpose = OtpPurposes.Normalize(request.Purpose);
        var code = SelectCode(request.Channel, _options);

        for (var attempt = 0; attempt < MaxSerializationRetries; attempt++)
        {
            try
            {
                var challenge = await PersistChallengeAsync(
                    request,
                    destination,
                    purpose,
                    code,
                    cancellationToken);

                try
                {
                    await _delivery.DeliverAsync(
                        request.Channel,
                        destination,
                        code,
                        purpose,
                        cancellationToken,
                        request.TemplateVariables,
                        request.Language);
                }
                catch (Exception ex)
                {
                    challenge.ConsumedAtUtc = _clock.UtcNow;
                    // Delivery is outside the command UoW; persist invalidation immediately
                    // so a failed send cannot leave a usable undisclosed OTP.
                    await _db.SaveChangesAsync(cancellationToken);
                    _logger.LogError(ex, "OTP delivery failed for {Destination} purpose={Purpose}", destination, purpose);
                    return Error.Failure(MessageCodes.Domain.Generic, "OTP delivery failed.");
                }

                _logger.LogInformation("OTP queued for {Destination} purpose={Purpose}", destination, purpose);
                return Result.Success();
            }
            catch (OtpRateLimitExceededException)
            {
                return Error.Validation(MessageCodes.Auth.OtpSendLimitExceeded, "Too many OTP requests. Please try again later.");
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex))
            {
                _db.ChangeTracker.Clear();
                if (attempt < MaxSerializationRetries - 1)
                {
                    _logger.LogWarning(ex, "OTP send serialization conflict for {Destination}; retrying.", destination);
                    continue;
                }

                _logger.LogError(ex, "OTP send serialization retries exhausted for {Destination}.", destination);
                return Error.Failure(MessageCodes.Common.ServerError, "OTP service temporarily unavailable. Please try again.");
            }
        }

        throw new UnreachableException();
    }

    public async Task<Result> VerifyAsync(OtpVerifyRequest request, CancellationToken cancellationToken = default)
    {
        var destination = NormalizeDestination(request.Destination);
        var purpose = OtpPurposes.Normalize(request.Purpose);
        var challenge = await _db.OtpChallenges
            .AsNoTracking()
            .Where(x => x.Destination == destination
                        && x.Purpose == purpose
                        && x.ConsumedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            return Error.Validation(MessageCodes.Auth.OtpInvalid, "No active OTP challenge.");
        }

        if (challenge.ExpiresAtUtc <= _clock.UtcNow)
        {
            return Error.Validation(MessageCodes.Auth.OtpExpired, "OTP expired.");
        }

        if (challenge.AttemptCount >= challenge.MaxAttempts)
        {
            return Error.Validation(MessageCodes.Auth.OtpTooManyAttempts, "Too many OTP attempts.");
        }

        if (!FixedTimeEquals(challenge.CodeHash, Hash(request.Code)))
        {
            if (UseCasUpdates())
            {
                var incremented = await _db.OtpChallenges
                    .Where(x => x.Id == challenge.Id
                                && x.ConsumedAtUtc == null
                                && x.AttemptCount == challenge.AttemptCount)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(c => c.AttemptCount, challenge.AttemptCount + 1),
                        cancellationToken);

                if (incremented == 0)
                {
                    return Error.Validation(MessageCodes.Auth.OtpInvalid, "Invalid OTP code.");
                }
            }
            else
            {
                await IncrementFailedAttemptAsync(challenge, cancellationToken);
            }

            return Error.Validation(MessageCodes.Auth.OtpInvalid, "Invalid OTP code.");
        }

        if (UseCasUpdates())
        {
            var consumed = await _db.OtpChallenges
                .Where(x => x.Id == challenge.Id
                            && x.ConsumedAtUtc == null
                            && x.AttemptCount == challenge.AttemptCount
                            && x.ExpiresAtUtc > _clock.UtcNow
                            && x.AttemptCount < x.MaxAttempts)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.AttemptCount, challenge.AttemptCount + 1)
                        .SetProperty(c => c.ConsumedAtUtc, _clock.UtcNow),
                    cancellationToken);

            if (consumed == 0)
            {
                return Error.Validation(MessageCodes.Auth.OtpInvalid, "No active OTP challenge.");
            }
        }
        else
        {
            if (!await ConsumeChallengeAsync(challenge, cancellationToken))
            {
                return Error.Validation(MessageCodes.Auth.OtpInvalid, "No active OTP challenge.");
            }
        }

        return Result.Success();
    }

    private bool UseCasUpdates() =>
        _db.Database.IsRelational()
        && _db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;

    private async Task IncrementFailedAttemptAsync(OtpChallenge challenge, CancellationToken cancellationToken)
    {
        var tracked = await _db.OtpChallenges
            .FirstOrDefaultAsync(x => x.Id == challenge.Id && x.ConsumedAtUtc == null, cancellationToken);
        if (tracked is null)
        {
            return;
        }

        tracked.AttemptCount++;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ConsumeChallengeAsync(OtpChallenge challenge, CancellationToken cancellationToken)
    {
        var tracked = await _db.OtpChallenges
            .FirstOrDefaultAsync(x => x.Id == challenge.Id && x.ConsumedAtUtc == null, cancellationToken);
        if (tracked is null)
        {
            return false;
        }

        tracked.AttemptCount++;
        tracked.ConsumedAtUtc = _clock.UtcNow;
        // Persist consumption before returning so downstream handler failures
        // cannot leave a reusable OTP in the database.
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<OtpChallenge> PersistChallengeAsync(
        OtpSendRequest request,
        string destination,
        string purpose,
        string code,
        CancellationToken cancellationToken)
    {
        return await PersistChallengeCoreAsync(request, destination, purpose, code, cancellationToken);
    }

    private async Task<OtpChallenge> PersistChallengeCoreAsync(
        OtpSendRequest request,
        string destination,
        string purpose,
        string code,
        CancellationToken cancellationToken)
    {
        var recentSends = await _db.OtpChallenges
            .CountAsync(x => x.Destination == destination
                             && x.Purpose == purpose
                             && x.CreatedAtUtc > _clock.UtcNow.AddHours(-1), cancellationToken);

        if (recentSends >= MaxSendsPerHour)
        {
            throw new OtpRateLimitExceededException();
        }

        var previous = await _db.OtpChallenges
            .Where(x => x.Destination == destination
                        && x.Purpose == purpose
                        && x.ConsumedAtUtc == null
                        && x.ExpiresAtUtc > _clock.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var item in previous)
        {
            item.ConsumedAtUtc = _clock.UtcNow;
        }

        var challenge = new OtpChallenge
        {
            UserId = request.UserId,
            Destination = destination,
            Channel = request.Channel,
            Purpose = purpose,
            CodeHash = Hash(code),
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = _clock.UtcNow.AddMinutes(10),
            MaxAttempts = 5,
        };

        _db.OtpChallenges.Add(challenge);

        // External OTP delivery cannot participate in the command UoW transaction.
        // Persist invalidation + the new challenge before delivery so a crash or
        // failed send never leaves a valid undisclosed code in the database.
        await _db.SaveChangesAsync(cancellationToken);
        return challenge;
    }

    private static bool IsSerializationFailure(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure };

    private static string NormalizeDestination(string destination) => destination.Trim().ToLowerInvariant();

    private static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    /// <summary>
    /// Pure selector: returns the OTP code for the given channel and options.
    /// WhatsApp/Phone channels use <see cref="OtpOptions.WhatsAppFixedCode"/> when set;
    /// all other channels generate a random code. Legacy <see cref="OtpOptions.FixedCode"/>
    /// is a test-only fallback when <c>WhatsAppFixedCode</c> is absent.
    /// </summary>
    /// <param name="channel">The delivery channel (e.g. WhatsApp, Email, Phone).</param>
    /// <param name="options">OTP configuration options.</param>
    /// <param name="codeFactory">
    /// Optional factory used when no applicable fixed code exists.
    /// Defaults to cryptographically secure <see cref="RandomNumberGenerator"/> in production.
    /// Intended for deterministic unit testing only.
    /// </param>
    public static string SelectCode(string? channel, OtpOptions options, Func<string>? codeFactory = null)
    {
        var normalized = channel?.Trim();
        if ((string.Equals(normalized, "WhatsApp", StringComparison.OrdinalIgnoreCase)
             || string.Equals(normalized, "Phone", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(options.WhatsAppFixedCode))
        {
            return options.WhatsAppFixedCode;
        }

        if (!string.IsNullOrWhiteSpace(options.FixedCode))
        {
            return options.FixedCode;
        }

        return codeFactory is not null
            ? codeFactory()
            : RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }

    private sealed class OtpRateLimitExceededException : Exception;
}
