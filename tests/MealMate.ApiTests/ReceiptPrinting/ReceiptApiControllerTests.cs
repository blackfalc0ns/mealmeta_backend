using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ReceiptPrinting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.ReceiptPrinting;

public class ReceiptApiControllerTests
{
    private readonly IReceiptPrintingService _receiptService = Substitute.For<IReceiptPrintingService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    [Fact]
    public async Task AdminController_GetAll_ReturnsTemplates()
    {
        var expected = new List<ReceiptTemplateDto>
        {
            new(Guid.NewGuid(), "classic-minimal", "كلاسيكي", "Classic", null, null, true, true, true, true, 1, "{}", DateTime.UtcNow)
        };
        _receiptService.GetAllTemplatesForAdminAsync(Arg.Any<CancellationToken>())
            .Returns(expected);

        var controller = new AdminReceiptTemplatesController(_receiptService);
        var result = await controller.GetAll(CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task AdminController_GetById_WhenNotFound_ReturnsNotFound()
    {
        _receiptService.GetTemplateByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ReceiptTemplateDto?)null);

        var controller = new AdminReceiptTemplatesController(_receiptService);
        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task AdminController_UpdateConfiguration_DelegatesToService()
    {
        var templateId = Guid.NewGuid();
        var updatedDto = new ReceiptTemplateDto(templateId, "classic-minimal", "كلاسيكي", "Classic", null, null, true, true, true, true, 2, "{\"color\":\"#000\"}", DateTime.UtcNow);
        _receiptService.UpdateConfigurationAsync(templateId, "{\"color\":\"#000\"}", Arg.Any<CancellationToken>())
            .Returns(Result<ReceiptTemplateDto>.Success(updatedDto));

        var controller = new AdminReceiptTemplatesController(_receiptService);
        var result = await controller.UpdateConfiguration(templateId, new UpdateReceiptTemplateConfigRequest("{\"color\":\"#000\"}"), CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(updatedDto);
    }

    [Fact]
    public async Task AdminController_SetGlobalDefault_DelegatesToService()
    {
        var templateId = Guid.NewGuid();
        _receiptService.SetGlobalDefaultTemplateAsync(templateId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var controller = new AdminReceiptTemplatesController(_receiptService);
        var result = await controller.SetGlobalDefault(templateId, null, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RestaurantController_WhenNotAssociatedWithRestaurant_ReturnsForbid()
    {
        _currentUser.RestaurantId.Returns((Guid?)null);

        var controller = new RestaurantReceiptController(_receiptService, _currentUser);
        var result = await controller.GetAllowedTemplates(CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RestaurantController_GetAllowedTemplates_WhenAuthorized_ReturnsTemplates()
    {
        var restaurantId = Guid.NewGuid();
        _currentUser.RestaurantId.Returns(restaurantId);

        var expected = new List<ReceiptTemplateDto>
        {
            new(Guid.NewGuid(), "classic-minimal", "كلاسيكي", "Classic", null, null, true, true, true, true, 1, "{}", DateTime.UtcNow)
        };
        _receiptService.GetAllowedTemplatesForRestaurantAsync(restaurantId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var controller = new RestaurantReceiptController(_receiptService, _currentUser);
        var result = await controller.GetAllowedTemplates(CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task RestaurantController_SelectTemplate_DelegatesToService()
    {
        var restaurantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        _currentUser.RestaurantId.Returns(restaurantId);

        var settingsDto = new RestaurantReceiptSettingsDto(restaurantId, templateId, "organic-elegant", null, null, true, templateId, "organic-elegant");
        _receiptService.RestaurantSelectTemplateAsync(restaurantId, templateId, Arg.Any<CancellationToken>())
            .Returns(Result<RestaurantReceiptSettingsDto>.Success(settingsDto));

        var controller = new RestaurantReceiptController(_receiptService, _currentUser);
        var result = await controller.SelectTemplate(new SelectRestaurantTemplateRequest(templateId), CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(settingsDto);
    }

    [Fact]
    public async Task RestaurantController_LogPrint_InvokesAuditLog()
    {
        var restaurantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _currentUser.RestaurantId.Returns(restaurantId);
        _currentUser.UserId.Returns(userId);

        _receiptService.LogPrintAsync(orderId, restaurantId, templateId, 1, userId, Arg.Any<string?>(), "Success", "Test", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var controller = new RestaurantReceiptController(_receiptService, _currentUser);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.LogPrint(orderId, new LogReceiptPrintRequest(orderId, templateId, 1, "Success", "Test"), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await _receiptService.Received(1).LogPrintAsync(orderId, restaurantId, templateId, 1, userId, Arg.Any<string?>(), "Success", "Test", Arg.Any<CancellationToken>());
    }
}
