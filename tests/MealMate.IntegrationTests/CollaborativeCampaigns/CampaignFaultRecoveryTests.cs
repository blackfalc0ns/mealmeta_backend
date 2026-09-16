using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CollaborativeCampaigns;

public sealed class CampaignFaultRecoveryTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private readonly DateTime _now = new(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();

            await _container.StartAsync();

            await using var db = CreateDb();
            await db.Database.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed to start for fault recovery tests.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task CrashDuringCommit_ReconciliationWorkerRecoversOrphanedSessionOnReboot()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 11, 25);

        // 1. Simulate Pre-Crash State: Session was left in 'Committing' with expired lease
        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة التعافي",
                NameEn = "Fault Recovery Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = _now.AddDays(-5),
                EndsAtUtc = _now.AddDays(20),
                TotalDiscountRate = Percentage.FromFraction(0.20m),
                MealMateShareRate = Percentage.FromFraction(0.10m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 50,
                CurrentSubscribers = 1
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;

            var session = new CampaignCheckoutSession(sessionId)
            {
                CampaignId = campaignId,
                BundleId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Status = CampaignCheckoutSessionStatus.Committing, // Died during commit
                ExpiresAtUtc = _now.AddMinutes(15),
                ProcessingLeaseExpiresAtUtc = _now.AddMinutes(-5) // Lease expired while node was down
            };
            session.Allocations.Add(new CampaignReservationAllocation
            {
                CheckoutSessionId = sessionId,
                CampaignId = campaignId,
                BundleId = session.BundleId,
                RestaurantId = restaurantId,
                ServiceDate = serviceDate,
                Units = 1
            });
            db.CampaignCheckoutSessions.Add(session);

            db.CampaignPaymentTransactions.Add(new CampaignPaymentTransaction
            {
                CheckoutSessionId = sessionId,
                CampaignId = campaignId,
                PaymentProvider = "Hesabe",
                ProviderTransactionId = "TX-CRASH-001",
                Amount = 100.000m,
                Currency = "KWD",
                Status = PaymentTransactionStatus.Succeeded,
                InitiatedAtUtc = _now.AddMinutes(-10),
                CompletedAtUtc = _now.AddMinutes(-10)
            });

            await db.SaveChangesAsync();
        }

        // 2. Server Restarts: Background Reconciliation Worker triggers
        await using (var db = CreateDb())
        {
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(_now);

            var mediator = Substitute.For<IMediator>();

            var reconService = new CampaignCheckoutReconciliationService(db, clock, mediator);

            var recoveredCount = await reconService.ReconcileStuckSessionsAsync(CancellationToken.None);

            Assert.Equal(1, recoveredCount);
            await mediator.Received(1).Send(
                Arg.Is<CompleteCampaignPurchaseCommand>(cmd => cmd.CheckoutSessionId == sessionId),
                Arg.Any<CancellationToken>());
        }

        // 3. Complete Purchase command executed
        await using (var db = CreateDb())
        {
            var session = await db.CampaignCheckoutSessions.SingleAsync(s => s.Id == sessionId);
            session.MarkCompleted();
            await db.SaveChangesAsync();
        }

        // 4. Verify Final State Converged
        await using var verifyDb = CreateDb();
        var finalSession = await verifyDb.CampaignCheckoutSessions.SingleAsync(s => s.Id == sessionId);
        Assert.Equal(CampaignCheckoutSessionStatus.Completed, finalSession.Status);
    }

    [Fact]
    public async Task CrashWhileSessionPending_ExpirationWorkerRestoresCapacitiesOnReboot()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 12, 5);

        // 1. Pre-Crash: Session expired while user never completed gateway checkout
        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة انتهاء الجلسة",
                NameEn = "Session Expiry Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = _now.AddDays(-2),
                EndsAtUtc = _now.AddDays(25),
                TotalDiscountRate = Percentage.FromFraction(0.15m),
                MealMateShareRate = Percentage.FromFraction(0.05m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 20,
                CurrentSubscribers = 1
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;

            var capacity = new CampaignRestaurantCapacity
            {
                CampaignId = campaignId,
                RestaurantId = restaurantId,
                ServiceDate = serviceDate,
                MaxBoxes = 10,
                ReservedBoxes = 1 // 1 box held by this pending session
            };
            db.CampaignRestaurantCapacities.Add(capacity);

            var session = new CampaignCheckoutSession(sessionId)
            {
                CampaignId = campaignId,
                BundleId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Status = CampaignCheckoutSessionStatus.Pending,
                ExpiresAtUtc = _now.AddMinutes(-10), // Expired 10 min ago
                ProcessingLeaseExpiresAtUtc = null
            };
            session.Allocations.Add(new CampaignReservationAllocation
            {
                CheckoutSessionId = sessionId,
                CampaignId = campaignId,
                BundleId = session.BundleId,
                RestaurantId = restaurantId,
                ServiceDate = serviceDate,
                Units = 1
            });
            db.CampaignCheckoutSessions.Add(session);

            await db.SaveChangesAsync();
        }

        // 2. Server Restarts: Expiration Worker cleans up stale pending reservations
        await using (var db = CreateDb())
        {
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(_now);

            var expirationService = new CampaignReservationExpirationService(db, clock);

            var cleanedCount = await expirationService.ExpireStaleReservationsAsync(CancellationToken.None);

            Assert.Equal(1, cleanedCount);
        }

        // 3. Verify: Session is Expired and Capacity is fully released
        await using (var verifyDb = CreateDb())
        {
            var session = await verifyDb.CampaignCheckoutSessions.SingleAsync(s => s.Id == sessionId);
            var capacity = await verifyDb.CampaignRestaurantCapacities.SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == serviceDate);

            Assert.Equal(CampaignCheckoutSessionStatus.Expired, session.Status);
            Assert.Equal(0, capacity.ReservedBoxes); // Restored from 1 to 0!
        }
    }

    [Fact]
    public async Task CrashAfterOutboxEnqueue_OutboxDispatcherResumesAndDispatchesWithoutDuplication()
    {
        var eventId = Guid.NewGuid();
        Guid outboxId;

        // 1. Enqueue outbox message right before node failure
        await using (var db = CreateDb())
        {
            var outbox = new OutboxMessage
            {
                EventType = "CampaignLaunchedIntegrationEvent",
                PayloadJson = $"{{\"CampaignId\":\"{Guid.NewGuid()}\",\"EventId\":\"{eventId}\"}}",
                CreatedAtUtc = _now.AddMinutes(-5),
                ProcessedAtUtc = null,
                RetryCount = 0
            };
            db.OutboxMessages.Add(outbox);
            await db.SaveChangesAsync();
            outboxId = outbox.Id;
        }

        // 2. Node reboots: Dispatcher reads pending message and dispatches it
        await using (var db = CreateDb())
        {
            var msg = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            msg.ProcessedAtUtc = _now;

            // Consumer marks inbox processed
            db.InboxProcessedEvents.Add(new InboxProcessedEvent
            {
                ConsumerName = "AnalyticsConsumer",
                EventId = eventId,
                ProcessedAtUtc = _now
            });

            await db.SaveChangesAsync();
        }

        // 3. Second run or duplicate delivery attempt
        await using (var db = CreateDb())
        {
            var pendingMessages = await db.OutboxMessages
                .Where(m => m.ProcessedAtUtc == null)
                .ToListAsync();

            Assert.Empty(pendingMessages); // Already processed, no duplicate processing

            var inboxCount = await db.InboxProcessedEvents
                .CountAsync(i => i.ConsumerName == "AnalyticsConsumer" && i.EventId == eventId);

            Assert.Equal(1, inboxCount);
        }
    }
}
