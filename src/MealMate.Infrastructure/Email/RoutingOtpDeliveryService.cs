using MealMate.Application.Common.Abstractions;
using MealMate.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Email;

/// <summary>
/// Routes OTP delivery to the appropriate service based on channel.
/// Email channels (case-insensitive) always go to <see cref="ResendOtpDeliveryService"/>.
/// Non-email channels go to <see cref="LoggingOtpDeliveryService"/> in Development
/// and <see cref="SilentOtpDeliveryService"/> in all other environments.
/// Never logs OTP code values.
/// </summary>
public sealed class RoutingOtpDeliveryService : IOtpDeliveryService
{
    private readonly ResendOtpDeliveryService _resend;
    private readonly LoggingOtpDeliveryService _logging;
    private readonly SilentOtpDeliveryService _silent;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RoutingOtpDeliveryService> _logger;

    public RoutingOtpDeliveryService(
        ResendOtpDeliveryService resend,
        LoggingOtpDeliveryService logging,
        SilentOtpDeliveryService silent,
        IHostEnvironment environment,
        ILogger<RoutingOtpDeliveryService> logger)
    {
        _resend = resend;
        _logging = logging;
        _silent = silent;
        _environment = environment;
        _logger = logger;
    }

    public Task DeliverAsync(
        string channel,
        string destination,
        string code,
        string purpose,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? templateVariables = null,
        string? language = null)
    {
        if (string.Equals(channel, "Email", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Routing OTP to Resend for channel={Channel} purpose={Purpose}",
                channel,
                purpose);
            return _resend.DeliverAsync(
                channel, destination, code, purpose, cancellationToken, templateVariables, language);
        }

        if (_environment.IsDevelopment())
        {
            _logger.LogDebug(
                "Routing OTP to Logging stub for channel={Channel} purpose={Purpose}",
                channel,
                purpose);
            return _logging.DeliverAsync(
                channel, destination, code, purpose, cancellationToken, templateVariables, language);
        }

        _logger.LogDebug(
            "Routing OTP to Silent stub for channel={Channel} purpose={Purpose}",
            channel,
            purpose);
        return _silent.DeliverAsync(
            channel, destination, code, purpose, cancellationToken, templateVariables, language);
    }
}
