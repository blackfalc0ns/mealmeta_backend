using System.Text.Json;
using FluentAssertions;
using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Interceptors;
using MealMate.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.Outbox;

public sealed class TestDateTimeProvider : IDateTimeProvider
{
    private TimeSpan _offset = TimeSpan.Zero;
    private DateTime? _fixedUtcNow;

    public DateTime UtcNow => _fixedUtcNow ?? (DateTime.UtcNow + _offset);

    public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);

    public void Advance(TimeSpan duration)
    {
        if (_fixedUtcNow.HasValue)
        {
            _fixedUtcNow = _fixedUtcNow.Value.Add(duration);
        }
        else
        {
            _offset += duration;
        }
    }

    public void SetFixed(DateTime fixedTime) => _fixedUtcNow = fixedTime;
}

public sealed class OutboxLeaseIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();

            await _container.StartAsync();

            await using var db = CreateDbContext();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/PostgreSQL container failed to start for OutboxLeaseIntegrationTests.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            })
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private IServiceProvider BuildServiceProvider(
        IDriverPushNotifier pushNotifier,
        TestDateTimeProvider clock,
        Action<OutboxOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();

        // 1. Database & Persistence core
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(_container.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
            });
            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // 2. Application Core (MediatR handlers, behaviors, validators)
        services.AddApplication();

        // 3. Controllable Clock and Test Push Notifier Substitute
        services.AddSingleton<IDateTimeProvider>(clock);
        services.AddSingleton(pushNotifier);
        // MediatR resolves every lifecycle handler for a published outbox event,
        // including handlers that ignore this test's event type.
        services.AddSingleton(Substitute.For<IDriverRegistrationNotificationService>());
        services.AddSingleton(Substitute.For<IMealPushNotifier>());
        services.AddSingleton(Substitute.For<ICustomerPushNotifier>());
        services.AddSingleton(Substitute.For<IRegistrationPushNotificationService>());
        services.AddSingleton(Substitute.For<IRegistrationReviewNotifier>());
        // Voice-call lifecycle handlers must resolve for ANY published event (MediatR
        // resolves every handler), so their transports need test substitutes here.
        services.AddSingleton(Substitute.For<MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallPushNotifier>());
        services.AddSingleton(Substitute.For<MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier>());

        // Current User placeholder for auditable entities
        var currentUserMock = Substitute.For<ICurrentUser>();
        currentUserMock.UserId.Returns(Guid.NewGuid());
        services.AddSingleton(currentUserMock);

        // Campaign notification sender mock
        services.AddScoped<MealMate.Application.Features.CollaborativeCampaigns.Services.ICampaignNotificationSender>(_ =>
            Substitute.For<MealMate.Application.Features.CollaborativeCampaigns.Services.ICampaignNotificationSender>());

        // 4. Logging & Production Worker Options
        services.AddLogging(b => b.AddDebug());

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddTransient<OutboxBackgroundJob>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void WorkerServices_ResolveAllLifecycleHandlers()
    {
        using var provider = (ServiceProvider)BuildServiceProvider(
            Substitute.For<IDriverPushNotifier>(), new TestDateTimeProvider());
        using var scope = provider.CreateScope();
        var resolve = () => scope.ServiceProvider
            .GetServices<MediatR.INotificationHandler<MealMate.Application.Common.Outbox.OutboxLifecycleNotification>>()
            .ToArray();

        resolve.Should().NotThrow("all lifecycle handlers must resolve before a worker can dispatch any event");
    }

    [Fact]
    public async Task ConcurrentWorkers_ExecuteRealJob_ShouldProcessAtomicallyWithoutDuplicates()
    {
        // Arrange
        var clock = new TestDateTimeProvider();
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var sp1 = BuildServiceProvider(pushNotifier, clock);
        var sp2 = BuildServiceProvider(pushNotifier, clock);

        var messageId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var boxCode = "BOX-TEST-001";
        var zoneName = "حولي";

        var payload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = "DriverBoxAssigned",
            DriverUserId = driverUserId,
            BoxId = boxId,
            BoxCode = boxCode,
            ZoneName = zoneName
        });

        await using (var db = CreateDbContext())
        {
            var outboxMessage = new OutboxMessage
            {
                EventType = "DriverBoxAssigned",
                PayloadJson = payload,
                CreatedAtUtc = clock.UtcNow,
                RetryCount = 0
            };
            messageId = outboxMessage.Id;
            db.OutboxMessages.Add(outboxMessage);
            await db.SaveChangesAsync();
        }

        var worker1 = sp1.GetRequiredService<OutboxBackgroundJob>();
        var worker2 = sp2.GetRequiredService<OutboxBackgroundJob>();

        // Act: Run two worker jobs concurrently against the same pending message
        var task1 = Task.Run(() => worker1.ProcessPendingMessagesAsync(10, CancellationToken.None));
        var task2 = Task.Run(() => worker2.ProcessPendingMessagesAsync(10, CancellationToken.None));

        var results = await Task.WhenAll(task1, task2);

        // Assert
        var totalProcessed = results.Sum();
        totalProcessed.Should().Be(1, "Exactly one worker must process the outbox message; concurrent processing must be prevented");

        // The push notification must be invoked exactly once
        await pushNotifier.Received(1).NotifyBoxAssignedAsync(
            driverUserId,
            boxId,
            boxCode,
            zoneName,
            Arg.Any<CancellationToken>());

        // Verify message state in DB
        await using (var verifyDb = CreateDbContext())
        {
            var msg = await verifyDb.OutboxMessages.FindAsync(messageId);
            msg.Should().NotBeNull();
            msg!.ProcessedAtUtc.Should().NotBeNull("Message must be marked as processed");
            msg.LastError.Should().BeNull();
            msg.LockedByWorkerId.Should().BeNull("Lease lock must be released upon completion");
            msg.LeaseToken.Should().BeNull();
        }
    }

    [Fact]
    public async Task WorkerCrash_RecoversImmediately_AfterLeaseExpiry_WithoutWaitingOneDay()
    {
        // Arrange
        var clock = new TestDateTimeProvider();
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var sp = BuildServiceProvider(pushNotifier, clock);

        var messageId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = "DriverBoxAssigned",
            DriverUserId = driverUserId,
            BoxId = boxId,
            BoxCode = "BOX-CRASH-01",
            ZoneName = "السالمية"
        });

        await using (var db = CreateDbContext())
        {
            var outboxMessage = new OutboxMessage
            {
                EventType = "DriverBoxAssigned",
                PayloadJson = payload,
                CreatedAtUtc = clock.UtcNow,
                RetryCount = 0,
                LockedByWorkerId = "Crashed-Worker-99",
                LockExpiresAtUtc = clock.UtcNow.AddSeconds(2), // Lease expired in 2 seconds
                LeaseToken = Guid.NewGuid(),
                LastAttemptedAtUtc = clock.UtcNow
            };
            messageId = outboxMessage.Id;
            db.OutboxMessages.Add(outboxMessage);
            await db.SaveChangesAsync();
        }

        // Advance clock past the 2s lease expiration (advance by 5 seconds)
        clock.Advance(TimeSpan.FromSeconds(5));

        // Act: Surviving worker executes polling
        var survivingWorker = sp.GetRequiredService<OutboxBackgroundJob>();
        var processed = await survivingWorker.ProcessPendingMessagesAsync(10, CancellationToken.None);

        // Assert: Message must be reclaimed immediately without waiting for a day
        processed.Should().Be(1, "The surviving worker must reclaim the crashed worker's expired message immediately");

        await pushNotifier.Received(1).NotifyBoxAssignedAsync(
            driverUserId,
            boxId,
            "BOX-CRASH-01",
            "السالمية",
            Arg.Any<CancellationToken>());

        await using (var verifyDb = CreateDbContext())
        {
            var msg = await verifyDb.OutboxMessages.FindAsync(messageId);
            msg.Should().NotBeNull();
            msg!.ProcessedAtUtc.Should().NotBeNull("Message must be finalized by surviving worker");
            msg.LockedByWorkerId.Should().BeNull();
            msg.LeaseToken.Should().BeNull();
        }
    }

    [Fact]
    public async Task Heartbeat_RenewsLease_WhenPushNotificationIsSlow_PreventingDoubleExecution()
    {
        // Arrange
        var clock = new TestDateTimeProvider();
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var messageId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = "DriverBoxAssigned",
            DriverUserId = driverUserId,
            BoxId = boxId,
            BoxCode = "BOX-SLOW-01",
            ZoneName = "الفروانية"
        });

        await using (var db = CreateDbContext())
        {
            var outboxMessage = new OutboxMessage
            {
                EventType = "DriverBoxAssigned",
                PayloadJson = payload,
                CreatedAtUtc = clock.UtcNow,
                RetryCount = 0
            };
            messageId = outboxMessage.Id;
            db.OutboxMessages.Add(outboxMessage);
            await db.SaveChangesAsync();
        }

        // Simulate slow push notification (1.2s delay) with 1s lease and 250ms heartbeat
        pushNotifier.NotifyBoxAssignedAsync(driverUserId, boxId, "BOX-SLOW-01", "الفروانية", Arg.Any<CancellationToken>())
            .Returns(async _ => await Task.Delay(1200));

        var sp1 = BuildServiceProvider(pushNotifier, clock, opt =>
        {
            opt.LeaseDuration = TimeSpan.FromSeconds(1);
            opt.HeartbeatInterval = TimeSpan.FromMilliseconds(250);
        });

        var sp2 = BuildServiceProvider(pushNotifier, clock, opt =>
        {
            opt.LeaseDuration = TimeSpan.FromSeconds(1);
            opt.HeartbeatInterval = TimeSpan.FromMilliseconds(250);
        });

        var worker1 = sp1.GetRequiredService<OutboxBackgroundJob>();
        var worker2 = sp2.GetRequiredService<OutboxBackgroundJob>();

        // Act: Worker 1 starts slow processing
        var worker1Task = Task.Run(() => worker1.ProcessPendingMessagesAsync(10, CancellationToken.None));

        // Wait 600ms (more than half the lease, heartbeat fires twice to extend lease)
        await Task.Delay(600);

        // Worker 2 attempts to process: should find 0 eligible messages because lease was renewed
        var worker2Processed = await worker2.ProcessPendingMessagesAsync(10, CancellationToken.None);
        worker2Processed.Should().Be(0, "Worker 2 must not steal a message actively leased and renewed by Worker 1");

        var worker1Processed = await worker1Task;
        worker1Processed.Should().Be(1, "Worker 1 must complete successfully after slow external call");

        await using (var verifyDb = CreateDbContext())
        {
            var msg = await verifyDb.OutboxMessages.FindAsync(messageId);
            msg!.ProcessedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task LegacyCampaignEvent_ProcessedSuccessfully_WithoutDoubleProcessing()
    {
        // Arrange
        var clock = new TestDateTimeProvider();
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var sp = BuildServiceProvider(pushNotifier, clock);

        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            CampaignId = Guid.NewGuid(),
            RecipientEmail = "chef@restaurant.com"
        });

        await using (var db = CreateDbContext())
        {
            var outboxMessage = new OutboxMessage
            {
                EventType = "CampaignInvitationSent",
                PayloadJson = payload,
                CreatedAtUtc = clock.UtcNow,
                RetryCount = 0
            };
            messageId = outboxMessage.Id;
            db.OutboxMessages.Add(outboxMessage);
            await db.SaveChangesAsync();
        }

        var worker = sp.GetRequiredService<OutboxBackgroundJob>();

        // Act
        var processed = await worker.ProcessPendingMessagesAsync(10, CancellationToken.None);

        // Assert
        processed.Should().Be(1, "OutboxBackgroundJob must process legacy campaign events successfully");

        await using (var verifyDb = CreateDbContext())
        {
            var msg = await verifyDb.OutboxMessages.FindAsync(messageId);
            msg.Should().NotBeNull();
            msg!.ProcessedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task WorkerLosesOwnership_WhenLeaseExpiresAndSecondWorkerReclaims_FirstWorkerCompletionFailsSafely()
    {
        // Arrange
        var clock = new TestDateTimeProvider();
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var messageId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = "DriverBoxAssigned",
            DriverUserId = driverUserId,
            BoxId = boxId,
            BoxCode = "BOX-STOLEN-01",
            ZoneName = "الدعية"
        });

        await using (var db = CreateDbContext())
        {
            var outboxMessage = new OutboxMessage
            {
                EventType = "DriverBoxAssigned",
                PayloadJson = payload,
                CreatedAtUtc = clock.UtcNow,
                RetryCount = 0
            };
            messageId = outboxMessage.Id;
            db.OutboxMessages.Add(outboxMessage);
            await db.SaveChangesAsync();
        }

        // Worker 1 has 1s lease with NO heartbeat (heartbeat disabled by setting huge interval 1 hour)
        var sp1 = BuildServiceProvider(pushNotifier, clock, opt =>
        {
            opt.LeaseDuration = TimeSpan.FromSeconds(1);
            opt.HeartbeatInterval = TimeSpan.FromHours(1);
        });

        // Worker 2 has normal lease
        var sp2 = BuildServiceProvider(pushNotifier, clock, opt =>
        {
            opt.LeaseDuration = TimeSpan.FromSeconds(10);
            opt.HeartbeatInterval = TimeSpan.FromSeconds(1);
        });

        // Push notifier simulates slow call (1.5s delay)
        pushNotifier.NotifyBoxAssignedAsync(driverUserId, boxId, "BOX-STOLEN-01", "الدعية", Arg.Any<CancellationToken>())
            .Returns(async _ => await Task.Delay(1500));

        var worker1 = sp1.GetRequiredService<OutboxBackgroundJob>();
        var worker2 = sp2.GetRequiredService<OutboxBackgroundJob>();

        // Act: Worker 1 claims message
        var worker1Task = Task.Run(() => worker1.ProcessPendingMessagesAsync(10, CancellationToken.None));

        // Wait 1.1s and advance clock by 3s: Worker 1's 1s lease expires while its push is still in-flight
        await Task.Delay(1100);
        clock.Advance(TimeSpan.FromSeconds(3));

        // Worker 2 polls: should steal the expired message
        var worker2Processed = await worker2.ProcessPendingMessagesAsync(10, CancellationToken.None);
        worker2Processed.Should().Be(1, "Worker 2 must reclaim the message whose lease expired without heartbeat");

        // Now Worker 1's push finishes and it tries to complete with its obsolete token
        var worker1Processed = await worker1Task;
        worker1Processed.Should().Be(0, "Worker 1 must return 0 processed because its ownership was lost");

        // Assert: External push was called twice across both workers (At-Least-Once delivery verified!)
        await pushNotifier.Received(2).NotifyBoxAssignedAsync(
            driverUserId,
            boxId,
            "BOX-STOLEN-01",
            "الدعية",
            Arg.Any<CancellationToken>());

        // DB state is valid and finalized by Worker 2
        await using (var verifyDb = CreateDbContext())
        {
            var msg = await verifyDb.OutboxMessages.FindAsync(messageId);
            msg!.ProcessedAtUtc.Should().NotBeNull();
            msg.LockedByWorkerId.Should().BeNull();
            msg.LeaseToken.Should().BeNull();
        }
    }
}
