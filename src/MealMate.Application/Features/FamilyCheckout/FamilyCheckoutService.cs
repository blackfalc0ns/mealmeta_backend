using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyStartCheckoutRequest(
    string CallbackUrl,
    string ErrorUrl,
    string? CorrelationId = null,
    int PaymentMethodId = 1);

public sealed record FamilyCheckoutStartedDto(
    Guid FamilyGroupId,
    Guid PaymentAnchorSubscriptionId,
    string PaymentUrl,
    DateTime HoldExpiresAtUtc,
    decimal FinalPrice);

public interface IFamilyCheckoutService
{
    Task<Result<FamilyCheckoutStartedDto>> StartAsync(FamilyStartCheckoutRequest request, CancellationToken cancellationToken = default);
}

public sealed class FamilyCheckoutService : IFamilyCheckoutService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFamilyQuoteService _quote;
    private readonly IFamilyPricingSettingsReader _settings;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly IRestaurantPoolSnapshotService _restaurantPool;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly ISender _sender;
    private readonly IDateTimeProvider _clock;

    public FamilyCheckoutService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFamilyQuoteService quote,
        IFamilyPricingSettingsReader settings,
        ISubscriptionScheduleService schedule,
        IRestaurantEligibilityService eligibility,
        ICapacityConcurrencyLock capacityLock,
        ICheckoutTransactionExecutor transactions,
        IRestaurantPoolSnapshotService restaurantPool,
        IMyFatoorahPaymentService payments,
        ISender sender,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _quote = quote;
        _settings = settings;
        _schedule = schedule;
        _eligibility = eligibility;
        _capacityLock = capacityLock;
        _transactions = transactions;
        _restaurantPool = restaurantPool;
        _payments = payments;
        _sender = sender;
        _clock = clock;
    }

    public async Task<Result<FamilyCheckoutStartedDto>> StartAsync(
        FamilyStartCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid managerId)
            return Error.Forbidden("auth.forbidden", "A customer account is required.");

        var expiredCount = await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId);

        var group = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId
                    && (x.Status == FamilyGroupStatus.Draft
                        || x.Status == FamilyGroupStatus.CheckoutPending
                        || x.Status == FamilyGroupStatus.Active),
                cancellationToken);

        if (group is null)
        {
            var cutoff = _clock.UtcNow.AddMinutes(-FamilyDraftExpiry.TtlMinutes);
            var recentlyExpired = expiredCount > 0
                || await _db.FamilyGroups.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(
                        g => g.ManagerCustomerUserId == managerId
                             && g.IsDeleted
                             && g.DeletedAtUtc != null
                             && g.DeletedAtUtc >= cutoff,
                        cancellationToken);
            return recentlyExpired
                ? Error.Conflict(MessageCodes.Family.SessionExpired, FamilyDraftExpiry.SessionEndedMessageAr)
                : Error.NotFound("family.not_found", "No family group was found.");
        }

        var pendingPayInFlight = await _db.Subscriptions.AsNoTracking()
            .AnyAsync(x => x.FamilyGroupId == group.Id && x.Status == SubscriptionStatus.PendingPayment, cancellationToken);
        if (group.Status == FamilyGroupStatus.CheckoutPending || pendingPayInFlight)
        {
            var resumed = await TryResumePendingCheckoutAsync(group, request, cancellationToken);
            if (resumed is not null)
                return resumed;
            group = await _db.FamilyGroups
                .Include(x => x.Members)
                .FirstAsync(x => x.Id == group.Id, cancellationToken);
        }

        await UnlinkPaymentFailedLeftoversAsync(group.Id, cancellationToken);

        var members = group.Members
            .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.CreatedAtUtc)
            .ToList();

        var settings = await _settings.GetAsync(cancellationToken);
        var quoteResult = await _quote.QuoteAsync(group, cancellationToken);
        if (quoteResult.IsFailure) return quoteResult.Error;
        var quote = quoteResult.Value;
        var packageResult = FamilyPackageResolver.ResolveForGroup(settings, group.SelectedFamilyPackageId);
        if (packageResult.IsFailure)
            return packageResult.Error;
        var payableMembers = FamilyCheckoutGate.PayableMembers(members);
        var allergiesCompleted = await FamilyCheckoutAllergyLookup.CompletedMemberIdsAsync(
            _db, group, payableMembers, cancellationToken);
        var (canCheckout, blockReason) = FamilyCheckoutGate.Evaluate(
            group, members, packageResult.Value, quote, allergiesCompleted);
        if (!canCheckout)
            return Error.Conflict(blockReason ?? "family.checkout_blocked", "Family checkout is not available.");

        var user = await _users.FindByIdAsync(managerId, cancellationToken);
        if (user is null)
            return Error.Validation("checkout.customer_not_found", "Customer account was not found.");

        var priceByMember = quote.Lines.ToDictionary(x => x.MemberId, x => x.MemberPrice);
        var memberIds = members.Select(m => m.Id).ToList();
        var accesses = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(a => memberIds.Contains(a.FamilyMemberId) && a.RemovedAtUtc == null)
            .ToListAsync(cancellationToken);
        var previousStatus = group.Status;
        var prepared = new List<PreparedMemberReservation>();
        foreach (var member in members)
        {
            if (group.ConvertedFromSubscriptionId is not null && member.Role == FamilyMemberRole.Manager)
                continue;
            if (member.MemberSubscriptionId is not null)
                continue;
            if (!FamilyMemberPlanRules.HasPricablePlan(member))
                continue;

            var customerUserId = ResolveMemberCustomerUserId(member, managerId, accesses);
            if (customerUserId is null)
                return Error.Conflict("family.member_account_required", "Family member must be linked before checkout.");

            var address = await ResolveMemberCheckoutAddressAsync(member, customerUserId.Value, managerId, cancellationToken);
            if (address is null)
                return Error.Validation("family.address_required", "Each payable family member needs a delivery address.");

            var startDate = member.StartDate ?? DateOnly.FromDateTime(_clock.UtcNow.Date);
            var regionId = address.RegionId ?? Guid.Empty;
            var serviceDates = _schedule.GetServiceDates(startDate, member.DurationDays!.Value, member.IncludeFriday);
            var allRows = await _eligibility.EvaluateAsync(
                new RestaurantEligibilityRequest(
                    member.ProgramId!.Value,
                    member.BundleId!.Value,
                    member.SelectedTier!.Value,
                    regionId,
                    startDate,
                    member.DurationDays.Value,
                    member.IncludeFriday,
                    customerUserId.Value),
                cancellationToken);
            var eligible = allRows.Where(x => x.IsEligible).OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId).ToList();
            var restaurant = eligible.FirstOrDefault();
            if (restaurant is null)
                return Error.Validation("restaurant.not_eligible", "No eligible restaurant is available for a family member.");

            var resolvedRegionId = regionId;
            if (resolvedRegionId == Guid.Empty)
            {
                resolvedRegionId = await _db.RestaurantServiceRegions.AsNoTracking()
                    .Where(x => x.RestaurantId == restaurant.RestaurantId && x.IsActive)
                    .Select(x => x.RegionId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (resolvedRegionId == Guid.Empty)
                    return Error.Validation("restaurant.not_eligible", "No eligible restaurant is available for a family member.");
            }

            prepared.Add(new PreparedMemberReservation(
                member,
                customerUserId.Value,
                address,
                resolvedRegionId,
                eligible,
                restaurant,
                startDate,
                serviceDates));
        }

        FamilyReservationOutcome reservation;
        try
        {
            reservation = await _transactions.ExecuteAsync(async ct =>
            {
                var expiresAt = _clock.UtcNow.AddMinutes(15);
                var created = new List<(FamilyMember Member, Subscription Subscription)>();

                foreach (var member in members)
                {
                    // Converted manager keeps the existing subscription — no re-create / no new holds.
                    if (group.ConvertedFromSubscriptionId is Guid convertedSubId
                        && member.Role == FamilyMemberRole.Manager)
                    {
                        var existing = await _db.Subscriptions.FirstOrDefaultAsync(
                            x => x.Id == convertedSubId && x.CustomerUserId == managerId, ct)
                            ?? throw new FamilyCheckoutReservationException("conversion.no_individual_subscription");
                        existing.FamilyGroupId = group.Id;
                        existing.FamilyMemberId = member.Id;
                        member.MemberSubscriptionId = existing.Id;
                        await _db.SaveChangesAsync(ct);
                        created.Add((member, existing));
                    }
                }

                var poolCaptures = new List<PendingPoolCapture>();
                foreach (var plan in prepared)
                {
                    var member = members.First(x => x.Id == plan.Member.Id);
                    await _capacityLock.AcquireAsync(plan.Restaurant.RestaurantId, plan.ServiceDates, ct);
                    var create = await _sender.Send(new CreateSubscriptionCommand(
                        plan.CustomerUserId,
                        member.ProgramId!.Value,
                        member.BundleId!.Value,
                        member.SelectedTier!.Value,
                        member.DurationDays!.Value,
                        plan.StartDate,
                        plan.Address.AddressLine,
                        RegionId: plan.ResolvedRegionId,
                        CorrelationId: request.CorrelationId,
                        IncludeFriday: member.IncludeFriday), ct);
                    if (create.IsFailure)
                        throw new FamilyCheckoutReservationException(create.Error.Code);

                    var subscriptionEntity = await _db.Subscriptions.FirstAsync(x => x.Id == create.Value.Id, ct);
                    if (!priceByMember.TryGetValue(member.Id, out var memberPrice))
                        throw new FamilyCheckoutReservationException("family.invalid_amount");

                    subscriptionEntity.PaidAmount = new Money(memberPrice, quote.CurrencyCode);
                    subscriptionEntity.FamilyGroupId = group.Id;
                    subscriptionEntity.FamilyMemberId = member.Id;

                    foreach (var resolved in plan.Restaurant.MenuPlanVersionsByDate)
                    {
                        _db.CapacityHolds.Add(new CapacityHold
                        {
                            RestaurantId = plan.Restaurant.RestaurantId,
                            SubscriptionId = subscriptionEntity.Id,
                            MenuPlanVersionId = resolved.MenuPlanVersionId,
                            ServiceDate = resolved.ServiceDate,
                            Quantity = 1,
                            Status = CapacityHoldStatus.PendingPayment,
                            ExpiresAtUtc = expiresAt,
                        });
                    }

                    await _db.SaveChangesAsync(ct);
                    poolCaptures.Add(new PendingPoolCapture(
                        subscriptionEntity.Id, plan.Eligible, plan.Restaurant.RestaurantId));
                    created.Add((member, subscriptionEntity));
                }

                var payableCreated = created
                    .Where(x => x.Subscription.Status == SubscriptionStatus.PendingPayment)
                    .ToList();
                if (payableCreated.Count == 0)
                    throw new FamilyCheckoutReservationException("family.member_incomplete");

                var managerCreated = created.FirstOrDefault(x => x.Member.Role == FamilyMemberRole.Manager);
                var paymentAnchorId = managerCreated.Member is not null
                    ? managerCreated.Subscription.Id
                    : payableCreated[0].Subscription.Id;

                group.DiscountPercentSnapshot = quote.FamilyDiscountPercent;
                group.QuotedFinalPrice = quote.FinalPrice;
                group.CurrencyCode = quote.CurrencyCode;
                if (previousStatus != FamilyGroupStatus.Active)
                    group.Status = FamilyGroupStatus.CheckoutPending;
                group.PaymentAnchorSubscriptionId = paymentAnchorId;
                group.PaymentId = null;
                group.PaymentInvoiceId = null;
                if (previousStatus != FamilyGroupStatus.Active)
                    group.PaidAtUtc = null;
                await _db.SaveChangesAsync(ct);

                return new FamilyReservationOutcome(
                    group.Id,
                    paymentAnchorId,
                    payableCreated.Select(x => x.Subscription.Id).ToList(),
                    expiresAt,
                    quote.FinalPrice,
                    poolCaptures);
            }, cancellationToken);
        }
        catch (FamilyCheckoutReservationException ex)
        {
            return Error.Validation(ex.Code, "Family checkout eligibility or restaurant snapshot changed. Refresh and try again.");
        }

        try
        {
            foreach (var capture in reservation.PoolCaptures)
            {
                var pool = await _restaurantPool.CaptureAsync(
                    capture.SubscriptionId, capture.Eligible, capture.SelectedRestaurantId, cancellationToken);
                if (!pool.IsSuccess)
                    throw new FamilyCheckoutReservationException(pool.ErrorCode ?? "checkout.restaurant_pool_failed");
            }
        }
        catch (FamilyCheckoutReservationException ex)
        {
            await ReleaseReservationsAsync(reservation.SubscriptionIds, cancellationToken);
            await RevertGroupAfterFailedPaymentAsync(reservation.FamilyGroupId, previousStatus, cancellationToken);
            return Error.Validation(ex.Code, "Family checkout eligibility or restaurant snapshot changed. Refresh and try again.");
        }

        string paymentUrl;
        try
        {
            paymentUrl = await _payments.ExecutePaymentAsync(
                reservation.FinalPrice,
                user.FullName ?? "MealMate Customer",
                user.Email ?? "",
                user.PhoneNumber ?? "",
                request.CallbackUrl,
                request.ErrorUrl,
                reservation.FamilyGroupId.ToString(),
                request.PaymentMethodId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await ReleaseReservationsAsync(reservation.SubscriptionIds, cancellationToken);
            await RevertGroupAfterFailedPaymentAsync(reservation.FamilyGroupId, previousStatus, cancellationToken);
            return Error.Failure("payment.initialization_failed", ex.Message);
        }

        var gatewayPaymentId = PaymentIdFromUrl(paymentUrl);
        if (!string.IsNullOrWhiteSpace(gatewayPaymentId))
        {
            var pending = await _db.Subscriptions
                .Where(x => reservation.SubscriptionIds.Contains(x.Id)
                            && x.Status == SubscriptionStatus.PendingPayment)
                .ToListAsync(cancellationToken);
            foreach (var sub in pending)
                sub.PaymentId = gatewayPaymentId;

            var paidGroup = await _db.FamilyGroups.FirstAsync(x => x.Id == reservation.FamilyGroupId, cancellationToken);
            paidGroup.PaymentId = gatewayPaymentId;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new FamilyCheckoutStartedDto(
            reservation.FamilyGroupId,
            reservation.PaymentAnchorSubscriptionId,
            paymentUrl,
            reservation.ExpiresAtUtc,
            reservation.FinalPrice);
    }

    private async Task<Result<FamilyCheckoutStartedDto>?> TryResumePendingCheckoutAsync(
        FamilyGroup group,
        FamilyStartCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var pending = await _db.Subscriptions
            .Where(x => x.FamilyGroupId == group.Id && x.Status == SubscriptionStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return Error.Conflict("family.not_draft", "Family checkout is already in progress.");

        var pendingIds = pending.Select(x => x.Id).ToList();
        var holds = await _db.CapacityHolds
            .Where(x => pendingIds.Contains(x.SubscriptionId) && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        var expiresAt = holds.Count > 0
            ? holds.Max(x => x.ExpiresAtUtc)
            : _clock.UtcNow.AddMinutes(15);
        if (holds.Count > 0 && holds.All(x => x.ExpiresAtUtc <= _clock.UtcNow))
        {
            await ReleaseReservationsAsync(pendingIds, cancellationToken);
            var expiredGroup = await _db.FamilyGroups.FirstAsync(x => x.Id == group.Id, cancellationToken);
            if (expiredGroup.Status == FamilyGroupStatus.CheckoutPending)
                expiredGroup.Status = FamilyGroupStatus.Draft;
            expiredGroup.PaymentAnchorSubscriptionId = null;
            expiredGroup.PaymentId = null;
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (_currentUser.UserId is not Guid managerId)
            return Error.Forbidden("auth.forbidden", "A customer account is required.");
        var user = await _users.FindByIdAsync(managerId, cancellationToken);
        if (user is null)
            return Error.Validation("checkout.customer_not_found", "Customer account was not found.");

        var trackedGroup = await _db.FamilyGroups.FirstAsync(x => x.Id == group.Id, cancellationToken);
        var paymentAnchorId = trackedGroup.PaymentAnchorSubscriptionId
            ?? pending[0].Id;
        string paymentUrl;
        try
        {
            paymentUrl = await _payments.ExecutePaymentAsync(
                trackedGroup.QuotedFinalPrice > 0 ? trackedGroup.QuotedFinalPrice : pending.Sum(x => x.PaidAmount.Amount),
                user.FullName ?? "MealMate Customer",
                user.Email ?? "",
                user.PhoneNumber ?? "",
                request.CallbackUrl,
                request.ErrorUrl,
                trackedGroup.Id.ToString(),
                request.PaymentMethodId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Error.Failure("payment.initialization_failed", ex.Message);
        }

        await AttachGatewayPaymentIdAsync(pendingIds, trackedGroup, paymentUrl, cancellationToken);
        return new FamilyCheckoutStartedDto(
            trackedGroup.Id,
            paymentAnchorId,
            paymentUrl,
            expiresAt,
            trackedGroup.QuotedFinalPrice);
    }

    private async Task AttachGatewayPaymentIdAsync(
        IReadOnlyList<Guid> subscriptionIds,
        FamilyGroup group,
        string paymentUrl,
        CancellationToken cancellationToken)
    {
        var gatewayPaymentId = PaymentIdFromUrl(paymentUrl);
        if (string.IsNullOrWhiteSpace(gatewayPaymentId))
            return;

        var pending = await _db.Subscriptions
            .Where(x => subscriptionIds.Contains(x.Id) && x.Status == SubscriptionStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        foreach (var sub in pending)
            sub.PaymentId = gatewayPaymentId;
        group.PaymentId = gatewayPaymentId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task UnlinkPaymentFailedLeftoversAsync(Guid familyGroupId, CancellationToken cancellationToken)
    {
        var leftovers = await _db.Subscriptions
            .Where(x => x.FamilyGroupId == familyGroupId && x.Status == SubscriptionStatus.PaymentFailed)
            .ToListAsync(cancellationToken);
        if (leftovers.Count == 0)
            return;

        var leftoverIds = leftovers.Select(x => x.Id).ToHashSet();
        foreach (var leftover in leftovers)
        {
            leftover.FamilyGroupId = null;
            leftover.FamilyMemberId = null;
        }

        var members = await _db.FamilyMembers
            .Where(x => x.FamilyGroupId == familyGroupId && x.MemberSubscriptionId != null)
            .ToListAsync(cancellationToken);
        foreach (var member in members)
        {
            if (member.MemberSubscriptionId is Guid subId && leftoverIds.Contains(subId))
                member.MemberSubscriptionId = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseReservationsAsync(IReadOnlyList<Guid> subscriptionIds, CancellationToken cancellationToken)
    {
        var subscriptions = await _db.Subscriptions
            .Where(x => subscriptionIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            subscription.Status = SubscriptionStatus.PaymentFailed;
            subscription.LastOperationAtUtc = _clock.UtcNow;
        }

        var holds = await _db.CapacityHolds
            .Where(x => subscriptionIds.Contains(x.SubscriptionId) && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Released;
            hold.ReleasedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static Guid? ResolveMemberCustomerUserId(
        FamilyMember member,
        Guid managerId,
        IReadOnlyList<FamilyMemberAccess> accesses)
    {
        var access = accesses.FirstOrDefault(a => a.FamilyMemberId == member.Id);
        if (access?.LinkedCustomerUserId is Guid linked)
            return linked;
        return member.Role == FamilyMemberRole.Manager ? managerId : null;
    }

    private async Task<CustomerAddress?> ResolveMemberCheckoutAddressAsync(
        FamilyMember member,
        Guid customerUserId,
        Guid managerId,
        CancellationToken ct)
    {
        if (member.DeliveryAddressId is Guid memberAddressId)
        {
            var assigned = await _db.CustomerAddresses.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == memberAddressId && (x.UserId == customerUserId || x.UserId == managerId),
                    ct);
            if (assigned is not null)
                return assigned;
        }

        var owned = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (owned is not null)
            return owned;

        return member.Role == FamilyMemberRole.Manager
            ? await EnsureManagerCheckoutAddressAsync(managerId, ct)
            : null;
    }

    /// <summary>
    /// Prefers an existing manager address; otherwise creates a provisional default so checkout
    /// can proceed when the mobile profile has no saved addresses yet.
    /// </summary>
    private async Task<CustomerAddress> EnsureManagerCheckoutAddressAsync(Guid managerId, CancellationToken ct)
    {
        var existing = await _db.CustomerAddresses
            .Where(x => x.UserId == managerId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        var provisional = new CustomerAddress
        {
            UserId = managerId,
            Label = "عنوان مؤقت",
            AddressLine = "Kuwait",
            IsDefault = true,
            NotifyOnDelivery = true,
            Notes = "Provisional address created during family checkout",
        };
        _db.CustomerAddresses.Add(provisional);
        await _db.SaveChangesAsync(ct);
        return provisional;
    }

    private static string? PaymentIdFromUrl(string paymentUrl)
    {
        if (!Uri.TryCreate(paymentUrl, UriKind.Absolute, out var uri)) return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            if (part[..separator].Equals("paymentId", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(part[(separator + 1)..]);
        }

        return null;
    }

    private async Task RevertGroupAfterFailedPaymentAsync(
        Guid familyGroupId,
        FamilyGroupStatus previousStatus,
        CancellationToken cancellationToken)
    {
        var failedGroup = await _db.FamilyGroups.FirstAsync(x => x.Id == familyGroupId, cancellationToken);
        failedGroup.Status = previousStatus == FamilyGroupStatus.Active
            ? FamilyGroupStatus.Active
            : FamilyGroupStatus.Draft;
        failedGroup.PaymentAnchorSubscriptionId = previousStatus == FamilyGroupStatus.Active
            ? failedGroup.PaymentAnchorSubscriptionId
            : null;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed record PreparedMemberReservation(
        FamilyMember Member,
        Guid CustomerUserId,
        CustomerAddress Address,
        Guid ResolvedRegionId,
        IReadOnlyList<RestaurantEligibilityDto> Eligible,
        RestaurantEligibilityDto Restaurant,
        DateOnly StartDate,
        IReadOnlyList<DateOnly> ServiceDates);

    private sealed record PendingPoolCapture(
        Guid SubscriptionId,
        IReadOnlyList<RestaurantEligibilityDto> Eligible,
        Guid SelectedRestaurantId);

    private sealed record FamilyReservationOutcome(
        Guid FamilyGroupId,
        Guid PaymentAnchorSubscriptionId,
        IReadOnlyList<Guid> SubscriptionIds,
        DateTime ExpiresAtUtc,
        decimal FinalPrice,
        IReadOnlyList<PendingPoolCapture> PoolCaptures);

    private sealed class FamilyCheckoutReservationException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
