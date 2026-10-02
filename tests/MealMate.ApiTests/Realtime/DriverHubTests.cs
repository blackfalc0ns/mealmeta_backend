using System.Security.Claims;
using FluentAssertions;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Realtime;

public sealed class DriverHubTests
{
    private static (DriverHub Hub, IClientProxy GroupProxy, ISender Sender) SetupHub(
        bool authenticated,
        ISender? sender = null,
        UpdateDriverLocationResponse? response = null)
    {
        var hubContext = Substitute.For<HubCallerContext>();
        var dispatcherHubContext = Substitute.For<IHubContext<DispatcherHub>>();
        var clients = Substitute.For<IHubClients>();
        var groupProxy = Substitute.For<IClientProxy>();

        dispatcherHubContext.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(groupProxy);
        clients.All.Returns(groupProxy);

        var identity = authenticated
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "TestAuth")
            : new ClaimsIdentity();

        hubContext.User.Returns(new ClaimsPrincipal(identity));
        hubContext.ConnectionId.Returns($"conn-{Guid.NewGuid():N}");
        hubContext.GetHttpContext().Returns(new DefaultHttpContext());

        sender ??= Substitute.For<ISender>();
        if (response is not null)
        {
            sender.Send(Arg.Any<UpdateDriverLocationCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(response));
        }

        var hub = new DriverHub(sender, new DriverLocationBroadcaster(dispatcherHubContext))
        {
            Context = hubContext
        };

        return (hub, groupProxy, sender);
    }

    private static UpdateDriverLocationResponse SuccessfulResponse() => new(
        TrackingPointId: Guid.NewGuid(),
        RecordedAtUtc: DateTime.UtcNow,
        Message: "Driver location updated successfully",
        DriverId: Guid.NewGuid(),
        TripId: Guid.NewGuid(),
        RestaurantId: Guid.NewGuid(),
        Latitude: 29.3375m,
        Longitude: 48.0281m,
        Heading: 90.0,
        SpeedKmh: 35.0,
        LocationZone: "اليرموك",
        RemainingDistanceKm: 1.8,
        RemainingDistanceText: "1.8 كم");

    [Fact]
    public async Task UpdateLocation_AnonymousConnection_ThrowsHubException()
    {
        // Arrange: no access_token was presented on the connection
        var (hub, _, sender) = SetupHub(authenticated: false);

        // Act
        var act = () => hub.UpdateLocation(29.3375m, 48.0281m);

        // Assert: nothing must be sent to the application layer
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*unauthorized*");
        await sender.DidNotReceive().Send(Arg.Any<UpdateDriverLocationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateLocation_AuthenticatedDriver_PersistsViaCommand_AndBroadcastsToDispatchers()
    {
        // Arrange
        var response = SuccessfulResponse();
        var (hub, groupProxy, sender) = SetupHub(authenticated: true, response: response);

        // Act
        var ack = await hub.UpdateLocation(29.3375m, 48.0281m, 90.0, 35.0);

        // Assert: the command always leaves driver identity to the authenticated user
        await sender.Received(1).Send(
            Arg.Is<UpdateDriverLocationCommand>(c =>
                c.DriverId == null &&
                c.Latitude == 29.3375m &&
                c.Longitude == 48.0281m &&
                c.Heading == 90.0 &&
                c.SpeedKmh == 35.0),
            Arg.Any<CancellationToken>());

        // The dispatcher live map receives the update on the restaurant and universal groups
        await groupProxy.Received(2).SendCoreAsync(
            DispatcherHub.EventDriverLocationUpdated,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] != null),
            Arg.Any<CancellationToken>());

        // The caller gets an ack with the resolved ids
        ack.Should().NotBeNull();
        var ackDriverId = (Guid?)ack!.GetType().GetProperty("driverId")!.GetValue(ack)!;
        ackDriverId.Should().Be(response.DriverId);
    }

    [Fact]
    public async Task UpdateLocation_CommandFailure_PropagatesHubException()
    {
        // Arrange: the application layer rejects the update (e.g. zero coordinates)
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<UpdateDriverLocationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UpdateDriverLocationResponse>(
                Error.Validation("validation.invalid_format", "Coordinates cannot be zero.")));
        var (hub, groupProxy, _) = SetupHub(authenticated: true, sender: sender);

        // Act
        var act = () => hub.UpdateLocation(0m, 0m);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*validation.invalid_format*");
        await groupProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
    }
}
