using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Identity;

public sealed class OtpService : IOtpService
{
    private readonly AppDbContext _db;
    private readonly IOtpDeliveryService _delivery;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<OtpService> _logger;

    public OtpService(
        AppDbContext db,
        IOtpDeliveryService delivery,
        IDateTimeProvider clock,
        ILogger<OtpService> logger)
    {
        _db = db;
        _delivery = delivery;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> SendAsync(OtpSendRequest request, CancellationToken cancellationToken = default)
    {
        var destination = Normalize(request.Destination);
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        // Invalidate previous active challenges for same destination+purpose
        var previous = await _db.OtpChallenges
            .Where(x => x.Destination == destination
                        && x.Purpose == request.Purpose
                        && x.ConsumedAtUtc == null
                        && x.ExpiresAtUtc > _clock.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var item in previous)
        {
            item.ConsumedAtUtc = _clock.UtcNow;
        }

        _db.OtpChallenges.Add(new OtpChallenge
        {
            UserId = request.UserId,
            Destination = destination,
            Channel = request.Channel,
            Purpose = request.Purpose,
            CodeHash = Hash(code),
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = _clock.UtcNow.AddMinutes(10),
            MaxAttempts = 5,
        });

        await _delivery.DeliverAsync(request.Channel, destination, code, request.Purpose, cancellationToken);
        _logger.LogInformation("OTP queued for {Destination} purpose={Purpose}", destination, request.Purpose);
        return Result.Success();
    }

    public async Task<Result> VerifyAsync(OtpVerifyRequest request, CancellationToken cancellationToken = default)
    {
        var destination = Normalize(request.Destination);
        var challenge = await _db.OtpChallenges
            .Where(x => x.Destination == destination
                        && x.Purpose == request.Purpose
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

        challenge.AttemptCount++;
        if (!FixedTimeEquals(challenge.CodeHash, Hash(request.Code)))
        {
            return Error.Validation(MessageCodes.Auth.OtpInvalid, "Invalid OTP code.");
        }

        challenge.ConsumedAtUtc = _clock.UtcNow;
        return Result.Success();
    }

    private static string Normalize(string destination) => destination.Trim().ToLowerInvariant();

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
}
