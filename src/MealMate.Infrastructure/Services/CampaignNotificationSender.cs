using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Services;

public sealed class CampaignNotificationSender : ICampaignNotificationSender
{
    private readonly ILogger<CampaignNotificationSender> _logger;

    public CampaignNotificationSender(ILogger<CampaignNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending email notification to {Email} with subject: {Subject}", recipientEmail, subject);
        return Task.CompletedTask;
    }

    public Task SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending SMS notification to {Phone}: {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }

    public Task SendPushNotificationAsync(string recipientUserId, string title, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending push notification to User {UserId} [{Title}]: {Message}", recipientUserId, title, message);
        return Task.CompletedTask;
    }
}
