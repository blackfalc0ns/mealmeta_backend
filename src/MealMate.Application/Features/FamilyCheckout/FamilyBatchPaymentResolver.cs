using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

/// <summary>
/// Resolves which family checkout batch a gateway payment belongs to. A payment
/// link alone is not proof of success and a callback must never roll forward
/// onto a newer batch: only the batch whose reservation recorded this gateway
/// payment (or attempt) is eligible. Unresolvable payments go to review.
/// </summary>
public sealed record FamilyBatchPaymentResolution(
    Guid BatchId,
    long BatchVersion,
    FamilyCheckoutBatchStatus BatchStatus,
    decimal FinalPrice,
    string CurrencyCode,
    IReadOnlyList<Guid> SubscriptionIds);

public static class FamilyBatchPaymentResolver
{
    public static async Task<FamilyBatchPaymentResolution?> ResolveAsync(
        IAppDbContext db,
        Guid familyGroupId,
        string gatewayPaymentId,
        CancellationToken cancellationToken)
    {
        // The live batch the payment was reserved against (gateway id recorded at start).
        var openBatch = await db.FamilyCheckoutBatches.AsNoTracking()
            .Where(x => x.FamilyGroupId == familyGroupId
                && x.Status == FamilyCheckoutBatchStatus.PaymentPending)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.Version, x.Status, x.FinalPrice, x.CurrencyCode, x.GatewayPaymentId })
            .FirstOrDefaultAsync(cancellationToken);

        if (openBatch is not null
            && !string.IsNullOrWhiteSpace(openBatch.GatewayPaymentId)
            && string.Equals(openBatch.GatewayPaymentId, gatewayPaymentId, StringComparison.OrdinalIgnoreCase))
            return await BuildResolutionAsync(db, openBatch.Id, openBatch.Version, openBatch.Status, openBatch.FinalPrice, openBatch.CurrencyCode, cancellationToken);

        // Retry/re-entry: the attempt ledger decides — the unique gateway payment
        // index guarantees at most one attempt (and therefore one batch) per payment.
        var attempt = await db.FamilyCheckoutPaymentAttempts.AsNoTracking()
            .Join(db.FamilyCheckoutBatches.AsNoTracking(),
                a => a.BatchId,
                b => b.Id,
                (a, b) => new { Attempt = a, Batch = b })
            .Where(x => x.Attempt.GatewayPaymentId == gatewayPaymentId
                && x.Batch.FamilyGroupId == familyGroupId)
            .Select(x => new { x.Batch.Id, x.Batch.Version, x.Batch.Status, x.Batch.FinalPrice, x.Batch.CurrencyCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null)
            return null; // Unknown or foreign payment: caller must not charge/activate anything.

        return await BuildResolutionAsync(db, attempt.Id, attempt.Version, attempt.Status, attempt.FinalPrice, attempt.CurrencyCode, cancellationToken);
    }

    /// <summary>
    /// Compatible rollout reading (spec §9): a family group with no batch rows at
    /// all is pre-batch legacy data — its callback keeps the legacy group-wide
    /// behaviour until reconciliation creates its batches. Any batch presence
    /// makes the group strict: only resolved batches can be paid.
    /// </summary>
    public static async Task<bool> GroupHasAnyBatchAsync(
        IAppDbContext db,
        Guid familyGroupId,
        CancellationToken cancellationToken)
        => await db.FamilyCheckoutBatches.AsNoTracking()
            .AnyAsync(x => x.FamilyGroupId == familyGroupId, cancellationToken);

    private static async Task<FamilyBatchPaymentResolution> BuildResolutionAsync(
        IAppDbContext db,
        Guid batchId,
        long batchVersion,
        FamilyCheckoutBatchStatus batchStatus,
        decimal finalPrice,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var subscriptionIds = await db.FamilyCheckoutBatchLines.AsNoTracking()
            .Where(x => x.BatchId == batchId && x.CheckoutSelected && x.SubscriptionId != null)
            .OrderBy(x => x.FamilyMemberId)
            .Select(x => x.SubscriptionId!.Value)
            .ToListAsync(cancellationToken);

        return new FamilyBatchPaymentResolution(batchId, batchVersion, batchStatus, finalPrice, currencyCode, subscriptionIds);
    }
}
