using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record OfferCheckoutQuoteCreationRequest(
    Guid CustomerId,
    Guid OfferId,
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IncludeFriday,
    int FridayDays,
    int TotalServiceDays,
    string? PromoCode,
    decimal BasePrice,
    decimal CampaignDiscount,
    decimal PromoDiscount,
    decimal WalletCredit,
    decimal GatewayFee,
    decimal FinalPrice,
    string CurrencyCode,
    Guid? AddressId);

public interface IOfferCheckoutQuoteService
{
    Task<Result<OfferCheckoutQuote>> CreateQuoteAsync(
        OfferCheckoutQuoteCreationRequest request,
        CancellationToken ct = default);

    Task<Result<OfferCheckoutQuote>> ValidateQuoteAtCheckoutAsync(
        Guid quoteId,
        Guid customerUserId,
        StartCheckoutRequest checkoutRequest,
        PricingEngineResult pricingResult,
        Guid? currentAddressId,
        CancellationToken ct = default);

    Task MarkQuoteAsUsedAsync(
        Guid quoteId,
        CancellationToken ct = default);
}

public sealed class OfferCheckoutQuoteService : IOfferCheckoutQuoteService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration? _configuration;

    public OfferCheckoutQuoteService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IConfiguration? configuration = null)
    {
        _db = db;
        _clock = clock;
        _configuration = configuration;
    }

    public async Task<Result<OfferCheckoutQuote>> CreateQuoteAsync(
        OfferCheckoutQuoteCreationRequest request,
        CancellationToken ct = default)
    {
        var lifetimeMinutes = 15;
        if (int.TryParse(_configuration?["Checkout:QuoteLifetimeMinutes"], out var parsed) && parsed > 0)
        {
            lifetimeMinutes = parsed;
        }

        var now = _clock.UtcNow;
        var expiresAt = now.AddMinutes(lifetimeMinutes);

        var hash = ComputeSnapshotHash(
            request.CustomerId, request.OfferId, request.ProgramId, request.BundleId,
            request.Tier, request.DurationDays, request.StartDate, request.IncludeFriday,
            request.PromoCode, request.BasePrice, request.CampaignDiscount,
            request.PromoDiscount, request.WalletCredit, request.GatewayFee,
            request.FinalPrice, request.AddressId);

        var quote = new OfferCheckoutQuote(Guid.NewGuid())
        {
            CustomerId = request.CustomerId,
            OfferId = request.OfferId,
            ProgramId = request.ProgramId,
            BundleId = request.BundleId,
            Tier = request.Tier,
            DurationDays = request.DurationDays,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IncludeFriday = request.IncludeFriday,
            FridayDays = request.FridayDays,
            TotalServiceDays = request.TotalServiceDays,
            PromoCode = NormalizePromoCode(request.PromoCode),
            BasePrice = request.BasePrice,
            CampaignDiscount = request.CampaignDiscount,
            PromoDiscount = request.PromoDiscount,
            WalletCredit = request.WalletCredit,
            GatewayFee = request.GatewayFee,
            FinalPrice = request.FinalPrice,
            CurrencyCode = request.CurrencyCode,
            AddressId = request.AddressId,
            PayloadSnapshotHash = hash,
            ExpiresAtUtc = expiresAt,
            IsUsed = false,
            CreatedAtUtc = now
        };

        _db.OfferCheckoutQuotes.Add(quote);
        await _db.SaveChangesAsync(ct);

        return Result.Success(quote);
    }

    public async Task<Result<OfferCheckoutQuote>> ValidateQuoteAtCheckoutAsync(
        Guid quoteId,
        Guid customerUserId,
        StartCheckoutRequest checkoutRequest,
        PricingEngineResult pricingResult,
        Guid? currentAddressId,
        CancellationToken ct = default)
    {
        var quote = await _db.OfferCheckoutQuotes.FirstOrDefaultAsync(x => x.Id == quoteId, ct);
        if (quote is null)
            return Error.NotFound("offers.customer.unavailable");

        // Foreign quotes must return 404 to prevent information disclosure
        if (quote.CustomerId != customerUserId)
            return Error.NotFound("offers.customer.unavailable");

        if (quote.IsUsed)
            return Error.Conflict("checkout.quote_changed");

        if (_clock.UtcNow >= quote.ExpiresAtUtc)
            return Error.Gone("checkout.quote_expired");

        // Selection agreement
        if (checkoutRequest.OfferId.HasValue && quote.OfferId != checkoutRequest.OfferId.Value)
            return Error.Conflict("checkout.quote_changed");

        if (quote.ProgramId != checkoutRequest.ProgramId
            || quote.BundleId != checkoutRequest.BundleId
            || quote.Tier != checkoutRequest.Tier
            || quote.DurationDays != checkoutRequest.DurationDays
            || quote.StartDate != checkoutRequest.StartDate
            || quote.IncludeFriday != checkoutRequest.IncludeFriday
            || NormalizePromoCode(quote.PromoCode) != NormalizePromoCode(checkoutRequest.PromoCode))
        {
            return Error.Conflict("checkout.quote_changed");
        }

        // Address change check
        if (currentAddressId != quote.AddressId)
            return Error.Conflict("checkout.quote_changed");

        // Price / fee drift check
        if (pricingResult.FinalCustomerPrice != quote.FinalPrice
            || pricingResult.UnroundedCorePrice != quote.BasePrice
            || pricingResult.CampaignDiscountApplied != quote.CampaignDiscount
            || pricingResult.PromoCodeDiscountApplied != quote.PromoDiscount
            || pricingResult.WalletCreditApplied != quote.WalletCredit
            || pricingResult.GatewayCostRecoveryAmount != quote.GatewayFee)
        {
            return Error.Conflict("checkout.quote_changed");
        }

        return Result.Success(quote);
    }

    public async Task MarkQuoteAsUsedAsync(
        Guid quoteId,
        CancellationToken ct = default)
    {
        var quote = await _db.OfferCheckoutQuotes.FirstOrDefaultAsync(x => x.Id == quoteId, ct);
        if (quote != null)
        {
            quote.IsUsed = true;
            quote.UsedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string? NormalizePromoCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return code.Trim().ToUpperInvariant();
    }

    private static string ComputeSnapshotHash(
        Guid customerId, Guid offerId, Guid programId, Guid bundleId,
        RestaurantTier tier, int durationDays, DateOnly startDate, bool includeFriday,
        string? promoCode, decimal basePrice, decimal campaignDiscount,
        decimal promoDiscount, decimal walletCredit, decimal gatewayFee,
        decimal finalPrice, Guid? addressId)
    {
        var raw = FormattableString.Invariant(
            $"{customerId}|{offerId}|{programId}|{bundleId}|{tier}|{durationDays}|{startDate:O}|{includeFriday}|{NormalizePromoCode(promoCode)}|{basePrice:F3}|{campaignDiscount:F3}|{promoDiscount:F3}|{walletCredit:F3}|{gatewayFee:F3}|{finalPrice:F3}|{addressId}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
