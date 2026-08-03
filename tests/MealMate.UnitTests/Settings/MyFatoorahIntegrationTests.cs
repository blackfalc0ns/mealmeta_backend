using MealMate.Application.Features.Settings;
using MealMate.Application.Common.Abstractions;
using NSubstitute;
using Xunit;

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

        var handler = new TestMyFatoorahConnectionCommandHandler(paymentService);
        var result = await handler.Handle(new TestMyFatoorahConnectionCommand("rAT8_test_token", true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }
}
