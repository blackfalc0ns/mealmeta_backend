using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record GetFamilySubscriptionConfirmationQuery(Guid CallerUserId, Guid? BatchId = null)
    : IQuery<FamilySubscriptionConfirmationDto>;

public sealed record FamilySubscriptionConfirmationDto(
    Guid FamilyGroupId,
    string FamilyName,
    decimal TotalAmountPaid,
    string CurrencyCode,
    DateTime PaymentVerifiedAtUtc,
    IReadOnlyList<FamilySubscriptionConfirmationMemberDto> Members);

public sealed record FamilySubscriptionConfirmationMemberDto(
    Guid MemberId,
    Guid SubscriptionId,
    string DisplayName,
    string Role,
    bool IsManager,
    string ProgramNameAr,
    string BundleNameAr,
    string TierNameAr,
    int DurationDays,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>Reads one verified family invoice and its purchased subscriptions without changing checkout state.</summary>
public sealed class FamilySubscriptionConfirmationHandler(IAppDbContext db)
    : IQueryHandler<GetFamilySubscriptionConfirmationQuery, FamilySubscriptionConfirmationDto>
{
    public async Task<Result<FamilySubscriptionConfirmationDto>> Handle(
        GetFamilySubscriptionConfirmationQuery request, CancellationToken cancellationToken)
    {
        var batches = from checkout in db.FamilyCheckoutBatches.AsNoTracking()
                      join family in db.FamilyGroups.AsNoTracking() on checkout.FamilyGroupId equals family.Id
                      where family.ManagerCustomerUserId == request.CallerUserId
                      select new { Batch = checkout, FamilyName = family.Name };

        // Never skip an unpaid latest purchase and accidentally confirm a previous invoice.
        batches = request.BatchId is Guid batchId
            ? batches.Where(x => x.Batch.Id == batchId)
            : batches.Where(x => x.Batch.Purpose == "Initial");
        var purchase = await batches.OrderByDescending(x => x.Batch.CreatedAtUtc)
            .ThenByDescending(x => x.Batch.Id).FirstOrDefaultAsync(cancellationToken);
        if (purchase is null)
            return Error.NotFound("family.confirmation_not_found", "Family checkout confirmation was not found.");

        var batch = purchase.Batch;
        if (batch.Status != FamilyCheckoutBatchStatus.Paid || batch.PaidAtUtc is null)
            return Error.Conflict("family.confirmation_not_ready", "The family payment has not been verified yet.");

        var lines = await db.FamilyCheckoutBatchLines.AsNoTracking()
            .Where(x => x.BatchId == batch.Id && x.CheckoutSelected).ToListAsync(cancellationToken);
        if (lines.Count == 0 || lines.Any(x => x.SubscriptionId is null))
            return InvalidTargets();

        var memberIds = lines.Select(x => x.FamilyMemberId).ToArray();
        var subscriptionIds = lines.Select(x => x.SubscriptionId!.Value).ToArray();
        var members = await db.FamilyMembers.AsNoTracking()
            .Where(x => memberIds.Contains(x.Id) && x.FamilyGroupId == batch.FamilyGroupId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var subscriptions = await db.Subscriptions.AsNoTracking().Include(x => x.Program).Include(x => x.Bundle)
            .Where(x => subscriptionIds.Contains(x.Id) && x.FamilyGroupId == batch.FamilyGroupId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (members.Count != lines.Count || subscriptions.Count != lines.Count
            || lines.Any(x => !members.ContainsKey(x.FamilyMemberId)
                || !subscriptions.TryGetValue(x.SubscriptionId!.Value, out var subscription)
                || subscription.FamilyMemberId != x.FamilyMemberId
                || subscription.PaymentVerifiedAtUtc is null
                || subscription.Program is null || subscription.Bundle is null))
            return InvalidTargets();

        var pricing = await db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var labels = SubscriptionPricingSettingsReader.Deserialize(pricing?.ValueJson).TierLabels;
        var confirmedMembers = lines.Select(line =>
        {
            var member = members[line.FamilyMemberId];
            var subscription = subscriptions[line.SubscriptionId!.Value];
            // An upgrade can change the live subscription after this invoice was paid.
            var tierName = (line.Tier ?? subscription.SelectedTier) switch
            {
                RestaurantTier.Platinum => labels.PlatinumAr,
                RestaurantTier.Elite => labels.EliteAr,
                _ => labels.BasicAr
            };
            return new FamilySubscriptionConfirmationMemberDto(
                member.Id, subscription.Id, member.DisplayName, member.Role.ToString(),
                member.Role == FamilyMemberRole.Manager, subscription.Program.NameAr, subscription.Bundle.NameAr,
                tierName, subscription.DurationDays, subscription.StartDate, subscription.EndDate);
        }).OrderByDescending(x => x.IsManager).ThenBy(x => x.DisplayName).ThenBy(x => x.MemberId).ToList();

        return Result.Success(new FamilySubscriptionConfirmationDto(
            batch.FamilyGroupId, purchase.FamilyName, batch.FinalPrice, batch.CurrencyCode,
            batch.PaidAtUtc.Value, confirmedMembers));
    }

    private static Error InvalidTargets() => Error.Conflict(
        "family.batch_targets_invalid", "The members reserved for this family payment need review.");
}
