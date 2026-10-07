using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record SandboxCheckoutPolicy(bool Enabled, string PublicBaseUrl)
{
    public const string SettingKey = "checkout.sandbox_completion";
    public const int PaymentMethodId = -1;
    public static async Task<SandboxCheckoutPolicy?> ReadAsync(IAppDbContext db, CancellationToken ct = default)
    {
        // Both switches must be explicit. Switching the real gateway to live immediately disables completion.
        if (!await db.PayzahGatewayConfigs.AsNoTracking().AnyAsync(
            x => x.GatewayName == "Payzah" && x.IsSandbox && x.BaseUrl == "https://development.payzah.net/ws/paymentgateway", ct))
            return null;
        var json = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key == SettingKey && x.IsActive).Select(x => x.ValueJson).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var policy = JsonSerializer.Deserialize<SandboxCheckoutPolicy>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return policy is { Enabled: true } && Uri.TryCreate(policy.PublicBaseUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)
                && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) ? policy : null;
        }
        catch (JsonException) { return null; }
    }
}

public sealed record SandboxCheckoutPage(Guid SubscriptionId, decimal Amount, string CurrencyCode, string CallbackUrl);
public interface ISandboxCheckoutService
{
    Task<bool> IsEnabledAsync(CancellationToken ct = default);
    Task<string> StartAsync(Guid subscriptionId, Guid customerUserId, DateTime expiresAtUtc, string callbackUrl, CancellationToken ct = default);
    Task<bool> HasStartedAsync(Guid subscriptionId, CancellationToken ct = default);
    Task<Result<SandboxCheckoutPage>> ReadPageAsync(string token, CancellationToken ct = default);
    Task<Result<SandboxCheckoutPage>> CompleteAsync(string token, CancellationToken ct = default);
}

