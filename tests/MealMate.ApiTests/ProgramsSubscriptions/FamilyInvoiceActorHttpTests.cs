using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Renewal;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediatR;
using NSubstitute;

namespace MealMate.ApiTests.ProgramsSubscriptions;

public sealed class FamilyInvoiceActorHttpTests
{
    [Fact]
    public async Task Real_invoice_routes_keep_terminal_upgrade_and_base_payers_in_their_own_scopes_and_localize_review_status()
    {
        await using var fx = await Fixture.Create();

        using var managerCalendar = await fx.Get(fx.Manager, $"/api/v1/customer/subscriptions/{fx.CalendarSubscriptionId}/calendar");
        Assert.Equal(HttpStatusCode.OK, managerCalendar.StatusCode);
        using var managerInvoices = await fx.Get(fx.Manager, "/api/v1/customer/invoices");
        Assert.Equal(HttpStatusCode.OK, managerInvoices.StatusCode);
        Assert.Contains((await managerInvoices.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(),
            x => x.GetProperty("amount").GetDecimal() == 100m);

        using var calendarOnlyInvoices = await fx.Get(fx.CalendarActor, $"/api/v1/customer/subscriptions/{fx.CalendarSubscriptionId}/invoices");
        // A calendar grant does not reveal the invoice endpoint or its subscription existence.
        Assert.Equal(HttpStatusCode.NotFound, calendarOnlyInvoices.StatusCode);

        using var upgradeList = await fx.Get(fx.UpgradePayer, "/api/v1/customer/invoices", "ar");
        Assert.Equal(HttpStatusCode.OK, upgradeList.StatusCode);
        var upgradeRows = await upgradeList.Content.ReadFromJsonAsync<JsonElement>();
        var upgradeItem = upgradeRows.EnumerateArray().Single();
        Assert.Equal(fx.UpgradeReceiptId, upgradeItem.GetProperty("receiptId").GetGuid());
        Assert.Equal(fx.UpgradePayer, upgradeItem.GetProperty("payerCustomerUserId").GetGuid());
        Assert.Equal("PAID_REVIEW_REQUIRED", upgradeItem.GetProperty("status").GetString());
        Assert.Equal("تم التحقق من الدفع، ويتطلب مراجعة مالية", upgradeItem.GetProperty("statusName").GetString());
        Assert.False(upgradeItem.TryGetProperty("statusNameEn", out _));
        using var upgradeListEnglish = await fx.Get(fx.UpgradePayer, "/api/v1/customer/invoices", "en");
        var englishReview = (await upgradeListEnglish.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();
        Assert.Equal("Payment verified; financial review required", englishReview.GetProperty("statusName").GetString());
        Assert.False(englishReview.TryGetProperty("statusNameAr", out _));

        using var siblingBase = await fx.Get(fx.UpgradePayer, $"/api/v1/customer/invoices/{fx.BaseReceiptId}");
        Assert.Equal(HttpStatusCode.NotFound, siblingBase.StatusCode);
        using var baseList = await fx.Get(fx.BasePayer, "/api/v1/customer/invoices", "en");
        Assert.Equal(HttpStatusCode.OK, baseList.StatusCode);
        var baseRows = await baseList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(fx.BaseReceiptId, baseRows.EnumerateArray().Single().GetProperty("receiptId").GetGuid());

        using var stranger = await fx.Get(Guid.NewGuid(), "/api/v1/customer/invoices");
        Assert.Equal(HttpStatusCode.OK, stranger.StatusCode);
        Assert.False((await stranger.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Any());

        using var calendarAllowed = await fx.Get(fx.CalendarActor, $"/api/v1/customer/subscriptions/{fx.CalendarSubscriptionId}/calendar");
        Assert.Equal(HttpStatusCode.OK, calendarAllowed.StatusCode);
        using var calendarDenied = await fx.Get(fx.UpgradePayer, $"/api/v1/customer/subscriptions/{fx.CalendarSubscriptionId}/calendar");
        Assert.Equal(HttpStatusCode.Forbidden, calendarDenied.StatusCode);

        await fx.SetPermissionsAsync(viewCalendar: false, viewInvoices: true, viewFamilyInvoices: false);
        using var invoiceOnlyCalendar = await fx.Get(fx.CalendarActor, $"/api/v1/customer/subscriptions/{fx.CalendarSubscriptionId}/calendar");
        Assert.Equal(HttpStatusCode.Forbidden, invoiceOnlyCalendar.StatusCode);
        using var invoiceOnlyList = await fx.Get(fx.CalendarActor, $"/api/v1/customer/subscriptions/{fx.CalendarSubscriptionId}/invoices");
        Assert.Equal(HttpStatusCode.OK, invoiceOnlyList.StatusCode);
        Assert.Equal(60m, (await invoiceOnlyList.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single().GetProperty("amount").GetDecimal());

        using var siblingDenied = await fx.Get(fx.SiblingActor, $"/api/v1/customer/subscriptions/{fx.SiblingSubscriptionId}/invoices");
        Assert.Equal(HttpStatusCode.NotFound, siblingDenied.StatusCode);
        await fx.SetSiblingPermissionsAsync(viewCalendar: true, viewInvoices: true, viewFamilyInvoices: true);
        using var siblingFamilyScope = await fx.Get(fx.SiblingActor, $"/api/v1/customer/subscriptions/{fx.SiblingSubscriptionId}/invoices");
        Assert.Equal(HttpStatusCode.OK, siblingFamilyScope.StatusCode);
        Assert.Contains((await siblingFamilyScope.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(),
            x => x.GetProperty("amount").GetDecimal() == 100m);
        using var pausedCalendar = await fx.Get(fx.SiblingActor, $"/api/v1/customer/subscriptions/{fx.SiblingSubscriptionId}/calendar");
        Assert.Equal(HttpStatusCode.OK, pausedCalendar.StatusCode);
        await fx.SetSiblingAccessPausedAsync(true);
        using var pausedCalendarDenied = await fx.Get(fx.SiblingActor, $"/api/v1/customer/subscriptions/{fx.SiblingSubscriptionId}/calendar");
        Assert.Equal(HttpStatusCode.Forbidden, pausedCalendarDenied.StatusCode);
        await fx.SetSiblingAccessPausedAsync(false);
        await fx.SetSiblingAccountEnabledAsync(false);
        using var disabledInvoices = await fx.Get(fx.SiblingActor, $"/api/v1/customer/subscriptions/{fx.SiblingSubscriptionId}/invoices");
        Assert.Equal(HttpStatusCode.NotFound, disabledInvoices.StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private Fixture(AppDbContext db, WebApplication app, HttpClient client, Guid manager, Guid upgradePayer, Guid basePayer,
            Guid baseReceiptId, Guid upgradeReceiptId, Guid calendarActor, Guid calendarSubscriptionId,
            Guid siblingActor, Guid siblingSubscriptionId)
            => (Db, _app, _client, Manager, UpgradePayer, BasePayer, BaseReceiptId, UpgradeReceiptId,
                CalendarActor, CalendarSubscriptionId, SiblingActor, SiblingSubscriptionId) =
                (db, app, client, manager, upgradePayer, basePayer, baseReceiptId, upgradeReceiptId,
                    calendarActor, calendarSubscriptionId, siblingActor, siblingSubscriptionId);

        public Guid UpgradePayer { get; }
        public Guid BasePayer { get; }
        public Guid BaseReceiptId { get; }
        public Guid UpgradeReceiptId { get; }
        public Guid CalendarActor { get; }
        public Guid CalendarSubscriptionId { get; }
        public Guid Manager { get; }
        public Guid SiblingActor { get; }
        public Guid SiblingSubscriptionId { get; }
        private AppDbContext Db { get; }

        public static async Task<Fixture> Create()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                Substitute.For<IDomainEventDispatcher>());
            var manager = Guid.NewGuid();
            var basePayer = Guid.NewGuid();
            var upgradePayer = Guid.NewGuid();
            var oldLinkedActor = Guid.NewGuid();
            var siblingActor = Guid.NewGuid();
            var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "invoice-http", NameAr = "خطة", NameEn = "Plan", IsActive = true };
            var bundle = new Bundle { ProgramId = program.Id, Code = "invoice-http", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
            var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Invoice family", Status = FamilyGroupStatus.Active };
            var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
                RelationshipCode = "child", NutritionalGoalCode = "maintain" };
            var siblingMember = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Sibling", Role = FamilyMemberRole.Member,
                RelationshipCode = "child", NutritionalGoalCode = "maintain" };
            var subscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id,
                FamilyGroupId = group.Id, FamilyMemberId = member.Id, Status = SubscriptionStatus.Completed,
                PaidAmount = new Money(100m, "KWD"), StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), DurationDays = 26 };
            var calendarSubscription = new Subscription { CustomerUserId = oldLinkedActor, ProgramId = program.Id, BundleId = bundle.Id,
                FamilyGroupId = group.Id, FamilyMemberId = member.Id, Status = SubscriptionStatus.Active,
                PaymentVerifiedAtUtc = DateTime.UtcNow, FulfillmentCompletedAtUtc = DateTime.UtcNow,
                PaidAmount = new Money(100m, "KWD"), StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), DurationDays = 26 };
            var siblingSubscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id,
                FamilyGroupId = group.Id, FamilyMemberId = siblingMember.Id, Status = SubscriptionStatus.Completed,
                PaymentVerifiedAtUtc = DateTime.UtcNow, FulfillmentCompletedAtUtc = DateTime.UtcNow,
                PaidAmount = new Money(40m, "KWD"), StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), DurationDays = 26 };
            // The active subscription is the member's service row and payment anchor;
            // the completed row remains historical evidence for the terminal upgrade.
            member.MemberSubscriptionId = calendarSubscription.Id;
            group.PaymentAnchorSubscriptionId = calendarSubscription.Id;
            siblingMember.MemberSubscriptionId = siblingSubscription.Id;
            group.Members.Add(member); group.Members.Add(siblingMember);
            var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = basePayer,
                Status = FamilyCheckoutBatchStatus.Paid, IdempotencyKey = "invoice-http-base", FinalPrice = 100m, CurrencyCode = "KWD",
                GatewayPaymentId = "invoice-http-base-payment", GatewayInvoiceId = "invoice-http-base-invoice", PaidAtUtc = DateTime.UtcNow,
                PaymentAnchorSubscriptionId = calendarSubscription.Id };
            batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, FamilyMemberId = member.Id,
                SubscriptionId = calendarSubscription.Id, PriceBeforeFamilyDiscount = 60m, NetPaidAmount = 60m, CurrencyCode = "KWD" });
            batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, FamilyMemberId = siblingMember.Id,
                SubscriptionId = siblingSubscription.Id, PriceBeforeFamilyDiscount = 40m, NetPaidAmount = 40m, CurrencyCode = "KWD" });
            var attempt = new FamilyCheckoutPaymentAttempt { BatchId = batch.Id, AttemptKey = "invoice-http-attempt",
                GatewayPaymentId = batch.GatewayPaymentId, GatewayInvoiceId = batch.GatewayInvoiceId, ResultCode = "Paid",
                VerifiedAtUtc = batch.PaidAtUtc, RequestedAtUtc = DateTime.UtcNow };
            var baseReceipt = new PaymentWebhookReceipt { Gateway = "Fixture", EventKey = "invoice-http-base-payment",
                SubscriptionId = calendarSubscription.Id,
                PaymentId = batch.GatewayPaymentId, InvoiceId = batch.GatewayInvoiceId, Amount = 100m, CurrencyCode = "KWD",
                Status = "FulfillmentCompleted", VerifiedAtUtc = batch.PaidAtUtc, ProcessedAtUtc = DateTime.UtcNow };
            var upgrade = new SubscriptionTierUpgrade { SubscriptionId = subscription.Id, FromTier = RestaurantTier.Basic,
                ToTier = RestaurantTier.Platinum, PriceDifference = new Money(12m, "KWD"), QuotedTotalDue = new Money(12m, "KWD"),
                PaymentId = "invoice-http-upgrade-payment", PayerCustomerUserId = upgradePayer, VerifiedPaidAmount = 12m,
                VerifiedCurrencyCode = "KWD", VerifiedInvoiceId = "invoice-http-upgrade-invoice", VerifiedPaidAtUtc = DateTime.UtcNow,
                Status = VerificationStatus.Submitted };
            var upgradeReceipt = new PaymentWebhookReceipt { Gateway = "Fixture", EventKey = "invoice-http-upgrade-payment",
                SubscriptionId = subscription.Id, PaymentId = upgrade.PaymentId, InvoiceId = upgrade.VerifiedInvoiceId,
                Amount = 12m, CurrencyCode = "KWD", Status = "FinancialReviewRequired", VerifiedAtUtc = DateTime.UtcNow,
                ProcessedAtUtc = DateTime.UtcNow };
            var siblingAccess = new FamilyMemberAccess { FamilyMemberId = siblingMember.Id, LinkedCustomerUserId = siblingActor, HasSeparateAccount = true };
            var siblingPermissions = new FamilyMemberPermissions { FamilyMemberId = siblingMember.Id };
            db.AddRange(program, bundle, group, member, siblingMember, subscription, calendarSubscription, siblingSubscription,
                batch, attempt, baseReceipt, upgrade, upgradeReceipt,
                new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = oldLinkedActor, HasSeparateAccount = true },
                new FamilyMemberPermissions { FamilyMemberId = member.Id, ViewCalendar = true }, siblingAccess, siblingPermissions);
            await db.SaveChangesAsync();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(CustomerSubscriptionManagementController).Assembly)
                .AddJsonOptions(options => MealMateJsonSerializer.Configure(options.JsonSerializerOptions));
            builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CustomerCalendarHandlers).Assembly));
            builder.Services.AddMealMateLocalization();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IAppDbContext>(db);
            builder.Services.AddSingleton<IDomainEventDispatcher>(Substitute.For<IDomainEventDispatcher>());
            builder.Services.AddSingleton(Substitute.For<IDateTimeProvider>());
            builder.Services.AddSingleton<IRestaurantDiversificationCalculator, RestaurantDiversificationCalculator>();
            builder.Services.AddScoped<CustomerCalendarEngine>();
            builder.Services.AddSingleton<IPaymentGatewayService>(Substitute.For<IPaymentGatewayService>());
            builder.Services.AddSingleton<ISubscriptionTierUpgradeQuoteBuilder>(Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>());
            builder.Services.AddSingleton<ISubscriptionCancellationService>(Substitute.For<ISubscriptionCancellationService>());
            builder.Services.AddSingleton<ISubscriptionFreezeService>(Substitute.For<ISubscriptionFreezeService>());
            builder.Services.AddSingleton<ISubscriptionUpgradeService>(Substitute.For<ISubscriptionUpgradeService>());
            builder.Services.AddSingleton<ISubscriptionRenewalService>(Substitute.For<ISubscriptionRenewalService>());
            builder.Services.AddSingleton<ICustomerSubscriptionQueryService>(sp => new CustomerSubscriptionQueryService(db,
                sp.GetRequiredService<IDateTimeProvider>(), messages: sp.GetRequiredService<MealMate.Application.Common.Localization.ILocalizedMessageProvider>()));
            builder.Services.AddIdentityCore<ApplicationUser>();
            builder.Services.AddSingleton<IUserStore<ApplicationUser>>(Substitute.For<IUserStore<ApplicationUser>>());
            builder.Services.AddScoped<ICurrentUser>(sp =>
            {
                var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                var current = Substitute.For<ICurrentUser>();
                current.UserId.Returns(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
                current.UserType.Returns(UserType.Customer);
                return current;
            });
            builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseMealMateLocalization();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            return new Fixture(db, app, app.GetTestClient(), manager, upgradePayer, basePayer, baseReceipt.Id,
                upgradeReceipt.Id, oldLinkedActor, calendarSubscription.Id, siblingActor, siblingSubscription.Id);
        }

        public async Task SetPermissionsAsync(bool viewCalendar, bool viewInvoices, bool viewFamilyInvoices)
        {
            var row = await Db.FamilyMemberPermissions.SingleAsync(x => x.FamilyMemberId == Db.FamilyMembers.Single(m => m.DisplayName == "Member").Id);
            row.ViewCalendar = viewCalendar; row.ViewInvoices = viewInvoices; row.ViewFamilyInvoices = viewFamilyInvoices;
            await Db.SaveChangesAsync();
        }
        public async Task SetSiblingPermissionsAsync(bool viewCalendar, bool viewInvoices, bool viewFamilyInvoices)
        {
            var member = await Db.FamilyMembers.SingleAsync(x => x.DisplayName == "Sibling");
            var row = await Db.FamilyMemberPermissions.SingleAsync(x => x.FamilyMemberId == member.Id);
            row.ViewCalendar = viewCalendar; row.ViewInvoices = viewInvoices; row.ViewFamilyInvoices = viewFamilyInvoices;
            await Db.SaveChangesAsync();
        }
        public async Task SetSiblingAccessPausedAsync(bool paused)
        {
            var member = await Db.FamilyMembers.SingleAsync(x => x.DisplayName == "Sibling");
            var row = await Db.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == member.Id);
            row.IsPaused = paused; await Db.SaveChangesAsync();
        }
        public async Task SetSiblingAccountEnabledAsync(bool enabled)
        {
            var member = await Db.FamilyMembers.SingleAsync(x => x.DisplayName == "Sibling");
            var row = await Db.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == member.Id);
            row.HasSeparateAccount = enabled; await Db.SaveChangesAsync();
        }

        public Task<HttpResponseMessage> Get(Guid actor, string path, string language = "en")
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Fixture-Actor", actor.ToString());
            request.Headers.Add("Accept-Language", language);
            return _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); await Db.DisposeAsync(); }
    }

    public sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Fixture-Actor"].ToString())], "fixture");
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), "fixture")));
        }
    }
}
