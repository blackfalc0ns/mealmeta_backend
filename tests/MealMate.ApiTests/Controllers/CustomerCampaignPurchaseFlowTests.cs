using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Controllers.Customer;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.CollaborativeCampaigns;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using CampaignStatus = MealMate.Domain.Enums.CampaignStatus;
using CatalogProgram = MealMate.Domain.ProgramsSubscriptions.Program;

/// <summary>
/// Full customer campaign purchase flow over real HTTP (test server, real routing, auth,
/// controllers and MediatR handlers): campaigns list -> details -> quote -> checkout-session
/// (quoteId) -> payment -> gateway callback (+ duplicate) -> polling -> subscription.
/// Atomicity is proven separately on PostgreSQL (PostgreSqlConcurrencyTests); this suite proves
/// the end-to-end contract and the money parity between list, details, quote and payment.
/// </summary>
public sealed class CustomerCampaignPurchaseFlowTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private const decimal ExpectedFinalPrice = 109.200m; // 156 - round(156 * 0.30)

    [Fact]
    public async Task FullFlow_List_Details_Quote_Checkout_Payment_Polling_ProducesOneSubscription()
    {
        await using var fx = await FlowFixture.CreateAsync();

        // 1. Campaigns list shows the active campaign with the policy-backed min price.
        var listResponse = await fx.Client.GetAsync("/api/v1/customer/campaigns?pageSize=20");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        var list = JsonDocument.Parse(listBody).RootElement;
        list.GetProperty("serverNowUtc").GetDateTime().Should().Be(Now);
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("campaignId").GetGuid() == fx.CampaignId);
        item.GetProperty("minPrice").GetDecimal().Should().Be(ExpectedFinalPrice);
        item.GetProperty("currencyCode").GetString().Should().Be("KWD");
        item.GetProperty("fullSequenceAvailable").GetBoolean().Should().BeTrue();
        listBody.Should().NotContain("Contribution").And.NotContain("ShareRate");

        // 2. Details: no blockers for a complete profile; parity with the list price.
        var detailsResponse = await fx.Client.GetAsync($"/api/v1/customer/campaigns/{fx.CampaignId}");
        detailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
        details.GetProperty("canCheckout").GetBoolean().Should().BeTrue();
        details.GetProperty("blockingReasons").GetArrayLength().Should().Be(0);
        var option = details.GetProperty("options").EnumerateArray().Single(o =>
            o.GetProperty("campaignBundleId").GetGuid() == fx.CampaignBundleId);
        option.GetProperty("finalPrice").GetDecimal().Should().Be(ExpectedFinalPrice);
        option.GetProperty("customerDiscountPercent").GetDecimal().Should().Be(30.000m);
        option.GetProperty("referencePrice").GetDecimal().Should().Be(156.000m);

        // 3. Quote: server-side price offer bound to the selection.
        var quoteResponse = await fx.Client.PostAsJsonAsync("/api/v1/customer/campaigns/quote", new
        {
            customerId = fx.CustomerId,
            campaignId = fx.CampaignId,
            programId = fx.ProgramId,
            campaignBundleId = fx.CampaignBundleId,
            durationDays = 1,
            startDate = "2026-10-22",
            addressId = fx.AddressId
        });
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = await quoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        quote.GetProperty("canCheckout").GetBoolean().Should().BeTrue();
        quote.GetProperty("quoteId").GetGuid().Should().NotBe(Guid.Empty);
        quote.GetProperty("price").GetProperty("finalPrice").GetDecimal().Should().Be(ExpectedFinalPrice);
        var quoteId = quote.GetProperty("quoteId").GetGuid();

        // 4. Checkout session from the quote: amount parity with the quote.
        var checkoutResponse = await fx.Client.PostAsJsonAsync("/api/v1/customer/campaigns/checkout-session", new
        {
            idempotencyKey = "flow-ck-1",
            campaignId = fx.CampaignId,
            bundleId = fx.CampaignBundleId,
            customerId = fx.CustomerId,
            durationDays = 1,
            requestedServiceDates = new[] { "2026-10-22" },
            quoteId
        });
        checkoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();
        session.GetProperty("finalCustomerPrice").GetDecimal().Should().Be(ExpectedFinalPrice);
        session.GetProperty("status").GetString().Should().Be("Pending");
        var sessionId = session.GetProperty("sessionId").GetGuid();

        // 4b. Same key + same payload replays the same session (interruption recovery).
        var replayResponse = await fx.Client.PostAsJsonAsync("/api/v1/customer/campaigns/checkout-session", new
        {
            idempotencyKey = "flow-ck-1",
            campaignId = fx.CampaignId,
            bundleId = fx.CampaignBundleId,
            customerId = fx.CustomerId,
            durationDays = 1,
            requestedServiceDates = new[] { "2026-10-22" },
            quoteId
        });
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replayResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetGuid()
            .Should().Be(sessionId);

        // 5. Start payment with the gateway stub.
        var payResponse = await fx.Client.PostAsJsonAsync($"/api/v1/customer/campaigns/{sessionId}/payment", new
        {
            idempotencyKey = "flow-pay-1",
            paymentMethodId = 3
        });
        payResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payment = await payResponse.Content.ReadFromJsonAsync<JsonElement>();
        payment.GetProperty("amount").GetDecimal().Should().Be(ExpectedFinalPrice);
        payment.GetProperty("paymentId").GetString().Should().Be("pz-track-flow");

        // 5b. Harness: the provider-side attempt journal and a verified provider status.
        fx.Db.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt
        {
            Provider = "Payzah",
            TrackId = "track-flow",
            PaymentId = "provider-id-flow",
            CustomerReference = $"campaign-checkout:{sessionId:N}",
            ExpectedAmount = ExpectedFinalPrice,
            ExpectedCurrency = "KWD"
        });
        await fx.Db.SaveChangesAsync();
        fx.Gateway.GetPaymentStatusAsync("pz-track-flow", "PaymentId", Arg.Any<CancellationToken>())
            .Returns(FlowFixture.VerifiedStatus(sessionId));

        // 6. Provider callback (webhook secret, no customer auth) — amount verified against
        //    the session snapshot; a duplicate callback is idempotent.
        fx.Client.DefaultRequestHeaders.Remove("X-Fixture-Actor");
        fx.Client.DefaultRequestHeaders.Add("X-Webhook-Secret", "flow-secret");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var callback = await fx.Client.PostAsJsonAsync("/api/v1/webhooks/campaign-payment/callback", new
            {
                checkoutSessionId = sessionId,
                paymentProvider = "Payzah",
                providerTransactionId = "pz-track-flow",
                providerPaymentReference = "provider-reference-flow",
                amount = ExpectedFinalPrice,
                currency = "KWD",
                isSuccess = true
            });
            callback.StatusCode.Should().Be(HttpStatusCode.OK);
            var callbackBody = await callback.Content.ReadFromJsonAsync<JsonElement>();
            callbackBody.GetProperty("sessionStatus").GetString().Should().Be("Completed");
        }

        // 7. Polling: paid + subscription id.
        fx.Client.DefaultRequestHeaders.Add("X-Fixture-Actor", fx.CustomerId.ToString());
        var statusResponse = await fx.Client.GetAsync($"/api/v1/customer/campaigns/{sessionId}/payment");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        status.GetProperty("isPaid").GetBoolean().Should().BeTrue();
        status.GetProperty("amount").GetDecimal().Should().Be(ExpectedFinalPrice);
        status.GetProperty("subscriptionId").GetGuid().Should().NotBe(Guid.Empty);

        // 8. Invariants: one session, one allocation on the quote's service date, one
        //    subscription pinned to the snapshot amount and session correlation.
        var sessions = await fx.Db.CampaignCheckoutSessions.Include(s => s.Allocations).ToListAsync();
        sessions.Should().ContainSingle();
        sessions[0].Allocations.Should().ContainSingle(a => a.ServiceDate == new DateOnly(2026, 10, 22));
        sessions[0].FinalAmount.Should().Be(ExpectedFinalPrice);
        sessions[0].CustomerQuoteId.Should().Be(quoteId);
        (await fx.Db.Subscriptions.ToListAsync()).Should().ContainSingle();
        var subscription = await fx.Db.Subscriptions.SingleAsync();
        subscription.PaidAmount!.Amount.Should().Be(ExpectedFinalPrice);
        subscription.CorrelationId.Should().Be(sessions[0].Id.ToString("N"));
    }

    [Fact]
    public async Task ExpiredQuote_Checkout_ReturnsGoneWithRefreshHints()
    {
        await using var fx = await FlowFixture.CreateAsync();

        var quote = new CustomerCampaignQuote
        {
            CustomerId = fx.CustomerId,
            CampaignId = fx.CampaignId,
            ProgramId = fx.ProgramId,
            CampaignBundleId = fx.CampaignBundleId,
            CatalogBundleId = Guid.NewGuid(),
            DurationDays = 1,
            StartDate = new DateOnly(2026, 10, 22),
            AddressId = fx.AddressId,
            ReferencePrice = 156m,
            DiscountAmount = 46.8m,
            FinalPrice = ExpectedFinalPrice,
            CurrencyCode = "KWD",
            PricingPolicyId = Guid.NewGuid(),
            PricingPolicyVersion = 1,
            CampaignSnapshotVersion = 1,
            ExpiresAtUtc = Now.AddMinutes(-1)
        };
        fx.Db.CustomerCampaignQuotes.Add(quote);
        await fx.Db.SaveChangesAsync();

        var response = await fx.Client.PostAsJsonAsync("/api/v1/customer/campaigns/checkout-session", new
        {
            idempotencyKey = "flow-expired-quote",
            campaignId = fx.CampaignId,
            bundleId = fx.CampaignBundleId,
            customerId = fx.CustomerId,
            durationDays = 1,
            requestedServiceDates = new[] { "2026-10-22" },
            quoteId = quote.Id
        });

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Campaign.QuoteExpired");
        problem.GetProperty("refreshSummary").GetBoolean().Should().BeTrue();
        problem.GetProperty("refreshAvailability").GetBoolean().Should().BeTrue();
    }

    private sealed class FlowFixture : IAsyncDisposable
    {
        public AppDbContext Db = null!;
        public HttpClient Client = null!;
        public IPaymentGatewayService Gateway = null!;
        public Guid CampaignId, CampaignBundleId, CatalogBundleId, ProgramId, CustomerId, AddressId;

        private WebApplication _app = null!;

        public static PaymentGatewayStatusResponse VerifiedStatus(Guid sessionId) => new(
            "provider-reference-flow", "CAPTURED", ExpectedFinalPrice, "Flow Customer",
            "customer@mealmate.example", "+96550000001", "provider-transaction-flow", "Payzah", Now,
            $"campaign-checkout:{sessionId:N}", "KWD", "pz-track-flow");

        public static async Task<FlowFixture> CreateAsync()
        {
            var fx = new FlowFixture { CustomerId = Guid.NewGuid() };
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            fx.Db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(Now);

            await SeedAsync(fx);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Checkout:DefaultCallbackUrl"] = "https://mealmate.example/pay/callback",
                ["Checkout:DefaultErrorUrl"] = "https://mealmate.example/pay/error",
                ["Checkout:AllowedRedirectSchemes:0"] = "https",
                ["Checkout:AllowedRedirectHosts:0"] = "mealmate.example",
                ["PaymentGateways:CampaignWebhookSecret"] = "flow-secret"
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(CustomerCollaborativeCampaignsController).Assembly);
            builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateCustomerCampaignQuoteCommand).Assembly));
            builder.Services.AddMealMateLocalization();
            builder.Services.AddSingleton(fx.Db);
            builder.Services.AddSingleton<IAppDbContext>(fx.Db);
            builder.Services.AddSingleton<IDateTimeProvider>(clock);

            builder.Services.AddScoped<ICustomerCampaignEvaluationService>(_ => new CustomerCampaignEvaluationService(
                fx.Db, clock, new CampaignEligibilityEngine(),
                new CustomerDeliveryAddressService(fx.Db, clock),
                new SubscriptionScheduleService(clock)));
            builder.Services.AddScoped<ISubscriptionFlowCouponService>(_ => new SubscriptionFlowCouponService(fx.Db, new OfferEligibilityEngine(), clock));
            builder.Services.AddSingleton<CustomerCampaignOptionProjector>(_ => new CustomerCampaignOptionProjector(fx.Db));
            builder.Services.AddSingleton<ISubscriberCapManager, SubscriberCapManager>();
            builder.Services.AddSingleton<ICampaignFulfillmentAllocator, CampaignFulfillmentAllocator>();
            builder.Services.AddSingleton<ICampaignReservationCoordinator>(_ => new CampaignReservationCoordinator(
                new SubscriberCapManager(), new CampaignFulfillmentAllocator(), clock));
            builder.Services.AddSingleton<ICampaignCapacityLockRepository>(_ => new CampaignCapacityLockRepository(fx.Db));

            var users = Substitute.For<IUserAccountService>();
            users.FindByIdAsync(fx.CustomerId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
                fx.CustomerId, "customer@mealmate.example", "+96550000001", "Flow Customer", UserType.Customer,
                AccountStatus.Active, null, true, true, Now, null));
            builder.Services.AddSingleton(users);

            fx.Gateway = Substitute.For<IPaymentGatewayService>();
            fx.Gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new PaymentCreationReceipt("provider-id-flow", "https://payzah.example/checkout/flow", "track-flow"));
            builder.Services.AddSingleton(fx.Gateway);

            var latePayments = Substitute.For<ILatePaymentResolutionService>();
            latePayments.ResolveLatePaymentAsync(
                    Arg.Do<CampaignCheckoutSession>(s => s.MarkPaid()),
                    Arg.Any<CampaignPaymentTransaction>(), Arg.Any<CancellationToken>())
                .Returns(new LatePaymentResolutionResult(true, CampaignCheckoutSessionStatus.Paid, false, "late capture honored"));
            builder.Services.AddSingleton(latePayments);

            var locks = Substitute.For<IApplicationLockManager>();
            locks.LockCampaignPaymentKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            locks.LockCampaignCheckoutSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            builder.Services.AddSingleton(locks);
            builder.Services.AddSingleton<IApplicationTransactionExecutor>(_ => new TestTransactionExecutor());
            builder.Services.AddScoped<ICampaignPaymentService>(_ => new CampaignPaymentService(
                fx.Db,
                new TestTransactionExecutor(),
                locks, fx.Gateway, users, latePayments, clock,
                builder.Configuration));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser>(sp =>
            {
                var user = Substitute.For<ICurrentUser>();
                var id = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier);
                user.UserId.Returns(Guid.Parse(id!));
                user.UserType.Returns(UserType.Customer);
                return user;
            });
            builder.Services.AddAuthentication("fixture")
                .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
            builder.Services.AddAuthorization();

            fx._app = builder.Build();
            fx._app.UseAuthentication();
            fx._app.UseAuthorization();
            fx._app.MapControllers();
            await fx._app.StartAsync();
            fx.Client = fx._app.GetTestClient();
            fx.Client.DefaultRequestHeaders.Add("X-Fixture-Actor", fx.CustomerId.ToString());
            return fx;
        }

        private static async Task SeedAsync(FlowFixture fx)
        {
            var db = fx.Db;
            var campaign = new Campaign
            {
                NameAr = "حملة التدفق",
                NameEn = "Flow Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = Now.AddHours(-2),
                EndsAtUtc = Now.AddDays(30),
                TotalDiscountRate = Percentage.FromFraction(0.30m),
                MealMateShareRate = Percentage.FromFraction(0.15m),
                RestaurantShareRate = Percentage.FromFraction(0.15m),
                MaxSubscribers = 50
            };
            db.Campaigns.Add(campaign);

            var program = new CatalogProgram
            {
                Code = "P-FLOW",
                NameAr = "برنامج التدفق",
                NameEn = "Flow Program",
                Status = ProgramStatus.Active,
                IsActive = true
            };
            db.Programs.Add(program);
            var catalogBundle = new Bundle
            {
                ProgramId = program.Id,
                Code = "B-FLOW",
                NameAr = "باقة يوم",
                NameEn = "Day Bundle",
                MealsPerDay = 3,
                Status = ProgramStatus.Active,
                IsActive = true
            };
            db.Bundles.Add(catalogBundle);
            var campaignBundle = new CampaignBundle { CampaignId = campaign.Id, BundleId = catalogBundle.Id };
            db.CampaignBundles.Add(campaignBundle);
            await db.SaveChangesAsync();

            db.CampaignBundleDurationPrices.Add(CampaignBundleDurationPrice.Create(
                campaignBundle.Id, 1, new Money(156.000m, "KWD"), "System", 1, Now).Value);

            var restaurantId = Guid.NewGuid();
            db.CampaignRestaurantEnrollments.Add(new CampaignRestaurantEnrollment
            {
                CampaignId = campaign.Id,
                RestaurantId = restaurantId,
                ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
                RequestedAtUtc = Now
            });
            for (var i = 1; i <= 45; i++)
            {
                db.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
                {
                    CampaignId = campaign.Id,
                    RestaurantId = restaurantId,
                    ServiceDate = new DateOnly(2026, 10, 21).AddDays(i - 1),
                    MaxBoxes = 5,
                    ReservedBoxes = 0
                });
            }

            var policy = CampaignPricingPolicy.Create(
                1, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KWD",
                CampaignPricingPolicy.RoundingAwayFromZero,
                "{\"restaurantShare\":0.5}", "{\"stacking\":\"exclusive\"}", "{\"basis\":\"undeliveredDays\"}").Value;
            policy.Approve(Guid.NewGuid(), Now);
            db.CampaignPricingPolicies.Add(policy);

            var address = new CustomerAddress
            {
                UserId = fx.CustomerId,
                Label = "المنزل",
                AddressLine = "شارع التدفق",
                RegionId = Guid.NewGuid(),
                Governorate = "العاصمة",
                IsDefault = true
            };
            db.CustomerAddresses.Add(address);
            db.CustomerProfiles.Add(new CustomerProfile
            {
                UserId = fx.CustomerId,
                AllergiesCompletedAtUtc = Now.AddMinutes(-1),
                DietaryPreferencesCompletedAtUtc = Now.AddMinutes(-1),
                ActiveDeliveryAddressId = address.Id,
                ActiveDeliveryRegionId = address.RegionId
            });
            await db.SaveChangesAsync();

            fx.CampaignId = campaign.Id;
            fx.CampaignBundleId = campaignBundle.Id;
            fx.CatalogBundleId = catalogBundle.Id;
            fx.ProgramId = program.Id;
            fx.AddressId = address.Id;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    private sealed class TestTransactionExecutor : IApplicationTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class FixtureAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Fixture-Actor"].ToString())], "fixture");
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), "fixture")));
        }
    }
}
