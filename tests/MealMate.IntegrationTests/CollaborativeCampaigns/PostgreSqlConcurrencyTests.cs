using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CollaborativeCampaigns;

public sealed class PostgreSqlConcurrencyTests : IAsyncLifetime
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
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed to start for concurrency tests.", ex);
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
    public async Task ScenarioA_LastDailyCapacity_AllowsExactlyOneSuccessAcrossParallelThreads()
    {
        var campaignId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 11, 15);

        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة السعة",
                NameEn = "Capacity Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(30),
                TotalDiscountRate = Percentage.FromFraction(0.20m),
                MealMateShareRate = Percentage.FromFraction(0.10m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 100
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;

            var capacity = new CampaignRestaurantCapacity
            {
                CampaignId = campaignId,
                RestaurantId = restaurantId,
                ServiceDate = serviceDate,
                MaxBoxes = 1, // Exactly 1 box available
                ReservedBoxes = 0
            };
            db.CampaignRestaurantCapacities.Add(capacity);
            await db.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int concurrentRequests = 10;

        async Task<bool> TryReserveCapacityAsync()
        {
            await using var db = CreateDb();
            var lockRepo = new CampaignCapacityLockRepository(db);

            await startGate.Task;

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var locked = await lockRepo.LockDailyCapacitiesAsync(
                    campaignId,
                    new[] { (serviceDate, restaurantId) });

                var target = locked.FirstOrDefault(c => c.ServiceDate == serviceDate && c.RestaurantId == restaurantId);
                if (target == null || target.ReservedBoxes >= target.MaxBoxes)
                {
                    await tx.RollbackAsync();
                    return false;
                }

                target.ReservedBoxes += 1;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                return false;
            }
        }

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => TryReserveCapacityAsync())
            .ToList();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r);
        var failureCount = results.Count(r => !r);

        Assert.Equal(1, successCount);
        Assert.Equal(9, failureCount);

        await using var verifyDb = CreateDb();
        var finalCap = await verifyDb.CampaignRestaurantCapacities
            .SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == serviceDate);

        Assert.Equal(1, finalCap.ReservedBoxes);
        Assert.Equal(finalCap.MaxBoxes, finalCap.ReservedBoxes);
    }

    [Fact]
    public async Task ScenarioB_LastSubscriberSlot_AllowsExactlyOneSuccessAcrossParallelThreads()
    {
        var campaignId = Guid.NewGuid();

        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة المشتركين",
                NameEn = "Subscriber Slot Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(30),
                TotalDiscountRate = Percentage.FromFraction(0.15m),
                MealMateShareRate = Percentage.FromFraction(0.05m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 1, // Exactly 1 slot
                CurrentSubscribers = 0
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int concurrentRequests = 10;

        async Task<bool> TryClaimSlotAsync()
        {
            await using var db = CreateDb();
            var lockRepo = new CampaignCapacityLockRepository(db);

            await startGate.Task;

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var lockedCamp = await lockRepo.LockCampaignAsync(campaignId);
                if (lockedCamp == null || (lockedCamp.MaxSubscribers.HasValue && lockedCamp.CurrentSubscribers >= lockedCamp.MaxSubscribers.Value))
                {
                    await tx.RollbackAsync();
                    return false;
                }

                lockedCamp.CurrentSubscribers += 1;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                return false;
            }
        }

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => TryClaimSlotAsync())
            .ToList();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(9, results.Count(r => !r));

        await using var verifyDb = CreateDb();
        var finalCamp = await verifyDb.Campaigns.SingleAsync(c => c.Id == campaignId);
        Assert.Equal(1, finalCamp.CurrentSubscribers);
    }

    [Fact]
    public async Task ScenarioC_MultiDayPartialFail_RollsBackAllDaysAtomically()
    {
        var campaignId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var baseDate = new DateOnly(2026, 12, 1);

        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة 26 يوم",
                NameEn = "26-Day Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(60),
                TotalDiscountRate = Percentage.FromFraction(0.20m),
                MealMateShareRate = Percentage.FromFraction(0.10m),
                RestaurantShareRate = Percentage.FromFraction(0.10m),
                MaxSubscribers = 50
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;

            // Seed 26 days: Days 1-25 have capacity 5, but Day 26 has capacity 0 (Sold out)
            for (int i = 0; i < 26; i++)
            {
                db.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
                {
                    CampaignId = campaignId,
                    RestaurantId = restaurantId,
                    ServiceDate = baseDate.AddDays(i),
                    MaxBoxes = (i == 25) ? 0 : 5,
                    ReservedBoxes = 0
                });
            }
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            var lockRepo = new CampaignCapacityLockRepository(db);

            await using var tx = await db.Database.BeginTransactionAsync();
            var targetDates = Enumerable.Range(0, 26)
                .Select(i => (baseDate.AddDays(i), restaurantId))
                .ToList();

            var lockedRows = await lockRepo.LockDailyCapacitiesAsync(campaignId, targetDates);

            bool allAvailable = true;
            foreach (var row in lockedRows)
            {
                if (row.ReservedBoxes >= row.MaxBoxes)
                {
                    allAvailable = false;
                    break;
                }
                row.ReservedBoxes += 1;
            }

            if (!allAvailable)
            {
                await tx.RollbackAsync();
            }
            else
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
        }

        // Verify zero days were committed
        await using var verifyDb = CreateDb();
        var allCapacities = await verifyDb.CampaignRestaurantCapacities
            .Where(c => c.CampaignId == campaignId)
            .ToListAsync();

        Assert.Equal(26, allCapacities.Count);
        Assert.All(allCapacities, c => Assert.Equal(0, c.ReservedBoxes));
    }

    [Fact]
    public async Task ScenarioD_DeadlockPrevention_KeyOrderedLockingAvoidsDeadlockUnderOverlap()
    {
        var campaignId = Guid.NewGuid();
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var date1 = new DateOnly(2027, 1, 10);
        var date2 = new DateOnly(2027, 1, 11);

        await using (var db = CreateDb())
        {
            var campaign = new Campaign
            {
                NameAr = "حملة منع التعليق",
                NameEn = "Deadlock Free Campaign",
                Status = CampaignStatus.Active,
                StartsAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddDays(30),
                TotalDiscountRate = Percentage.FromFraction(0.15m),
                MealMateShareRate = Percentage.FromFraction(0.05m),
                RestaurantShareRate = Percentage.FromFraction(0.10m)
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;

            db.CampaignRestaurantCapacities.AddRange(
                new CampaignRestaurantCapacity { CampaignId = campaignId, RestaurantId = restA, ServiceDate = date1, MaxBoxes = 10, ReservedBoxes = 0 },
                new CampaignRestaurantCapacity { CampaignId = campaignId, RestaurantId = restB, ServiceDate = date1, MaxBoxes = 10, ReservedBoxes = 0 },
                new CampaignRestaurantCapacity { CampaignId = campaignId, RestaurantId = restA, ServiceDate = date2, MaxBoxes = 10, ReservedBoxes = 0 },
                new CampaignRestaurantCapacity { CampaignId = campaignId, RestaurantId = restB, ServiceDate = date2, MaxBoxes = 10, ReservedBoxes = 0 }
            );
            await db.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Thread 1 requests (Date1, RestA) then (Date2, RestB)
        async Task<bool> ThreadOneAsync()
        {
            await using var db = CreateDb();
            var lockRepo = new CampaignCapacityLockRepository(db);
            await startGate.Task;

            await using var tx = await db.Database.BeginTransactionAsync();
            var targets = new[] { (date1, restA), (date2, restB) };
            var locked = await lockRepo.LockDailyCapacitiesAsync(campaignId, targets);
            foreach (var item in locked) item.ReservedBoxes += 1;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        // Thread 2 requests in reverse order: (Date2, RestB) then (Date1, RestA)
        async Task<bool> ThreadTwoAsync()
        {
            await using var db = CreateDb();
            var lockRepo = new CampaignCapacityLockRepository(db);
            await startGate.Task;

            await using var tx = await db.Database.BeginTransactionAsync();
            var targets = new[] { (date2, restB), (date1, restA) };
            var locked = await lockRepo.LockDailyCapacitiesAsync(campaignId, targets);
            foreach (var item in locked) item.ReservedBoxes += 1;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        var t1 = ThreadOneAsync();
        var t2 = ThreadTwoAsync();

        startGate.SetResult();
        var results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.True(r));

        await using var verifyDb = CreateDb();
        var capA = await verifyDb.CampaignRestaurantCapacities.SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == date1 && c.RestaurantId == restA);
        var capB = await verifyDb.CampaignRestaurantCapacities.SingleAsync(c => c.CampaignId == campaignId && c.ServiceDate == date2 && c.RestaurantId == restB);

        Assert.Equal(2, capA.ReservedBoxes);
        Assert.Equal(2, capB.ReservedBoxes);
    }
}
