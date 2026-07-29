using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Abstraction for sending transactional emails (OTP, password reset, notifications).
/// Implementations must not log message bodies or OTP codes.
/// </summary>
public interface ITransactionalEmailSender
{
    /// <summary>
    /// Sends a transactional email. Returns the provider message ID on success.
    /// </summary>
    Task<Result<string>> SendAsync(TransactionalEmailRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a transactional email to be sent.
/// </summary>
public sealed record TransactionalEmailRequest(
    string To,
    string Subject,
    string Html,
    string? Text = null,
    string? ReplyTo = null,
    string? IdempotencyKey = null,
    string? TemplateId = null);
