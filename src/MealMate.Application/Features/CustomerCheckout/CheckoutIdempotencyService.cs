using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public enum CheckoutAttemptState
{
    New,
    InProgress,
    Replay,
    Conflict,
    QuoteConsumed,
    Unavailable
}

public sealed record CheckoutAttemptInspection(
    CheckoutAttemptState State,
    CheckoutStartedDto? Response = null,
    Guid? SubscriptionId = null);

public sealed class CheckoutIdempotencyClaimException(string code, Exception? innerException = null) : Exception(code, innerException)
{
    public string Code { get; } = code;
}

/// <summary>Persists the ownership and outcome of quote-backed individual checkout attempts.</summary>
public sealed class CheckoutIdempotencyService(IAppDbContext db, IDateTimeProvider clock)
{
    private const string QuoteScope = "CustomerCheckout:Quote";
    private const int InProgressStatus = 202;
    private const int CompletedStatus = 200;

    public async Task<CheckoutAttemptInspection> InspectAsync(
        Guid customerId,
        StartCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return new(CheckoutAttemptState.Conflict);

        var scope = CustomerScope(customerId);
        var fingerprint = ComputeFingerprint(customerId, request);
        var attempt = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == request.IdempotencyKey, cancellationToken);
        if (attempt is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(attempt.RequestHash), Encoding.ASCII.GetBytes(fingerprint)))
                return new(CheckoutAttemptState.Conflict, SubscriptionId: ParseSubscriptionId(attempt.ResourceUri));

            var subscriptionId = ParseSubscriptionId(attempt.ResourceUri);
            if (attempt.HttpStatusCode == CompletedStatus && !string.IsNullOrWhiteSpace(attempt.ResponseBody))
            {
                var response = JsonSerializer.Deserialize<CheckoutStartedDto>(attempt.ResponseBody);
                if (response is not null) return new(CheckoutAttemptState.Replay, response, response.SubscriptionId);
            }
            return new(CheckoutAttemptState.InProgress, SubscriptionId: subscriptionId);
        }

        if (request.QuoteId is Guid quoteId)
        {
            var quoteClaim = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Scope == QuoteScope && x.Key == quoteId.ToString("N"), cancellationToken);
            if (quoteClaim is not null)
                return quoteClaim.UserId == customerId
                    ? new(CheckoutAttemptState.QuoteConsumed, SubscriptionId: ParseSubscriptionId(quoteClaim.ResourceUri))
                    : new(CheckoutAttemptState.Unavailable);
        }

        return new(CheckoutAttemptState.New);
    }

    /// <summary>
    /// Must be called inside the reservation transaction. The request claim, quote claim,
    /// subscription and capacity hold then commit or roll back together.
    /// </summary>
    public async Task ClaimAsync(
        Guid customerId,
        StartCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.QuoteId is null)
            throw new CheckoutIdempotencyClaimException("checkout.idempotency_key_required");

        var scope = CustomerScope(customerId);
        if (await db.IdempotencyRecords.AnyAsync(
                x => x.Scope == scope && x.Key == request.IdempotencyKey, cancellationToken))
            throw new CheckoutIdempotencyClaimException("checkout.idempotency_conflict");

        var quoteKey = request.QuoteId.Value.ToString("N");
        if (await db.IdempotencyRecords.AnyAsync(
                x => x.Scope == QuoteScope && x.Key == quoteKey, cancellationToken))
            throw new CheckoutIdempotencyClaimException("checkout.quote_changed");

        var now = clock.UtcNow;
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = scope,
            Key = request.IdempotencyKey,
            RequestHash = ComputeFingerprint(customerId, request),
            HttpStatusCode = InProgressStatus,
            UserId = customerId,
            CreatedAtUtc = now,
            ExpiresAtUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
        });
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = QuoteScope,
            Key = quoteKey,
            RequestHash = ComputeFingerprint(customerId, request),
            HttpStatusCode = InProgressStatus,
            UserId = customerId,
            CreatedAtUtc = now,
            ExpiresAtUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new CheckoutIdempotencyClaimException("checkout.idempotency_conflict", ex);
        }
    }

    public async Task BindSubscriptionAsync(
        Guid customerId,
        StartCheckoutRequest request,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var scope = CustomerScope(customerId);
        var attempt = await db.IdempotencyRecords.FirstAsync(
            x => x.Scope == scope && x.Key == request.IdempotencyKey, cancellationToken);
        var quoteClaim = await db.IdempotencyRecords.FirstAsync(
            x => x.Scope == QuoteScope && x.Key == request.QuoteId!.Value.ToString("N"), cancellationToken);
        attempt.ResourceUri = subscriptionId.ToString("D");
        quoteClaim.ResourceUri = subscriptionId.ToString("D");
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        Guid customerId,
        StartCheckoutRequest request,
        CheckoutStartedDto response,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.IdempotencyRecords.FirstAsync(
            x => x.Scope == CustomerScope(customerId) && x.Key == request.IdempotencyKey,
            cancellationToken);
        attempt.ResponseBody = JsonSerializer.Serialize(response);
        attempt.ResourceUri = response.SubscriptionId.ToString("D");
        attempt.HttpStatusCode = CompletedStatus;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CheckoutAttemptInspection?> FindForSubscriptionAsync(
        Guid customerId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var resource = subscriptionId.ToString("D");
        var attempt = await db.IdempotencyRecords.AsNoTracking()
            .Where(x => x.UserId == customerId
                && x.Scope.StartsWith($"CustomerCheckout:Individual:{customerId:N}")
                && x.ResourceUri == resource)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null) return null;
        if (attempt.HttpStatusCode == CompletedStatus && !string.IsNullOrWhiteSpace(attempt.ResponseBody))
        {
            var response = JsonSerializer.Deserialize<CheckoutStartedDto>(attempt.ResponseBody);
            if (response is not null) return new(CheckoutAttemptState.Replay, response, subscriptionId);
        }
        return new(CheckoutAttemptState.InProgress, SubscriptionId: subscriptionId);
    }

    public static string ComputeFingerprint(Guid customerId, StartCheckoutRequest request)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(new
        {
            customerId,
            request.QuoteId,
            request.OfferId,
            request.ProgramId,
            request.BundleId,
            request.Tier,
            request.DurationDays,
            request.StartDate,
            request.IncludeFriday,
            PromoCode = Normalize(request.PromoCode),
            CallbackUrl = request.CallbackUrl?.Trim(),
            ErrorUrl = request.ErrorUrl?.Trim(),
            request.PaymentMethodId
        });
        return Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    }

    private static string CustomerScope(Guid customerId) => $"CustomerCheckout:Individual:{customerId:N}";
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private static Guid? ParseSubscriptionId(string? value)
        => Guid.TryParse(value, out var id) ? id : null;
}
