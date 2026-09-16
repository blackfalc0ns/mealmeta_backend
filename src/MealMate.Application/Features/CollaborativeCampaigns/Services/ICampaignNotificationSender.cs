namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface ICampaignNotificationSender
{
    Task SendEmailAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken = default);
    Task SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
    Task SendPushNotificationAsync(string recipientUserId, string title, string message, CancellationToken cancellationToken = default);
}
