using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.CustomerCheckout;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MealMate.Application.Common.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CustomerCheckout;

/// <summary>
/// Real-PostgreSQL proofs for the family checkout batch model: unique open-batch
/// per group, per-group idempotency keys, gateway payment uniqueness, money
/// check constraints, financial-history delete protection, and the family-group
/// row lock serializing concurrent batch creation. In-memory tests cannot prove
/// any of these.
/// </summary>
public sealed class FamilyCheckoutBatchPersistenceTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker is required for family checkout batch integration tests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Only_one_open_batch_per_family_group_is_allowed()
    {
        var groupId = await SeedGroupAsync();
        await using var db = CreateDb();
        db.FamilyCheckoutBatches.Add(NewBatch(groupId, status: FamilyCheckoutBatchStatus.Draft));
        await db.SaveChangesAsync();

        db.FamilyCheckoutBatches.Add(NewBatch(groupId, status: FamilyCheckoutBatchStatus.PaymentPending));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // After the first batch is Paid, a new open batch is allowed.
        var first = await db.FamilyCheckoutBatches.SingleAsync(x => x.FamilyGroupId == groupId);
        first.Status = FamilyCheckoutBatchStatus.Paid;
        await db.SaveChangesAsync();
        db.FamilyCheckoutBatches.Add(NewBatch(groupId, status: FamilyCheckoutBatchStatus.Draft));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.FamilyCheckoutBatches.CountAsync(x => x.FamilyGroupId == groupId));
    }

    [Fact]
    public async Task Initial_draft_can_coexist_with_one_renewal_pending_but_only_one_invoice_can_be_pending()
    {
        var draftGroup = await SeedGroupAsync();
        await using (var db = CreateDb())
        {
            db.FamilyCheckoutBatches.Add(NewBatch(draftGroup, FamilyCheckoutBatchStatus.Draft));
            await db.SaveChangesAsync();
            db.FamilyCheckoutBatches.Add(NewBatch(draftGroup, FamilyCheckoutBatchStatus.PaymentPending, purpose: "Renewal"));
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.FamilyCheckoutBatches.CountAsync(x => x.FamilyGroupId == draftGroup));
        }

        var initialPendingGroup = await SeedGroupAsync();
        await using (var db = CreateDb())
        {
            db.FamilyCheckoutBatches.Add(NewBatch(initialPendingGroup, FamilyCheckoutBatchStatus.PaymentPending));
            await db.SaveChangesAsync();
            db.FamilyCheckoutBatches.Add(NewBatch(initialPendingGroup, FamilyCheckoutBatchStatus.PaymentPending, purpose: "Renewal"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var renewalPendingGroup = await SeedGroupAsync();
        await using (var db = CreateDb())
        {
            db.FamilyCheckoutBatches.Add(NewBatch(renewalPendingGroup, FamilyCheckoutBatchStatus.PaymentPending, purpose: "Renewal"));
            await db.SaveChangesAsync();
            db.FamilyCheckoutBatches.Add(NewBatch(renewalPendingGroup, FamilyCheckoutBatchStatus.PaymentPending, purpose: "Renewal"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Idempotency_key_is_unique_within_group_but_reusable_across_groups()
    {
        var groupA = await SeedGroupAsync();
        var groupB = await SeedGroupAsync();
        await using var db = CreateDb();

        db.FamilyCheckoutBatches.Add(NewBatch(groupA, idempotencyKey: "retry-1"));
        await db.SaveChangesAsync();

        db.FamilyCheckoutBatches.Add(NewBatch(groupA, idempotencyKey: "retry-1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        db.FamilyCheckoutBatches.Add(NewBatch(groupB, idempotencyKey: "retry-1"));
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.FamilyCheckoutBatches.CountAsync(x => x.FamilyGroupId == groupB));
    }

    [Fact]
    public async Task Same_gateway_payment_cannot_be_attached_to_two_batches()
    {
        var groupA = await SeedGroupAsync();
        var groupB = await SeedGroupAsync();
        await using var db = CreateDb();
        db.FamilyCheckoutBatches.Add(NewBatch(groupA, gatewayPaymentId: "PAY-DUP"));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        db.FamilyCheckoutBatches.Add(NewBatch(groupB, gatewayPaymentId: "PAY-DUP"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Migrated_lines_persist_promo_discount_and_enforce_exact_net_paid()
    {
        var groupId = await SeedGroupAsync();
        var memberId = await SeedMemberAsync(groupId);
        await using var db = CreateDb();
        var batch = NewBatch(groupId);
        var line = new FamilyCheckoutBatchLine
        {
            BatchId = batch.Id,
            FamilyMemberId = memberId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 9, 1),
            AddressLine = "Kuwait City",
            PriceBeforeFamilyDiscount = 100m,
            FamilyDiscountAllocated = 10m,
            PromoCodeDiscountAllocated = 5.125m,
            NetPaidAmount = 84.875m,
        };
        batch.Lines.Add(line);
        db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.FamilyCheckoutBatchLines.SingleAsync(x => x.Id == line.Id);
        Assert.Equal(5.125m, reloaded.PromoCodeDiscountAllocated);
        Assert.Equal(84.875m, reloaded.NetPaidAmount);

        reloaded.NetPaidAmount = 85m;
        var mismatch = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(mismatch.InnerException).SqlState);
        db.ChangeTracker.Clear();

        reloaded = await db.FamilyCheckoutBatchLines.SingleAsync(x => x.Id == line.Id);
        reloaded.PromoCodeDiscountAllocated = -1m;
        reloaded.NetPaidAmount = 91m;
        var negative = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(negative.InnerException).SqlState);
    }

    [Fact]
    public async Task Line_net_paid_cannot_exceed_price_before_discount_or_go_negative()
    {
        var groupId = await SeedGroupAsync();
        var memberId = await SeedMemberAsync(groupId);
        await using var db = CreateDb();
        var batch = NewBatch(groupId);
        batch.Lines.Add(new FamilyCheckoutBatchLine
        {
            BatchId = batch.Id,
            FamilyMemberId = memberId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 9, 1),
            AddressLine = "Kuwait City",
            PriceBeforeFamilyDiscount = 100m,
            FamilyDiscountAllocated = 10m,
            NetPaidAmount = 90m,
        });
        db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.FamilyCheckoutBatchLines.CountAsync());

        batch.Lines.Single().NetPaidAmount = 101m;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var reloaded = await db.FamilyCheckoutBatchLines.SingleAsync();
        reloaded.NetPaidAmount = -1m;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Three_decimal_currency_amounts_round_trip_exactly()
    {
        var groupId = await SeedGroupAsync();
        var memberId = await SeedMemberAsync(groupId);
        await using var db = CreateDb();
        var batch = NewBatch(groupId);
        batch.Subtotal = 133.875m;
        batch.FamilyDiscountAmount = 13.388m;
        batch.FinalPrice = 120.487m;
        batch.Lines.Add(new FamilyCheckoutBatchLine
        {
            BatchId = batch.Id,
            FamilyMemberId = memberId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 9, 1),
            AddressLine = "Kuwait City",
            PriceBeforeFamilyDiscount = 133.875m,
            FamilyDiscountAllocated = 13.388m,
            NetPaidAmount = 120.487m,
        });
        db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.FamilyCheckoutBatches.Include(x => x.Lines).SingleAsync(x => x.FamilyGroupId == groupId);
        Assert.Equal(133.875m, reloaded.Subtotal);
        Assert.Equal(120.487m, reloaded.FinalPrice);
        Assert.Equal(120.487m, reloaded.Lines.Single().NetPaidAmount);
    }

    [Fact]
    public async Task Database_blocks_hard_delete_of_member_with_paid_batch_history()
    {
        var groupId = await SeedGroupAsync();
        Guid memberId;
        await using (var db = CreateDb())
        {
            var member = new FamilyMember
            {
                FamilyGroupId = groupId,
                Role = Domain.Enums.FamilyMemberRole.Member,
                DisplayName = "Paid Member",
                RelationshipCode = "child",
                NutritionalGoalCode = "maintain",
            };
            memberId = member.Id;
            db.FamilyMembers.Add(member);
            var batch = NewBatch(groupId, status: FamilyCheckoutBatchStatus.Paid);
            batch.Lines.Add(new FamilyCheckoutBatchLine
            {
                BatchId = batch.Id,
                FamilyMemberId = memberId,
                ProgramId = Guid.NewGuid(),
                BundleId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 9, 1),
                AddressLine = "Kuwait City",
                PriceBeforeFamilyDiscount = 90m,
                FamilyDiscountAllocated = 0m,
                NetPaidAmount = 90m,
            });
            db.FamilyCheckoutBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        await using var raw = CreateDb();
        await Assert.ThrowsAsync<PostgresException>(() =>
            raw.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"FamilyMembers\" WHERE \"Id\" = {memberId}"));
    }

    [Fact]
    public async Task Concurrent_batch_creation_for_same_group_yields_exactly_one_open_batch()
    {
        var groupId = await SeedGroupAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryCreateAsync()
        {
            await using var db = CreateDb();
            var lockManager = new PostgresApplicationLockManager(db);
            var transactions = new PostgresCheckoutTransactionExecutor(db);
            await start.Task;
            return await transactions.ExecuteAsync(async ct =>
            {
                await lockManager.LockFamilyGroupAsync(groupId, ct);
                var openExists = await db.FamilyCheckoutBatches.AsNoTracking()
                    .AnyAsync(x => x.FamilyGroupId == groupId
                        && (x.Status == FamilyCheckoutBatchStatus.Draft || x.Status == FamilyCheckoutBatchStatus.PaymentPending), ct);
                if (openExists) return false;
                db.FamilyCheckoutBatches.Add(NewBatch(groupId));
                await db.SaveChangesAsync(ct);
                return true;
            });
        }

        var first = TryCreateAsync();
        var second = TryCreateAsync();
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, x => x);
        await using var verification = CreateDb();
        Assert.Equal(1, await verification.FamilyCheckoutBatches.CountAsync(x => x.FamilyGroupId == groupId));
    }

    private async Task<Guid> SeedMemberAsync(Guid groupId)
    {
        await using var db = CreateDb();
        var member = new FamilyMember
        {
            FamilyGroupId = groupId,
            Role = Domain.Enums.FamilyMemberRole.Member,
            DisplayName = "Member",
            RelationshipCode = "child",
            NutritionalGoalCode = "maintain",
        };
        db.FamilyMembers.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task<Guid> SeedGroupAsync()
    {
        await using var db = CreateDb();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = Guid.NewGuid(),
            Name = "Family",
            Status = Domain.Enums.FamilyGroupStatus.Draft,
        };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    private static FamilyCheckoutBatch NewBatch(
        Guid groupId,
        FamilyCheckoutBatchStatus status = FamilyCheckoutBatchStatus.Draft,
        string idempotencyKey = "",
        string? gatewayPaymentId = null,
        string purpose = "Initial")
        => new()
        {
            FamilyGroupId = groupId,
            Purpose = purpose,
            PayerCustomerUserId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey.Length == 0 ? Guid.NewGuid().ToString("N") : idempotencyKey,
            Status = status,
            PackageId = Guid.NewGuid(),
            PackageMinMembers = 2,
            PackageMaxMembers = 6,
            FamilyDiscountPercent = 0.10m,
            CurrencyCode = "KWD",
            Subtotal = 200m,
            FamilyDiscountAmount = 20m,
            FinalPrice = 180m,
            GatewayPaymentId = gatewayPaymentId,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
