using MealMate.Application.Common.Models;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyPaymentStatusDto(
    Guid FamilyGroupId,
    Guid? BatchId,
    long? BatchVersion,
    Guid? PaymentAnchorSubscriptionId,
    string Status,
    string? PaymentStatus,
    decimal? FinalPrice,
    string? CurrencyCode,
    DateTime? HoldExpiresAtUtc,
    bool IsPaid,
    bool IsFailed,
    bool IsExpired,
    bool CanRetry,
    string? RetryPaymentEndpoint,
    FamilyCheckoutStartedDto? PendingCheckout);

public static class FamilyPaymentStatusReader
{
    public static async Task<Result<FamilyPaymentStatusDto>> ReadAsync(
        IAppDbContext db,
        ISender sender,
        CancellationToken ct)
    {
        var current = await sender.Send(new GetCurrentFamilyGroupQuery(), ct);
        if (current.IsFailure) return current.Error;

        var groupId = current.Value!.FamilyGroupId;
        var batch = await db.FamilyCheckoutBatches.AsNoTracking()
            .Where(x => x.FamilyGroupId == groupId && x.Purpose == "Initial")
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        var pending = await FamilyCheckoutResumeReader.GetAsync(db, groupId, DateTime.UtcNow, ct);
        var canRetry = await FamilyCheckoutResumeReader.CanRetryAsync(db, groupId, ct);
        var status = batch?.Status.ToString() ?? FamilyCheckoutBatchStatus.Draft.ToString();
        var paid = batch?.PaidAtUtc is not null || batch?.Status == FamilyCheckoutBatchStatus.Paid;
        var failed = batch?.Status == FamilyCheckoutBatchStatus.Failed;
        var expired = batch?.Status == FamilyCheckoutBatchStatus.Expired;

        return Result.Success(new FamilyPaymentStatusDto(
            groupId, batch?.Id, batch?.Version, batch?.PaymentAnchorSubscriptionId,
            status,
            paid ? "Paid" : failed ? "Failed" : expired ? "Expired" : batch?.Status == FamilyCheckoutBatchStatus.Cancelled ? "Cancelled" : "Pending",
            batch?.FinalPrice, batch?.CurrencyCode, batch?.ExpiresAtUtc,
            paid, failed, expired, canRetry,
            canRetry ? FamilyCheckoutResumeReader.RetryEndpoint : null,
            pending));
    }
}
