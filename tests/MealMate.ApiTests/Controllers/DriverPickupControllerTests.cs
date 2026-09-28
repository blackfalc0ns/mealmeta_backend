using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class DriverPickupControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization_And_HasNoAllowAnonymous()
    {
        var authAttr = typeof(DriverPickupController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();

        var allowAnon = typeof(DriverPickupController).GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().BeNull();
    }

    [Fact]
    public void Endpoints_HaveCorrectRoutes()
    {
        var validateMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.ValidateBarcode));
        validateMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("validate-barcode");

        var uploadMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.UploadConditionPhoto));
        uploadMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("boxes/{boxId:guid}/condition-photo");

        var confirmMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.ConfirmPickup));
        confirmMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("boxes/{boxId:guid}/confirm");

        var summaryMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.GetPickupSummary));
        var summaryAttrs = summaryMethod!.GetCustomAttributes<HttpGetAttribute>().Select(a => a.Template).ToList();
        summaryAttrs.Should().Contain("/api/v1/driver/trips/{tripId:guid}/pickup-summary");
        summaryAttrs.Should().Contain("trips/{tripId:guid}/pickup-summary");

        var startMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.StartTrip));
        var startAttrs = startMethod!.GetCustomAttributes<HttpPostAttribute>().Select(a => a.Template).ToList();
        startAttrs.Should().Contain("/api/v1/driver/trips/{tripId:guid}/start");
        startAttrs.Should().Contain("trips/{tripId:guid}/start");

        // Verify legacy start-trip method is completely deleted from DriverPickupController
        var legacyStartMethod = typeof(DriverPickupController).GetMethod("StartTripDelivery");
        legacyStartMethod.Should().BeNull();

        // Verify legacy PickupBox method is completely deleted from DriverHomeController
        var legacyPickupBox = typeof(DriverHomeController).GetMethod("PickupBox");
        legacyPickupBox.Should().BeNull();
    }

    [Fact]
    public void ConfirmPickup_And_StartTrip_HaveIdempotencyKeyHeaderParameter()
    {
        var confirmMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.ConfirmPickup));
        var confirmParam = confirmMethod!.GetParameters().FirstOrDefault(p => p.Name == "idempotencyKey");
        confirmParam.Should().NotBeNull();
        var confirmHeaderAttr = confirmParam!.GetCustomAttribute<FromHeaderAttribute>();
        confirmHeaderAttr.Should().NotBeNull();
        confirmHeaderAttr!.Name.Should().Be("Idempotency-Key");

        var startMethod = typeof(DriverPickupController).GetMethod(nameof(DriverPickupController.StartTrip));
        var startParam = startMethod!.GetParameters().FirstOrDefault(p => p.Name == "idempotencyKey");
        startParam.Should().NotBeNull();
        var startHeaderAttr = startParam!.GetCustomAttribute<FromHeaderAttribute>();
        startHeaderAttr.Should().NotBeNull();
        startHeaderAttr!.Name.Should().Be("Idempotency-Key");
    }

    [Fact]
    public async Task ValidateBarcode_ReturnsExpectedContractProperties()
    {
        var sender = Substitute.For<MediatR.ISender>();
        var boxId = Guid.NewGuid();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        var boxDto = new ValidatePickupBoxDto(boxId, "#BX-999", "Ahmed", "Riyadh", 1);
        var responseDto = new ValidatePickupBarcodeResponseDto(
            IsValid: true,
            ValidationToken: "secret_token_123",
            ExpiresAtUtc: expiresAtUtc,
            Box: boxDto,
            ScanStatus: "BarcodeValidated",
            NextAction: "CaptureConditionPhoto");

        sender.Send(Arg.Any<ValidatePickupBarcodeCommand>(), Arg.Any<CancellationToken>())
            .Returns(MealMate.Application.Common.Models.Result.Success(responseDto));

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(sender);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var controller = new DriverPickupController(
            Substitute.For<Microsoft.AspNetCore.SignalR.IHubContext<MealMate.Api.Realtime.DispatcherHub>>(),
            Substitute.For<Microsoft.AspNetCore.SignalR.IHubContext<MealMate.Api.Realtime.DriverHub>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.ValidateBarcode(new ValidatePickupBarcodeRequest("MM-20260928-1234"), CancellationToken.None);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("boxId", out _).Should().BeTrue("Contract requires 'boxId' on root");
        root.TryGetProperty("orderCode", out _).Should().BeTrue("Contract requires 'orderCode' on root");
        root.TryGetProperty("validationToken", out _).Should().BeTrue("Contract requires 'validationToken' on root");
        root.TryGetProperty("expiresAtUtc", out _).Should().BeTrue("Contract requires 'expiresAtUtc' on root");
    }
}

