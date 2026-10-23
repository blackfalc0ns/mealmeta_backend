using System.Globalization;
using System.Security.Claims;
using MealMate.Api.Controllers.V2;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Options;
using MealMate.Application.Features.VoiceCalls.V2.Commands;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.ApiTests.VoiceCalls;

public sealed class VoiceCallAuthExpiryBoundaryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-timestamp")]
    [InlineData("253402300800")]
    [InlineData("-62135596801")]
    public async Task Initiation_RejectsMissingOrInvalidJwtExpiry_BeforeSendingCommand(string? expiry)
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sender = Substitute.For<ISender>();
        using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var controller = CreateController(userId, deviceId, expiry, services);
        var result = await controller.Initiate(new(Guid.NewGuid(), Guid.NewGuid()), default);
        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(sender.ReceivedCalls());
    }

    [Fact]
    public async Task Initiation_ForwardsJwtExpiry_AndRefreshConflictKeepsStableCode()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var unixExpiry = DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<InitiateVoiceCallV2Command>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CallSnapshotDto>(Error.Conflict("calls.auth_refresh_required")));
        using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var controller = CreateController(userId, deviceId, unixExpiry.ToString(CultureInfo.InvariantCulture), services);
        // An untrusted custom header cannot replace the validated JWT claim.
        controller.Request.Headers["X-Access-Token-Expires"] = "253402300799";
        var key = Guid.NewGuid();
        var result = await controller.Initiate(new(Guid.NewGuid(), key), default);
        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, response.StatusCode);
        Assert.Equal("calls.auth_refresh_required", Assert.IsType<ProblemDetails>(response.Value).Extensions["code"]);
        await sender.Received(1).Send(Arg.Is<InitiateVoiceCallV2Command>(c => c.ClientRequestId == key
            && c.Actor.UserId == userId && c.Actor.DeviceSessionId == deviceId
            && c.Actor.AccessTokenExpiresAtUtc == DateTimeOffset.FromUnixTimeSeconds(unixExpiry).UtcDateTime), default);
    }

    private static VoiceCallsV2Controller CreateController(Guid userId, Guid deviceId, string? expiry, IServiceProvider services)
    {
        var sessions = Substitute.For<IVoiceCallDeviceSessionService>();
        sessions.ResolveAsync(userId, deviceId, "test-proof", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CallControlContext(userId, deviceId)));
        var claims = new List<Claim> { new("sub", userId.ToString()) };
        if (expiry is not null) claims.Add(new("exp", expiry));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), RequestServices = services
        };
        context.Request.Headers["X-Voice-Device-Session"] = deviceId.ToString();
        context.Request.Headers["X-Voice-Device-Proof"] = "test-proof";
        return new VoiceCallsV2Controller(Options.Create(new VoiceCallOptions { V2Enabled = true }), sessions)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
