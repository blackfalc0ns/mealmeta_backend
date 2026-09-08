using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyDraftExpiryTests
{
    [Fact]
    public async Task ExpireDueAsync_removes_stale_draft()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        db.FamilyGroups.Add(StaleGroup(FamilyGroupStatus.Draft, now));
        await db.SaveChangesAsync();

        var expired = await FamilyDraftExpiry.ExpireDueAsync(db, now, CancellationToken.None);

        expired.Should().Be(1);
        (await db.FamilyGroups.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExpireDueAsync_does_not_remove_checkout_pending_group()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        db.FamilyGroups.Add(StaleGroup(FamilyGroupStatus.CheckoutPending, now));
        await db.SaveChangesAsync();

        var expired = await FamilyDraftExpiry.ExpireDueAsync(db, now, CancellationToken.None);

        expired.Should().Be(0);
        (await db.FamilyGroups.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveOrphanCheckoutPending_keeps_group_with_pending_payment()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        var group = StaleGroup(FamilyGroupStatus.CheckoutPending, now);
        var (program, bundle) = AddCatalog(db);
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = group.ManagerCustomerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = new DateOnly(2026, 9, 10),
            EndDate = new DateOnly(2026, 9, 22),
            PaidAmount = new Money(50m, "KWD"),
            FamilyGroupId = group.Id,
            LastOperationAtUtc = now,
        });
        await db.SaveChangesAsync();

        var removed = await FamilyDraftExpiry.RemoveOrphanCheckoutPendingAsync(db, now, CancellationToken.None);

        removed.Should().Be(0);
        (await db.FamilyGroups.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveOrphanCheckoutPending_removes_group_when_unpaid_siblings_are_gone()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        var group = StaleGroup(FamilyGroupStatus.CheckoutPending, now);
        var (program, bundle) = AddCatalog(db);
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = group.ManagerCustomerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            Status = SubscriptionStatus.Cancelled,
            StartDate = new DateOnly(2026, 9, 10),
            EndDate = new DateOnly(2026, 9, 22),
            PaidAmount = new Money(50m, "KWD"),
            FamilyGroupId = group.Id,
            LastOperationAtUtc = now,
        });
        await db.SaveChangesAsync();

        var removed = await FamilyDraftExpiry.RemoveOrphanCheckoutPendingAsync(db, now, CancellationToken.None);

        removed.Should().Be(1);
        (await db.FamilyGroups.CountAsync()).Should().Be(0);
    }

    private static FamilyGroup StaleGroup(FamilyGroupStatus status, DateTime now)
        => new()
        {
            ManagerCustomerUserId = Guid.NewGuid(),
            Name = "عائلة",
            Status = status,
            CreatedAtUtc = now.AddMinutes(-20),
            ModifiedAtUtc = now.AddMinutes(-20),
        };

    private static (Program Program, Bundle Bundle) AddCatalog(AppDbContext db)
    {
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Program = program,
            Code = "B",
            NameAr = "ب",
            NameEn = "B",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        db.AddRange(program, bundle);
        return (program, bundle);
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
