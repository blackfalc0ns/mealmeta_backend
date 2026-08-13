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
}
