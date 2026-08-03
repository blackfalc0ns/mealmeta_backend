using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using Microsoft.AspNetCore.DataProtection;

namespace MealMate.Persistence.Identity;

public sealed class RestaurantAccountRegistrationTokenService : IRestaurantAccountRegistrationTokenService
{
    private const string ProtectorPurpose = "MealMate.RestaurantAccountRegistration.v1";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IDataProtector _protector;
    private readonly IDateTimeProvider _clock;

    public RestaurantAccountRegistrationTokenService(
        IDataProtectionProvider dataProtection,
        IDateTimeProvider clock)
    {
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _clock = clock;
    }

    public CreatedRegistrationToken CreateToken(PendingRegistration payload)
    {
        var expiresAtUtc = _clock.UtcNow.Add(TokenLifetime);

        var envelope = new TokenEnvelope
        {
            Email = payload.Email,
            Phone = payload.Phone,
            Password = payload.Password,
            FullName = payload.FullName,
            ExpiresAtUtc = expiresAtUtc,
        };

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var protectedToken = _protector.Protect(json);

        return new CreatedRegistrationToken(protectedToken, expiresAtUtc);
    }

    public Result<PendingRegistration> ResolveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Error.Validation(MessageCodes.Auth.InvalidState, "Registration token is required.");
        }

        string json;
        try
        {
            json = _protector.Unprotect(token);
        }
        catch
        {
            return Error.Validation(MessageCodes.Auth.InvalidState, "Invalid registration token.");
        }

        TokenEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<TokenEnvelope>(json, JsonOptions);
        }
        catch
        {
            return Error.Validation(MessageCodes.Auth.InvalidState, "Invalid registration token.");
        }

        if (envelope is null)
        {
            return Error.Validation(MessageCodes.Auth.InvalidState, "Invalid registration token.");
        }

        if (envelope.ExpiresAtUtc < _clock.UtcNow)
        {
            return Error.Validation(MessageCodes.Auth.InvalidState, "Registration token has expired.");
        }

        return new PendingRegistration(
            envelope.Email,
            envelope.Phone,
            envelope.Password,
            envelope.FullName);
    }

    private sealed class TokenEnvelope
    {
        public string Email { get; set; } = default!;
        public string Phone { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string FullName { get; set; } = default!;
        public DateTime ExpiresAtUtc { get; set; }
    }
}
