using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Entities;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.Settings;

public class MyFatoorahWebhookControllerTests
{
    [Fact]
    public async Task HandleWebhook_WithValidSignature_ConfirmsPayment()
    {
        var protector = CreateProtector();
        await using var db = CreateDbContext();
        await SeedConfigAsync(db, protector, "whsec_valid_secret");

        var paymentService = Substitute.For<IMyFatoorahPaymentService>();
        paymentService.VerifyWebhookSignature(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        processor.VerifyAndProcessAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentProcessingResult(true, false, null, MealMate.Domain.Enums.SubscriptionStatus.Active, "Completed"));

        var controller = CreateController(db, protector, paymentService, processor);
        controller.Request.Headers["MyFatoorah-Signature"] = "sig";

        var reference = "6f9b5e7b-3c3e-4fd1-8f4a-0d7c3f7e9f11";
        var payload = PaymentPayload(reference);
        var body = JsonSerializer.SerializeToElement(payload);

        var result = await controller.HandleWebhook(body, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        paymentService.Received(1).VerifyWebhookSignature(
            "Invoice.Id=INV-123,Invoice.Status=PAID,Transaction.Status=SUCCESS,Transaction.PaymentId=PAY-123,Invoice.ExternalIdentifier=",
            "sig",
            "whsec_valid_secret");
        await processor.Received(1).VerifyAndProcessAsync(
            Guid.Parse("6f9b5e7b-3c3e-4fd1-8f4a-0d7c3f7e9f11"),
            "PAY-123", "INV-123", 12.5m, "KWD", reference,
            Arg.Is<string>(x => x.Length == 64), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleWebhook_WithInvalidSignature_ReturnsUnauthorized()
    {
        var protector = CreateProtector();
        await using var db = CreateDbContext();
        await SeedConfigAsync(db, protector, "whsec_valid_secret");

        var paymentService = Substitute.For<IMyFatoorahPaymentService>();
        paymentService.VerifyWebhookSignature(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        var controller = CreateController(db, protector, paymentService, processor);
        controller.Request.Headers["MyFatoorah-Signature"] = "bad-sig";

        var payload = PaymentPayload("6f9b5e7b-3c3e-4fd1-8f4a-0d7c3f7e9f11");

        var result = await controller.HandleWebhook(JsonSerializer.SerializeToElement(payload), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await processor.DidNotReceiveWithAnyArgs().VerifyAndProcessAsync(default, default!, default, default, default, default, default, default);
    }

    private static MyFatoorahWebhookV2Payload PaymentPayload(string reference)
        => new(
            new MyFatoorahWebhookV2Event(1, "PAYMENT_STATUS_CHANGED", "KWT", DateTime.UtcNow, "WH-1"),
            new MyFatoorahWebhookV2Data(
                new MyFatoorahWebhookV2Invoice("INV-123", "PAID", reference, null),
                new MyFatoorahWebhookV2Transaction("SUCCESS", "PAY-123"),
                new MyFatoorahWebhookV2Amount("KWD", "12.5")));

    private static MyFatoorahWebhookController CreateController(
        AppDbContext db,
        IEmailSecretProtector protector,
        IMyFatoorahPaymentService paymentService,
        ISubscriptionPaymentProcessor processor)
    {
        return new MyFatoorahWebhookController(db, protector, paymentService, processor)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider(),
                },
            },
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static IEmailSecretProtector CreateProtector() => new TestEmailSecretProtector();

    private static async Task SeedConfigAsync(AppDbContext db, IEmailSecretProtector protector, string webhookSecret)
    {
        db.MyFatoorahGatewayConfigs.Add(new MyFatoorahGatewayConfig(
            "MyFatoorah",
            true,
            "https://apitest.myfatoorah.com",
            protector.Encrypt("api-token"),
            protector.Encrypt(webhookSecret),
            true,
            true,
            true,
            "KWD",
            "KW",
            "rAT8...****"));

        await db.SaveChangesAsync();
    }

    private static SubscriptionDto ResultDto()
    {
        return new SubscriptionDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Program",
            "Program",
            Guid.NewGuid(),
            "Bundle",
            "Bundle",
            MealMate.Domain.Enums.RestaurantTier.Basic,
            26,
            MealMate.Domain.Enums.SubscriptionStatus.Active,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(26)),
            12.5m,
            "KWD",
            null,
            null,
            DateTime.UtcNow,
            Array.Empty<SubscriptionSnapshotDto>(),
            Array.Empty<SubscriptionOperationDto>());
    }

    private sealed class TestEmailSecretProtector : IEmailSecretProtector
    {
        public string Encrypt(string plaintext) => $"enc::{plaintext}";

        public string Decrypt(string encryptedBase64)
            => encryptedBase64.StartsWith("enc::", StringComparison.Ordinal)
                ? encryptedBase64["enc::".Length..]
                : encryptedBase64;

        public string? MaskLastFour(string? plaintext)
            => string.IsNullOrEmpty(plaintext)
                ? null
                : plaintext.Length <= 4
                    ? "****"
                    : $"****{plaintext[^4..]}";
    }
}
