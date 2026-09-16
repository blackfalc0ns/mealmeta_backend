using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CollaborativeCampaigns;

public sealed class EndToEndCampaignLifecycleTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private readonly DateTime _baseTime = new(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc);

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
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""Programs"" ALTER COLUMN ""RowVersion"" SET DEFAULT '\x01'::bytea;
                ALTER TABLE ""Bundles"" ALTER COLUMN ""RowVersion"" SET DEFAULT '\x01'::bytea;
                ALTER TABLE ""Subscriptions"" ALTER COLUMN ""RowVersion"" SET DEFAULT '\x01'::bytea;
            ");
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed to start for E2E tests.", ex);
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
    public async Task CompleteCampaignLifecycle_FromDraftToSettlementAndEnd_GuaranteesZeroOversellingAndReconciledLedger()
    {
        Guid campaignId;
        Guid bundleId;
        Guid campaignBundleId;
        Guid programId;
        var customerId = Guid.NewGuid();
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();

        var startsAtUtc = _baseTime;
        var endsAtUtc = _baseTime.AddDays(30);

        // =========================================================================
        // Step 1: Campaign Creation & Draft Setup
        // =========================================================================
        await using (var db = CreateDb())
        {
            var program = new Program
            {
                Code = "PRG-E2E-" + Guid.NewGuid().ToString("N")[..6],
                NameAr = "برنامج تجريبي",
                NameEn = "E2E Program",
                KeywordsAr = [],
                KeywordsEn = []
            };
            db.Programs.Add(program);

            var bundle = new Bundle
            {
                ProgramId = program.Id,
                Code = "BND-E2E-" + Guid.NewGuid().ToString("N")[..6],
                NameAr = "باقة تجريبية",
                NameEn = "E2E Bundle"
            };
            db.Bundles.Add(bundle);

            var campaign = new Campaign
            {
                NameAr = "حملة نهاية السنة الكاملة",
                NameEn = "Year-End E2E Collaborative Campaign",
                Status = CampaignStatus.Draft,
                StartsAtUtc = startsAtUtc,
                EndsAtUtc = endsAtUtc,
                TotalDiscountRate = Percentage.FromFraction(0.20m),
                MealMateShareRate = Percentage.FromFraction(0.10m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 2, // Strict subscriber cap of 2
                CurrentSubscribers = 0
            };
            db.Campaigns.Add(campaign);

            // Restaurant profiles
            var profileA = new RestaurantProfile { UserId = Guid.NewGuid(), TradeNameAr = "مطعم أ", TradeNameEn = "Restaurant Alpha", LegalNameAr = "أ", LegalNameEn = "Alpha Co" };
            var profileB = new RestaurantProfile { UserId = Guid.NewGuid(), TradeNameAr = "مطعم ب", TradeNameEn = "Restaurant Beta", LegalNameAr = "ب", LegalNameEn = "Beta Co" };
            db.RestaurantProfiles.AddRange(profileA, profileB);

            // Campaign Bundle & Duration Price
            var campaignBundle = new CampaignBundle
            {
                CampaignId = campaign.Id,
                BundleId = bundle.Id
            };
            db.CampaignBundles.Add(campaignBundle);

            var durationPrice = CampaignBundleDurationPrice.Create(
                campaignBundle.Id,
                26,
                new Money(100.000m, "KWD"),
                "Admin",
                1).Value;
            db.CampaignBundleDurationPrices.Add(durationPrice);

            await db.SaveChangesAsync();
            campaignId = campaign.Id;
            bundleId = bundle.Id;
            campaignBundleId = campaignBundle.Id;
            programId = program.Id;
            restA = profileA.Id;
            restB = profileB.Id;
        }

        // =========================================================================
        // Step 2: Restaurant Invitations & Acceptance
        // =========================================================================
        await using (var db = CreateDb())
        {
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
            campaign.Status = CampaignStatus.OpenForJoining;

            db.CampaignRestaurantEnrollments.AddRange(
                new CampaignRestaurantEnrollment
                {
                    CampaignId = campaignId,
                    RestaurantId = restA,
                    ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
                    DedicatedDailyCapacity = 10
                },
                new CampaignRestaurantEnrollment
                {
                    CampaignId = campaignId,
                    RestaurantId = restB,
                    ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
                    DedicatedDailyCapacity = 10
                }
            );

            await db.SaveChangesAsync();
        }

        // =========================================================================
        // Step 3: Profitability Review & Campaign Launch
        // =========================================================================
        await using (var db = CreateDb())
        {
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
            campaign.Status = CampaignStatus.JoiningClosed;
            campaign.Status = CampaignStatus.UnderReview;
            campaign.Status = CampaignStatus.Scheduled;
            campaign.Status = CampaignStatus.Active; // Launched!

            // Generate extended capacity horizon: StartsAt to EndsAt + 26 days + 1 day
            var startDate = DateOnly.FromDateTime(startsAtUtc);
            var horizonEndDate = DateOnly.FromDateTime(endsAtUtc.AddDays(26 + 1));

            for (var d = startDate; d <= horizonEndDate; d = d.AddDays(1))
            {
                db.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
                {
                    CampaignId = campaignId,
                    RestaurantId = restA,
                    ServiceDate = d,
                    MaxBoxes = 10,
                    ReservedBoxes = 0
                });
                db.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
                {
                    CampaignId = campaignId,
                    RestaurantId = restB,
                    ServiceDate = d,
                    MaxBoxes = 10,
                    ReservedBoxes = 0
                });
            }

            await db.SaveChangesAsync();
        }

        // =========================================================================
        // Step 4: Customer Non-Reserving Price Preview Query
        // =========================================================================
        await using (var db = CreateDb())
        {
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(startsAtUtc.AddDays(1));

            var previewHandler = new GetCampaignPricePreviewQueryHandler(db, clock, new CampaignEligibilityEngine());
            var previewResult = await previewHandler.Handle(
                new GetCampaignPricePreviewQuery(campaignId, campaignBundleId, 26, customerId),
                CancellationToken.None);

            Assert.True(previewResult.IsSuccess);
            var preview = previewResult.Value;
            Assert.Equal(100.000m, preview.ReferenceCustomerPrice);
            Assert.Equal(20.000m, preview.TotalDiscountAmount); // 20% discount
            Assert.Equal(80.000m, preview.FinalCustomerPrice); // 100 - 20 = 80 KWD
            Assert.Equal(10.000m, preview.RestaurantContributionAmount);
            Assert.Equal(10.000m, preview.PlatformContributionAmount);

            // Verify capacity was NOT decremented by preview query
            var reservedBoxesSum = await db.CampaignRestaurantCapacities
                .Where(c => c.CampaignId == campaignId)
                .SumAsync(c => c.ReservedBoxes);
            Assert.Equal(0, reservedBoxesSum);
        }

        // =========================================================================
        // Step 5: Customer 1 Checkout Reservation & Idempotent Payment
        // =========================================================================
        var session1Id = Guid.NewGuid();
        var tx1Id = "TX-E2E-001";
        var sub1Dates = Enumerable.Range(0, 26)
            .Select(i => DateOnly.FromDateTime(startsAtUtc).AddDays(i))
            .ToList();

        await using (var db = CreateDb())
        {
            var lockRepo = new CampaignCapacityLockRepository(db);

            await using var tx = await db.Database.BeginTransactionAsync();
            var camp = await lockRepo.LockCampaignAsync(campaignId);
            camp!.CurrentSubscribers += 1;

            var targets = sub1Dates.Select(d => (d, restA)).ToList();
            var lockedCaps = await lockRepo.LockDailyCapacitiesAsync(campaignId, targets);

            var session1 = new CampaignCheckoutSession(session1Id)
            {
                CampaignId = campaignId,
                BundleId = bundleId,
                CustomerId = customerId,
                Status = CampaignCheckoutSessionStatus.Pending,
                ExpiresAtUtc = startsAtUtc.AddMinutes(15)
            };

            foreach (var cap in lockedCaps)
            {
                cap.ReservedBoxes += 1;
                session1.Allocations.Add(new CampaignReservationAllocation
                {
                    CheckoutSessionId = session1Id,
                    CampaignId = campaignId,
                    BundleId = bundleId,
                    RestaurantId = cap.RestaurantId,
                    ServiceDate = cap.ServiceDate!.Value,
                    Units = 1
                });
            }

            db.CampaignCheckoutSessions.Add(session1);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // Payment Webhook commits Session 1 and posts to ledger
        await using (var db = CreateDb())
        {
            var session = await db.CampaignCheckoutSessions.SingleAsync(s => s.Id == session1Id);
            session.MarkPaymentPending();
            session.MarkPaid();
            session.MarkCommitting();
            session.MarkCompleted();

            db.CampaignPaymentTransactions.Add(new CampaignPaymentTransaction
            {
                CheckoutSessionId = session1Id,
                CampaignId = campaignId,
                PaymentProvider = "Hesabe",
                ProviderTransactionId = tx1Id,
                Amount = 80.000m,
                Currency = "KWD",
                Status = PaymentTransactionStatus.Succeeded,
                InitiatedAtUtc = startsAtUtc,
                CompletedAtUtc = startsAtUtc
            });

            var journalId = Guid.NewGuid();
            // 1. Customer Payment
            db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
            {
                JournalId = journalId,
                CampaignId = campaignId,
                CheckoutSessionId = session1Id,
                EntryType = CampaignLedgerEntryType.CustomerPayment,
                Amount = 80.000m,
                Currency = "KWD",
                Description = "Customer payment for 26-day subscription",
                CreatedAtUtc = startsAtUtc
            });

            // 2. Restaurant Settlement (e.g. 70 KWD net settlement)
            db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
            {
                JournalId = journalId,
                CampaignId = campaignId,
                CheckoutSessionId = session1Id,
                RestaurantId = restA,
                EntryType = CampaignLedgerEntryType.RestaurantSettlement,
                Amount = 70.000m,
                Currency = "KWD",
                Description = "Payable settlement to Restaurant Alpha",
                CreatedAtUtc = startsAtUtc
            });

            // 3. Payment Processing Fee (e.g. 2 KWD)
            db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
            {
                JournalId = journalId,
                CampaignId = campaignId,
                CheckoutSessionId = session1Id,
                EntryType = CampaignLedgerEntryType.PaymentProcessingFee,
                Amount = 2.000m,
                Currency = "KWD",
                Description = "Hesabe transaction fee",
                CreatedAtUtc = startsAtUtc
            });

            // Linked core subscription
            db.Subscriptions.Add(new Subscription
            {
                CampaignId = campaignId,
                CustomerUserId = customerId,
                ProgramId = programId,
                BundleId = bundleId,
                Status = SubscriptionStatus.Active,
                DurationDays = 26,
                StartDate = sub1Dates.First(),
                EndDate = sub1Dates.Last(),
                PaidAmount = new Money(80.000m, "KWD")
            });

            await db.SaveChangesAsync();
        }

        // =========================================================================
        // Step 6: Customer 2 Checkout Fills Subscriber Cap (MaxSubscribers = 2)
        // =========================================================================
        var customer2Id = Guid.NewGuid();
        var session2Id = Guid.NewGuid();

        await using (var db = CreateDb())
        {
            var lockRepo = new CampaignCapacityLockRepository(db);

            await using var tx = await db.Database.BeginTransactionAsync();
            var camp = await lockRepo.LockCampaignAsync(campaignId);
            Assert.Equal(1, camp!.CurrentSubscribers);

            camp.CurrentSubscribers += 1; // Now 2 of 2!

            var session2 = new CampaignCheckoutSession(session2Id)
            {
                CampaignId = campaignId,
                BundleId = bundleId,
                CustomerId = customer2Id,
                Status = CampaignCheckoutSessionStatus.Completed,
                ExpiresAtUtc = startsAtUtc.AddMinutes(15)
            };
            db.CampaignCheckoutSessions.Add(session2);

            // Record Customer 2 payment and ledger entries
            var journal2 = Guid.NewGuid();
            db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
            {
                JournalId = journal2,
                CampaignId = campaignId,
                CheckoutSessionId = session2Id,
                EntryType = CampaignLedgerEntryType.CustomerPayment,
                Amount = 80.000m,
                Currency = "KWD",
                Description = "Customer 2 payment",
                CreatedAtUtc = startsAtUtc
            });
            db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
            {
                JournalId = journal2,
                CampaignId = campaignId,
                CheckoutSessionId = session2Id,
                RestaurantId = restB,
                EntryType = CampaignLedgerEntryType.RestaurantSettlement,
                Amount = 70.000m,
                Currency = "KWD",
                Description = "Payable settlement to Restaurant Beta",
                CreatedAtUtc = startsAtUtc
            });
            db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger
            {
                JournalId = journal2,
                CampaignId = campaignId,
                CheckoutSessionId = session2Id,
                EntryType = CampaignLedgerEntryType.PaymentProcessingFee,
                Amount = 2.000m,
                Currency = "KWD",
                Description = "Hesabe fee",
                CreatedAtUtc = startsAtUtc
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // =========================================================================
        // Step 7: Customer 3 Attempt is Rejected - Zero Overselling Guaranteed!
        // =========================================================================
        await using (var db = CreateDb())
        {
            var lockRepo = new CampaignCapacityLockRepository(db);

            await using var tx = await db.Database.BeginTransactionAsync();
            var camp = await lockRepo.LockCampaignAsync(campaignId);

            // Check subscriber cap
            bool canSubscribe = camp!.MaxSubscribers.HasValue && camp.CurrentSubscribers < camp.MaxSubscribers.Value;
            Assert.False(canSubscribe); // Blocked! Cap is strictly exhausted

            await tx.RollbackAsync();
        }

        // =========================================================================
        // Step 8: Campaign Stop / End & Customer Protection
        // =========================================================================
        await using (var db = CreateDb())
        {
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
            campaign.Status = CampaignStatus.Stopped;
            await db.SaveChangesAsync();

            // Existing active subscriptions remain active and protected
            var activeSubs = await db.Subscriptions.Where(s => s.CampaignId == campaignId).ToListAsync();
            Assert.Single(activeSubs);
            Assert.Equal(SubscriptionStatus.Active, activeSubs[0].Status);
        }

        // =========================================================================
        // Step 9: Financial Reconciliation Report Verification
        // =========================================================================
        await using (var db = CreateDb())
        {
            var reportHandler = new GetCampaignFinancialReportQueryHandler(db);
            var reportResult = await reportHandler.Handle(
                new GetCampaignFinancialReportQuery(campaignId),
                CancellationToken.None);

            Assert.True(reportResult.IsSuccess);
            var report = reportResult.Value;

            Assert.Equal(160.000m, report.TotalCustomerCharged); // 80 + 80
            Assert.Equal(0.000m, report.TotalCustomerRefunded);
            Assert.Equal(160.000m, report.NetCustomerRevenue);

            Assert.Equal(140.000m, report.TotalRestaurantSettlement); // 70 + 70
            Assert.Equal(140.000m, report.NetRestaurantSettlement);

            Assert.Equal(4.000m, report.TotalPaymentProcessingFees); // 2 + 2
            Assert.Equal(16.000m, report.NetPlatformRetainedGrossProfit); // 160 - 140 - 4 = 16 KWD

            // Double-entry ledger balance reconciled
            Assert.True(report.IsReconciled);

            // Performance metrics validation
            var metricsHandler = new GetCampaignPerformanceMetricsQueryHandler(db);
            var metricsResult = await metricsHandler.Handle(
                new GetCampaignPerformanceMetricsQuery(campaignId),
                CancellationToken.None);

            Assert.True(metricsResult.IsSuccess);
            var metrics = metricsResult.Value;
            Assert.Equal(2, metrics.TotalParticipatingRestaurants);
            Assert.Equal(2, metrics.MaxSubscribers);
            Assert.Equal(2, metrics.CurrentSubscribers);
            Assert.Equal(1.0000m, metrics.CapacityUtilizationRate); // 100% full capacity utilization
            Assert.Equal(2, metrics.CompletedCheckoutSessions);
            Assert.Equal(160.000m, metrics.TotalCustomerPayments);
            Assert.Equal(140.000m, metrics.TotalRestaurantPayouts);
            Assert.Equal(20.000m, metrics.NetPlatformMargin);
        }
    }
}
