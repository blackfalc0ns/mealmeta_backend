using System.Net;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Entities;
using MealMate.Infrastructure.Services;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
namespace MealMate.UnitTests.Settings;
public sealed class PayzahReceiptTests
{
    [Fact]
    public async Task InitializationPersistsExplicitPaymentIdAndUsesBase64PrivateKey()
    {
        await using var fixture = new Fixture();
        fixture.Handler.Response = """{"status":true,"data":{"PaymentID":"creation-id","transit_url":"https://devgateway.trytajer.app?id=hosted"}}""";
        var result = await fixture.Service.ExecutePaymentAsync(12.345m, "customer", "", "", "mealmate://checkout/success", "mealmate://checkout/error", Guid.NewGuid().ToString(), 3);
        Assert.Equal("creation-id", result.PaymentId);
        var attempt = await fixture.Db.PaymentGatewayAttempts.SingleAsync();
        Assert.Equal(result.TrackId, attempt.TrackId);
        Assert.Equal(12.345m, attempt.ExpectedAmount);
        Assert.Equal("KWD", attempt.ExpectedCurrency);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("private-key")), fixture.Handler.Authorization);
        using var body = JsonDocument.Parse(fixture.Handler.Body!);
        Assert.Equal("414", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal("12.345", body.RootElement.GetProperty("amount").GetString());
        Assert.Contains(result.TrackId, body.RootElement.GetProperty("success_url").GetString());
    }
    [Fact]
    public async Task InitiationNormalizesStoredMobileToLocalFormatPayzahAccepts()
    {
        await using var fixture = new Fixture();
        fixture.Handler.Response = """{"status":true,"data":{"PaymentID":"creation-id","transit_url":"https://devgateway.trytajer.app?id=hosted"}}""";
        var receipt = await fixture.Service.ExecutePaymentAsync(12.345m, "customer", "", "+96553473360", "mealmate://checkout/success", "mealmate://checkout/error", Guid.NewGuid().ToString(), 3);
        using var body = JsonDocument.Parse(fixture.Handler.Body!);
        Assert.Equal("53473360", body.RootElement.GetProperty("customer_phone").GetString());

        fixture.Handler.Response = """{"status":true,"data":{"PaymentID":"creation-id","transit_url":"https://devgateway.trytajer.app?id=hosted"}}""";
        await fixture.Service.ExecutePaymentAsync(12.345m, "customer", "", "00965 5347 3360", "mealmate://checkout/success", "mealmate://checkout/error", Guid.NewGuid().ToString(), 3);
        using var second = JsonDocument.Parse(fixture.Handler.Body!);
        Assert.Equal("53473360", second.RootElement.GetProperty("customer_phone").GetString());
    }
    [Fact]
    public async Task UnboundLegacyPaymentDoesNotContactPayzah()
    {
        await using var fixture = new Fixture();
        Assert.Null(await fixture.Service.GetPaymentStatusAsync("legacy-id"));
        Assert.Equal(0, fixture.Handler.Calls);
    }
    [Fact]
    public async Task CapturedStatusRequiresExactPersistedTrackAndReference()
    {
        await using var fixture = new Fixture();
        fixture.Db.PaymentGatewayAttempts.Add(new() { TrackId = "bound-track", PaymentId = "creation-id", CustomerReference = "bound-reference", ExpectedAmount = 12m, IsSandbox = true });
        await fixture.Db.SaveChangesAsync();
        fixture.Handler.Response = """{"status":true,"data":{"trackId":"wrong-track","UDF1":"bound-reference","paymentStatus":"CAPTURED","payzahRefrenceCode":"reference","transactionNumber":"transaction"}}""";
        Assert.Null(await fixture.Service.GetPaymentStatusAsync("pz-bound-track"));
        fixture.Handler.Response = fixture.Handler.Response.Replace("wrong-track", "bound-track");
        var verified = await fixture.Service.GetPaymentStatusAsync("pz-bound-track");
        Assert.Equal("Paid", verified?.InvoiceStatus);
        Assert.Equal(12m, verified?.InvoiceValue);
    }
    [Fact]
    public async Task EnvironmentSwitchRejectsOldAttemptWithoutNetworkRequest()
    {
        await using var fixture = new Fixture();
        fixture.Db.PaymentGatewayAttempts.Add(new() { TrackId = "track", PaymentId = "live-id", CustomerReference = "reference", ExpectedAmount = 12m, IsSandbox = false });
        await fixture.Db.SaveChangesAsync();
        Assert.Null(await fixture.Service.GetPaymentStatusAsync("pz-track"));
        Assert.Equal(0, fixture.Handler.Calls);
    }
    [Fact]
    public async Task HostedRedirectWithFragmentIsRejected()
    {
        await using var fixture = new Fixture();
        fixture.Handler.Response = """{"status":true,"data":{"PaymentID":"creation-id","transit_url":"https://devgateway.trytajer.app#secret"}}""";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ExecutePaymentAsync(12m, "", "", "", "mealmate://checkout/success", "mealmate://checkout/error", "reference", 3));
    }
    [Fact]
    public async Task SamePaymentIdAcrossEnvironmentsResolvesOnlyCurrentMerchantAttempt()
    {
        await using var fixture = new Fixture();
        fixture.Db.PaymentGatewayAttempts.AddRange(
            new() { TrackId = "sandbox-track", PaymentId = "shared-id", CustomerReference = "sandbox-reference", ExpectedAmount = 12m, IsSandbox = true },
            new() { TrackId = "live-track", PaymentId = "shared-id", CustomerReference = "live-reference", ExpectedAmount = 24m, IsSandbox = false });
        await fixture.Db.SaveChangesAsync();
        fixture.Handler.Response = """{"status":true,"data":{"trackId":"sandbox-track","UDF1":"sandbox-reference","paymentStatus":"CAPTURED","payzahRefrenceCode":"reference","transactionNumber":"transaction"}}""";
        var status = await fixture.Service.GetPaymentStatusAsync("pz-sandbox-track");
        Assert.Equal(12m, status?.InvoiceValue);
        Assert.Equal("sandbox-reference", status?.CustomerReference);
    }
    [Fact]
    public async Task RefundUsesVerifiedReferenceAndNeverClaimsFundsReturned()
    {
        await using var fixture = new Fixture();
        fixture.Db.PaymentGatewayAttempts.Add(new() { TrackId = "track", PaymentId = "creation-id", CustomerReference = "reference", ExpectedAmount = 12m, IsSandbox = true });
        await fixture.Db.SaveChangesAsync();
        fixture.Handler.ResponseFactory = request => request.RequestUri!.AbsolutePath.EndsWith("/refund")
            ? """{"status":1}"""
            : """{"status":true,"data":{"trackId":"track","UDF1":"reference","paymentStatus":"CAPTURED","payzahRefrenceCode":"verified-reference","transactionNumber":"transaction"}}""";
        Assert.Null(await fixture.Service.MakeRefundAsync("pz-track", 5m, "KWD", "operation", "requested refund"));
        using var payload = JsonDocument.Parse(fixture.Handler.Body!);
        Assert.Equal("verified-reference", payload.RootElement.GetProperty("refrence_code").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("refund_type").GetInt32());
        Assert.Equal("5.000", payload.RootElement.GetProperty("amount").GetString());
        Assert.Equal(2, fixture.Handler.Calls);
    }
    [Fact]
    public async Task RefundRejectsUnboundHistoricalPaymentBeforeContactingProvider()
    {
        await using var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.MakeRefundAsync("legacy", 5m, "KWD", "operation", "reason"));
        Assert.Equal(0, fixture.Handler.Calls);
    }
    [Fact]
    public async Task RefundAfterEnvironmentSwitchCannotTargetCollidingProviderPaymentId()
    {
        await using var fixture = new Fixture();
        fixture.Db.PaymentGatewayAttempts.AddRange(
            new() { TrackId = "sandbox-track", PaymentId = "shared-id", CustomerReference = "sandbox-reference", ExpectedAmount = 12m, IsSandbox = true },
            new() { TrackId = "live-track", PaymentId = "shared-id", CustomerReference = "live-reference", ExpectedAmount = 12m, IsSandbox = false });
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.MakeRefundAsync("pz-live-track", 5m, "KWD", "operation", "reason"));
        Assert.Equal(0, fixture.Handler.Calls);
    }
    [Fact]
    public async Task ProviderFallbackRedirectAppendsOriginalCaseSensitivePaymentId()
    {
        await using var fixture = new Fixture();
        fixture.Handler.Response = """{"status":true,"data":{"PaymentID":"original-id","PaymentUrl":"https://development.payzah.net/pgaction"}}""";
        var receipt = await fixture.Service.ExecutePaymentAsync(12m, "", "", "", "", "", "reference", 3);
        Assert.EndsWith("?PaymentID=original-id", receipt.PaymentUrl);
        Assert.Equal("original-id", receipt.PaymentId);
        Assert.Equal("pz-" + receipt.TrackId, receipt.PaymentHandle);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        public ResponseHandler Handler { get; } = new();
        public PayzahPaymentService Service { get; }
        public Fixture()
        {
            var protector = Substitute.For<IEmailSecretProtector>();
            protector.Decrypt("encrypted-key").Returns("private-key");
            Db.PayzahGatewayConfigs.Add(new(true, "https://development.payzah.net/ws/paymentgateway", "encrypted-key", "****")); Db.SaveChanges();
            Service = new(new HttpClient(Handler), NullLogger<PayzahPaymentService>.Instance, Db, protector,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Payments:PublicBaseUrl"] = "https://merchant.mealmate.app" }).Build());
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class ResponseHandler : HttpMessageHandler
    {
        public string Response { get; set; } = "{}";
        public Func<HttpRequestMessage, string>? ResponseFactory { get; set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Authorization = request.Headers.GetValues("Authorization").Single(); Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(ResponseFactory?.Invoke(request) ?? Response, Encoding.UTF8, "application/json") };
        }
    }
}
