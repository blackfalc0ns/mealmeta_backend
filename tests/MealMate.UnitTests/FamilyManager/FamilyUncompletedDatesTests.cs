using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.FamilyManager;

public sealed class FamilyUncompletedDatesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_uses_the_same_delivery_schedule_as_checkout(bool includeFriday)
    {
        await using var db = CreateDb();
        var clock = Clock();
        var group = SeedGroup(db, FamilyGroupStatus.Draft, includeFriday);
        await db.SaveChangesAsync();
        var schedule = new SubscriptionScheduleService(clock);
        var expectedStart = schedule.ResolveFirstDeliveryDate(group.Members.Single().StartDate!.Value, includeFriday);
        var dates = schedule.GetServiceDates(expectedStart, 12, includeFriday);

        var result = await new CustomerSubscriptionQueryService(db, clock).GetUncompletedSubscriptionsAsync(group.ManagerCustomerUserId);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal(dates[0], row.StartDate);
        Assert.Equal(dates[^1], row.EndDate);
        Assert.Equal(12, row.DurationDays);
        Assert.Equal(includeFriday, row.IncludeFriday);
    }

    [Fact]
    public async Task Pending_payment_uses_reserved_subscription_dates_without_applying_lead_time_again()
    {
        await using var db = CreateDb();
        var clock = Clock();
        var group = SeedGroup(db, FamilyGroupStatus.CheckoutPending, false);
        var member = group.Members.Single();
        var reserved = new Subscription
        {
            CustomerUserId = group.ManagerCustomerUserId, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            ProgramId = member.ProgramId!.Value, BundleId = member.BundleId!.Value,
            Status = SubscriptionStatus.PendingPayment, SelectedTier = RestaurantTier.Basic,
            StartDate = new DateOnly(2026, 10, 25), EndDate = new DateOnly(2026, 11, 7),
            DurationDays = 12, PaidAmount = new Money(40, "KWD")
        };
        member.MemberSubscriptionId = reserved.Id;
        group.PaymentAnchorSubscriptionId = reserved.Id;
        db.Subscriptions.Add(reserved);
        await db.SaveChangesAsync();

        var result = await new CustomerSubscriptionQueryService(db, clock).GetUncompletedSubscriptionsAsync(group.ManagerCustomerUserId);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal(reserved.StartDate, row.StartDate);
        Assert.Equal(reserved.EndDate, row.EndDate);
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());

    private static IDateTimeProvider Clock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static FamilyGroup SeedGroup(AppDbContext db, FamilyGroupStatus status, bool includeFriday)
    {
        var program = new Program { Code = "DATES", NameAr = "خطة", NameEn = "Plan", IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "DATES", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
        var group = new FamilyGroup { ManagerCustomerUserId = Guid.NewGuid(), Name = "Family", Status = status,
            CreatedAtUtc = new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc) };
        group.Members.Add(new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Manager",
            Role = FamilyMemberRole.Manager, RelationshipCode = "self", NutritionalGoalCode = "maintain", ProgramId = program.Id, BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 12, IncludeFriday = includeFriday,
            StartDate = new DateOnly(2026, 10, 17) });
        db.AddRange(program, bundle, group);
        return group;
    }
}
