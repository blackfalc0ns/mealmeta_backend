using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}

