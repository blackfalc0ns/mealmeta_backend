using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Renewal;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace MealMate.UnitTests.OperationalPolicies;
public class OperationalConsumerTests
{
    [Fact]
    public async Task Freeze_RejectsDeliveredDayWithoutRewindingIt()
    {
        await using var db = Db();
        var sub = new Subscription { Status = SubscriptionStatus.Active, StartDate = new(2026, 9, 1), EndDate = new(2026, 10, 30) };
        var day = new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = new(2026, 9, 30), Status = SubscriptionDayStatus.Delivered };
        db.Subscriptions.Add(sub); db.SubscriptionDays.Add(day);
        await Store(db, new OperationalPolicySettings() with { Freeze = new FreezePolicySettings(0, 1, 14) });
        var result = await new SubscriptionOperationsHandlers(db, Substitute.For<IAuditLogger>(), Clock(), null!)
            .Handle(new RequestSubscriptionFreezeCommand(sub.Id, day.ServiceDate, day.ServiceDate, null, true), default);
        result.IsFailure.Should().BeTrue(); day.Status.Should().Be(SubscriptionDayStatus.Delivered);
    }
    [Fact]
    public async Task FreezeWithoutExtension_DoesNotPromiseExtraDays()
    {
        await using var db = Db();
        await Store(db, new OperationalPolicySettings() with { Freeze = new FreezePolicySettings(ExtendSubscriptionEnd: false) });
        var result = await new MealMate.Application.Features.ProgramsSubscriptions.Freeze.SubscriptionFreezeService(db, Clock()).GetPolicyAsync();
        result.Value.NoticeEn.Should().NotContain("extended automatically");
    }
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
    private static IDateTimeProvider Clock() { var c = Substitute.For<IDateTimeProvider>(); c.TodayUtc.Returns(new DateOnly(2026, 9, 30)); c.UtcNow.Returns(new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc)); return c; }
    private static async Task Store(AppDbContext db, OperationalPolicySettings s) { db.SystemSettings.Add(new SystemSetting { Key = OperationalPolicyService.SettingKey, ValueJson = JsonSerializer.Serialize(s) }); await db.SaveChangesAsync(); }
    [Fact]
    public async Task DisabledRenewal_RejectsRequestWithoutCreatingRenewal()
    {
        await using var db = Db(); var customer = Guid.NewGuid();
        var sub = new Subscription { CustomerUserId = customer, Status = SubscriptionStatus.Active, StartDate = new(2026, 9, 1), EndDate = new(2026, 10, 30) }; db.Subscriptions.Add(sub);
        await Store(db, new OperationalPolicySettings() with { Renewal = new RenewalPolicySettings(false) });
        var result = await new SubscriptionRenewalService(db, Clock()).RequestRenewalAsync(sub.Id, customer);
        result.IsFailure.Should().BeTrue(); (await db.SubscriptionRenewals.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task FreezeMinimum_RejectsShortRequest()
    {
        await using var db = Db();
        var sub = new Subscription { Status = SubscriptionStatus.Active, DurationDays = 30, StartDate = new(2026, 9, 1), EndDate = new(2026, 10, 30) }; db.Subscriptions.Add(sub);
        await Store(db, new OperationalPolicySettings() with { Freeze = new FreezePolicySettings(0, 3, 14) });
        var handler = new SubscriptionOperationsHandlers(db, Substitute.For<IAuditLogger>(), Clock(), null!);
        var result = await handler.Handle(new RequestSubscriptionFreezeCommand(sub.Id, new(2026, 10, 5), new(2026, 10, 5), null, false), default);
        result.IsFailure.Should().BeTrue(); (await db.SubscriptionFreezeRequests.CountAsync()).Should().Be(0);
    }
}
