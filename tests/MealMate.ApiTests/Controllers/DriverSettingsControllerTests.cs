using System.Reflection;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class DriverSettingsControllerTests
{
    [Fact]
    public void Controller_RequiresDriverRole_And_HasNoAllowAnonymous()
    {
        var authAttr = typeof(DriverSettingsController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();
        authAttr!.Roles.Should().Be(AppRoles.Driver);

        var allowAnon = typeof(DriverSettingsController).GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().BeNull();
    }

    [Fact]
    public void Endpoints_HaveCorrectRoutes()
    {
        typeof(DriverSettingsController).GetCustomAttribute<RouteAttribute>()!.Template
            .Should().Be("api/v1/driver/settings");

        var get = typeof(DriverSettingsController).GetMethod(nameof(DriverSettingsController.GetSettings));
        get!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();

        var put = typeof(DriverSettingsController).GetMethod(nameof(DriverSettingsController.UpdateSettings));
        put!.GetCustomAttribute<HttpPutAttribute>().Should().NotBeNull();
        put.GetParameters().Single(p => p.Name == "request")
            .GetCustomAttribute<FromBodyAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task GetSettings_SerializesTheScreenContract_CamelCaseWithOfflineSync()
    {
        var response = new DriverAppSettingsResponseDto(
            PreferredLanguage: "ar",
            PreferredNavigationApp: "GoogleMaps",
            AutoLaunchNavigation: true,
            SoundAlertsEnabled: true,
            VibrationEnabled: false,
            BiometricLoginEnabled: null,
            OfflineSync: new DriverAppSettingsOfflineSyncDto(true, null, null));

        var (result, json) = await SendViaController<GetDriverAppSettingsQuery, DriverAppSettingsResponseDto>(
            c => c.GetSettings(CancellationToken.None), response);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.StatusCode.Should().Be(200);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("preferredLanguage").GetString().Should().Be("ar");
        root.GetProperty("preferredNavigationApp").GetString().Should().Be("GoogleMaps");
        root.GetProperty("autoLaunchNavigation").GetBoolean().Should().BeTrue();
        root.GetProperty("soundAlertsEnabled").GetBoolean().Should().BeTrue();
        root.GetProperty("vibrationEnabled").GetBoolean().Should().BeFalse();
        root.GetProperty("biometricLoginEnabled").ValueKind.Should().Be(JsonValueKind.Null);

        var offlineSync = root.GetProperty("offlineSync");
        offlineSync.GetProperty("autoSyncOnReconnect").GetBoolean().Should().BeTrue();
        offlineSync.GetProperty("pendingSyncItemsCount").ValueKind.Should().Be(JsonValueKind.Null);
        offlineSync.GetProperty("lastSyncedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UpdateSettings_PassesTheCommand_AndReturnsReadAfterWritePayload()
    {
        var response = new DriverAppSettingsResponseDto(
            PreferredLanguage: "en",
            PreferredNavigationApp: "Waze",
            AutoLaunchNavigation: null,
            SoundAlertsEnabled: null,
            VibrationEnabled: true,
            BiometricLoginEnabled: null,
            OfflineSync: new DriverAppSettingsOfflineSyncDto(false, null, null));
        UpdateDriverAppSettingsCommand? sentCommand = null;

        var sender = Substitute.For<MediatR.ISender>();
        sender.Send(Arg.Do<MediatR.IRequest<Result<DriverAppSettingsResponseDto>>>(c => sentCommand = c as UpdateDriverAppSettingsCommand),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));

        var result = await Invoke(c => c.UpdateSettings(new UpdateDriverAppSettingsRequest(
                "EN", "waze", AutoLaunchNavigation: null, SoundAlertsEnabled: null,
                VibrationEnabled: true, BiometricLoginEnabled: null,
                new UpdateDriverAppSettingsOfflineSyncRequest(false)), CancellationToken.None), sender);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.StatusCode.Should().Be(200);
        sentCommand.Should().NotBeNull();
        sentCommand!.PreferredLanguage.Should().Be("EN");
        sentCommand.PreferredNavigationApp.Should().Be("waze");
        sentCommand.VibrationEnabled.Should().BeTrue();
        sentCommand.AutoSyncOnReconnect.Should().BeFalse();
    }

    private static async Task<(IActionResult Result, string Json)> SendViaController<TQuery, TResponse>(
        Func<DriverSettingsController, Task<IActionResult>> call, TResponse response)
        where TQuery : MediatR.IRequest<Result<TResponse>>
    {
        var sender = Substitute.For<MediatR.ISender>();
        sender.Send(Arg.Any<MediatR.IRequest<Result<TResponse>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));
        var result = await Invoke(call, sender);
        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return (result, json);
    }

    private static async Task<IActionResult> Invoke(Func<DriverSettingsController, Task<IActionResult>> call, MediatR.ISender sender)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sender);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        var controller = new DriverSettingsController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        return await call(controller);
    }
}

/// <summary>
/// Full-pipeline checks against the real endpoint routing table: the route must be
/// registered (401 from authorization, not the routing 404 that caused the mobile bug).
/// </summary>
public sealed class DriverSettingsEndpointTests : IClassFixture<Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DriverSettingsEndpointTests(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
        }).CreateClient();
    }

    [Fact]
    public async Task Unauthenticated_GetSettings_ReturnsUnauthorized_NotRouting404()
    {
        var response = await _client.GetAsync("/api/v1/driver/settings");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_UpdateSettings_ReturnsUnauthorized_NotRouting404()
    {
        var response = await _client.PutAsJsonAsync("/api/v1/driver/settings", new { });
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
