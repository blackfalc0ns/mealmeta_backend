using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Upgrade;

public sealed class SubscriptionUpgradeService : ISubscriptionUpgradeService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionTierUpgradeQuoteBuilder _upgradeQuotes;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly IUserAccountService? _users;
    private readonly IRestaurantTierClassificationService? _classification;
    private readonly IRestaurantDiversificationCalculator? _diversification;
    private readonly ISubscriptionPaymentProcessor? _paymentProcessor;

    public SubscriptionUpgradeService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes,
        IMyFatoorahPaymentService payments,
        IUserAccountService? users = null,
        IRestaurantTierClassificationService? classification = null,
        IRestaurantDiversificationCalculator? diversification = null,
        ISubscriptionPaymentProcessor? paymentProcessor = null)
    {
        _db = db;
        _clock = clock;
        _upgradeQuotes = upgradeQuotes;
        _payments = payments;
        _users = users;
        _classification = classification;
        _diversification = diversification;
        _paymentProcessor = paymentProcessor;
    }

    public async Task<Result<CustomerUpgradeOptionsDto>> GetUpgradeOptionsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

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
            .FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

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

        var earliestUpgradeDate = _clock.TodayUtc.AddDays(3);
        var latestLockedServiceDate = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id &&
                        x.ServiceDate >= _clock.TodayUtc &&
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
            .FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var invoiceAmount = amount.HasValue && amount.Value > 0 ? amount.Value : 1m;
        return Result.Success(await ResolvePaymentMethodsAsync(invoiceAmount, ct));
    }

    public async Task<Result<CustomerUpgradeStartedDto>> RequestUpgradeAsync(Guid subscriptionId, Guid customerUserId, CustomerUpgradeRequest request, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        if (subscription.FamilyGroupId is Guid familyGroupId)
        {
            var isManager = await _db.FamilyGroups.AnyAsync(g => g.Id == familyGroupId && g.ManagerCustomerUserId == customerUserId, ct);
            if (!isManager)
                return Error.Conflict("family.manager_only_action", "Only family managers can upgrade family subscriptions.");
        }

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
            FromTier = subscription.SelectedTier,
            ToTier = request.TargetTier,
            PriceDifference = new Money(option.PriceDifference, currency),
            QuotedRemainingDays = option.RemainingServiceDays,
            QuotedTotalDue = new Money(option.PriceDifference, currency),
            QuoteJson = SubscriptionTierUpgradeQuoteBuilder.SerializeQuoteJson(option),
            Status = VerificationStatus.Submitted,
        };
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
            string paymentUrl;
            try
            {
                paymentUrl = await _payments.ExecutePaymentAsync(
                    option.PriceDifference,
                    user?.FullName ?? "MealMate Customer",
                    user?.Email ?? "",
                    user?.PhoneNumber ?? "",
                    request.CallbackUrl,
                    request.ErrorUrl,
                    subscription.Id.ToString(),
                    request.PaymentMethodId <= 0 ? 1 : request.PaymentMethodId,
                    ct);
            }
            catch (Exception ex)
            {
                upgrade.Status = VerificationStatus.Rejected;
                await _db.SaveChangesAsync(ct);
                return Error.Failure("upgrade.payment_init_failed", ex.Message);
            }

            paymentUrl = (paymentUrl ?? string.Empty).Trim();
            upgrade.PaymentId = PaymentIdFromUrl(paymentUrl);
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

    public async Task<Result<CustomerUpgradeStatusDto>> GetUpgradeStatusAsync(Guid subscriptionId, Guid upgradeId, Guid customerUserId, string? paymentId = null, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId && x.CustomerUserId == customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

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
        var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", ct);

        IReadOnlyList<MyFatoorahPaymentMethod> methods;
        try
        {
            methods = await _payments.GetPaymentMethodsAsync(invoiceAmount, ct);
        }
        catch
        {
            methods = [];
        }

        return methods
            .Where(x => config is null || IsConfiguredMethod(x, config.IsKnetEnabled, config.IsVisaMasterEnabled, config.IsApplePayEnabled))
            .Select(x => new CustomerPaymentMethodDto(
                x.PaymentMethodId,
                x.Code,
                x.NameAr,
                x.NameEn,
                "MYFATOORAH_HOSTED",
                x.ServiceCharge,
                x.TotalAmount,
                x.CurrencyCode,
                true,
                null,
                x.ImageUrl))
            .ToList();
    }

    private static bool IsConfiguredMethod(MyFatoorahPaymentMethod method, bool knet, bool card, bool apple)
    {
        var code = (method.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (method.NameEn ?? string.Empty).Trim().ToUpperInvariant();
        if (code == "KN" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY") || name.Contains("APPLEPAY")) return apple;
        if (code == "VM" || name.Contains("VISA") || name.Contains("MASTER")) return card;
        return true;
    }

    private static string? PaymentIdFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["paymentId"] ?? query["Id"];
        }
        catch
        {
            return null;
        }
    }
}