public sealed class SandboxCheckoutService(
    IAppDbContext db, IEmailSecretProtector protector, IDateTimeProvider clock,
    ICheckoutTransactionExecutor transactions, ICapacityConcurrencyLock capacityLock,
    ISubscriptionFulfillmentService fulfillment) : ISandboxCheckoutService
{
    private const string Purpose = "MealMate.SandboxCheckout.v1";
    private const string Started = "SANDBOX_CHECKOUT_STARTED";
    private const string Completed = "SANDBOX_CHECKOUT_CONFIRMED";
    private sealed record Ticket(string Purpose, Guid SubscriptionId, Guid CustomerUserId, DateTime ExpiresAtUtc, string CallbackUrl, string ReservationHash);

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default) => await SandboxCheckoutPolicy.ReadAsync(db, ct) is not null;
    public Task<bool> HasStartedAsync(Guid subscriptionId, CancellationToken ct = default) => db.SubscriptionOperations.AsNoTracking()
        .AnyAsync(x => x.SubscriptionId == subscriptionId && x.OperationType == Started, ct);

    public async Task<string> StartAsync(Guid subscriptionId, Guid customerUserId, DateTime expiresAtUtc, string callbackUrl, CancellationToken ct = default)
    {
        return await transactions.ExecuteAsync(async transactionCt => {
        await capacityLock.AcquireSubscriptionAsync(subscriptionId, transactionCt);
        var policy = await SandboxCheckoutPolicy.ReadAsync(db, transactionCt)
            ?? throw new InvalidOperationException("Sandbox completion is disabled.");
        if (expiresAtUtc <= clock.UtcNow || !Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callback)
            || (callback.Scheme != "https" && callback.Scheme != "mealmate"))
            throw new InvalidOperationException("Invalid sandbox checkout destination or expiry.");
        if (!await db.Subscriptions.AnyAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId
            && x.FamilyGroupId == null && x.Status == SubscriptionStatus.PendingPayment && x.PaymentId == null, transactionCt))
            throw new InvalidOperationException("Subscription is not eligible for sandbox completion.");
        var token = protector.Encrypt(JsonSerializer.Serialize(new Ticket(Purpose, subscriptionId, customerUserId, expiresAtUtc, callbackUrl,
            await ReservationHashAsync(subscriptionId, transactionCt))));
        db.SubscriptionOperations.Add(new SubscriptionOperation {
            SubscriptionId = subscriptionId, OperationType = Started, PreviousStatus = SubscriptionStatus.PendingPayment,
            NewStatus = SubscriptionStatus.PendingPayment, Reason = "Sandbox checkout opened; no funds collected.",
            PerformedByUserId = customerUserId, PerformedByUserType = UserType.Customer,
            CorrelationId = Hash(token), TimestampUtc = clock.UtcNow
        });
        await db.SaveChangesAsync(transactionCt);
        return policy.PublicBaseUrl.TrimEnd('/') + "/api/v1/payments/sandbox?token=" + Uri.EscapeDataString(token);
        }, ct);
    }

    public async Task<Result<SandboxCheckoutPage>> ReadPageAsync(string token, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct)) return Error.Conflict("checkout.sandbox_disabled");
        var ticket = ReadTicket(token);
        if (ticket is null) return Error.Validation("checkout.sandbox_link_invalid");
        var subscription = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == ticket.SubscriptionId && x.CustomerUserId == ticket.CustomerUserId && x.FamilyGroupId == null, ct);
        if (subscription is null || !await IsIssuedAsync(ticket, token, ct)) return Error.NotFound("subscription.not_found");
        return Page(subscription, ticket);
    }

    public async Task<Result<SandboxCheckoutPage>> CompleteAsync(string token, CancellationToken ct = default)
    {
        var ticket = ReadTicket(token);
        if (ticket is null) return Error.Validation("checkout.sandbox_link_invalid");
        var transition = await transactions.ExecuteAsync<Result<SandboxCheckoutPage>>(async transactionCt => {
            await capacityLock.AcquireSubscriptionAsync(ticket.SubscriptionId, transactionCt);
            if (!await IsEnabledAsync(transactionCt)) return Error.Conflict("checkout.sandbox_disabled");
            var subscription = await db.Subscriptions.FirstOrDefaultAsync(
                x => x.Id == ticket.SubscriptionId && x.CustomerUserId == ticket.CustomerUserId && x.FamilyGroupId == null, transactionCt);
            if (subscription is null || !await IsIssuedAsync(ticket, token, transactionCt)) return Error.NotFound("subscription.not_found");
            if (await db.SubscriptionOperations.AnyAsync(x => x.SubscriptionId == subscription.Id && x.OperationType == Completed, transactionCt))
            {
                if (subscription.Status is not (SubscriptionStatus.Paid or SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
                    return Error.Conflict("checkout.sandbox_state_invalid");
                return Page(subscription, ticket);
            }
            if (subscription.Status != SubscriptionStatus.PendingPayment || !string.IsNullOrEmpty(subscription.PaymentId))
                return Error.Conflict("checkout.sandbox_state_invalid");
            var holds = await db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id).ToListAsync(transactionCt);
            if (holds.Count == 0 || holds.Any(x => x.Status != CapacityHoldStatus.PendingPayment || x.ExpiresAtUtc <= clock.UtcNow))
                return Error.Conflict("checkout.capacity_hold_expired");
            foreach (var hold in holds) { hold.Status = CapacityHoldStatus.PaymentConfirmed; hold.ConfirmedAtUtc = clock.UtcNow; }
            subscription.Status = SubscriptionStatus.Paid;
            subscription.LastOperationAtUtc = clock.UtcNow;
            // No provider receipt, invoice id or payment verification timestamp is fabricated.
            db.SubscriptionOperations.Add(new SubscriptionOperation {
                SubscriptionId = subscription.Id, OperationType = Completed, PreviousStatus = SubscriptionStatus.PendingPayment,
                NewStatus = SubscriptionStatus.Paid, Reason = "Customer confirmed sandbox checkout without collecting funds.",
                PerformedByUserId = ticket.CustomerUserId, PerformedByUserType = UserType.Customer,
                CorrelationId = Hash(token), TimestampUtc = clock.UtcNow
            });
            await db.SaveChangesAsync(transactionCt);
            return Page(subscription, ticket);
        }, ct);
        if (transition.IsFailure) return transition;
        var result = await fulfillment.TryFulfillAsync(ticket.SubscriptionId, ct);
        return result.IsSuccess ? transition : Error.Conflict(result.ErrorCode ?? "checkout.fulfillment_pending");
    }

    private Ticket? ReadTicket(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return null;
        try
        {
            var ticket = JsonSerializer.Deserialize<Ticket>(protector.Decrypt(token));
            return ticket is not null && ticket.Purpose == Purpose && ticket.ExpiresAtUtc > clock.UtcNow ? ticket : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException) { return null; }
    }
    private async Task<bool> IsIssuedAsync(Ticket ticket, string token, CancellationToken ct)
    {
        var tokenHash = Hash(token);
        return await db.SubscriptionOperations.AsNoTracking().AnyAsync(x => x.SubscriptionId == ticket.SubscriptionId
            && x.PerformedByUserId == ticket.CustomerUserId && x.OperationType == Started && x.CorrelationId == tokenHash, ct)
            && ticket.ReservationHash == await ReservationHashAsync(ticket.SubscriptionId, ct);
    }
    private async Task<string> ReservationHashAsync(Guid subscriptionId, CancellationToken ct)
    {
        var quote = await db.Subscriptions.AsNoTracking().Where(x => x.Id == subscriptionId)
            .Select(x => new { x.CustomerUserId, x.StartDate, x.DurationDays, x.IncludeFriday, x.PaidAmount.Amount, x.PaidAmount.CurrencyCode }).FirstAsync(ct);
        var holds = await db.CapacityHolds.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.RestaurantId, x.MenuPlanVersionId, x.ServiceDate, x.Quantity, x.ExpiresAtUtc }).ToListAsync(ct);
        return Hash(JsonSerializer.Serialize(new { quote, holds }));
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static SandboxCheckoutPage Page(Subscription subscription, Ticket ticket) => new(
        subscription.Id, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode, ticket.CallbackUrl);
}
