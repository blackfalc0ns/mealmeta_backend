using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
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

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ConfirmSubscriptionPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(ResultDto())));

        var controller = CreateController(db, protector, paymentService, sender);
        controller.Request.Headers["MyFatoorah-Signature"] = "sig";

        var payload = new MyFatoorahWebhookPayload(
            "PaymentStatusChanged",
            new MyFatoorahWebhookData(
                "PAY-123",
                "INV-123",
                "SUCCESS",
                12.5m,
                "6f9b5e7b-3c3e-4fd1-8f4a-0d7c3f7e9f11",
                "KNET"));
        var body = JsonSerializer.SerializeToElement(payload);

        var result = await controller.HandleWebhook(body, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        paymentService.Received(1).VerifyWebhookSignature(
            body.GetRawText(),
            "sig",
            "whsec_valid_secret");
        await sender.Received(1).Send(
            Arg.Is<ConfirmSubscriptionPaymentCommand>(cmd =>
                cmd.SubscriptionId == Guid.Parse("6f9b5e7b-3c3e-4fd1-8f4a-0d7c3f7e9f11") &&
                cmd.PaymentReference == "PAY-123"),
            Arg.Any<CancellationToken>());
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

        var sender = Substitute.For<ISender>();
        var controller = CreateController(db, protector, paymentService, sender);
        controller.Request.Headers["MyFatoorah-Signature"] = "bad-sig";

        var payload = new MyFatoorahWebhookPayload(
            "PaymentStatusChanged",
            new MyFatoorahWebhookData(
                "PAY-123",
                "INV-123",
                "SUCCESS",
                12.5m,
                "6f9b5e7b-3c3e-4fd1-8f4a-0d7c3f7e9f11",
                "KNET"));

        var result = await controller.HandleWebhook(JsonSerializer.SerializeToElement(payload), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await sender.DidNotReceive().Send(
            Arg.Any<ConfirmSubscriptionPaymentCommand>(),
            Arg.Any<CancellationToken>());
    }

    private static MyFatoorahWebhookController CreateController(
        AppDbContext db,
        IEmailSecretProtector protector,
        IMyFatoorahPaymentService paymentService,
        ISender sender)
    {
        var services = new ServiceCollection()
            .AddSingleton(sender)
            .BuildServiceProvider();

        return new MyFatoorahWebhookController(db, protector, paymentService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
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
