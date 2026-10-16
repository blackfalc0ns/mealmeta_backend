using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;

namespace MealMate.ApiTests.CustomerDelivery;

public sealed class CustomerDeliveryHubWebSocketTests
{
    [Fact]
    public async Task AnonymousCustomerDeliveryHubNegotiation_ReturnsUnauthorized()
    {
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSignalR();
                services.AddAuthentication("CustomerDeliveryTest")
                    .AddScheme<AuthenticationSchemeOptions, CustomerDeliveryTestAuthenticationHandler>("CustomerDeliveryTest", _ => { });
                services.AddAuthorization(options => options.AddPolicy(CustomerDeliveryHub.CustomerPolicy, policy =>
                    policy.RequireAuthenticatedUser().RequireAssertion(context =>
                        context.User.FindFirstValue(AppClaimTypes.UserType) == "Customer")));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapHub<CustomerDeliveryHub>(CustomerDeliveryHub.HubPath));
            }));

        using var client = server.CreateClient();
        using var response = await client.PostAsync($"{CustomerDeliveryHub.HubPath}/negotiate?negotiateVersion=1", content: null);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomerReceivesGroupEvent_AndOtherCustomerDoesNot()
    {
        var readyFilter = new CustomerDeliveryConnectionReadyFilter(expectedConnections: 2);
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSignalR(options => options.AddFilter(readyFilter));
                services.AddAuthentication("CustomerDeliveryTest")
                    .AddScheme<AuthenticationSchemeOptions, CustomerDeliveryTestAuthenticationHandler>("CustomerDeliveryTest", _ => { });
                services.AddAuthorization(options => options.AddPolicy(CustomerDeliveryHub.CustomerPolicy, policy =>
                    policy.RequireAuthenticatedUser().RequireAssertion(context =>
                        context.User.FindFirstValue(AppClaimTypes.UserType) == "Customer")));
            })
            .Configure(app =>
            {
                app.UseWebSockets();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapHub<CustomerDeliveryHub>(CustomerDeliveryHub.HubPath));
            }));

        var firstUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        using var firstSocket = await ConnectCustomerAsync(server, firstUser);
        using var otherSocket = await ConnectCustomerAsync(server, otherUser);
        await readyFilter.WaitAsync();
        var hub = server.Services.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<CustomerDeliveryHub>>();
        var eventId = Guid.NewGuid();
        var dayId = Guid.NewGuid();

        await hub.Clients.Group(CustomerDeliveryHub.GroupForUser(firstUser))
            .SendAsync(CustomerDeliveryHub.EventCustomerDeliveryChanged, new { eventId, subscriptionDayId = dayId });

        var message = await ReadRecordAsync(firstSocket, TimeSpan.FromSeconds(3));
        Assert.Contains(CustomerDeliveryHub.EventCustomerDeliveryChanged, message, StringComparison.Ordinal);
        Assert.Contains(eventId.ToString(), message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dayId.ToString(), message, StringComparison.OrdinalIgnoreCase);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => otherSocket.ReceiveAsync(
            new ArraySegment<byte>(new byte[256]), timeout.Token));
    }

    [Fact]
    public async Task ReconnectedCustomer_RejoinsClaimDerivedGroup()
    {
        var readyFilter = new CustomerDeliveryConnectionReadyFilter(expectedConnections: 2);
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSignalR(options => options.AddFilter(readyFilter));
                services.AddAuthentication("CustomerDeliveryTest")
                    .AddScheme<AuthenticationSchemeOptions, CustomerDeliveryTestAuthenticationHandler>("CustomerDeliveryTest", _ => { });
                services.AddAuthorization(options => options.AddPolicy(CustomerDeliveryHub.CustomerPolicy, policy =>
                    policy.RequireAuthenticatedUser().RequireAssertion(context =>
                        context.User.FindFirstValue(AppClaimTypes.UserType) == "Customer")));
            })
            .Configure(app =>
            {
                app.UseWebSockets();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapHub<CustomerDeliveryHub>(CustomerDeliveryHub.HubPath));
            }));

        var userId = Guid.NewGuid();
        using var firstSocket = await ConnectCustomerAsync(server, userId);
        var hub = server.Services.GetRequiredService<IHubContext<CustomerDeliveryHub>>();
        await SendEventAsync(hub, userId);
        Assert.Contains(CustomerDeliveryHub.EventCustomerDeliveryChanged,
            await ReadRecordAsync(firstSocket, TimeSpan.FromSeconds(3)), StringComparison.Ordinal);
        await firstSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);

        using var reconnectedSocket = await ConnectCustomerAsync(server, userId);
        await readyFilter.WaitAsync();
        await SendEventAsync(hub, userId);
        Assert.Contains(CustomerDeliveryHub.EventCustomerDeliveryChanged,
            await ReadRecordAsync(reconnectedSocket, TimeSpan.FromSeconds(3)), StringComparison.Ordinal);
    }

    private static Task SendEventAsync(IHubContext<CustomerDeliveryHub> hub, Guid userId)
        => hub.Clients.Group(CustomerDeliveryHub.GroupForUser(userId))
            .SendAsync(CustomerDeliveryHub.EventCustomerDeliveryChanged, new { eventId = Guid.NewGuid(), subscriptionDayId = Guid.NewGuid() });

    [Theory]
    [InlineData("Driver")]
    [InlineData("Restaurant")]
    public async Task NonCustomer_IsRejectedByCustomerOnlyHub(string userType)
    {
        var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSignalR();
                services.AddAuthentication("CustomerDeliveryTest")
                    .AddScheme<AuthenticationSchemeOptions, CustomerDeliveryTestAuthenticationHandler>("CustomerDeliveryTest", _ => { });
                services.AddAuthorization(options => options.AddPolicy(CustomerDeliveryHub.CustomerPolicy, policy =>
                    policy.RequireAuthenticatedUser().RequireAssertion(context =>
                        context.User.FindFirstValue(AppClaimTypes.UserType) == "Customer")));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapHub<CustomerDeliveryHub>(CustomerDeliveryHub.HubPath));
            }));
        using var client = server.CreateClient();
        var userId = Guid.NewGuid();
        using var response = await client.PostAsync(
            $"{CustomerDeliveryHub.HubPath}/negotiate?negotiateVersion=1&access_token={userType}|{userId:D}",
            content: null);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<WebSocket> ConnectCustomerAsync(TestServer server, Guid userId)
    {
        var socket = await server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost{CustomerDeliveryHub.HubPath}?access_token=Customer|{userId:D}"),
            CancellationToken.None);
        var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
        await socket.SendAsync(new ArraySegment<byte>(handshake), WebSocketMessageType.Text, true, CancellationToken.None);
        var acknowledgement = await ReadRecordAsync(socket, TimeSpan.FromSeconds(3));
        Assert.Equal("{}\u001e", acknowledgement);
        return socket;
    }

    private static async Task<string> ReadRecordAsync(WebSocket socket, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            result.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
            var text = result.ToString();
            var separator = text.IndexOf('\u001e');
            if (separator >= 0)
                return text[..(separator + 1)];
        }
    }
}

internal sealed class CustomerDeliveryConnectionReadyFilter(int expectedConnections) : IHubFilter
{
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connectedCount;

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await next(context);
        if (Interlocked.Increment(ref _connectedCount) >= expectedConnections)
            _connected.TrySetResult();
    }

    public Task WaitAsync() => _connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

public sealed class CustomerDeliveryTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public CustomerDeliveryTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Query["access_token"].FirstOrDefault();
        var parts = token?.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } || !Guid.TryParse(parts[1], out var userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
        [
            new Claim(AppClaimTypes.UserType, parts[0]),
            new Claim(AppClaimTypes.UserId, userId.ToString()),
        ], Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
