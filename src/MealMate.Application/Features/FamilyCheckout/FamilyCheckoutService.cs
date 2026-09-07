using MealMate.Application.Common.Abstractions;
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

        var group = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId
                    && (x.Status == FamilyGroupStatus.Draft
                        || x.Status == FamilyGroupStatus.CheckoutPending
                        || x.Status == FamilyGroupStatus.Active),
                cancellationToken);

        if (group is null)
            return Error.NotFound("family.not_found", "No family group was found.");

        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Family checkout is already in progress.");

        var pendingPayInFlight = await _db.Subscriptions.AsNoTracking()
            .AnyAsync(x => x.FamilyGroupId == group.Id && x.Status == SubscriptionStatus.PendingPayment, cancellationToken);
        if (pendingPayInFlight)
            return Error.Conflict("family.not_draft", "Family checkout is already in progress.");

        await UnlinkPaymentFailedLeftoversAsync(group.Id, cancellationToken);

        var members = group.Members
            .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.CreatedAtUtc)
            .ToList();

        var settings = await _settings.GetAsync(cancellationToken);
        var quote = await _quote.QuoteAsync(group, cancellationToken);
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
                        continue;
                    }

                    if (member.MemberSubscriptionId is not null)
                        continue;

                    if (!FamilyMemberPlanRules.HasPricablePlan(member))
                        continue;

                    var customerUserId = ResolveMemberCustomerUserId(member, managerId, accesses)
                        ?? throw new FamilyCheckoutReservationException("family.member_account_required");

                    var address = await ResolveMemberCheckoutAddressAsync(member, customerUserId, managerId, ct)
                        ?? throw new FamilyCheckoutReservationException("family.address_required");

                    var startDate = member.StartDate ?? DateOnly.FromDateTime(_clock.UtcNow.Date);
                    var regionId = address.RegionId ?? Guid.Empty;
                    var serviceDates = _schedule.GetServiceDates(startDate, member.DurationDays!.Value, member.IncludeFriday);
                    var eligibilityRequest = new RestaurantEligibilityRequest(
                        member.ProgramId!.Value,
                        member.BundleId!.Value,
                        member.SelectedTier!.Value,
                        regionId,
                        startDate,
                        member.DurationDays.Value,
                        member.IncludeFriday,
                        customerUserId);
                    var allRows = await _eligibility.EvaluateAsync(eligibilityRequest, ct);
                    var eligible = allRows.Where(x => x.IsEligible).OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId).ToList();
                    var restaurant = eligible.FirstOrDefault()
                        ?? throw new FamilyCheckoutReservationException("restaurant.not_eligible");

                    var resolvedRegionId = regionId;
                    if (resolvedRegionId == Guid.Empty)
                    {
                        resolvedRegionId = await _db.RestaurantServiceRegions.AsNoTracking()
                            .Where(x => x.RestaurantId == restaurant.RestaurantId && x.IsActive)
                            .Select(x => x.RegionId)
                            .FirstOrDefaultAsync(ct);
                        if (resolvedRegionId == Guid.Empty)
                            throw new FamilyCheckoutReservationException("restaurant.not_eligible");
                    }

                    await _capacityLock.AcquireAsync(restaurant.RestaurantId, serviceDates, ct);
                    var create = await _sender.Send(new CreateSubscriptionCommand(
                        customerUserId,
                        member.ProgramId.Value,
                        member.BundleId.Value,
                        member.SelectedTier.Value,
                        member.DurationDays.Value,
                        startDate,
                        address.AddressLine,
                        RegionId: resolvedRegionId,
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

                    foreach (var resolved in restaurant.MenuPlanVersionsByDate)
                    {
                        _db.CapacityHolds.Add(new CapacityHold
                        {
                            RestaurantId = restaurant.RestaurantId,
                            SubscriptionId = subscriptionEntity.Id,
                            MenuPlanVersionId = resolved.MenuPlanVersionId,
                            ServiceDate = resolved.ServiceDate,
                            Quantity = 1,
                            Status = CapacityHoldStatus.PendingPayment,
                            ExpiresAtUtc = expiresAt,
                        });
                    }

                    await _db.SaveChangesAsync(ct);
                    var pool = await _restaurantPool.CaptureAsync(subscriptionEntity.Id, eligible, restaurant.RestaurantId, ct);
                    if (!pool.IsSuccess)
                        throw new FamilyCheckoutReservationException(pool.ErrorCode ?? "checkout.restaurant_pool_failed");

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
                    quote.FinalPrice);
            }, cancellationToken);
        }
        catch (FamilyCheckoutReservationException ex)
        {
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
            var failedGroup = await _db.FamilyGroups.FirstAsync(x => x.Id == reservation.FamilyGroupId, cancellationToken);
            failedGroup.Status = previousStatus == FamilyGroupStatus.Active
                ? FamilyGroupStatus.Active
                : FamilyGroupStatus.Draft;
            failedGroup.PaymentAnchorSubscriptionId = previousStatus == FamilyGroupStatus.Active
                ? failedGroup.PaymentAnchorSubscriptionId
                : null;
            await _db.SaveChangesAsync(cancellationToken);
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

    private sealed record FamilyReservationOutcome(
        Guid FamilyGroupId,
        Guid PaymentAnchorSubscriptionId,
        IReadOnlyList<Guid> SubscriptionIds,
        DateTime ExpiresAtUtc,
        decimal FinalPrice);

    private sealed class FamilyCheckoutReservationException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
