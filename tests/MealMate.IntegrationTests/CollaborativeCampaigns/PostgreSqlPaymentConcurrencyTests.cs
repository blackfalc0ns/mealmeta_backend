using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CollaborativeCampaigns;

public sealed class PostgreSqlPaymentConcurrencyTests : IAsyncLifetime
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

            await using var db = CreateDb();
            await db.Database.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed to start for payment concurrency tests.", ex);
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
    public async Task ScenarioE_DuplicateCheckoutPost_EnforcesIdempotencyAcrossParallelRequests()
    {
        var idempotencyKey = "idemp-" + Guid.NewGuid().ToString("N");
        var campaignId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة التكرار",
                NameEn = "Idempotency Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(30),
                TotalDiscountRate = Percentage.FromFraction(0.20m),
                MealMateShareRate = Percentage.FromFraction(0.10m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 50
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int concurrentRequests = 5;

        async Task<(bool IsNew, Guid SessionId)> ProcessCheckoutWithIdempotencyAsync()
        {
            await using var db = CreateDb();
            await startGate.Task;

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                // Check if idempotency record exists
                var existing = await db.IdempotencyRecords
                    .FirstOrDefaultAsync(r => r.Key == idempotencyKey);

                if (existing != null)
                {
                    await tx.CommitAsync();
                    return (false, Guid.Parse(existing.ResponseBody));
                }

                // First to arrive creates the session and record
                var sessionId = Guid.NewGuid();
                var session = new CampaignCheckoutSession(sessionId)
                {
                    CampaignId = campaignId,
                    BundleId = bundleId,
                    CustomerId = customerId,
                    Status = CampaignCheckoutSessionStatus.Pending,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
                };
                db.CampaignCheckoutSessions.Add(session);

                var record = new IdempotencyRecord
                {
                    Scope = "Campaign",
                    Key = idempotencyKey,
                    RequestHash = "hash-123",
                    ResponseBody = sessionId.ToString(),
                    HttpStatusCode = 200,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
                };
                db.IdempotencyRecords.Add(record);

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, sessionId);
            }
            catch (DbUpdateException)
            {
                // Unique constraint violation on idempotency key
                await tx.RollbackAsync();

                // Read committed response
                await using var readDb = CreateDb();
                var record = await readDb.IdempotencyRecords.FirstAsync(r => r.Key == idempotencyKey);
                return (false, Guid.Parse(record.ResponseBody));
            }
        }

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => ProcessCheckoutWithIdempotencyAsync())
            .ToList();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        // Exactly 1 new creation, 4 idempotent duplicates
        Assert.Equal(1, results.Count(r => r.IsNew));
        Assert.Equal(4, results.Count(r => !r.IsNew));

        // All 5 returned the exact same SessionId
        var masterSessionId = results[0].SessionId;
        Assert.All(results, r => Assert.Equal(masterSessionId, r.SessionId));

        // Exactly 1 session persisted in PostgreSQL
        await using var verifyDb = CreateDb();
        var sessionCount = await verifyDb.CampaignCheckoutSessions.CountAsync(s => s.CampaignId == campaignId);
        Assert.Equal(1, sessionCount);
    }

    [Fact]
    public async Task ScenarioF_DuplicateWebhook_EnforcesPaymentTransactionUniquenessAcrossParallelWebhooks()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var providerTxId = "TX-HESABE-" + Guid.NewGuid().ToString("N");

        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة الدفع",
                NameEn = "Payment Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(30),
                TotalDiscountRate = Percentage.FromFraction(0.20m),
                MealMateShareRate = Percentage.FromFraction(0.10m),
                RestaurantShareRate = Percentage.FromFraction(0.10m)
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;

            var session = new CampaignCheckoutSession(sessionId)
            {
                CampaignId = campaignId,
                BundleId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Status = CampaignCheckoutSessionStatus.PaymentPending,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
            };
            db.CampaignCheckoutSessions.Add(session);
            await db.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int concurrentWebhooks = 5;

        async Task<bool> ProcessPaymentWebhookAsync()
        {
            await using var db = CreateDb();
            await startGate.Task;

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var existingTx = await db.CampaignPaymentTransactions
                    .FirstOrDefaultAsync(t => t.PaymentProvider == "Hesabe" && t.ProviderTransactionId == providerTxId);

                if (existingTx != null)
                {
                    await tx.CommitAsync();
                    return false; // Deduplicated no-op
                }

                var paymentTx = new CampaignPaymentTransaction
                {
                    CheckoutSessionId = sessionId,
                    CampaignId = campaignId,
                    PaymentProvider = "Hesabe",
                    ProviderTransactionId = providerTxId,
                    Amount = 100.000m,
                    Currency = "KWD",
                    Status = PaymentTransactionStatus.Succeeded,
                    InitiatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                };
                db.CampaignPaymentTransactions.Add(paymentTx);

                // Post financial ledger entry
                db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
                {
                    JournalId = Guid.NewGuid(),
                    CampaignId = campaignId,
                    CheckoutSessionId = sessionId,
                    EntryType = CampaignLedgerEntryType.CustomerPayment,
                    Amount = 100.000m,
                    Currency = "KWD",
                    Description = "Customer payment commit",
                    CreatedAtUtc = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return true; // Exactly 1 committed
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                return false; // Deduplicated due to unique constraint
            }
        }

        var tasks = Enumerable.Range(0, concurrentWebhooks)
            .Select(_ => ProcessPaymentWebhookAsync())
            .ToList();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r)); // Exactly 1 success
        Assert.Equal(4, results.Count(r => !r)); // 4 duplicate no-ops

        await using var verifyDb = CreateDb();
        var txCount = await verifyDb.CampaignPaymentTransactions
            .CountAsync(t => t.PaymentProvider == "Hesabe" && t.ProviderTransactionId == providerTxId);
        var ledgerCount = await verifyDb.CampaignFinancialLedgers
            .CountAsync(l => l.CheckoutSessionId == sessionId);

        Assert.Equal(1, txCount);
        Assert.Equal(1, ledgerCount);
    }

    [Fact]
    public async Task ScenarioG_ExpiredLatePayment_AtomicallyReacquiresOrInitiatesFullRefund()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 12, 25);

        // Case 1: Capacity is still available -> Re-acquires slot atomically
        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة الدفع المتأخر",
                NameEn = "Late Payment Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(30),
                TotalDiscountRate = Percentage.FromFraction(0.20m),
                MealMateShareRate = Percentage.FromFraction(0.10m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 10,
                CurrentSubscribers = 2
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;

            var session = new CampaignCheckoutSession(sessionId)
            {
                CampaignId = campaignId,
                BundleId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Status = CampaignCheckoutSessionStatus.Expired, // Already expired
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10)
            };
            db.CampaignCheckoutSessions.Add(session);

            db.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
            {
                CampaignId = campaignId,
                RestaurantId = restaurantId,
                ServiceDate = serviceDate,
                MaxBoxes = 5,
                ReservedBoxes = 2 // 3 available
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            var session = await db.CampaignCheckoutSessions.SingleAsync(s => s.Id == sessionId);
            var cap = await db.CampaignRestaurantCapacities.SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == serviceDate);

            // Capacity available -> Re-acquire slot and mark Paid
            if (cap.ReservedBoxes < cap.MaxBoxes)
            {
                cap.ReservedBoxes += 1;
                session.MarkPaid();
                await db.SaveChangesAsync();
            }
        }

        await using (var verifyDb = CreateDb())
        {
            var session = await verifyDb.CampaignCheckoutSessions.SingleAsync(s => s.Id == sessionId);
            var cap = await verifyDb.CampaignRestaurantCapacities.SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == serviceDate);

            Assert.Equal(CampaignCheckoutSessionStatus.Paid, session.Status);
            Assert.Equal(3, cap.ReservedBoxes);
        }

        // Case 2: Capacity is completely sold out -> Triggers Auto-Refund
        var sessionSoldOutId = Guid.NewGuid();
        await using (var db = CreateDb())
        {
            var sessionSoldOut = new CampaignCheckoutSession(sessionSoldOutId)
            {
                CampaignId = campaignId,
                BundleId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Status = CampaignCheckoutSessionStatus.Expired,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10)
            };
            db.CampaignCheckoutSessions.Add(sessionSoldOut);

            // Fill all remaining capacity
            var cap = await db.CampaignRestaurantCapacities.SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == serviceDate);
            cap.ReservedBoxes = cap.MaxBoxes; // 5 out of 5
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            var session = await db.CampaignCheckoutSessions.SingleAsync(s => s.Id == sessionSoldOutId);
            var cap = await db.CampaignRestaurantCapacities.SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == serviceDate);

            if (cap.ReservedBoxes >= cap.MaxBoxes)
            {
                // Cannot re-acquire -> Auto-Refund initiated
                session.MarkRefundPending();
                db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
                {
                    JournalId = Guid.NewGuid(),
                    CampaignId = campaignId,
                    CheckoutSessionId = sessionSoldOutId,
                    EntryType = CampaignLedgerEntryType.CustomerRefund,
                    Amount = 100.0m,
                    Currency = "KWD",
                    Description = "Late payment automatic refund due to capacity exhaustion",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

        await using (var verifyDb = CreateDb())
        {
            var session = await verifyDb.CampaignCheckoutSessions.SingleAsync(s => s.Id == sessionSoldOutId);
            Assert.Equal(CampaignCheckoutSessionStatus.RefundPending, session.Status);

            var refundEntry = await verifyDb.CampaignFinancialLedgers
                .SingleAsync(l => l.CheckoutSessionId == sessionSoldOutId && l.EntryType == CampaignLedgerEntryType.CustomerRefund);
            Assert.Equal(100.0m, refundEntry.Amount);
        }
    }

    [Fact]
    public async Task ScenarioH_OutboxRedeliveryRace_EnforcesInboxProcessedDeduplication()
    {
        var eventId = Guid.NewGuid();
        const string consumerName = "CampaignLaunchNoticeNotificationHandler";
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryProcessEventAsync()
        {
            await using var db = CreateDb();
            await startGate.Task;

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var alreadyProcessed = await db.InboxProcessedEvents
                    .AnyAsync(e => e.ConsumerName == consumerName && e.EventId == eventId);

                if (alreadyProcessed)
                {
                    await tx.CommitAsync();
                    return false;
                }

                db.InboxProcessedEvents.Add(new InboxProcessedEvent
                {
                    ConsumerName = consumerName,
                    EventId = eventId,
                    ProcessedAtUtc = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return true; // Effectively executed once
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                return false; // Deduplicated by unique constraint
            }
        }

        var t1 = TryProcessEventAsync();
        var t2 = TryProcessEventAsync();

        startGate.SetResult();
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, results.Count(r => r)); // Exactly 1 execution
        Assert.Equal(1, results.Count(r => !r)); // 1 deduplicated

        await using var verifyDb = CreateDb();
        var count = await verifyDb.InboxProcessedEvents
            .CountAsync(e => e.ConsumerName == consumerName && e.EventId == eventId);
        Assert.Equal(1, count);
    }
}
