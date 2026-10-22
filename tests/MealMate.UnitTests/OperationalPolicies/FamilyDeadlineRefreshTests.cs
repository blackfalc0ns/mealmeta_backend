using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace MealMate.UnitTests.OperationalPolicies;

public sealed class FamilyDeadlineRefreshTests
{
    private static readonly DateOnly ServiceDate = new(2026, 10, 24);
    private static readonly DateTime StandardDeadline = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ShortDeadline = new(2026, 10, 23, 20, 55, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Refresh_repairs_family_deadlines_with_or_without_published_policy(bool published, bool missingDeadline)
    {
        await using var db = CreateDb();
        var day = await SeedFamilyAsync(db, published);
        if (missingDeadline) day.SelectionDeadlineUtc = null;

        await OperationalDeadlineRules.RefreshAsync(db, new[] { day }, CreatePolicies(db), false, default);
        await db.SaveChangesAsync();

        Assert.Equal(StandardDeadline, (await db.SubscriptionDays.AsNoTracking().SingleAsync()).SelectionDeadlineUtc);
    }

    [Fact]
    public async Task Refresh_keeps_an_earlier_published_family_cutoff()
    {
        await using var db = CreateDb();
        var day = await SeedFamilyAsync(db, published: true, leadMinutes: 96 * 60);

        await OperationalDeadlineRules.RefreshAsync(db, new[] { day }, CreatePolicies(db), false, default);

        Assert.Equal(StandardDeadline.AddHours(-24), day.SelectionDeadlineUtc);
    }

    [Theory]
    [InlineData(SubscriptionDayStatus.Open)]
    [InlineData(SubscriptionDayStatus.Preparing)]
    [InlineData(SubscriptionDayStatus.Delivered)]
    [InlineData(SubscriptionDayStatus.Frozen)]
    [InlineData(SubscriptionDayStatus.Cancelled)]
    public async Task Refresh_preserves_locked_or_non_mutable_family_days(SubscriptionDayStatus status)
    {
        await using var db = CreateDb();
        var day = await SeedFamilyAsync(db, published: true);
        day.Status = status;
        if (status == SubscriptionDayStatus.Open) day.LockAtUtc = StandardDeadline;

        await OperationalDeadlineRules.RefreshAsync(db, new[] { day }, CreatePolicies(db), false, default);

        Assert.Equal(ShortDeadline, day.SelectionDeadlineUtc);
        Assert.Equal(status, day.Status);
    }

    [Fact]
    public async Task Refresh_preserves_a_family_replacement_window()
    {
        await using var db = CreateDb();
        var day = await SeedFamilyAsync(db, published: true);
        day.Status = SubscriptionDayStatus.ReplacementWindowOpen;
        day.ReplacementWindowOpensAtUtc = StandardDeadline.AddHours(1);
        day.ReplacementWindowClosesAtUtc = StandardDeadline.AddHours(25);

        await OperationalDeadlineRules.RefreshAsync(db, new[] { day }, CreatePolicies(db), false, default);

        Assert.Equal(ShortDeadline, day.SelectionDeadlineUtc);
        Assert.Equal(StandardDeadline.AddHours(25), day.GetEffectiveSelectionDeadlineUtc());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refresh_honors_trial_override_only_when_allowed(bool allowTrial)
    {
        await using var db = CreateDb();
        var day = await SeedFamilyAsync(db, published: true);
        db.SystemSettings.Add(new SystemSetting
        {
            Key = OperationalDeadlineRules.TrialPrefix + day.Id,
            ValueJson = JsonSerializer.Serialize(new TrialDeadlineOverride(day.SubscriptionId, day.ServiceDate,
                ShortDeadline, Guid.NewGuid(), "Cutoff regression trial", StandardDeadline)),
            IsActive = true
        });
        await db.SaveChangesAsync();

        await OperationalDeadlineRules.RefreshAsync(db, new[] { day }, CreatePolicies(db), allowTrial, default);

        Assert.Equal(allowTrial ? ShortDeadline : StandardDeadline, day.SelectionDeadlineUtc);
    }

    [Fact]
    public async Task Calendar_closes_family_day_at_exact_72_hour_boundary_and_rejects_edits()
    {
        await using var db = CreateDb();
        var day = await SeedFamilyAsync(db, published: true);
        var subscription = await db.Subscriptions.SingleAsync();
        db.FamilySubscriptionDeliveryAmendments.Add(new FamilySubscriptionDeliveryAmendment
        {
            SubscriptionId = subscription.Id, Version = 1, ActorUserId = subscription.CustomerUserId,
            EffectiveFromDate = ServiceDate, AddressId = Guid.NewGuid(), AddressLabel = "Home",
            AddressLine = "Family delivery address", RegionId = Guid.NewGuid(),
            AffectedDayIdsJson = JsonSerializer.Serialize(new[] { day.Id }), CreatedAtUtc = StandardDeadline.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(StandardDeadline.AddSeconds(-1));
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(subscription.CustomerUserId);
        user.UserType.Returns(UserType.Customer);
        var access = Substitute.For<IFamilyCalendarAccessGate>();
        access.AuthorizeAsync(Arg.Any<Subscription>(), Arg.Any<FamilyAction>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<FamilyCalendarAccess>.Success(new FamilyCalendarAccess(true, null, true))));
        var engine = new CustomerCalendarEngine(db, user, clock, new RestaurantDiversificationCalculator(),
            policies: CreatePolicies(db), accessGate: access);

        var before = await engine.GetDayAsync(subscription.Id, ServiceDate, default);
        Assert.True(before.IsSuccess, before.Error?.Code);
        Assert.True(before.Value.CanEdit);
        Assert.Equal(StandardDeadline, before.Value.SelectionDeadlineUtc);

        clock.UtcNow.Returns(StandardDeadline);
        var atDeadline = await engine.GetDayAsync(subscription.Id, ServiceDate, default);
        Assert.True(atDeadline.IsSuccess, atDeadline.Error?.Code);
        Assert.False(atDeadline.Value.CanEdit);
        Assert.Equal(CustomerCalendarUiStatus.Locked, atDeadline.Value.UiStatus);
        Assert.Equal(CalendarEditBlockReasons.SelectionDeadlinePassed, atDeadline.Value.EditBlockReason);
        var edit = await engine.SelectRestaurantAsync(subscription.Id, ServiceDate, Guid.NewGuid(), default);
        Assert.False(edit.IsSuccess);
        Assert.Equal(MessageCodes.Calendar.SelectionLocked, edit.Error?.Code);
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static IOperationalPolicyService CreatePolicies(AppDbContext db)
        => new OperationalPolicyService(db, new MemoryCache(new MemoryCacheOptions()), Substitute.For<IAuditLogger>());

    private static async Task<SubscriptionDay> SeedFamilyAsync(AppDbContext db, bool published, int leadMinutes = 5)
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(), ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            FamilyGroupId = Guid.NewGuid(), Status = SubscriptionStatus.Active,
            StartDate = ServiceDate, EndDate = ServiceDate, PaidAmount = new Money(10m, "KWD"),
            PaymentVerifiedAtUtc = StandardDeadline.AddHours(-1), FulfillmentCompletedAtUtc = StandardDeadline.AddHours(-1)
        };
        // Even the family's first service day must not acquire the individual payment-window exception.
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id, ServiceDate = ServiceDate,
            Status = SubscriptionDayStatus.Open, SelectionDeadlineUtc = ShortDeadline
        };
        db.AddRange(subscription, day);
        if (published)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = OperationalPolicyService.SettingKey,
                ValueJson = JsonSerializer.Serialize(new OperationalPolicySettings()
                    with { Cutoff = new CutoffPolicySettings(CutoffLeadMinutes: leadMinutes) }),
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
        return day;
    }
}
