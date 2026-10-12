using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MealMate.Application.Features.FamilyCheckout;

namespace MealMate.Application.Features.ProgramsSubscriptions.Upgrade;

public sealed class SubscriptionUpgradeService : ISubscriptionUpgradeService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IFamilySubscriptionAccess _familyAccess;
    private readonly ISubscriptionTierUpgradeQuoteBuilder _upgradeQuotes;
    private readonly IPaymentGatewayService _payments;
    private readonly IUserAccountService? _users;
    private readonly IRestaurantTierClassificationService? _classification;
    private readonly IRestaurantDiversificationCalculator? _diversification;
    private readonly ISubscriptionPaymentProcessor? _paymentProcessor;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly FamilyInvoiceOptions _invoiceOptions;

    public SubscriptionUpgradeService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes,
        IPaymentGatewayService payments,
        IUserAccountService? users = null,
        IRestaurantTierClassificationService? classification = null,
        IRestaurantDiversificationCalculator? diversification = null,
        ISubscriptionPaymentProcessor? paymentProcessor = null,
        IFamilySubscriptionAccess? familyAccess = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null,
        IOptions<FamilyInvoiceOptions>? invoiceOptions = null)
    {
        _db = db;
        _clock = clock;
        _familyAccess = familyAccess ?? new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db));
        _upgradeQuotes = upgradeQuotes;
        _payments = payments;
        _users = users;
        _classification = classification;
        _diversification = diversification;
        _paymentProcessor = paymentProcessor;
        _transactions = transactions;
        _locks = locks;
        _invoiceOptions = invoiceOptions?.Value ?? new FamilyInvoiceOptions();
    }

    public async Task<Result<CustomerUpgradeOptionsDto>> GetUpgradeOptionsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var authorization = await _familyAccess.AuthorizeAsync(subscription, customerUserId, FamilyAction.RequestUpgrade, ct);
        if (authorization.IsFailure) return authorization.Error;

        var pendingUpgrade = await _db.SubscriptionTierUpgrades.AsNoTracking()
            .AnyAsync(x => x.SubscriptionId == subscriptionId && x.Status == VerificationStatus.Submitted, ct);

        var quoteBundle = await _upgradeQuotes.BuildAsync(subscription, ct);
        var (canUpgrade, unavailableReason) = ComputeUpgradeAvailability(
            subscription, pendingUpgrade, quoteBundle.RemainingServiceDays, quoteBundle.PricingUnavailableReason);

        var options = quoteBundle.Options.Select(MapUpgradeOption).ToList();

        return new CustomerUpgradeOptionsDto(
            canUpgrade,
            unavailableReason,
            subscription.SelectedTier,
            quoteBundle.RemainingServiceDays,
            quoteBundle.TotalServiceDays,
            options,
            $"/api/v1/customer/subscriptions/{subscriptionId}/upgrade-requests",
            null);
    }

    public async Task<Result<CustomerUpgradeSummaryDto>> GetUpgradeSummaryAsync(Guid subscriptionId, Guid customerUserId, RestaurantTier targetTier, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var authorization = await _familyAccess.AuthorizeAsync(subscription, customerUserId, FamilyAction.RequestUpgrade, ct);
        if (authorization.IsFailure) return authorization.Error;

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
            return Error.Conflict("subscription.upgrade_not_available", "Upgrade is not available for this subscription status.");

        if (targetTier <= subscription.SelectedTier)
            return Error.Validation("subscription.upgrade_target_invalid", "Target tier must be higher than current tier.");

        var pendingUpgrade = await _db.SubscriptionTierUpgrades.AsNoTracking()
            .AnyAsync(x => x.SubscriptionId == subscriptionId && x.Status == VerificationStatus.Submitted, ct);
        if (pendingUpgrade)
            return Error.Conflict("subscription.upgrade_request_pending", "An upgrade request is already pending for this subscription.");

        var quoteBundle = await _upgradeQuotes.BuildAsync(subscription, ct);
        var (canUpgrade, unavailableReason) = ComputeUpgradeAvailability(
            subscription, pendingUpgrade, quoteBundle.RemainingServiceDays, quoteBundle.PricingUnavailableReason);

        if (!canUpgrade)
            return Error.Conflict(unavailableReason ?? "subscription.upgrade_not_available", "Subscription cannot be upgraded.");

        var targetQuote = quoteBundle.Options.FirstOrDefault(x => x.TargetTier == targetTier && x.Selectable);
        if (targetQuote is null)
            return Error.Conflict("subscription.target_tier_unavailable", "Target tier is not available for upgrade.");

        var pricingRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);
        var (currentTierNameAr, currentTierNameEn, currentTierLogoUrl) = ResolveTierInfo(subscription.SelectedTier, pricing.TierLabels);
        var (targetTierNameAr, targetTierNameEn, targetTierLogoUrl) = ResolveTierInfo(targetTier, pricing.TierLabels);

        var classificationService = _classification ?? new RestaurantTierClassificationService(_db, _clock);
        var classification = await classificationService.ResolveAsync(subscription.ProgramId, subscription.BundleId, eligibleOnly: true, ct);

        var currentRestaurantIds = classification.Assignments
            .Where(a => RestaurantTierAccess.CanSelect(subscription.SelectedTier, a.Tier))
            .Select(a => a.RestaurantId)
            .Distinct()
            .ToList();

        var targetRestaurantIds = classification.Assignments
            .Where(a => RestaurantTierAccess.CanSelect(targetTier, a.Tier))
            .Select(a => a.RestaurantId)
            .Distinct()
            .ToList();

        var newlyAddedRestaurantIds = targetRestaurantIds.Except(currentRestaurantIds).ToList();
        var allRestaurantIds = targetRestaurantIds.Union(currentRestaurantIds).Distinct().ToList();

        var restaurantProfiles = await _db.RestaurantProfiles.AsNoTracking()
            .Where(r => allRestaurantIds.Contains(r.Id))
            .Select(r => new { r.Id, r.TradeNameAr, r.TradeNameEn })
            .ToDictionaryAsync(r => r.Id, ct);

        var logoDocuments = await _db.Documents.AsNoTracking()
            .Where(x => allRestaurantIds.Contains(x.OwnerId)
                && x.OwnerType == DocumentOwnerType.Restaurant
                && x.DocumentType == DocumentType.ProfileImage)
            .Select(x => new { x.OwnerId, x.StoragePath })
            .ToListAsync(ct);
        var logos = logoDocuments.GroupBy(x => x.OwnerId).ToDictionary(x => x.Key, x => CatalogImageStorage.ToContentUrl(x.First().StoragePath));

        var ratingRows = await _db.RestaurantRatings.AsNoTracking()
            .Where(x => allRestaurantIds.Contains(x.RestaurantId))
            .Select(x => new { x.RestaurantId, x.Score })
            .ToListAsync(ct);
        var ratings = ratingRows.GroupBy(x => x.RestaurantId)
            .ToDictionary(x => x.Key, x => Math.Round((decimal)x.Average(r => r.Score), 1));

        var assignmentTiers = classification.Assignments
            .GroupBy(a => a.RestaurantId)
            .ToDictionary(g => g.Key, g => g.First().Tier);

        CustomerUpgradeRestaurantDto MapRestaurant(Guid restId)
        {
            var nameAr = restaurantProfiles.TryGetValue(restId, out var profile) ? profile.TradeNameAr : "مطعم معتمد";
            var nameEn = profile?.TradeNameEn ?? "Approved Restaurant";
            var logoUrl = logos.GetValueOrDefault(restId);
            var rating = ratings.TryGetValue(restId, out var r) ? (decimal?)r : null;
            var tier = assignmentTiers.GetValueOrDefault(restId, subscription.SelectedTier);

            return new CustomerUpgradeRestaurantDto(restId, nameAr, nameEn, logoUrl, rating, tier);
        }

        var currentTierRestaurants = currentRestaurantIds.Select(MapRestaurant).ToList();
        var targetTierRestaurants = targetRestaurantIds.Select(MapRestaurant).ToList();
        var newlyAddedRestaurants = newlyAddedRestaurantIds.Select(MapRestaurant).ToList();

        var businessToday = subscription.FamilyGroupId.HasValue ? BusinessCalendarDate.KuwaitToday(_clock.UtcNow) : _clock.TodayUtc;
        var earliestUpgradeDate = businessToday.AddDays(3);
        var latestLockedServiceDate = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id &&
                        x.ServiceDate >= businessToday &&
                        (x.Status == SubscriptionDayStatus.Locked72h ||
                         x.Status == SubscriptionDayStatus.SentToRestaurant ||
                         x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                         x.Status == SubscriptionDayStatus.Preparing))
            .MaxAsync(x => (DateOnly?)x.ServiceDate, ct);

        if (latestLockedServiceDate.HasValue && latestLockedServiceDate.Value >= earliestUpgradeDate)
        {
            earliestUpgradeDate = latestLockedServiceDate.Value.AddDays(1);
        }

        var firstApplicableDay = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.ServiceDate >= earliestUpgradeDate)
            .OrderBy(x => x.ServiceDate)
            .Select(x => (DateOnly?)x.ServiceDate)
            .FirstOrDefaultAsync(ct);

        var effectiveFromDate = firstApplicableDay ?? earliestUpgradeDate;
        var effectiveNoticeAr = SubscriptionLocalization.EffectiveUpgradeNoticeAr(effectiveFromDate);
        var effectiveNoticeEn = SubscriptionLocalization.EffectiveUpgradeNoticeEn(effectiveFromDate);

        var diversificationCalculator = _diversification ?? new RestaurantDiversificationCalculator();
        var repetition = diversificationCalculator.CalculateLimit(
            subscription.Id,
            targetQuote.RemainingServiceDays,
            Math.Max(targetRestaurantIds.Count, 1));

        var restaurantRepetition = new RestaurantRepetitionSummaryDto(
            repetition.LimitValue,
            targetRestaurantIds.Count,
            targetQuote.RemainingServiceDays);

        var rawPaymentMethods = await ResolvePaymentMethodsAsync(targetQuote.PriceDifference, ct);
        var paymentMethods = rawPaymentMethods.Select(m => m with
        {
            ServiceCharge = 0m,
            TotalAmount = targetQuote.PriceDifference
        }).ToList();

        var currentTierInfo = new CustomerUpgradeTierInfoDto(
            subscription.SelectedTier,
            currentTierNameAr,
            currentTierNameEn,
            currentTierLogoUrl,
            currentTierRestaurants.Count);

        var targetTierInfo = new CustomerUpgradeTierInfoDto(
            targetTier,
            targetTierNameAr,
            targetTierNameEn,
            targetTierLogoUrl,
            targetTierRestaurants.Count);

        return new CustomerUpgradeSummaryDto(
            subscription.Id,
            currentTierInfo,
            targetTierInfo,
            currentTierRestaurants,
            targetTierRestaurants,
            newlyAddedRestaurants,
            effectiveFromDate,
            effectiveNoticeAr,
            effectiveNoticeEn,
            restaurantRepetition,
            targetQuote.CurrentRemainingCredit,
            targetQuote.TargetRemainingPrice,
            targetQuote.PriceDifference,
            targetQuote.CurrencyCode,
            targetQuote.RemainingServiceDays,
            targetQuote.TotalServiceDays,
            paymentMethods,
            $"/api/v1/customer/subscriptions/{subscriptionId}/upgrade-requests");
    }

    public async Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetUpgradePaymentMethodsAsync(Guid subscriptionId, Guid customerUserId, decimal? amount, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var authorization = await _familyAccess.AuthorizeAsync(subscription, customerUserId, FamilyAction.RequestUpgrade, ct);
        if (authorization.IsFailure) return authorization.Error;

        var invoiceAmount = amount.HasValue && amount.Value > 0 ? amount.Value : 1m;
        return Result.Success(await ResolvePaymentMethodsAsync(invoiceAmount, ct));
    }

    public async Task<Result<CustomerUpgradeStartedDto>> RequestUpgradeAsync(Guid subscriptionId, Guid customerUserId, CustomerUpgradeRequest request, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var authorization = await _familyAccess.AuthorizeAsync(subscription, customerUserId, FamilyAction.RequestUpgrade, ct);
        if (authorization.IsFailure) return authorization.Error;

        if (subscription.FamilyGroupId.HasValue)
            return await RequestFamilyUpgradeAsync(subscriptionId, customerUserId, request, ct);


        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
            return Error.Conflict("subscription.upgrade_not_available", "Upgrade is not available for this subscription status.");

        if (request.TargetTier <= subscription.SelectedTier)
            return Error.Validation("subscription.upgrade_target_invalid", "Target tier must be higher than current tier.");

        var pendingUpgrade = await _db.SubscriptionTierUpgrades
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.Status == VerificationStatus.Submitted, ct);
        if (pendingUpgrade is not null)
            return Error.Conflict("subscription.upgrade_request_already_pending", "An upgrade request is already pending.");

        var option = await _upgradeQuotes.BuildForTargetAsync(subscription, request.TargetTier, ct);
        if (option is null)
            return Error.Conflict("subscription.upgrade_not_available", "Upgrade option unavailable.");

        var currency = subscription.PaidAmount.CurrencyCode;
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = subscription.Id,
            PayerCustomerUserId = customerUserId,
            FromTier = subscription.SelectedTier,
            ToTier = request.TargetTier,
            PriceDifference = new Money(option.PriceDifference, currency),
            QuotedRemainingDays = option.RemainingServiceDays,
            QuotedTotalDue = new Money(option.PriceDifference, currency),
            QuoteJson = SubscriptionTierUpgradeQuoteBuilder.SerializeQuoteJson(option),
            Status = VerificationStatus.Submitted,
        };
        var fundedDates = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id
            && x.Status != SubscriptionDayStatus.Delivered && x.Status != SubscriptionDayStatus.Cancelled && x.Status != SubscriptionDayStatus.Frozen)
            .OrderBy(x => x.ServiceDate).Select(x => x.ServiceDate).ToListAsync(ct);
        if (fundedDates.Count == option.RemainingServiceDays)
        {
            upgrade.EffectiveServiceDatesJson = System.Text.Json.JsonSerializer.Serialize(fundedDates);
            upgrade.EffectiveServiceDate = fundedDates.FirstOrDefault();
        }
        else if (subscription.FamilyGroupId.HasValue)
            return Error.Conflict("family.upgrade_entitlement_review_required");
        _db.SubscriptionTierUpgrades.Add(upgrade);
        await _db.SaveChangesAsync(ct);

        if (!option.PriceDifference.Equals(0m) && option.PriceDifference > 0m)
        {
            if (string.IsNullOrWhiteSpace(request.CallbackUrl) || string.IsNullOrWhiteSpace(request.ErrorUrl))
            {
                upgrade.Status = VerificationStatus.Rejected;
                await _db.SaveChangesAsync(ct);
                return Error.Validation("upgrade.redirect_urls_required", "Redirect URLs are required.");
            }

            var user = _users is not null ? await _users.FindByIdAsync(customerUserId, ct) : null;
            PaymentCreationReceipt paymentReceipt;
            try
            {
                paymentReceipt = await _payments.ExecutePaymentAsync(
                    option.PriceDifference,
                    user?.FullName ?? "",
                    user?.Email ?? "",
                    user?.PhoneNumber ?? "",
                    request.CallbackUrl,
                    request.ErrorUrl,
                    subscription.Id.ToString(),
                    request.PaymentMethodId <= 0 ? 3 : request.PaymentMethodId,
                    ct);
            }
            catch (Exception ex)
            {
                upgrade.Status = VerificationStatus.Rejected;
                await _db.SaveChangesAsync(ct);
                return Error.Failure("upgrade.payment_init_failed", ex.Message);
            }

            var paymentUrl = paymentReceipt.PaymentUrl;
            upgrade.PaymentId = paymentReceipt.PaymentHandle;
            await _db.SaveChangesAsync(ct);

            return new CustomerUpgradeStartedDto(
                upgrade.Id,
                subscription.Id,
                upgrade.FromTier,
                upgrade.ToTier,
                option.PriceDifference,
                currency,
                paymentUrl,
                false,
                upgrade.Status,
                option.RemainingServiceDays);
        }

        // Zero-due upgrade
        subscription.SelectedTier = request.TargetTier;
        subscription.LastOperationAtUtc = _clock.UtcNow;
        upgrade.Status = VerificationStatus.Approved;
        upgrade.EffectiveAtUtc = _clock.UtcNow;
        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "UPGRADE_PAID",
            PreviousStatus = subscription.Status,
            NewStatus = subscription.Status,
            Reason = $"Tier upgraded from {upgrade.FromTier} to {upgrade.ToTier} with zero due",
            PerformedByUserId = customerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return new CustomerUpgradeStartedDto(
            upgrade.Id,
            subscription.Id,
            upgrade.FromTier,
            upgrade.ToTier,
            0m,
            currency,
            null,
            true,
            upgrade.Status,
            option.RemainingServiceDays);
    }

    private async Task<Result<CustomerUpgradeStartedDto>> RequestFamilyUpgradeAsync(
        Guid subscriptionId, Guid customerUserId, CustomerUpgradeRequest request, CancellationToken ct)
    {
        if (_transactions is null || _locks is null)
            return Error.Conflict("family.upgrade_entitlement_review_required");
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > 100)
            return Error.Validation("upgrade.idempotency_key_invalid");
        if (string.IsNullOrWhiteSpace(request.CallbackUrl) || string.IsNullOrWhiteSpace(request.ErrorUrl))
            return Error.Validation("upgrade.redirect_urls_required", "Redirect URLs are required.");

        var reservation = await _transactions.ExecuteResultAsync<UpgradeReservation>(async transactionCt =>
        {
            var familyGroupId = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == subscriptionId)
                .Select(x => x.FamilyGroupId).SingleOrDefaultAsync(transactionCt);
            if (!familyGroupId.HasValue) return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");
            await _locks.LockFamilyGroupAsync(familyGroupId.Value, transactionCt);
            await _locks.LockSubscriptionAsync(subscriptionId, transactionCt);

            var current = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, transactionCt);
            if (current is null) return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");
            var currentAuthorization = await _familyAccess.AuthorizeAsync(current, customerUserId, FamilyAction.RequestUpgrade, transactionCt);
            if (currentAuthorization.IsFailure) return currentAuthorization.Error;
            var fingerprint = ComputeRequestFingerprint(customerUserId, request);
            var sameKey = await _db.SubscriptionTierUpgrades.FirstOrDefaultAsync(x => x.SubscriptionId == current.Id
                && x.RequestIdempotencyKey == idempotencyKey, transactionCt);
            if (sameKey is not null)
            {
                if (!string.Equals(sameKey.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                    return Error.Conflict("upgrade.idempotency_key_mismatch");
                if (sameKey.Status == VerificationStatus.Submitted && sameKey.PaymentCreationState == "Ready"
                    && !string.IsNullOrWhiteSpace(sameKey.PaymentUrl) && !string.IsNullOrWhiteSpace(sameKey.PaymentId))
                {
                    if (await CanIssueFamilyUpgradeLinkAsync(current, sameKey, customerUserId, transactionCt))
                        return new UpgradeReservation(sameKey.Id, current.Id, sameKey.FromTier, sameKey.ToTier,
                            sameKey.QuotedTotalDue.Amount, sameKey.QuotedTotalDue.CurrencyCode, sameKey.PaymentUrl,
                            false, sameKey.Status, sameKey.QuotedRemainingDays, customerUserId, true);
                    sameKey.PaymentCreationState = "ReviewRequired";
                    await _db.SaveChangesAsync(transactionCt);
                    return new UpgradeReservation(sameKey.Id, current.Id, sameKey.FromTier, sameKey.ToTier,
                        sameKey.QuotedTotalDue.Amount, sameKey.QuotedTotalDue.CurrencyCode, null,
                        false, sameKey.Status, sameKey.QuotedRemainingDays, customerUserId, IsReviewRequired: true);
                }
                if (sameKey.Status == VerificationStatus.Approved)
                    return new UpgradeReservation(sameKey.Id, current.Id, sameKey.FromTier, sameKey.ToTier,
                        sameKey.QuotedTotalDue.Amount, sameKey.QuotedTotalDue.CurrencyCode, sameKey.PaymentUrl,
                        sameKey.QuotedTotalDue.Amount <= 0m, sameKey.Status, sameKey.QuotedRemainingDays, customerUserId, true);
                return Error.Conflict("upgrade.payment_creation_review_required");
            }

            if (current.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
                return Error.Conflict("subscription.upgrade_not_available");
            if (request.TargetTier <= current.SelectedTier)
                return Error.Validation("subscription.upgrade_target_invalid");

            var option = await _upgradeQuotes.BuildForTargetAsync(current, request.TargetTier, transactionCt);
            if (option is null) return Error.Conflict("subscription.upgrade_not_available");
            var fundedDates = await ResolveUpgradeFundedDatesAsync(current, transactionCt);
            // The quote is priced from the builder's remaining-day count. If that
            // count cannot be reconciled with the days receiving the upgraded tier,
            // do not issue an invoice with an unverifiable refund denominator.
            if (fundedDates.Count != option.RemainingServiceDays)
                return Error.Conflict("family.upgrade_entitlement_review_required");

            var pending = await _db.SubscriptionTierUpgrades.FirstOrDefaultAsync(x => x.SubscriptionId == current.Id
                && x.Status == VerificationStatus.Submitted, transactionCt);
            if (pending is not null)
                return Error.Conflict("subscription.upgrade_request_already_pending");

            var currency = current.PaidAmount.CurrencyCode;
            if (option.PriceDifference > 0m)
            {
                var contractError = FamilyInvoiceGate.ValidateNewInvoice(request.ClientContractVersion, _invoiceOptions);
                if (contractError is not null) return contractError;
                var method = await FamilyInvoiceGate.ResolvePaymentMethodAsync(_payments, option.PriceDifference,
                    request.PaymentMethodId <= 0 ? 3 : request.PaymentMethodId, currency, transactionCt);
                if (method.IsFailure) return method.Error;
            }

            var upgrade = new SubscriptionTierUpgrade
            {
                SubscriptionId = current.Id,
                PayerCustomerUserId = customerUserId,
                FromTier = current.SelectedTier,
                ToTier = request.TargetTier,
                PriceDifference = new Money(option.PriceDifference, currency),
                QuotedRemainingDays = option.RemainingServiceDays,
                QuotedTotalDue = new Money(option.PriceDifference, currency),
                QuoteJson = SubscriptionTierUpgradeQuoteBuilder.SerializeQuoteJson(option),
                EffectiveServiceDatesJson = JsonSerializer.Serialize(fundedDates),
                EffectiveServiceDate = fundedDates.FirstOrDefault(),
                RequestIdempotencyKey = idempotencyKey,
                RequestFingerprint = fingerprint,
                PaymentCreationState = "Initializing",
                Status = VerificationStatus.Submitted,
            };
            _db.SubscriptionTierUpgrades.Add(upgrade);
            if (option.PriceDifference <= 0m)
            {
                current.SelectedTier = request.TargetTier;
                current.LastOperationAtUtc = _clock.UtcNow;
                upgrade.Status = VerificationStatus.Approved;
                upgrade.EffectiveAtUtc = _clock.UtcNow;
                upgrade.PaymentCreationState = "NotRequired";
                _db.SubscriptionOperations.Add(new SubscriptionOperation
                {
                    SubscriptionId = current.Id,
                    OperationType = "UPGRADE_PAID",
                    PreviousStatus = current.Status,
                    NewStatus = current.Status,
                    Reason = $"Tier upgraded from {upgrade.FromTier} to {upgrade.ToTier} with zero due",
                    PerformedByUserId = customerUserId,
                    PerformedByUserType = UserType.Customer,
                    TimestampUtc = _clock.UtcNow
                });
                await _db.SaveChangesAsync(transactionCt);
                return new UpgradeReservation(upgrade.Id, current.Id, upgrade.FromTier, upgrade.ToTier,
                    0m, currency, null, true, upgrade.Status, option.RemainingServiceDays, customerUserId);
            }
            await _db.SaveChangesAsync(transactionCt);
            return new UpgradeReservation(upgrade.Id, current.Id, upgrade.FromTier, upgrade.ToTier,
                option.PriceDifference, currency, null, false, upgrade.Status, option.RemainingServiceDays, customerUserId);
        }, ct);

        if (reservation.IsFailure) return reservation.Error;
        var reserved = reservation.Value;
        if (reserved.IsReviewRequired) return Error.Conflict("upgrade.payment_creation_review_required");
        if (reserved.IsAlreadyReady)
            return new CustomerUpgradeStartedDto(reserved.UpgradeId, reserved.SubscriptionId, reserved.FromTier,
                reserved.ToTier, reserved.Amount, reserved.CurrencyCode, reserved.PaymentUrl,
                reserved.Status == VerificationStatus.Approved, reserved.Status, reserved.RemainingDays);
        if (reserved.IsZeroDue)
            return new CustomerUpgradeStartedDto(reserved.UpgradeId, reserved.SubscriptionId, reserved.FromTier,
                reserved.ToTier, 0m, reserved.CurrencyCode, null, true, reserved.Status, reserved.RemainingDays);

        var user = _users is not null ? await _users.FindByIdAsync(customerUserId, ct) : null;
        PaymentCreationReceipt receipt;
        try
        {
            receipt = await _payments.ExecutePaymentAsync(reserved.Amount, user?.FullName ?? "", user?.Email ?? "",
                user?.PhoneNumber ?? "", request.CallbackUrl, request.ErrorUrl, reserved.SubscriptionId.ToString(),
                request.PaymentMethodId <= 0 ? 3 : request.PaymentMethodId, ct);
        }
        catch
        {
            await SetFamilyUpgradePaymentCreationStateAsync(reserved, "ReviewRequired", null, null, ct);
            return Error.Conflict("upgrade.payment_creation_review_required");
        }

        var stillPayable = await SetFamilyUpgradePaymentCreationStateAsync(reserved, "Ready", receipt.PaymentHandle, receipt.PaymentUrl, ct);
        if (!stillPayable) return Error.Conflict("upgrade.payment_creation_review_required");
        return new CustomerUpgradeStartedDto(reserved.UpgradeId, reserved.SubscriptionId, reserved.FromTier,
            reserved.ToTier, reserved.Amount, reserved.CurrencyCode, receipt.PaymentUrl, false, reserved.Status, reserved.RemainingDays);
    }

    private async Task<List<DateOnly>> ResolveUpgradeFundedDatesAsync(Subscription subscription, CancellationToken ct)
        => await SubscriptionTierUpgradeQuoteBuilder.ResolveFamilyUpgradeFundedDatesAsync(_db, subscription,
            BusinessCalendarDate.KuwaitToday(_clock.UtcNow), ct);

    private static string ComputeRequestFingerprint(Guid payerId, CustomerUpgradeRequest request)
    {
        var payload = string.Join("\n", payerId.ToString("D", CultureInfo.InvariantCulture),
            ((int)request.TargetTier).ToString(CultureInfo.InvariantCulture), request.CallbackUrl, request.ErrorUrl,
            (request.PaymentMethodId <= 0 ? 3 : request.PaymentMethodId).ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private async Task<bool> CanIssueFamilyUpgradeLinkAsync(Subscription subscription, SubscriptionTierUpgrade upgrade,
        Guid payerId, CancellationToken ct)
    {
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled)
            || upgrade.FromTier != subscription.SelectedTier || subscription.FamilyMemberId is not Guid memberId)
            return false;
        var member = await _db.FamilyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == memberId, ct);
        // The family roster pointer can already reference a paid future renewal while this
        // subscription still owns funded service dates. The captured/current date comparison
        // below is the authority for upgrade scope, so pointer equality would hide live service.
        if (member is null || member.IsDeleted)
            return false;
        if (await _db.FamilyMemberAccesses.AsNoTracking().AnyAsync(x => x.FamilyMemberId == memberId && x.RemovedAtUtc != null, ct))
            return false;
        var group = await _db.FamilyGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscription.FamilyGroupId, ct);
        if (group is null || group.IsDeleted || group.Status == FamilyGroupStatus.Cancelled)
            return false;
        var authorization = await _familyAccess.AuthorizeAsync(subscription, payerId, FamilyAction.RequestUpgrade, ct);
        if (authorization.IsFailure) return false;
        List<DateOnly>? capturedDates;
        try { capturedDates = JsonSerializer.Deserialize<List<DateOnly>>(upgrade.EffectiveServiceDatesJson ?? "null"); }
        catch (JsonException) { return false; }
        if (capturedDates is null || capturedDates.Count == 0 || capturedDates.Count != upgrade.QuotedRemainingDays
            || capturedDates.Distinct().Count() != capturedDates.Count)
            return false;
        var currentDates = await ResolveUpgradeFundedDatesAsync(subscription, ct);
        return capturedDates.OrderBy(x => x).SequenceEqual(currentDates);
    }

    private async Task<bool> SetFamilyUpgradePaymentCreationStateAsync(UpgradeReservation reservation, string state,
        string? paymentHandle, string? paymentUrl, CancellationToken ct)
    {
        var familyGroupId = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == reservation.SubscriptionId)
            .Select(x => x.FamilyGroupId).SingleAsync(ct);
        if (!familyGroupId.HasValue || _transactions is null || _locks is null) return false;
        return await _transactions.ExecuteAsync(async transactionCt =>
        {
            await _locks.LockFamilyGroupAsync(familyGroupId.Value, transactionCt);
            await _locks.LockSubscriptionAsync(reservation.SubscriptionId, transactionCt);
            var subscription = await _db.Subscriptions.FirstAsync(x => x.Id == reservation.SubscriptionId, transactionCt);
            var upgrade = await _db.SubscriptionTierUpgrades.FirstAsync(x => x.Id == reservation.UpgradeId, transactionCt);
            var canIssueLink = state == "Ready" && upgrade.Status == VerificationStatus.Submitted
                && await CanIssueFamilyUpgradeLinkAsync(subscription, upgrade, reservation.PayerCustomerUserId, transactionCt);
            upgrade.PaymentCreationState = canIssueLink ? "Ready" : "ReviewRequired";
            if (state == "Ready")
            {
                upgrade.PaymentId = paymentHandle;
                upgrade.PaymentUrl = paymentUrl;
            }
            await _db.SaveChangesAsync(transactionCt);
            return canIssueLink;
        }, ct);
    }

    private sealed record UpgradeReservation(Guid UpgradeId, Guid SubscriptionId, RestaurantTier FromTier, RestaurantTier ToTier,
        decimal Amount, string CurrencyCode, string? PaymentUrl, bool IsZeroDue, VerificationStatus Status, int RemainingDays,
        Guid PayerCustomerUserId, bool IsAlreadyReady = false, bool IsReviewRequired = false);

    public async Task<Result<CustomerUpgradeStatusDto>> GetUpgradeStatusAsync(Guid subscriptionId, Guid upgradeId, Guid customerUserId, string? paymentId = null, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var authorization = await _familyAccess.AuthorizeAsync(subscription, customerUserId, FamilyAction.RequestUpgrade, ct);
        if (authorization.IsFailure) return authorization.Error;

        var upgrade = await _db.SubscriptionTierUpgrades
            .FirstOrDefaultAsync(x => x.Id == upgradeId && x.SubscriptionId == subscriptionId, ct);
        if (upgrade is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Upgrade request not found.");

        if (upgrade.Status == VerificationStatus.Submitted && _paymentProcessor is not null)
        {
            await SyncPendingUpgradePaymentAsync(subscription, upgrade, paymentId, ct);
            upgrade = await _db.SubscriptionTierUpgrades.AsNoTracking()
                .FirstAsync(x => x.Id == upgradeId && x.SubscriptionId == subscriptionId, ct);
        }

        var isPaid = upgrade.Status == VerificationStatus.Approved;
        var isFailed = upgrade.Status is VerificationStatus.Rejected or VerificationStatus.Expired;
        return new CustomerUpgradeStatusDto(
            upgrade.Id,
            upgrade.SubscriptionId,
            upgrade.FromTier,
            upgrade.ToTier,
            upgrade.QuotedTotalDue.Amount,
            upgrade.QuotedTotalDue.CurrencyCode,
            upgrade.Status,
            isPaid,
            isFailed,
            upgrade.EffectiveAtUtc,
            null);
    }

    private async Task SyncPendingUpgradePaymentAsync(Subscription subscription, SubscriptionTierUpgrade upgrade, string? paymentId, CancellationToken ct)
    {
        var key = paymentId ?? upgrade.PaymentId;
        var keyType = "PaymentId";
        if (string.IsNullOrWhiteSpace(key))
        {
            key = subscription.Id.ToString();
            keyType = "CustomerReference";
        }

        var gateway = await _payments.GetPaymentStatusAsync(key, keyType, ct);
        if (gateway is null) return;
        var resolvedPaymentId = gateway.PaymentId ?? paymentId ?? upgrade.PaymentId;
        if (string.IsNullOrWhiteSpace(resolvedPaymentId)) return;

        var paid = gateway.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase);
        var failed = gateway.InvoiceStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Expired", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

        if (paid)
        {
            await _paymentProcessor!.VerifyAndProcessAsync(subscription.Id, resolvedPaymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CurrencyCode, gateway.CustomerReference, cancellationToken: ct);
        }
        else if (failed)
        {
            await _paymentProcessor!.VerifyAndReleaseFailedAsync(subscription.Id, resolvedPaymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CustomerReference, cancellationToken: ct);
        }
    }

    private static (bool CanUpgrade, string? Reason) ComputeUpgradeAvailability(
        Subscription subscription, bool hasPendingUpgrade, int remainingDays, string? pricingReason)
    {
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
            return (false, "subscription_not_active_or_scheduled");
        if (hasPendingUpgrade)
            return (false, "upgrade_request_pending");
        if (remainingDays <= 0)
            return (false, "no_remaining_service_days");
        if (!string.IsNullOrWhiteSpace(pricingReason))
            return (false, pricingReason);
        if (subscription.SelectedTier == RestaurantTier.Elite)
            return (false, "already_highest_tier");
        return (true, null);
    }

    private static CustomerUpgradeOptionDto MapUpgradeOption(SubscriptionTierUpgradeOptionQuote option) => new(
        option.TargetTier,
        option.TierNameAr,
        option.TierNameEn,
        option.LogoUrl,
        option.IsCurrent,
        option.Selectable,
        option.AccessibleRestaurantCount,
        option.CurrentRemainingCredit,
        option.TargetRemainingPrice,
        0m,
        option.PriceDifference,
        option.CurrencyCode);

    private static (string NameAr, string NameEn, string? LogoUrl) ResolveTierInfo(RestaurantTier tier, TierLabelsSettings labels) => tier switch
    {
        RestaurantTier.Basic => (labels.BasicAr, labels.BasicEn, CatalogImageStorage.ToContentUrl(labels.BasicLogoStorageKey)),
        RestaurantTier.Platinum => (labels.PlatinumAr, labels.PlatinumEn, CatalogImageStorage.ToContentUrl(labels.PlatinumLogoStorageKey)),
        RestaurantTier.Elite => (labels.EliteAr, labels.EliteEn, CatalogImageStorage.ToContentUrl(labels.EliteLogoStorageKey)),
        _ => (tier.ToString(), tier.ToString(), null),
    };

    private async Task<IReadOnlyList<CustomerPaymentMethodDto>> ResolvePaymentMethodsAsync(decimal invoiceAmount, CancellationToken ct)
    {
        var config = await _db.PayzahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "Payzah", ct);

        IReadOnlyList<PaymentGatewayMethod> methods;
        try
        {
            methods = await _payments.GetPaymentMethodsAsync(invoiceAmount, ct);
        }
        catch
        {
            methods = [];
        }

        return methods
            .Select(x => new CustomerPaymentMethodDto(
                x.PaymentMethodId,
                x.Code,
                x.NameAr,
                x.NameEn,
                "PAYZAH_HOSTED",
                x.ServiceCharge,
                x.TotalAmount,
                x.CurrencyCode,
                true,
                null,
                x.ImageUrl))
            .ToList();
    }

    private static bool IsConfiguredMethod(PaymentGatewayMethod method, bool knet, bool card, bool apple)
    {
        var code = (method.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (method.NameEn ?? string.Empty).Trim().ToUpperInvariant();
        if (code == "KN" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY") || name.Contains("APPLEPAY")) return apple;
        if (code == "VM" || name.Contains("VISA") || name.Contains("MASTER")) return card;
        return true;
    }


}
