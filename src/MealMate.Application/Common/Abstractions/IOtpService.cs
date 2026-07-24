using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

public sealed record OtpSendRequest(
    string Destination,
    string Channel,
    string Purpose,
    Guid? UserId = null);

public sealed record OtpVerifyRequest(
    string Destination,
    string Purpose,
    string Code);

public interface IOtpService
{
    /// <summary>Creates and "sends" an OTP. Returns the plaintext code only in Development/stub mode via out of band logging.</summary>
    Task<Result> SendAsync(OtpSendRequest request, CancellationToken cancellationToken = default);

    Task<Result> VerifyAsync(OtpVerifyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Delivers OTP codes (SMS/email). Stub logs to console in development.</summary>
public interface IOtpDeliveryService
{
    Task DeliverAsync(string channel, string destination, string code, string purpose, CancellationToken cancellationToken = default);
}
