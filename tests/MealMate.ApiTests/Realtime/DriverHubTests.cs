using System.Security.Claims;
using FluentAssertions;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.DriverDelivery.DriverApp;
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
        var groupProxy = Substitute.For<ISingleClientProxy>();

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

    #region Authenticated group membership (identity-derived only)

    private static (DriverHub Hub, IGroupManager Groups, IHubCallerClients Clients, ISingleClientProxy Caller, ISender Sender) SetupHubWithGroups(
        bool authenticated,
        Result<DriverIdentityForCurrentUserResponse>? identityResult)
    {
        var hubContext = Substitute.For<HubCallerContext>();
        var dispatcherHubContext = Substitute.For<IHubContext<DispatcherHub>>();
        var dispatcherClients = Substitute.For<IHubClients>();
        var clients = Substitute.For<IHubCallerClients>();
        var caller = Substitute.For<ISingleClientProxy>();
        var groupProxy = Substitute.For<ISingleClientProxy>();
        var groups = Substitute.For<IGroupManager>();

        dispatcherHubContext.Clients.Returns(dispatcherClients);
        dispatcherClients.Group(Arg.Any<string>()).Returns(groupProxy);
        clients.Caller.Returns(caller);
        clients.Group(Arg.Any<string>()).Returns(groupProxy);
        clients.All.Returns(groupProxy);

        var identity = authenticated
            ? new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth")
            : new ClaimsIdentity();

        hubContext.User.Returns(new ClaimsPrincipal(identity));
        hubContext.ConnectionId.Returns($"conn-{Guid.NewGuid():N}");
        hubContext.GetHttpContext().Returns(new DefaultHttpContext());

        var sender = Substitute.For<ISender>();
        if (identityResult is not null)
        {
            sender.Send(Arg.Any<GetDriverIdentityForCurrentUserQuery>(), Arg.Any<CancellationToken>())
                .Returns(identityResult);
        }

        var hub = new DriverHub(sender, new DriverLocationBroadcaster(dispatcherHubContext))
        {
            Context = hubContext,
            Clients = clients,
            Groups = groups
        };

        return (hub, groups, clients, caller, sender);
    }

    [Fact]
    public async Task OnConnected_DriverConnection_BindsGroupFromResolvedIdentity()
    {
        // Arrange — the driver profile id comes from the JWT identity, never from
        // query strings or headers.
        var driverProfileId = Guid.NewGuid();
        var (hub, groups, _, caller, sender) = SetupHubWithGroups(
            authenticated: true,
            Result.Success(new DriverIdentityForCurrentUserResponse(driverProfileId, Guid.NewGuid())));

        // Act
        await hub.OnConnectedAsync();

        // Assert
        await groups.Received(1).AddToGroupAsync(
            Arg.Any<string>(), $"driver:{driverProfileId:D}", Arg.Any<CancellationToken>());
        await groups.Received(1).AddToGroupAsync(
            Arg.Any<string>(), DriverHub.UniversalDriverGroup, Arg.Any<CancellationToken>());
        await caller.Received(1).SendCoreAsync(
            DriverHub.EventConnectionEstablished,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] != null),
            Arg.Any<CancellationToken>());
        await sender.Received(1).Send(
            Arg.Any<GetDriverIdentityForCurrentUserQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnected_UnauthenticatedConnection_IsAborted_WithoutGroupMembership()
    {
        // Arrange — the hub is no longer anonymous
        var (hub, groups, _, _, sender) = SetupHubWithGroups(authenticated: false, identityResult: null);

        // Act
        await hub.OnConnectedAsync();

        // Assert
        hub.Context.Received(1).Abort();
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
        await sender.DidNotReceive().Send(Arg.Any<GetDriverIdentityForCurrentUserQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnected_AuthenticatedNonDriverConnection_IsAborted()
    {
        // Arrange — a valid JWT that does not map to a driver profile
        var (hub, groups, _, _, sender) = SetupHubWithGroups(
            authenticated: true,
            Result.Failure<DriverIdentityForCurrentUserResponse>(Error.NotFound("driver.not_found", "No driver profile.")));

        // Act
        await hub.OnConnectedAsync();

        // Assert
        hub.Context.Received(1).Abort();
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task JoinDriverGroup_OwnDriverId_IsAllowed()
    {
        // Arrange
        var driverProfileId = Guid.NewGuid();
        var (hub, groups, _, _, _) = SetupHubWithGroups(
            authenticated: true,
            Result.Success(new DriverIdentityForCurrentUserResponse(driverProfileId, Guid.NewGuid())));

        // Act
        await hub.JoinDriverGroup(driverProfileId.ToString());

        // Assert
        await groups.Received(1).AddToGroupAsync(
            Arg.Any<string>(), $"driver:{driverProfileId:D}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinDriverGroup_AnotherDriverId_IsRejected()
    {
        // Arrange
        var ownDriverId = Guid.NewGuid();
        var (hub, groups, _, _, _) = SetupHubWithGroups(
            authenticated: true,
            Result.Success(new DriverIdentityForCurrentUserResponse(ownDriverId, Guid.NewGuid())));

        // Act
        var act = () => hub.JoinDriverGroup(Guid.NewGuid().ToString());

        // Assert — private driver events must not be reachable by group spoofing
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*forbidden*");
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task LeaveDriverGroup_AnotherDriverId_IsRejected()
    {
        // Arrange
        var ownDriverId = Guid.NewGuid();
        var (hub, groups, _, _, _) = SetupHubWithGroups(
            authenticated: true,
            Result.Success(new DriverIdentityForCurrentUserResponse(ownDriverId, Guid.NewGuid())));

        // Act
        var act = () => hub.LeaveDriverGroup(Guid.NewGuid().ToString());

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*forbidden*");
        await groups.DidNotReceiveWithAnyArgs().RemoveFromGroupAsync(default!, default!, default);
    }

    #endregion
}
