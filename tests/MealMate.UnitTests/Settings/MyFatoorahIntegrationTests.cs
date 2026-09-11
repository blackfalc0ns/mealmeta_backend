using MealMate.Application.Features.Settings;
using MealMate.Application.Common.Abstractions;
using NSubstitute;
using Xunit;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MealMate.UnitTests.Settings;

public class MyFatoorahIntegrationTests
{
    [Fact]
    public void VerifyWebhookSignature_ValidHMAC_ReturnsTrue()
    {
        var secret = "test_secret_key_123";
        var payload = "{\"Event\":\"PaymentStatusChanged\",\"Data\":{\"PaymentId\":\"1001\"}}";

        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var validSig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload)));

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
        using var hmac2 = new System.Security.Cryptography.HMACSHA256(keyBytes);
        var computedSignature = Convert.ToBase64String(hmac2.ComputeHash(payloadBytes));

        Assert.Equal(validSig, computedSignature);
    }

    [Fact]
    public async Task TestMyFatoorahConnectionCommandHandler_ValidToken_ReturnsSuccess()
    {
        var paymentService = Substitute.For<IMyFatoorahPaymentService>();
        paymentService.TestConnectionAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var handler = new TestMyFatoorahConnectionCommandHandler(paymentService, db, Substitute.For<IEmailSecretProtector>());
        var result = await handler.Handle(new TestMyFatoorahConnectionCommand("rAT8_test_token", true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task UpdateMyFatoorahSettings_EncryptsCredentialsAndReturnsReadyConfiguration()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var protector = Substitute.For<IEmailSecretProtector>();
        protector.Encrypt("live-token").Returns("encrypted-token");
        protector.Encrypt("webhook-secret").Returns("encrypted-secret");
        var handler = new UpdateMyFatoorahSettingsCommandHandler(db, protector);

        var result = await handler.Handle(new UpdateMyFatoorahSettingsCommand(
            false,
            "https://untrusted.example",
            "live-token",
            "webhook-secret",
            true,
            true,
            true,
            "KWD",
            "KW"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsReady);
        Assert.Equal("https://api.myfatoorah.com", result.Value.BaseUrl);
        Assert.Equal("KWT", result.Value.CountryCode);
        Assert.Equal("https://maelmate.runasp.net/api/v1/webhooks/myfatoorah", result.Value.WebhookUrl);
        var stored = await db.MyFatoorahGatewayConfigs.SingleAsync();
        Assert.Equal("encrypted-token", stored.EncryptedApiToken);
        Assert.Equal("encrypted-secret", stored.EncryptedWebhookSecret);
        Assert.DoesNotContain("live-token", result.Value.MaskedApiToken);
        Assert.DoesNotContain("webhook-secret", result.Value.MaskedWebhookSecret);
    }

    [Fact]
    public async Task MyFatoorahPaymentService_GetPaymentMethodsAsync_WhenMockEnabled_ReturnsMockMethods()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var config = Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();
        var allowMockSection = Substitute.For<Microsoft.Extensions.Configuration.IConfigurationSection>();
        allowMockSection.Value.Returns("true");
        config.GetSection("Payments:AllowMockPaymentMethods").Returns(allowMockSection);
        config["Payments:AllowMockPaymentMethods"].Returns("true");

        var publicBaseSection = Substitute.For<Microsoft.Extensions.Configuration.IConfigurationSection>();
        publicBaseSection.Value.Returns("https://maelmate.runasp.net");
        config.GetSection("Payments:PublicBaseUrl").Returns(publicBaseSection);
        config["Payments:PublicBaseUrl"].Returns("https://maelmate.runasp.net");

        var service = new MealMate.Infrastructure.Services.MyFatoorahPaymentService(
            new HttpClient(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<MealMate.Infrastructure.Services.MyFatoorahPaymentService>>(),
            db,
            Substitute.For<IEmailSecretProtector>(),
            config);

        var methods = await service.GetPaymentMethodsAsync(15m);

        Assert.NotEmpty(methods);
        Assert.Contains(methods, m => m.Code == "KNET");
        Assert.Contains(methods, m => m.Code == "VM");
        Assert.Contains(methods, m => m.Code == "AP");
    }

    [Fact]
    public async Task MyFatoorahPaymentService_ExecuteAndStatus_WhenMockEnabled_ReturnsValidFlow()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var config = Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();
        var allowMockSection = Substitute.For<Microsoft.Extensions.Configuration.IConfigurationSection>();
        allowMockSection.Value.Returns("true");
        config.GetSection("Payments:AllowMockPaymentMethods").Returns(allowMockSection);
        config["Payments:AllowMockPaymentMethods"].Returns("true");

        var publicBaseSection = Substitute.For<Microsoft.Extensions.Configuration.IConfigurationSection>();
        publicBaseSection.Value.Returns("https://maelmate.runasp.net");
        config.GetSection("Payments:PublicBaseUrl").Returns(publicBaseSection);
        config["Payments:PublicBaseUrl"].Returns("https://maelmate.runasp.net");

        var service = new MealMate.Infrastructure.Services.MyFatoorahPaymentService(
            new HttpClient(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<MealMate.Infrastructure.Services.MyFatoorahPaymentService>>(),
            db,
            Substitute.For<IEmailSecretProtector>(),
            config);

        var subId = Guid.NewGuid().ToString();
        var paymentUrl = await service.ExecutePaymentAsync(
            25m,
            "Fatima Al-Sabah",
            "fatima@example.com",
            "50000001",
            "mealmate://checkout/success",
            "mealmate://checkout/error",
            customerReference: subId,
            paymentMethodId: 1);

        Assert.Contains("/api/v1/payments/myfatoorah/return", paymentUrl);
        Assert.Contains("paymentId=MOCK-PAY-", paymentUrl);
        Assert.Contains("InvoiceId=MOCK-INV-", paymentUrl);

        var uri = new Uri(paymentUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var paymentId = query["paymentId"];
        Assert.NotNull(paymentId);

        var status = await service.GetPaymentStatusAsync(paymentId);
        Assert.NotNull(status);
        Assert.Equal("Paid", status.InvoiceStatus);
        Assert.Equal(25m, status.InvoiceValue);
        Assert.Equal(subId, status.CustomerReference);
        Assert.Equal("KWD", status.CurrencyCode);
    }
}

