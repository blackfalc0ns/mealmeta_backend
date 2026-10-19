using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class OfferCheckoutQuoteServiceTests
{
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _config;
    private readonly DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public OfferCheckoutQuoteServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("OfferCheckoutQuoteServiceTests_" + Guid.NewGuid().ToString("N"))
            .Options;
        _db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(_now);

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Checkout:QuoteLifetimeMinutes"] = "10"
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
    }

    [Fact]
    public async Task CreateQuoteAsync_UsesConfiguredLifetime_AndGeneratesDeterministicHash()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var req = SampleQuoteRequest();

        var result = await service.CreateQuoteAsync(req);

        Assert.True(result.IsSuccess);
        var quote = result.Value;
        Assert.Equal(req.CustomerId, quote.CustomerId);
        Assert.Equal(req.OfferId, quote.OfferId);
        Assert.Equal(_now.AddMinutes(10), quote.ExpiresAtUtc);
        Assert.NotNull(quote.PayloadSnapshotHash);
        Assert.False(quote.IsUsed);

        var inDb = await _db.OfferCheckoutQuotes.FirstOrDefaultAsync(x => x.Id == quote.Id);
        Assert.NotNull(inDb);
    }

    [Fact]
    public async Task ValidateQuoteAtCheckout_Fails_WhenQuoteNotFound()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var result = await service.ValidateQuoteAtCheckoutAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            SampleCheckoutRequest(), SamplePricingResult(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("offers.customer.unavailable", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task ValidateQuoteAtCheckout_Fails_WhenForeignCustomer()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var quote = await CreateAndSaveQuoteAsync();

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, Guid.NewGuid(), // different customer
            ToCheckoutRequest(quote), ToPricingResult(quote), quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("offers.customer.unavailable", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task ValidateQuoteAtCheckout_Fails_WhenQuoteExpired()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var quote = await CreateAndSaveQuoteAsync();

        // Advance clock past expiration
        _clock.UtcNow.Returns(quote.ExpiresAtUtc.AddSeconds(1));

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote), ToPricingResult(quote), quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_expired", result.Error.Code);
        Assert.Equal(ErrorType.Gone, result.Error.Type);
    }

    [Fact]
    public async Task ValidateQuoteAtCheckout_Fails_WhenQuoteAlreadyUsed()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var quote = await CreateAndSaveQuoteAsync();
        await service.MarkQuoteAsUsedAsync(quote.Id);

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote), ToPricingResult(quote), quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_changed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Theory]
    [InlineData("ProgramId")]
    [InlineData("BundleId")]
    [InlineData("Tier")]
    [InlineData("DurationDays")]
    [InlineData("StartDate")]
    [InlineData("IncludeFriday")]
    [InlineData("PromoCode")]
    [InlineData("OfferId")]
    public async Task ValidateQuoteAtCheckout_Fails_WhenSelectionMismatched(string fieldToMutate)
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var quote = await CreateAndSaveQuoteAsync();

        var req = ToCheckoutRequest(quote);
        req = fieldToMutate switch
        {
            "ProgramId" => req with { ProgramId = Guid.NewGuid() },
            "BundleId" => req with { BundleId = Guid.NewGuid() },
            "Tier" => req with { Tier = RestaurantTier.Platinum },
            "DurationDays" => req with { DurationDays = 12 },
            "StartDate" => req with { StartDate = quote.StartDate.AddDays(7) },
            "IncludeFriday" => req with { IncludeFriday = !quote.IncludeFriday },
            "PromoCode" => req with { PromoCode = "DIFFERENT" },
            "OfferId" => req with { OfferId = Guid.NewGuid() },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldToMutate))
        };

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            req, ToPricingResult(quote), quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_changed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task ValidateQuoteAtCheckout_Fails_WhenAddressChanged()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var quote = await CreateAndSaveQuoteAsync();

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote), ToPricingResult(quote),
            Guid.NewGuid()); // Different address

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_changed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task ValidateQuoteAtCheckout_Fails_WhenPriceDrifted()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var quote = await CreateAndSaveQuoteAsync();

        var driftedPricing = ToPricingResult(quote) with { FinalCustomerPrice = quote.FinalPrice + 5m };

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote), driftedPricing, quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_changed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task ValidateQuoteAtCheckout_Succeeds_WhenAllFieldsAgree()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var quote = await CreateAndSaveQuoteAsync();

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote), ToPricingResult(quote), quote.AddressId);

        Assert.True(result.IsSuccess);
        Assert.Equal(quote.Id, result.Value.Id);
    }

    private static OfferCheckoutQuoteCreationRequest SampleQuoteRequest()
        => new(
            CustomerId: Guid.NewGuid(),
            OfferId: Guid.NewGuid(),
            ProgramId: Guid.NewGuid(),
            BundleId: Guid.NewGuid(),
            Tier: RestaurantTier.Basic,
            DurationDays: 26,
            StartDate: new DateOnly(2026, 11, 1),
            EndDate: new DateOnly(2026, 11, 30),
            IncludeFriday: false,
            FridayDays: 0,
            TotalServiceDays: 26,
            PromoCode: "SAVE20",
            BasePrice: 200.000m,
            CampaignDiscount: 20.000m,
            PromoDiscount: 0.000m,
            WalletCredit: 0.000m,
            GatewayFee: 0.250m,
            FinalPrice: 180.250m,
            CurrencyCode: "KWD",
            AddressId: Guid.NewGuid());

    private async Task<OfferCheckoutQuote> CreateAndSaveQuoteAsync()
    {
        var service = new OfferCheckoutQuoteService(_db, _clock, _config);
        var res = await service.CreateQuoteAsync(SampleQuoteRequest());
        return res.Value;
    }

    private static StartCheckoutRequest ToCheckoutRequest(OfferCheckoutQuote q)
        => new(
            q.ProgramId,
            q.BundleId,
            q.Tier,
            q.DurationDays,
            "https://maelmate.runasp.net/callback",
            "https://maelmate.runasp.net/error",
            q.StartDate,
            q.IncludeFriday,
            PromoCode: q.PromoCode,
            OfferId: q.OfferId,
            QuoteId: q.Id);

    private static PricingEngineResult ToPricingResult(OfferCheckoutQuote q)
        => new(
            A_t: 10m,
            DurationDays: q.DurationDays,
            DurationMultiplierR: 1m,
            UnroundedCorePrice: q.BasePrice,
            CampaignDiscountApplied: q.CampaignDiscount,
            PromoCodeDiscountApplied: q.PromoDiscount,
            WalletCreditApplied: q.WalletCredit,
            PriceAfterDiscounts: q.BasePrice - q.CampaignDiscount - q.PromoDiscount,
            GatewayCostRecoveryAmount: q.GatewayFee,
            FinalCustomerPrice: q.FinalPrice,
            TotalSupplyCost: 100m,
            EstimatedPlatformMargin: 80m,
            EstimatedPlatformMarginPct: 0.40m,
            IsProfitabilityAlertTriggered: false,
            CurrencyCode: q.CurrencyCode);

    private static StartCheckoutRequest SampleCheckoutRequest()
        => new(
            Guid.NewGuid(), Guid.NewGuid(), RestaurantTier.Basic, 26,
            "https://maelmate.runasp.net/callback", "https://maelmate.runasp.net/error",
            new DateOnly(2026, 11, 1));

    private static PricingEngineResult SamplePricingResult()
        => new(
            10m, 26, 1m, 200m, 0m, 0m, 0m, 200m, 0.250m, 200.250m, 100m, 100m, 0.5m, false, "KWD");
}
