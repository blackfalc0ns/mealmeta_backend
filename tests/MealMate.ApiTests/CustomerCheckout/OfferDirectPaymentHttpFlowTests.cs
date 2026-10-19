using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MealMate.Api.Controllers.Customer;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

/// <summary>Real HTTP binding/authentication/serialization and application pricing, quote,
/// reservation, wallet and payment verification. External gateway, menu eligibility,
/// pool preparation and fulfillment are controlled test boundaries, not a merchant payment.</summary>
public sealed class OfferDirectPaymentHttpFlowTests
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Selection_to_verified_payment_preserves_price_and_one_attempt(string language)
    {
        await using var flow = await Flow.CreateAsync(language);
        var card = await flow.CardAsync();
        Assert.True(card.GetProperty("canCheckout").GetBoolean(), card.GetRawText());
        Assert.Equal(decimal.Round(card.GetProperty("basePrice").GetDecimal() * .2m, 3, MidpointRounding.AwayFromZero),
            card.GetProperty("campaignDiscount").GetDecimal());
        Assert.Equal(3m, card.GetProperty("walletCredit").GetDecimal());
        var summary = await flow.SummaryAsync(card);
        Assert.True(summary.GetProperty("canCheckout").GetBoolean());
        Assert.Equal(card.GetProperty("finalPrice").GetDecimal(), summary.GetProperty("finalPrice").GetDecimal());
        Assert.Equal(card.GetProperty("campaignDiscount").GetDecimal(), summary.GetProperty("campaignDiscount").GetDecimal());
        Assert.Equal(card.GetProperty("walletCredit").GetDecimal(), summary.GetProperty("walletCredit").GetDecimal());
        var request = flow.CheckoutRequest(card, summary);
        using var first = await flow.Client.PostAsJsonAsync("/api/v1/customer/subscriptions/checkout", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var started = await first.Content.ReadFromJsonAsync<JsonElement>();
        var subscriptionId = started.GetProperty("subscriptionId").GetGuid();
        Assert.Equal(summary.GetProperty("finalPrice").GetDecimal(), started.GetProperty("finalPrice").GetDecimal());
        Assert.Equal(card.GetProperty("startDate").GetString(), started.GetProperty("paymentDate").GetString());

        using var retry = await flow.Client.PostAsJsonAsync("/api/v1/customer/subscriptions/checkout", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var replay = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(started.GetRawText(), replay.GetRawText());
        using var conflict = await flow.Client.PostAsJsonAsync("/api/v1/customer/subscriptions/checkout", request with { ErrorUrl = "https://callback.test/other" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("checkout.idempotency_conflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        using var resumed = await flow.Client.PostAsJsonAsync($"/api/v1/customer/subscriptions/{subscriptionId}/resume", new { });
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(started.GetProperty("paymentUrl").GetString(), (await resumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentUrl").GetString());
        Assert.Equal(1, flow.InvoiceCount);
        Assert.Single(await flow.Db.Subscriptions.ToListAsync());
        Assert.Equal(6, await flow.Db.CapacityHolds.CountAsync());

        using var pending = await flow.Client.GetAsync($"/api/v1/customer/subscriptions/{subscriptionId}/payment");
        Assert.False((await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isPaid").GetBoolean());
        flow.GatewayPaid = true;
        using var paid = await flow.Client.GetAsync($"/api/v1/customer/subscriptions/{subscriptionId}/payment");
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.True((await paid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isPaid").GetBoolean());
        Assert.Equal(SubscriptionStatus.Paid, (await flow.Db.Subscriptions.SingleAsync()).Status);
        Assert.Single(await flow.Db.PaymentWebhookReceipts.ToListAsync());
        Assert.All(await flow.Db.CapacityHolds.ToListAsync(), hold => Assert.Equal(CapacityHoldStatus.PaymentConfirmed, hold.Status));
        Assert.Equal(0m, (await flow.Db.CustomerPromotionalWallets.SingleAsync()).Balance.Amount);
        using var paidAgain = await flow.Client.GetAsync($"/api/v1/customer/subscriptions/{subscriptionId}/payment");
        Assert.True((await paidAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isPaid").GetBoolean());
        Assert.Single(await flow.Db.PaymentWebhookReceipts.ToListAsync());
        Assert.Equal(1, flow.InvoiceCount);
    }

    [Fact]
    public async Task Missing_profile_and_expired_quote_cannot_start_payment()
    {
        await using var flow = await Flow.CreateAsync("ar");
        using var anonymous = flow.App.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(flow.OptionsUrl)).StatusCode);
        var profile = await flow.Db.CustomerProfiles.SingleAsync();
        profile.AllergiesCompletedAtUtc = null;
        await flow.Db.SaveChangesAsync();
        var blocked = await flow.CardAsync();
        Assert.False(blocked.GetProperty("canCheckout").GetBoolean());
        using var blockedSummary = await flow.Client.GetAsync(flow.SummaryUrl(blocked));
        Assert.Equal(HttpStatusCode.Conflict, blockedSummary.StatusCode);
        Assert.Empty(await flow.Db.OfferCheckoutQuotes.ToListAsync());
        profile.AllergiesCompletedAtUtc = flow.Clock.UtcNow;
        await flow.Db.SaveChangesAsync();
        var card = await flow.CardAsync();
        var summary = await flow.SummaryAsync(card);
        var quote = await flow.Db.OfferCheckoutQuotes.SingleAsync();
        quote.ExpiresAtUtc = flow.Clock.UtcNow.AddSeconds(-1);
        await flow.Db.SaveChangesAsync();
        using var expired = await flow.Client.PostAsJsonAsync("/api/v1/customer/subscriptions/checkout", flow.CheckoutRequest(card, summary));
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal("checkout.quote_expired", (await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(0, flow.InvoiceCount);
        Assert.Empty(await flow.Db.Subscriptions.ToListAsync());
    }

    private sealed class Flow : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public required IDateTimeProvider Clock { get; init; }
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid OfferId { get; private set; }
        public int InvoiceCount { get; private set; }
        public bool GatewayPaid { get; set; }
        public string OptionsUrl => $"/api/v1/customer/offers/{OfferId}/purchase-options";

        public static async Task<Flow> CreateAsync(string language)
        {
            var clock = Substitute.For<IDateTimeProvider>();
            var now = DateTime.UtcNow;
            clock.UtcNow.Returns(now);
            clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                Substitute.For<IDomainEventDispatcher>());
            var flow = new Flow { Db = db, Clock = clock };
            var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "HTTP", NameAr = "برنامج", NameEn = "Program", IsActive = true, Status = ProgramStatus.Active };
            var bundle = new Bundle { ProgramId = program.Id, Code = "HTTP_B", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, IsActive = true, Status = ProgramStatus.Active };
            var restaurant = new RestaurantProfile { TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant", VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true };
            var address = new CustomerAddress { UserId = flow.CustomerId, RegionId = Guid.NewGuid(), Label = "Home", AddressLine = "Address", Governorate = "Capital", IsDefault = true };
            var offer = new Offer { NameAr = "عرض", NameEn = "Offer", Status = OfferStatus.Active, StartsAtUtc = clock.UtcNow.AddDays(-1), EndsAtUtc = clock.UtcNow.AddDays(10), DiscountType = DiscountType.Percentage, DiscountRate = Percentage.FromFraction(0.2m), PerCustomerLimit = 1,
                PackageTargets = [new OfferPackageTarget { ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic, SubscriptionType = "Individual", AllowedDurationDays = [6] }] };
            flow.OfferId = offer.Id;
            db.AddRange(program, bundle, restaurant, address, offer,
                new CustomerProfile { UserId = flow.CustomerId, AllergiesCompletedAtUtc = clock.UtcNow, DietaryPreferencesCompletedAtUtc = clock.UtcNow, ActiveDeliveryAddressId = address.Id, ActiveDeliveryRegionId = address.RegionId, ActiveDeliveryAddressLine = address.AddressLine },
                new CustomerPromotionalWallet { CustomerUserId = flow.CustomerId, Balance = new Money(3m, "KWD"), IsActive = true },
                new RestaurantServiceRegion { RestaurantId = restaurant.Id, RegionId = address.RegionId!.Value, IsActive = true },
                new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true },
                new RestaurantBundlePricing { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"), LockStatus = PriceLockStatus.Approved, EffectiveFromUtc = clock.UtcNow.AddDays(-1) },
                new PricingRuleVersion { IsActive = true, RMax = Percentage.FromPercent(15m), RMin = Percentage.FromPercent(5m), DShort = 6, DLong = 26, Gamma = 1m, RoundingStepKwd = .25m },
                new SystemSetting { Key = SubscriptionPricingSettingsDefaults.Key, ValueJson = JsonSerializer.Serialize(SubscriptionPricingSettingsDefaults.Create()), IsActive = true });
            await db.SaveChangesAsync();
            var schedule = new SubscriptionScheduleService(clock);
            var tiers = new RestaurantTierClassificationService(db, clock);
            var audit = Substitute.For<IAuditLogger>();
            var pricing = new PricingAndClassificationHandlers(db, audit, clock, tiers, new OfferEligibilityEngine());
            var commands = new SubscriptionCommandHandlers(db, audit, clock, schedule: schedule);
            var sender = Substitute.For<ISender>();
            sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>()).Returns(c => pricing.Handle(c.ArgAt<PreviewCustomerPricingQuery>(0), c.ArgAt<CancellationToken>(1)));
            sender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>()).Returns(c => commands.Handle(c.ArgAt<CreateSubscriptionCommand>(0), c.ArgAt<CancellationToken>(1)));
            var eligibility = Substitute.For<IRestaurantEligibilityService>();
            var menuVersion = Guid.NewGuid();
            eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>()).Returns(c =>
            {
                var selection = c.ArgAt<RestaurantEligibilityRequest>(0);
                return Task.FromResult<IReadOnlyList<RestaurantEligibilityDto>>([new(restaurant.Id, restaurant.TradeNameAr, restaurant.TradeNameEn, true, [],
                    schedule.GetServiceDates(selection.StartDate, selection.DurationDays, selection.IncludeFriday).Select(day => new EligibleMenuDateDto(day, menuVersion)).ToList())]);
            });
            var addresses = new CustomerDeliveryAddressService(db, clock);
            var gate = new CustomerIndividualSubscriptionGate(db, clock);
            var availability = new SubscriptionRestaurantAvailabilityService(db, clock, addresses, eligibility, tiers);
            var allowed = new AllowedSubscriptionDurationService(db);
            var readiness = new SubscriptionCheckoutReadinessService(db, allowed, tiers, addresses, gate, availability, schedule);
            var slots = new BundleSlotRequirementService(db);
            var options = new GetCustomerOfferPurchaseOptionsQueryHandler(db, new OfferEligibilityEngine(), clock, schedule, allowed, addresses, gate, availability, sender);
            sender.Send(Arg.Any<GetCustomerOfferPurchaseOptionsQuery>(), Arg.Any<CancellationToken>()).Returns(c => options.Handle(c.ArgAt<GetCustomerOfferPurchaseOptionsQuery>(0), c.ArgAt<CancellationToken>(1)));
            var payments = Substitute.For<IPaymentGatewayService>();
            payments.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ => { flow.InvoiceCount++; return Task.FromResult(new PaymentCreationReceipt("fixture-payment", "https://payzah.net/fixture", "fixture-track")); });
            payments.GetPaymentStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ =>
            {
                var subscription = await db.Subscriptions.SingleAsync();
                return new PaymentGatewayStatusResponse("fixture-invoice", flow.GatewayPaid ? "Paid" : "Pending", subscription.PaidAmount.Amount, "", "", "", "fixture-transaction", "Payzah", clock.UtcNow, subscription.Id.ToString(), "KWD", "pz-fixture-track");
            });
            var transactions = new InlineTransactions();
            var capacityLock = Substitute.For<ICapacityConcurrencyLock>();
            var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
            fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new FulfillmentResult(true, false));
            var processor = new SubscriptionPaymentProcessor(db, payments, transactions, capacityLock, schedule, fulfillment, clock);
            var pool = Substitute.For<IRestaurantPoolSnapshotService>();
            pool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(), restaurant.Id, Arg.Any<CancellationToken>()).Returns(async c =>
            {
                var id = c.ArgAt<Guid>(0);
                var selected = c.ArgAt<IReadOnlyList<RestaurantEligibilityDto>>(1).Single();
                var snapshot = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = id, RestaurantCount = 1, EligibilityCheckedAtUtc = clock.UtcNow };
                db.SubscriptionRestaurantPoolSnapshots.Add(snapshot);
                db.SubscriptionRestaurantSnapshots.Add(new SubscriptionRestaurantSnapshot { PoolSnapshotId = snapshot.Id, SubscriptionId = id, RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, RegionId = address.RegionId!.Value, Tier = RestaurantTier.Basic, IsInitiallySelected = true, CapturedAtUtc = clock.UtcNow, NameAr = restaurant.TradeNameAr, NameEn = restaurant.TradeNameEn });
                foreach (var day in selected.MenuPlanVersionsByDate)
                    db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot { SubscriptionId = id, RestaurantId = restaurant.Id, ServiceDate = day.ServiceDate, MenuPlanVersionId = day.MenuPlanVersionId, ContentHash = new string('a', 64) });
                await db.SaveChangesAsync();
                return new RestaurantPoolCaptureResult(true, snapshot.Id, 1);
            });
            var capacity = Substitute.For<IRestaurantCapacityReader>();
            capacity.ReadAsync(Arg.Any<IReadOnlyCollection<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>()).Returns(c => c.ArgAt<IReadOnlyCollection<(Guid, DateOnly)>>(0).ToDictionary(k => k, k => new RestaurantCapacityUnits(k.Item1, k.Item2, 10, 0, 0, 0, 0, 0, 0, 0, 0)));
            var users = Substitute.For<IUserAccountService>();
            users.FindByIdAsync(flow.CustomerId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(flow.CustomerId, "customer@example.test", "50000000", "Customer", UserType.Customer, AccountStatus.Active, null, true, true, now, null));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Checkout:AllowedRedirectHosts:0"] = "callback.test", ["Checkout:AllowedRedirectSchemes:0"] = "https" }).Build();
            var checkout = new CustomerCheckoutService(db, payments, clock, users, eligibility, capacityLock, transactions, schedule, pool, config, allowed, processor, readiness, sender, capacity);
            var summary = new SubscriptionFlowQueryService(db, eligibility, Substitute.For<IMenuPlanResolutionService>(), schedule, slots, payments, allowed, tiers, readiness, gate, new RestaurantDiversificationCalculator(), sender, configuration: config, availability: availability, clock: clock);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(CustomerOffersController).Assembly)
                .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new FlowControllers()))
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddMealMateLocalization();
            builder.Services.AddSingleton<ISender>(sender);
            builder.Services.AddSingleton<ISubscriptionFlowQueryService>(summary);
            builder.Services.AddSingleton<ICustomerCheckoutService>(checkout);
            builder.Services.AddSingleton<ISubscriptionCatalogAvailabilityService>(Substitute.For<ISubscriptionCatalogAvailabilityService>());
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser>(sp =>
            {
                var current = Substitute.For<ICurrentUser>();
                current.UserId.Returns(Guid.Parse(sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
                current.UserType.Returns(UserType.Customer);
                return current;
            });
            builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, CustomerAuthentication>("fixture", _ => { });
            builder.Services.AddAuthorization();
            flow.App = builder.Build();
            flow.App.UseMealMateLocalization();
            flow.App.UseAuthentication();
            flow.App.UseAuthorization();
            flow.App.MapControllers();
            await flow.App.StartAsync();
            flow.Client = flow.App.GetTestClient();
            flow.Client.DefaultRequestHeaders.Add("X-Test-Customer", flow.CustomerId.ToString());
            flow.Client.DefaultRequestHeaders.Add("Accept-Language", language);
            return flow;
        }

        public async Task<JsonElement> CardAsync()
        {
            using var response = await Client.GetAsync(OptionsUrl);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("options").EnumerateArray().Single();
        }
        public string SummaryUrl(JsonElement card) => $"/api/v1/customer/subscription-flow/summary?offerId={OfferId}&programId={card.GetProperty("programId").GetString()}&bundleId={card.GetProperty("bundleId").GetString()}&tier=Basic&durationDays=6&startDate={card.GetProperty("startDate").GetString()}&includeFriday=false";
        public async Task<JsonElement> SummaryAsync(JsonElement card)
        {
            using var response = await Client.GetAsync(SummaryUrl(card));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        public StartCheckoutRequest CheckoutRequest(JsonElement card, JsonElement summary) => new(
            card.GetProperty("programId").GetGuid(), card.GetProperty("bundleId").GetGuid(), RestaurantTier.Basic, 6,
            "https://callback.test/success", "https://callback.test/error", DateOnly.Parse(card.GetProperty("startDate").GetString()!, CultureInfo.InvariantCulture),
            IdempotencyKey: "persisted-mobile-attempt", OfferId: OfferId, QuoteId: summary.GetProperty("quoteId").GetGuid());
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); await Db.DisposeAsync(); }
    }

    private sealed class InlineTransactions : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }
    private sealed class FlowControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var included = new[] { typeof(CustomerOffersController), typeof(CustomerSubscriptionFlowController), typeof(CustomerCheckoutController) };
            foreach (var controller in feature.Controllers.Where(c => !included.Contains(c.AsType())).ToList())
                feature.Controllers.Remove(controller);
        }
    }
    public sealed class CustomerAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-Customer"], out var id)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "fixture"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "fixture")));
        }
    }
}
