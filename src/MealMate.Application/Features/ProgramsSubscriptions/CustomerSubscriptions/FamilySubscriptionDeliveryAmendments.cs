using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;

public sealed record FamilyDeliveryAddressRequest(Guid AddressId, DateOnly EffectiveFromDate, long ExpectedVersion);
public sealed record FamilyDeliveryAddressResult(Guid SubscriptionId, long Version, Guid AmendmentId,
    DateOnly EffectiveFromDate, IReadOnlyList<Guid> AffectedDayIds, IReadOnlyList<Guid> SkippedDayIds);
public sealed record FamilyDeliveryAddressChoice(Guid Id, string? Label, string AddressLine, Guid? RegionId);
public sealed record FamilyDeliveryAddressDetails(Guid SubscriptionId, long CurrentVersion,
    CustomerActiveDeliveryAddress? AcceptedAddress, IReadOnlyList<FamilyDeliveryAddressChoice> EligibleAddresses);
public interface IFamilySubscriptionDeliveryAmendments
{
    Task<Result<FamilyDeliveryAddressDetails>> GetAsync(Guid subscriptionId, Guid actor, CancellationToken ct);
    Task<Result<FamilyDeliveryAddressResult>> AmendAsync(Guid subscriptionId, Guid actor, FamilyDeliveryAddressRequest request, CancellationToken ct);
}

