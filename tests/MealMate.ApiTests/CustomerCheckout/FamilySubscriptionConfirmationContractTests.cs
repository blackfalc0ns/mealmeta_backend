using System.Reflection;
using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class FamilySubscriptionConfirmationContractTests
{
    [Fact]
    public void Confirmation_route_requires_authentication_and_uses_requested_path()
    {
        var controller = typeof(CustomerFamilySubscriptionFlowController);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/v1/customer/family-subscription-flow", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("confirmation", controller.GetMethod("Confirmation")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_passes_authenticated_customer_and_optional_batch_to_query(bool explicitBatch)
    {
        var userId = Guid.NewGuid();
        Guid? batchId = explicitBatch ? Guid.NewGuid() : null;
        var sender = Substitute.For<ISender>();
        var payload = Payload();
        sender.Send(Arg.Any<GetFamilySubscriptionConfirmationQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(payload));
        using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var controller = CreateController(userId, UserType.Customer, services);

        var result = Assert.IsType<OkObjectResult>(await controller.Confirmation(batchId, default));

        Assert.Same(payload, result.Value);
        await sender.Received(1).Send(
            Arg.Is<GetFamilySubscriptionConfirmationQuery>(q => q.CallerUserId == userId && q.BatchId == batchId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Non_customer_is_forbidden_before_reading_confirmation()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var controller = CreateController(Guid.NewGuid(), (UserType)(-1), services);
        Assert.IsType<ForbidResult>(await controller.Confirmation(null, default));
    }

    [Fact]
    public async Task Missing_customer_id_is_forbidden()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var controller = CreateController(null, UserType.Customer, services);
        Assert.IsType<ForbidResult>(await controller.Confirmation(null, default));
    }

    [Fact]
    public async Task Unverified_payment_maps_to_http_conflict()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetFamilySubscriptionConfirmationQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<FamilySubscriptionConfirmationDto>(Error.Conflict("family.confirmation_not_ready")));
        using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var controller = CreateController(Guid.NewGuid(), UserType.Customer, services);
        var result = Assert.IsType<ObjectResult>(await controller.Confirmation(null, default));
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public void Serialized_response_preserves_invoice_amount_arabic_fields_and_iso_dates()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        MealMateJsonSerializer.Configure(options);
        var json = JsonSerializer.SerializeToElement(Payload(), options);

        Assert.Equal(77.97m, json.GetProperty("totalAmountPaid").GetDecimal());
        Assert.Equal("KWD", json.GetProperty("currencyCode").GetString());
        Assert.Equal("2026-10-22T04:50:24Z", json.GetProperty("paymentVerifiedAtUtc").GetString());
        var manager = json.GetProperty("members")[0];
        Assert.Equal("Manager", manager.GetProperty("role").GetString());
        Assert.True(manager.GetProperty("isManager").GetBoolean());
        Assert.Equal("خسارة الوزن", manager.GetProperty("programNameAr").GetString());
        Assert.Equal("ريوق وغداء", manager.GetProperty("bundleNameAr").GetString());
        Assert.Equal("أساسي", manager.GetProperty("tierNameAr").GetString());
        Assert.Equal("2026-10-26", manager.GetProperty("startDate").GetString());
        Assert.Equal("2026-11-01", manager.GetProperty("endDate").GetString());
    }

    private static CustomerFamilySubscriptionFlowController CreateController(Guid? userId, UserType type, IServiceProvider services)
    {
        var current = Substitute.For<ICurrentUser>();
        current.UserId.Returns(userId);
        current.UserType.Returns(type);
        return new CustomerFamilySubscriptionFlowController(current, Substitute.For<IFamilySubscriptionFlowService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
    }

    private static FamilySubscriptionConfirmationDto Payload() => new(
        Guid.NewGuid(), "عائلة التأكيد", 77.97m, "KWD", new DateTime(2026, 10, 22, 4, 50, 24, DateTimeKind.Utc),
        [new(Guid.NewGuid(), Guid.NewGuid(), "المدير", "Manager", true,
            "خسارة الوزن", "ريوق وغداء", "أساسي", 6, new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1))]);
}
