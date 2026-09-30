using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class CustomerIndividualSubscriptionGateTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Frozen)]
    [InlineData(SubscriptionStatus.Paid)]
    [InlineData(SubscriptionStatus.Scheduled)]
    [InlineData(SubscriptionStatus.SuspendedForReview)]
    public async Task FindBlocking_returns_individual_current(SubscriptionStatus status)
    {
        var (db, customerId, subId) = await SeedAsync(status, familyGroupId: null);
        var gate = new CustomerIndividualSubscriptionGate(db);
        var blocking = await gate.FindBlockingAsync(customerId);
        Assert.NotNull(blocking);
        Assert.Equal(subId, blocking!.SubscriptionId);
        Assert.Equal(status, blocking.Status);
        Assert.True(await gate.HasBlockingAsync(customerId));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Completed)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    [InlineData(SubscriptionStatus.PaymentFailed)]
    [InlineData(SubscriptionStatus.PendingPayment)]
    [InlineData(SubscriptionStatus.Upgraded)]
    public async Task FindBlocking_ignores_non_current(SubscriptionStatus status)
    {
        var (db, customerId, _) = await SeedAsync(status, familyGroupId: null);
        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.Null(await gate.FindBlockingAsync(customerId));
        Assert.False(await gate.HasBlockingAsync(customerId));
    }

    [Fact]
    public async Task FindBlocking_ignores_family_group_subscription()
    {
        var (db, customerId, _) = await SeedAsync(SubscriptionStatus.Active, familyGroupId: Guid.NewGuid());
        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.Null(await gate.FindBlockingAsync(customerId));
    }

    [Fact]
    public async Task HasBlocking_true_when_manager_of_draft_family_without_individual()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        db.FamilyGroups.Add(new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "عائلة",
            Status = FamilyGroupStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.Null(await gate.FindBlockingAsync(customerId));
        Assert.True(await gate.HasBlockingFamilyGroupAsync(customerId));
        Assert.True(await gate.HasBlockingAsync(customerId));
    }

    [Theory]
    [InlineData(FamilyGroupStatus.Active, SubscriptionStatus.Completed)]
    [InlineData(FamilyGroupStatus.Active, SubscriptionStatus.Expired)]
    [InlineData(FamilyGroupStatus.Active, SubscriptionStatus.Cancelled)]
    [InlineData(FamilyGroupStatus.Active, SubscriptionStatus.Upgraded)]
    [InlineData(FamilyGroupStatus.Active, SubscriptionStatus.PaymentFailed)]
    [InlineData(FamilyGroupStatus.Frozen, SubscriptionStatus.Completed)]
    [InlineData(FamilyGroupStatus.Frozen, SubscriptionStatus.Expired)]
    public async Task HasBlockingFamilyGroup_WhenFamilyHasOnlyArchivedSubscriptions_ReturnsFalse(
        FamilyGroupStatus groupStatus, SubscriptionStatus subStatus)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "عائلة تاريخية",
            Status = groupStatus,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-60),
        };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var program = new Program { Code = "P1", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "B1", NameAr = "ب", NameEn = "B", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            FamilyGroupId = group.Id,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = subStatus,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100m, "KWD"),
        };
        db.AddRange(program, bundle, sub);
        await db.SaveChangesAsync();

        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.False(await gate.HasBlockingFamilyGroupAsync(customerId));
        Assert.False(await gate.HasBlockingAsync(customerId));
    }

    [Fact]
    public async Task HasBlockingFamilyGroup_WhenDraftIsExpired_ReturnsFalse()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var expiredTime = DateTime.UtcNow.AddMinutes(-30);
        db.FamilyGroups.Add(new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "عائلة منتهية الجلسة",
            Status = FamilyGroupStatus.Draft,
            CreatedAtUtc = expiredTime,
            ModifiedAtUtc = expiredTime,
        });
        await db.SaveChangesAsync();

        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.False(await gate.HasBlockingFamilyGroupAsync(customerId));
        Assert.False(await gate.HasBlockingAsync(customerId));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active, false, false)]
    [InlineData(SubscriptionStatus.PendingPayment, false, false)]
    [InlineData(SubscriptionStatus.PendingPayment, true, false)]
    [InlineData(SubscriptionStatus.PendingPayment, true, true)]
    public async Task HasBlockingFamilyGroup_CheckoutPendingRequiresLivePendingPaymentHold(
        SubscriptionStatus subscriptionStatus, bool hasHold, bool holdIsLive)
    {
        var now = DateTime.UtcNow;
        var (db, managerId) = await SeedCheckoutPendingAsync(
            subscriptionStatus,
            hasHold ? now.AddMinutes(holdIsLive ? 5 : -5) : null);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var gate = new CustomerIndividualSubscriptionGate(db, clock);

        Assert.Equal(
            subscriptionStatus == SubscriptionStatus.PendingPayment && hasHold && holdIsLive,
            await gate.HasBlockingFamilyGroupAsync(managerId));
    }

    [Theory]
    [InlineData(FamilyGroupStatus.Active, SubscriptionStatus.Active)]
    [InlineData(FamilyGroupStatus.Active, SubscriptionStatus.Paid)]
    [InlineData(FamilyGroupStatus.Frozen, SubscriptionStatus.Frozen)]
    public async Task HasBlockingFamilyGroup_WhenFamilyHasActiveSubscription_ReturnsTrue(
        FamilyGroupStatus groupStatus, SubscriptionStatus subStatus)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "عائلة نشطة",
            Status = groupStatus,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var program = new Program { Code = "P2", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "B2", NameAr = "ب", NameEn = "B", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            FamilyGroupId = group.Id,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = subStatus,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 26),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100m, "KWD"),
        };
        db.AddRange(program, bundle, sub);
        await db.SaveChangesAsync();

        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.True(await gate.HasBlockingFamilyGroupAsync(customerId));
        Assert.True(await gate.HasBlockingAsync(customerId));
    }

    private static async Task<(AppDbContext Db, Guid CustomerId, Guid SubscriptionId)> SeedAsync(
        SubscriptionStatus status, Guid? familyGroupId)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = status,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            FamilyGroupId = familyGroupId,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100m, "KWD"),
        };
        db.AddRange(program, bundle, sub);
        await db.SaveChangesAsync();
        return (db, customerId, sub.Id);
    }

    private static async Task<(AppDbContext Db, Guid ManagerId)> SeedCheckoutPendingAsync(
        SubscriptionStatus status, DateTime? holdExpiresAtUtc)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة قيد الدفع",
            Status = FamilyGroupStatus.CheckoutPending,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
        };
        var program = new Program { Code = "PENDING", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "PENDING", NameAr = "ب", NameEn = "B", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var subscription = new Subscription
        {
            CustomerUserId = managerId,
            FamilyGroupId = group.Id,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = status,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 26),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100m, "KWD"),
        };
        db.AddRange(group, program, bundle, subscription);
        if (holdExpiresAtUtc is DateTime expiresAt)
        {
            db.CapacityHolds.Add(new CapacityHold
            {
                RestaurantId = Guid.NewGuid(),
                SubscriptionId = subscription.Id,
                MenuPlanVersionId = Guid.NewGuid(),
                ServiceDate = new DateOnly(2026, 9, 2),
                Quantity = 1,
                Status = CapacityHoldStatus.PendingPayment,
                ExpiresAtUtc = expiresAt,
            });
        }
        await db.SaveChangesAsync();
        return (db, managerId);
    }
}