public sealed class FamilySubscriptionDeliveryAmendments(IAppDbContext db, IDateTimeProvider clock,
    IApplicationTransactionExecutor transactions, IApplicationLockManager locks, IFamilySubscriptionAccess access,
    IOperationalPolicyService policies, IRestaurantEligibilityService eligibility, IRestaurantCapacityReader capacity)
    : IFamilySubscriptionDeliveryAmendments
{
    public async Task<Result<FamilyDeliveryAddressDetails>> GetAsync(Guid subscriptionId, Guid actor, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription?.FamilyGroupId is null) return Error.NotFound("common.not_found");
        var authorization = await access.AuthorizeAsync(subscription, actor, FamilyAction.EditAddress, ct);
        if (authorization.IsFailure) return authorization.Error;
        var version = await db.FamilySubscriptionDeliveryAmendments.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId)
            .Select(x => (long?)x.Version).MaxAsync(ct) ?? 0;
        var today = BusinessCalendarDate.KuwaitToday(clock.UtcNow);
        var day = await db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId && x.ServiceDate >= today
                && x.Status != SubscriptionDayStatus.Cancelled && x.Status != SubscriptionDayStatus.Frozen)
            .OrderBy(x => x.ServiceDate).FirstOrDefaultAsync(ct);
        CustomerActiveDeliveryAddress? accepted = null;
        if (day is not null)
        {
            var resolved = await ResolveAsync(db, subscription, day, ct);
            if (resolved.IsFailure) return resolved.Error;
            accepted = resolved.Value;
        }
        var addresses = await db.CustomerAddresses.AsNoTracking().Where(x => (x.UserId == actor || x.UserId == subscription.CustomerUserId)
                && x.RegionId.HasValue && x.AddressLine != null)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Label)
            .Select(x => new FamilyDeliveryAddressChoice(x.Id, x.Label, x.AddressLine!, x.RegionId)).ToListAsync(ct);
        return new FamilyDeliveryAddressDetails(subscriptionId, version, accepted, addresses);
    }

    public static async Task<Result<CustomerActiveDeliveryAddress>> ResolveAsync(IAppDbContext db, Subscription subscription, SubscriptionDay day, CancellationToken ct)
    {
        if (day.SourceFamilyDeliveryAmendmentId is Guid sourceId)
        {
            var source = await db.FamilySubscriptionDeliveryAmendments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sourceId
                && x.SubscriptionId == subscription.Id && x.EffectiveFromDate <= day.ServiceDate, ct);
            if (source is null) return Error.Conflict("family.delivery_snapshot_review_required");
            return ToSnapshot(source);
        }
        var amendments = await db.FamilySubscriptionDeliveryAmendments.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id
            && x.EffectiveFromDate <= day.ServiceDate).OrderByDescending(x => x.Version).ToListAsync(ct);
        FamilySubscriptionDeliveryAmendment? amendment = null;
        foreach (var candidate in amendments)
        {
            List<Guid>? ids;
            try { ids = JsonSerializer.Deserialize<List<Guid>>(candidate.AffectedDayIdsJson); }
            catch (JsonException) { return Error.Conflict("family.delivery_snapshot_review_required"); }
            if (ids is null) return Error.Conflict("family.delivery_snapshot_review_required");
            if (ids.Contains(day.Id)) { amendment = candidate; break; }
        }
        if (amendment is not null)
            return ToSnapshot(amendment);
        var lines = await db.FamilyCheckoutBatchLines.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id
            && x.FamilyMemberId == subscription.FamilyMemberId && x.Batch.FamilyGroupId == subscription.FamilyGroupId
            && x.Batch.Status == FamilyCheckoutBatchStatus.Paid).ToListAsync(ct);
        if (lines.Count != 1 || lines[0].DeliveryAddressId is not Guid addressId || lines[0].RegionId is not Guid regionId
            || string.IsNullOrWhiteSpace(lines[0].AddressLine)) return Error.Conflict("family.delivery_snapshot_review_required");
        var line = lines[0];
        return new CustomerActiveDeliveryAddress(addressId, regionId, line.AddressLabel ?? "", line.AddressLine!,
            line.Governorate, line.Area, line.Block, line.BlockRegionId);
    }

    public async Task<Result<FamilyDeliveryAddressResult>> AmendAsync(Guid subscriptionId, Guid actor, FamilyDeliveryAddressRequest request, CancellationToken ct)
    {
        var route = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (route?.FamilyGroupId is not Guid groupId) return Error.NotFound("common.not_found");
        return await transactions.ExecuteResultAsync<FamilyDeliveryAddressResult>(async token => {
            await locks.LockFamilyGroupAsync(groupId, token);
            await locks.LockSubscriptionAsync(subscriptionId, token);
            await locks.LockSubscriptionDaysAsync(subscriptionId, token);
            var subscription = await db.Subscriptions.FirstAsync(x => x.Id == subscriptionId, token);
            var authorization = await access.AuthorizeAsync(subscription, actor, FamilyAction.EditAddress, token);
            if (authorization.IsFailure) return authorization.Error;
            if (subscription.Status is not (SubscriptionStatus.Paid or SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen))
                return Error.Conflict("family.delivery_amendment_not_available");
            var version = await db.FamilySubscriptionDeliveryAmendments.Where(x => x.SubscriptionId == subscriptionId)
                .Select(x => (long?)x.Version).MaxAsync(token) ?? 0;
            if (request.ExpectedVersion != version) return Error.ConflictWithMetadata("family.delivery_version_changed",
                new Dictionary<string, object?> { ["currentVersion"] = version });
            var address = await db.CustomerAddresses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AddressId
                && (x.UserId == actor || x.UserId == subscription.CustomerUserId), token);
            if (address?.RegionId is not Guid regionId || string.IsNullOrWhiteSpace(address.AddressLine))
                return Error.Validation("family.address_required");
            if (!await db.Regions.AnyAsync(x => x.Id == regionId && x.IsActive, token)) return Error.Validation("address.invalid_region");
            if (address.BlockRegionId.HasValue && !await db.Regions.AnyAsync(x => x.Id == address.BlockRegionId && x.ParentRegionId == regionId && x.IsActive, token))
                return Error.Validation("address.invalid_region");
            var today = BusinessCalendarDate.KuwaitToday(clock.UtcNow);
            if (request.EffectiveFromDate < today) return Error.Validation("family.delivery_effective_date_invalid");
            var days = await db.SubscriptionDays.Where(x => x.SubscriptionId == subscriptionId && x.ServiceDate >= request.EffectiveFromDate)
                .OrderBy(x => x.ServiceDate).ToListAsync(token);
            var printed = (await db.MealInvoices.AsNoTracking().Where(x => x.SubscriptionDayId.HasValue && days.Select(d => d.Id).Contains(x.SubscriptionDayId.Value))
                .Select(x => x.SubscriptionDayId!.Value).ToListAsync(token)).ToHashSet();
            var settings = await policies.GetSettingsAsync(token);
            var affected = days.Where(x => OperationalDeadlineRules.IsMutable(x) && !printed.Contains(x.Id)
                && clock.UtcNow < (x.SelectionDeadlineUtc ?? policies.CalculateSelectionDeadlineUtc(x.ServiceDate, settings))).ToList();
            var skippedIds = days.Where(x => !affected.Contains(x)).Select(x => x.Id).ToList();
            if (affected.Count == 0) return Error.Conflict("family.delivery_no_mutable_days");
            var paidLines = await db.FamilyCheckoutBatchLines.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId
                    && x.FamilyMemberId == subscription.FamilyMemberId && x.Batch.FamilyGroupId == subscription.FamilyGroupId
                    && x.Batch.Status == FamilyCheckoutBatchStatus.Paid)
                .ToListAsync(token);
            if (paidLines.Count != 1) return Error.Conflict("family.delivery_snapshot_review_required");
            var paidLine = paidLines[0];
            if (string.IsNullOrWhiteSpace(paidLine.MenuPlanVersionsJson))
                return Error.Conflict("family.delivery_snapshot_review_required");
            List<EligibleMenuDateDto>? committedMenuDates;
            try { committedMenuDates = JsonSerializer.Deserialize<List<EligibleMenuDateDto>>(paidLine.MenuPlanVersionsJson); }
            catch (JsonException) { return Error.Conflict("family.delivery_snapshot_review_required"); }
            if (committedMenuDates is null) return Error.Conflict("family.delivery_snapshot_review_required");
            foreach (var day in affected)
            {
                var original = await ResolveAsync(db, subscription, day, token);
                if (original.IsFailure) return original.Error;
                if (!day.RestaurantId.HasValue)
                {
                    var committedHold = await db.CapacityHolds.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId
                            && x.ServiceDate == day.ServiceDate && (x.Status == CapacityHoldStatus.Confirmed
                                || x.Status == CapacityHoldStatus.PaymentConfirmed))
                        .Select(x => (Guid?)x.RestaurantId).FirstOrDefaultAsync(token);
                    var committedPlan = paidLine.ResolvedRestaurantId.HasValue
                        && committedMenuDates.Any(x => x.ServiceDate == day.ServiceDate);
                    if (committedHold is Guid heldRestaurant) day.RestaurantId = heldRestaurant;
                    else if (committedPlan) day.RestaurantId = paidLine.ResolvedRestaurantId;
                    else return Error.Conflict("family.delivery_restaurant_required");
                }
            }
            var keys = affected.Select(x => (x.RestaurantId!.Value, x.ServiceDate)).Distinct().ToList();
            await locks.LockRestaurantCapacityAsync(keys, token);
            var units = await capacity.ReadAsync(keys, token);
            foreach (var day in affected)
            {
                if (!units.TryGetValue((day.RestaurantId!.Value, day.ServiceDate), out var unit) || unit.LimitUnknown || unit.TotalCommittedUnits > unit.LimitBoxes)
                    return Error.Conflict("family.capacity_unavailable");
                var choices = await eligibility.EvaluateAsync(new RestaurantEligibilityRequest(subscription.ProgramId, subscription.BundleId,
                    subscription.SelectedTier, regionId, day.ServiceDate, subscription.DurationDays, subscription.IncludeFriday,
                    subscription.CustomerUserId, day.RestaurantId), token);
                var choice = choices.FirstOrDefault(x => x.RestaurantId == day.RestaurantId);
                // The same live commitment already occupies its one box; the shared reader above checks the actual limit.
                if (choice is null || choice.ReasonCodes.Any(x => x != EligibilityReasonCodes.CapacityUnavailable))
                    return Error.Conflict("address.restaurant_does_not_service_region");
            }
            var amendment = new FamilySubscriptionDeliveryAmendment { SubscriptionId = subscriptionId, Version = version + 1,
                ActorUserId = actor, EffectiveFromDate = request.EffectiveFromDate, AddressId = address.Id,
                AddressLabel = address.Label, AddressLine = address.AddressLine, RegionId = regionId, BlockRegionId = address.BlockRegionId,
                Governorate = address.Governorate, Area = address.Area, Block = address.Block,
                AffectedDayIdsJson = JsonSerializer.Serialize(affected.Select(x => x.Id)), CreatedAtUtc = clock.UtcNow };
            db.FamilySubscriptionDeliveryAmendments.Add(amendment);
            foreach (var day in affected) Apply(day, new CustomerActiveDeliveryAddress(address.Id, regionId, address.Label,
                address.AddressLine, address.Governorate, address.Area, address.Block, address.BlockRegionId), amendment.Id);
            await db.SaveChangesAsync(token);
            return new FamilyDeliveryAddressResult(subscriptionId, amendment.Version, amendment.Id, request.EffectiveFromDate,
                affected.Select(x => x.Id).ToList(), skippedIds);
        }, ct);
    }

    public static void Apply(SubscriptionDay day, CustomerActiveDeliveryAddress snapshot)
    { day.DeliveryAddressId = snapshot.Id; day.DeliveryAddressLine = snapshot.AddressLine; day.DeliveryRegionId = snapshot.RegionId; day.DeliveryBlockRegionId = snapshot.BlockRegionId; }

    public static void Apply(SubscriptionDay day, CustomerActiveDeliveryAddress snapshot, Guid amendmentId)
    { Apply(day, snapshot); day.SourceFamilyDeliveryAmendmentId = amendmentId; }

    private static CustomerActiveDeliveryAddress ToSnapshot(FamilySubscriptionDeliveryAmendment amendment)
        => new(amendment.AddressId, amendment.RegionId, amendment.AddressLabel, amendment.AddressLine,
            amendment.Governorate, amendment.Area, amendment.Block, amendment.BlockRegionId);
}
