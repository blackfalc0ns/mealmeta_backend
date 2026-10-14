using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Interceptors;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class FamilyCheckoutHttpContractTests
{
    [Fact]
    public async Task Checkout_contract_gate_is_bound_localized_and_metadata_is_serialized_over_http()
    {
        await using var fx = await Fixture.Create();

        using (var metadataResponse = await fx.Get("metadata", "en"))
        {
            Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
            var metadata = await metadataResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(FamilyInvoiceGate.SupportedContractVersion, metadata.GetProperty("supportedContractVersion").GetInt32());
            Assert.Equal(1, metadata.GetProperty("minimumInvoiceContractVersion").GetInt32());
        }

        var arabic = await fx.PostCheckout("ar", "\"http-key\"");
        var english = await fx.PostCheckout("en", "\"http-key\"", clientContractVersion: 0);
        var arabicAgain = await fx.PostCheckout("ar", "\"http-key\"");

        var arBody = await AssertGateResponse(arabic);
        var enBody = await AssertGateResponse(english);
        var arAgainBody = await AssertGateResponse(arabicAgain);
        Assert.Equal(arBody.GetProperty("detail").GetString(), arAgainBody.GetProperty("detail").GetString());
        Assert.NotEqual(arBody.GetProperty("detail").GetString(), enBody.GetProperty("detail").GetString());
        Assert.DoesNotContain("family.client_update_required", arBody.GetProperty("detail").GetString());

        var supported = await fx.PostCheckout("en", "\"http-key\"", clientContractVersion: 1);
        Assert.Equal(HttpStatusCode.Conflict, supported.StatusCode);
        var supportedBody = await supported.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("family.payment_method_unavailable", supportedBody.GetProperty("code").GetString());

        Assert.Equal(new int?[] { null, 0, null, 1 }, fx.CapturedRequests.Select(x => x.ClientContractVersion));
        Assert.All(fx.CapturedRequests, request => Assert.Equal("http-key", request.IdempotencyKey));
        Assert.Equal(1, fx.PaymentMethodsReceived);
        Assert.Equal(FamilyCheckoutBatchStatus.Draft, (await fx.Db.FamilyCheckoutBatches.SingleAsync()).Status);
        Assert.Equal(string.Empty, (await fx.Db.FamilyCheckoutBatches.SingleAsync()).IdempotencyKey);
        Assert.Empty(await fx.Db.Subscriptions.ToListAsync());
        Assert.Empty(await fx.Db.CapacityHolds.ToListAsync());

        arabic.Dispose(); english.Dispose(); arabicAgain.Dispose(); supported.Dispose();
    }

    private static async Task<JsonElement> AssertGateResponse(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Conflict,
            $"Expected conflict but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("family.client_update_required", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        return body;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        public AppDbContext Db { get; }
        public List<FamilyStartCheckoutRequest> CapturedRequests { get; } = [];
        public int PaymentMethodsReceived { get; private set; }
        private readonly Guid _managerId = Guid.NewGuid();

        private Fixture(AppDbContext db, Guid managerId)
            => (Db, _managerId) = (db, managerId);

        public static async Task<Fixture> Create()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
            var fx = new Fixture(db, Guid.NewGuid());
            var manager = fx._managerId;
            var program = new MealMate.Domain.ProgramsSubscriptions.Program
            {
                Code = "HTTP-P", NameAr = "برنامج", NameEn = "Program", IsActive = true, Status = ProgramStatus.Active,
            };
            var bundle = new Bundle
            {
                ProgramId = program.Id, Program = program, Code = "HTTP-B", NameAr = "باقة", NameEn = "Bundle",
                MealsPerDay = 1, IsActive = true, Status = ProgramStatus.Active,
            };
            var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "HTTP family", Status = FamilyGroupStatus.Draft };
            var member = new FamilyMember
            {
                FamilyGroupId = group.Id, FamilyGroup = group, Role = FamilyMemberRole.Manager, DisplayName = "Manager",
                RelationshipCode = "self", NutritionalGoalCode = "maintain", AgeYears = 30,
                SetupStatus = FamilyMemberSetupStatus.Ready, ProgramId = program.Id, BundleId = bundle.Id,
                SelectedTier = RestaurantTier.Basic, DurationDays = 20, StartDate = new DateOnly(2026, 11, 1),
            };
            group.Members.Add(member);
            var batch = new FamilyCheckoutBatch
            {
                FamilyGroupId = group.Id, FamilyGroup = group, PayerCustomerUserId = manager, IdempotencyKey = "",
                Status = FamilyCheckoutBatchStatus.Draft, Purpose = "Initial", CurrencyCode = "KWD",
            };
            batch.Lines.Add(new FamilyCheckoutBatchLine
            {
                BatchId = batch.Id, Batch = batch, FamilyMemberId = member.Id, FamilyMember = member, CheckoutSelected = true,
                ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic, DurationDays = 20,
                StartDate = member.StartDate, CurrencyCode = "KWD",
            });
            db.AddRange(program, bundle, group, member, batch,
                new CustomerProfile { UserId = manager, AllergiesCompletedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var quote = Substitute.For<IFamilyQuoteService>();
            quote.QuoteAsync(Arg.Any<FamilyGroup>(), Arg.Any<CancellationToken>(), Arg.Any<FamilyQuoteScope>())
                .Returns(Result.Success(new FamilyQuoteResult(
                    [new FamilyQuoteLine(member.Id, "Manager", 20m, 0m, 0m, 0m)], 20m, 0m, 0m, 20m, "KWD", false,
                    Guid.NewGuid(), 1, 4, 1, RequiredParticipantCount: 1)));
            var payments = Substitute.For<IPaymentGatewayService>();
            payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>())
                .Returns(_ => { fx.PaymentMethodsReceived++; return Task.FromResult<IReadOnlyList<PaymentGatewayMethod>>([]); });
            var pricing = Substitute.For<IFamilyPricingSettingsReader>();
            pricing.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
            var invoiceOptions = Options.Create(new FamilyInvoiceOptions { MinimumContractVersion = 1 });

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(CustomerFamilySubscriptionFlowController).Assembly);
            builder.Services.AddMealMateLocalization();
            builder.Services.AddSingleton<IAppDbContext>(db);
            builder.Services.AddSingleton(db);
            builder.Services.AddSingleton<IDomainEventDispatcher>(Substitute.For<IDomainEventDispatcher>());
            builder.Services.AddSingleton<IUserAccountService>(Substitute.For<IUserAccountService>());
            builder.Services.AddSingleton<IFamilyQuoteService>(quote);
            builder.Services.AddSingleton<IFamilyPricingSettingsReader>(pricing);
            builder.Services.AddSingleton(invoiceOptions);
            builder.Services.AddSingleton(Substitute.For<IDateTimeProvider>());
            builder.Services.AddSingleton(Substitute.For<IFamilyPlanEligibilityService>());
            builder.Services.AddSingleton(Substitute.For<ICustomerAddressBook>());
            builder.Services.AddSingleton(Substitute.For<IFamilyPlanEligibilityService>());
            builder.Services.AddSingleton(Substitute.For<IRestaurantEligibilityService>());
            builder.Services.AddSingleton(Substitute.For<ICapacityConcurrencyLock>());
            builder.Services.AddSingleton(Substitute.For<IRestaurantPoolSnapshotService>());
            builder.Services.AddSingleton(Substitute.For<IRestaurantCapacityReader>());
            var locks = Substitute.For<IApplicationLockManager>();
            locks.LockFamilyGroupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            builder.Services.AddSingleton(locks);
            builder.Services.AddSingleton<IApplicationLockManager>(locks);
            builder.Services.AddSingleton(Substitute.For<ISubscriptionScheduleService>());
            builder.Services.AddSingleton(Substitute.For<ISubscriptionFlowQueryService>());
            builder.Services.AddSingleton<IPaymentGatewayService>(payments);
            builder.Services.AddSingleton<ICheckoutTransactionExecutor, InlineCheckoutTransactionExecutor>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser>(services =>
            {
                var id = services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var current = Substitute.For<ICurrentUser>();
                current.UserId.Returns(Guid.Parse(id!));
                current.UserType.Returns(UserType.Customer);
                return current;
            });
            builder.Services.AddScoped<ISender>(services =>
            {
                var current = services.GetRequiredService<ICurrentUser>();
                var handlers = new FamilyDraftHandlers(db, current, Substitute.For<IUserAccountService>(), pricing,
                    Substitute.For<IFamilyPlanEligibilityService>(), quote, Substitute.For<IDateTimeProvider>(),
                    Substitute.For<ICustomerAddressBook>(), invoiceOptions: invoiceOptions);
                return new MetadataSender(handlers);
            });
            builder.Services.AddScoped<IFamilyCheckoutService>(services =>
            {
                var method = payments;
                var real = new FamilyCheckoutService(db, services.GetRequiredService<ICurrentUser>(),
                    services.GetRequiredService<IUserAccountService>(), quote, Substitute.For<ISubscriptionScheduleService>(),
                    Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
                    services.GetRequiredService<ICheckoutTransactionExecutor>(), Substitute.For<IRestaurantPoolSnapshotService>(),
                    method, services.GetRequiredService<ISender>(), Substitute.For<IDateTimeProvider>(),
                    Substitute.For<IRestaurantCapacityReader>(), Substitute.For<IApplicationLockManager>(),
                    familyPricing: pricing, invoiceOptions: invoiceOptions);
                return new CapturingCheckoutService(real, fx.CapturedRequests);
            });
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Checkout:AllowedRedirectSchemes:0"] = "https",
                ["Checkout:AllowedRedirectHosts:0"] = "callback.test",
                ["Checkout:AllowedRedirectHosts:1"] = "error.test",
            }).Build();
            builder.Services.AddScoped<IFamilySubscriptionFlowService>(services => new FamilySubscriptionFlowService(
                services.GetRequiredService<ISender>(), db, payments, services.GetRequiredService<IFamilyCheckoutService>(),
                services.GetRequiredService<ISubscriptionFlowQueryService>(), configuration));
            builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseMealMateLocalization();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            fx._app = app;
            fx._client = app.GetTestClient();
            return fx;
        }

        public Task<HttpResponseMessage> Get(string path, string language)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/family-subscription-flow/" + path);
            SetHeaders(request, language);
            return _client.SendAsync(request);
        }

        public Task<HttpResponseMessage> PostCheckout(string language, string idempotencyKeyJson, int? clientContractVersion = null)
        {
            var version = clientContractVersion.HasValue ? $",\"clientContractVersion\":{clientContractVersion.Value}" : string.Empty;
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customer/family-subscription-flow/checkout")
            {
                Content = new StringContent($"{{\"callbackUrl\":\"https://callback.test\",\"errorUrl\":\"https://error.test\",\"idempotencyKey\":{idempotencyKeyJson}{version}}}",
                    System.Text.Encoding.UTF8, "application/json"),
            };
            SetHeaders(request, language);
            return _client.SendAsync(request);
        }

        private void SetHeaders(HttpRequestMessage request, string language)
        {
            request.Headers.Add("X-Fixture-Actor", _managerId.ToString());
            request.Headers.Add("Accept-Language", language);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    private sealed class CapturingCheckoutService(IFamilyCheckoutService inner, List<FamilyStartCheckoutRequest> requests) : IFamilyCheckoutService
    {
        public Task<Result<FamilyCheckoutStartedDto>> StartAsync(FamilyStartCheckoutRequest request, CancellationToken cancellationToken = default)
        {
            requests.Add(request);
            return inner.StartAsync(request, cancellationToken);
        }

        public Task<Result<FamilyCheckoutStartedDto>> StartRenewalAsync(Guid renewalRequestId, FamilyRenewalCheckoutRequest request, CancellationToken cancellationToken = default)
            => inner.StartRenewalAsync(renewalRequestId, request, cancellationToken);
    }

    private sealed class InlineCheckoutTransactionExecutor : ICheckoutTransactionExecutor
    {
        public bool HasActiveTransaction => false;
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private sealed class MetadataSender(FamilyDraftHandlers handlers) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetFamilyDraftMetadataQuery metadata)
                return (TResponse)(object)await handlers.Handle(metadata, cancellationToken);
            throw new NotSupportedException(request.GetType().Name);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Fixture-Actor"].ToString())], "fixture");
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), "fixture")));
        }
    }
}
