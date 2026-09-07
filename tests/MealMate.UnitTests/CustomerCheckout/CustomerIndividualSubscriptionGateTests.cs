using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
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
        });
        await db.SaveChangesAsync();

        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.Null(await gate.FindBlockingAsync(customerId));
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
}
