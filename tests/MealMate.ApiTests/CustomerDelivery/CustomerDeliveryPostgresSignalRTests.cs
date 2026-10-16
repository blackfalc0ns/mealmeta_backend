using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MealMate.Api.Realtime;
using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Common.Security;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Infrastructure;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using MealMate.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.CustomerDelivery;

public sealed class CustomerDeliveryPostgresSignalRTests
{
    [Fact]
    public async Task PersistedDeliveryChange_IsDispatchedByOutboxAndReachesAuthorizedConnectedCustomer()
    {
        Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION")),
            "The live PostgreSQL integration test refuses to run while MEALMATE_DB_CONNECTION can override its isolated database.");
        await using var postgres = new PostgresTestDatabase();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MealMateDb"] = connectionString,
            ["BackgroundJobs:OutboxEnabled"] = "false",
            ["Jwt:Issuer"] = "CustomerDeliveryTests",
            ["Jwt:Audience"] = "CustomerDeliveryTests",
            ["Jwt:SecretKey"] = "TestOnlySigningKeyForCustomerDeliveryRealtime",
        }).Build();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        var readyFilter = new CustomerDeliveryConnectionReadyFilter(expectedConnections: 4);

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddApplication();
                services.AddInfrastructure(configuration, context.HostingEnvironment);
                services.AddPersistence(configuration);
                services.AddScoped<ICurrentUser>(_ => currentUser);
                services.AddSingleton<IDateTimeProvider>(clock);
                services.AddSignalR(options => options.AddFilter(readyFilter));
                services.AddAuthentication("CustomerDeliveryTest")
                    .AddScheme<AuthenticationSchemeOptions, CustomerDeliveryTestAuthenticationHandler>("CustomerDeliveryTest", _ => { });
                services.AddAuthorization(options => options.AddPolicy(CustomerDeliveryHub.CustomerPolicy, policy =>
                    policy.RequireAuthenticatedUser().RequireAssertion(context =>
                        context.User.FindFirstValue(AppClaimTypes.UserType) == UserType.Customer.ToString())));
                services.AddScoped<SignalRCustomerDeliveryChangeNotifier>();
                services.AddSingleton(sp => new CustomerDeliveryPostgresNotificationListener(
                    connectionString,
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CustomerDeliveryPostgresNotificationListener>>()));
                services.AddHostedService(sp => sp.GetRequiredService<CustomerDeliveryPostgresNotificationListener>());
            })
            .Configure(app =>
            {
                app.UseWebSockets();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapHub<CustomerDeliveryHub>(CustomerDeliveryHub.HubPath));
            }));

        await using (var scope = server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();

            var customerId = Guid.NewGuid();
            var program = new MealMate.Domain.ProgramsSubscriptions.Program
            {
                Code = $"RT-{Guid.NewGuid():N}",
                NameAr = "برنامج اختبار",
                NameEn = "Realtime test plan",
                Status = ProgramStatus.Active,
            };
            var bundle = new Bundle
            {
                Program = program,
                ProgramId = program.Id,
                Code = $"RT-{Guid.NewGuid():N}",
                NameAr = "باقة اختبار",
                NameEn = "Realtime test bundle",
                MealsPerDay = 1,
            };
            var subscription = new Subscription
            {
                Program = program,
                ProgramId = program.Id,
                Bundle = bundle,
                BundleId = bundle.Id,
                CustomerUserId = customerId,
                DurationDays = 1,
                StartDate = new DateOnly(2026, 10, 16),
                EndDate = new DateOnly(2026, 10, 16),
                Status = SubscriptionStatus.Active,
                PaidAmount = Money.Zero("KWD"),
            };
            var family = new FamilyGroup
            {
                ManagerCustomerUserId = customerId,
                Name = "Realtime access test",
                Status = FamilyGroupStatus.Active,
            };
            var linkedRecipientId = Guid.NewGuid();
            var removedRecipientId = Guid.NewGuid();
            var linkedMember = new FamilyMember
            {
                FamilyGroup = family,
                Role = FamilyMemberRole.Member,
                DisplayName = "Linked recipient",
                RelationshipCode = "member",
                Gender = FamilyMemberGender.Female,
                AgeYears = 30,
                NutritionalGoalCode = "balanced",
                ActivityLevel = FamilyActivityLevel.Moderate,
            };
            var removedMember = new FamilyMember
            {
                FamilyGroup = family,
                Role = FamilyMemberRole.Member,
                DisplayName = "Removed recipient",
                RelationshipCode = "member",
                Gender = FamilyMemberGender.Female,
                AgeYears = 31,
                NutritionalGoalCode = "balanced",
                ActivityLevel = FamilyActivityLevel.Moderate,
            };
            subscription.FamilyGroupId = family.Id;
            subscription.FamilyMemberId = linkedMember.Id;
            var day = new SubscriptionDay
            {
                SubscriptionId = subscription.Id,
                ServiceDate = new DateOnly(2026, 10, 16),
                Status = SubscriptionDayStatus.Open,
            };
            db.Users.Add(new ApplicationUser
            {
                Id = customerId,
                UserName = customerId.ToString("D"),
                UserType = UserType.Customer,
                AccountStatus = AccountStatus.Active,
            });
            var unrelatedCustomerId = Guid.NewGuid();
            db.Users.Add(new ApplicationUser
            {
                Id = unrelatedCustomerId,
                UserName = unrelatedCustomerId.ToString("D"),
                UserType = UserType.Customer,
                AccountStatus = AccountStatus.Active,
            });
            db.Users.AddRange(
                new ApplicationUser
                {
                    Id = linkedRecipientId,
                    UserName = linkedRecipientId.ToString("D"),
                    UserType = UserType.Customer,
                    AccountStatus = AccountStatus.Active,
                },
                new ApplicationUser
                {
                    Id = removedRecipientId,
                    UserName = removedRecipientId.ToString("D"),
                    UserType = UserType.Customer,
                    AccountStatus = AccountStatus.Active,
                });
            db.FamilyMemberAccesses.AddRange(
                new FamilyMemberAccess
                {
                    FamilyMember = linkedMember,
                    HasSeparateAccount = true,
                    LinkedCustomerUserId = linkedRecipientId,
                },
                new FamilyMemberAccess
                {
                    FamilyMember = removedMember,
                    HasSeparateAccount = true,
                    LinkedCustomerUserId = removedRecipientId,
                    RemovedAtUtc = DateTime.UtcNow,
                });
            db.FamilyMemberPermissions.Add(new FamilyMemberPermissions
            {
                FamilyMember = linkedMember,
                ViewCalendar = true,
            });
            db.Programs.Add(program);
            db.Bundles.Add(bundle);
            db.FamilyGroups.Add(family);
            db.Subscriptions.Add(subscription);
            db.SubscriptionDays.Add(day);
            await db.SaveChangesAsync();

            var listener = server.Services.GetRequiredService<CustomerDeliveryPostgresNotificationListener>();
            await listener.Listening.WaitAsync(TimeSpan.FromSeconds(10));

            using var socket = await server.CreateWebSocketClient().ConnectAsync(
                new Uri($"ws://localhost{CustomerDeliveryHub.HubPath}?access_token=Customer|{customerId:D}"),
                CancellationToken.None);
            await HandshakeAsync(socket);
            using var unrelatedSocket = await server.CreateWebSocketClient().ConnectAsync(
                new Uri($"ws://localhost{CustomerDeliveryHub.HubPath}?access_token=Customer|{unrelatedCustomerId:D}"),
                CancellationToken.None);
            await HandshakeAsync(unrelatedSocket);
            using var linkedRecipientSocket = await server.CreateWebSocketClient().ConnectAsync(
                new Uri($"ws://localhost{CustomerDeliveryHub.HubPath}?access_token=Customer|{linkedRecipientId:D}"),
                CancellationToken.None);
            await HandshakeAsync(linkedRecipientSocket);
            using var removedRecipientSocket = await server.CreateWebSocketClient().ConnectAsync(
                new Uri($"ws://localhost{CustomerDeliveryHub.HubPath}?access_token=Customer|{removedRecipientId:D}"),
                CancellationToken.None);
            await HandshakeAsync(removedRecipientSocket);
            await readyFilter.WaitAsync();

            day.Status = SubscriptionDayStatus.Preparing;
            await db.SaveChangesAsync();
            var outbox = await db.OutboxMessages.SingleAsync(message =>
                message.EventType == MealMate.Application.Features.DriverDelivery.CustomerTracking.CustomerDeliveryChangedV1.OutboxEventType
                && message.ProcessedAtUtc == null);
            var eventPayload = JsonDocument.Parse(outbox.PayloadJson).RootElement;
            var expectedEventId = outbox.Id;
            var expectedDayId = day.Id;
            var expectedOccurredAtUtc = eventPayload.GetProperty("occurredAtUtc").GetDateTime();

            var processed = await scope.ServiceProvider.GetRequiredService<IOutboxService>()
                .ProcessPendingMessagesAsync(batchSize: 10);
            await db.Entry(outbox).ReloadAsync();
            var attemptedOutbox = await db.OutboxMessages.SingleAsync(message => message.Id == expectedEventId);
            Assert.True(processed == 1,
                $"Expected one claimed outbox row, got {processed}; retry={attemptedOutbox.RetryCount}; " +
                $"lastError={attemptedOutbox.LastError}; nextAttempt={attemptedOutbox.NextAttemptAtUtc:o}; " +
                $"lockedBy={attemptedOutbox.LockedByWorkerId}; lastAttempted={attemptedOutbox.LastAttemptedAtUtc:o}");

            var eventMessage = await ReadRecordAsync(socket, TimeSpan.FromSeconds(5));
            using var wire = JsonDocument.Parse(eventMessage[..^1]);
            Assert.Equal(CustomerDeliveryHub.EventCustomerDeliveryChanged, wire.RootElement.GetProperty("target").GetString());
            var payload = wire.RootElement.GetProperty("arguments")[0];
            Assert.Equal(expectedEventId, payload.GetProperty("eventId").GetGuid());
            Assert.Equal(expectedDayId, payload.GetProperty("subscriptionDayId").GetGuid());
            Assert.Equal(expectedOccurredAtUtc, payload.GetProperty("occurredAtUtc").GetDateTime());
            Assert.Equal(1, payload.GetProperty("schemaVersion").GetInt32());
            Assert.True((await db.OutboxMessages.SingleAsync(message => message.Id == expectedEventId)).ProcessedAtUtc.HasValue);

            var linkedEvent = await ReadRecordAsync(linkedRecipientSocket, TimeSpan.FromSeconds(5));
            using var linkedWire = JsonDocument.Parse(linkedEvent[..^1]);
            Assert.Equal(CustomerDeliveryHub.EventCustomerDeliveryChanged, linkedWire.RootElement.GetProperty("target").GetString());
            Assert.Equal(expectedEventId, linkedWire.RootElement.GetProperty("arguments")[0].GetProperty("eventId").GetGuid());

            await AssertNoEventAsync(unrelatedSocket);
            await AssertNoEventAsync(removedRecipientSocket);
        }
    }

    private static async Task HandshakeAsync(WebSocket socket)
    {
        var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
        await socket.SendAsync(new ArraySegment<byte>(handshake), WebSocketMessageType.Text, true, CancellationToken.None);
        var result = await ReadRecordAsync(socket, TimeSpan.FromSeconds(3));
        Assert.Equal("{}\u001e", result);
    }

    private static async Task AssertNoEventAsync(WebSocket socket)
    {
        using var noDeliveryTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => socket.ReceiveAsync(
            new ArraySegment<byte>(new byte[512]), noDeliveryTimeout.Token));
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
