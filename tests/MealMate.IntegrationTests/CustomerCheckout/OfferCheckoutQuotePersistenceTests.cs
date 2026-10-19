using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CustomerCheckout;

public sealed class OfferCheckoutQuotePersistenceTests : IAsyncLifetime
{
    private MealMate.Testing.PostgresTestDatabase _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker or an explicitly configured local PostgreSQL test database is required.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Quote_persistence_roundtrips_precision_and_indexes()
    {
        await using var db = CreateDb();
        var quote = NewQuote();
        db.OfferCheckoutQuotes.Add(quote);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var loaded = await db.OfferCheckoutQuotes.AsNoTracking().SingleAsync(x => x.Id == quote.Id);
        Assert.Equal(quote.CustomerId, loaded.CustomerId);
        Assert.Equal(quote.OfferId, loaded.OfferId);
        Assert.Equal(quote.FinalPrice, loaded.FinalPrice);
        Assert.Equal(180.250m, loaded.FinalPrice);
        Assert.Equal(quote.BasePrice, loaded.BasePrice);
        Assert.Equal("KWD", loaded.CurrencyCode);
        Assert.False(loaded.IsUsed);
    }

    [Fact]
    public async Task Ownership_foreign_customer_returns_unavailable()
    {
        await using var db = CreateDb();
        var quote = NewQuote();
        db.OfferCheckoutQuotes.Add(quote);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var service = new OfferCheckoutQuoteService(db, clock);
        var foreignCustomerId = Guid.NewGuid();

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, foreignCustomerId,
            ToCheckoutRequest(quote),
            ToPricingResult(quote),
            quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("offers.customer.unavailable", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Expiration_boundary_returns_410_gone_when_expired()
    {
        await using var db = CreateDb();
        var quote = NewQuote();
        db.OfferCheckoutQuotes.Add(quote);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        // Set clock right at the expiration boundary
        clock.UtcNow.Returns(quote.ExpiresAtUtc);

        var service = new OfferCheckoutQuoteService(db, clock);

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote),
            ToPricingResult(quote),
            quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_expired", result.Error.Code);
        Assert.Equal(ErrorType.Gone, result.Error.Type);
    }

    [Fact]
    public async Task Payload_mismatch_returns_409_quote_changed()
    {
        await using var db = CreateDb();
        var quote = NewQuote();
        db.OfferCheckoutQuotes.Add(quote);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(quote.CreatedAtUtc.AddMinutes(1));

        var service = new OfferCheckoutQuoteService(db, clock);

        // Mismatched duration
        var mismatchedRequest = ToCheckoutRequest(quote) with { DurationDays = 14 };

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            mismatchedRequest,
            ToPricingResult(quote),
            quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_changed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Price_drift_returns_409_quote_changed()
    {
        await using var db = CreateDb();
        var quote = NewQuote();
        db.OfferCheckoutQuotes.Add(quote);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(quote.CreatedAtUtc.AddMinutes(1));

        var service = new OfferCheckoutQuoteService(db, clock);

        // Price changed
        var driftedPricing = ToPricingResult(quote) with { FinalCustomerPrice = 195.000m };

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote),
            driftedPricing,
            quote.AddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_changed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Address_change_returns_409_quote_changed()
    {
        await using var db = CreateDb();
        var quote = NewQuote();
        db.OfferCheckoutQuotes.Add(quote);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(quote.CreatedAtUtc.AddMinutes(1));

        var service = new OfferCheckoutQuoteService(db, clock);

        // New address
        var newAddressId = Guid.NewGuid();

        var result = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote),
            ToPricingResult(quote),
            newAddressId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.quote_changed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Repeated_reads_and_used_quote_is_rejected()
    {
        await using var db = CreateDb();
        var quote = NewQuote();
        db.OfferCheckoutQuotes.Add(quote);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(quote.CreatedAtUtc.AddMinutes(1));

        var service = new OfferCheckoutQuoteService(db, clock);

        // First validation passes
        var firstResult = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote),
            ToPricingResult(quote),
            quote.AddressId);
        Assert.True(firstResult.IsSuccess);

        // Mark quote used
        await service.MarkQuoteAsUsedAsync(quote.Id);

        // Second validation fails because quote is already used
        var secondResult = await service.ValidateQuoteAtCheckoutAsync(
            quote.Id, quote.CustomerId,
            ToCheckoutRequest(quote),
            ToPricingResult(quote),
            quote.AddressId);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("checkout.quote_changed", secondResult.Error.Code);
        Assert.Equal(ErrorType.Conflict, secondResult.Error.Type);
    }

    private static OfferCheckoutQuote NewQuote()
        => new(Guid.NewGuid())
        {
            CustomerId = Guid.NewGuid(),
            OfferId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Tier = RestaurantTier.Standard,
            DurationDays = 26,
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 30),
            IncludeFriday = false,
            FridayDays = 0,
            TotalServiceDays = 26,
            PromoCode = "SAVE20",
            BasePrice = 200.000m,
            CampaignDiscount = 20.000m,
            PromoDiscount = 0.000m,
            WalletCredit = 0.000m,
            GatewayFee = 0.250m,
            FinalPrice = 180.250m,
            CurrencyCode = "KWD",
            AddressId = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            IsUsed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

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

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
