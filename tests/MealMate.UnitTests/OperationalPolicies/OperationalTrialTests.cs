using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.OperationalPolicies;
public class OperationalTrialTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task TrialDeadline_IsScopedAndUnavailableInProduction(string environmentName, bool success)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var sub = Guid.NewGuid(); var date = new DateOnly(2026, 10, 3);
        var first = new SubscriptionDay { SubscriptionId = sub, ServiceDate = date, Status = SubscriptionDayStatus.Open };
        var other = new SubscriptionDay { SubscriptionId = Guid.NewGuid(), ServiceDate = date, Status = SubscriptionDayStatus.Open };
        db.SubscriptionDays.AddRange(first, other); await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); var now = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc); clock.UtcNow.Returns(now);
        var env = Substitute.For<IHostEnvironment>(); env.EnvironmentName.Returns(environmentName);
        var trial = new OperationalTrialService(db, clock, env, Substitute.For<IOperationalPolicyService>());
        var result = await trial.ConfigureAsync(sub, date, new(5, "Repeat lifecycle test"), Guid.NewGuid(), default);
        result.IsSuccess.Should().Be(success);
        (await db.SystemSettings.CountAsync()).Should().Be(success ? 2 : 0);
        other.SelectionDeadlineUtc.Should().BeNull();
        if (success) first.SelectionDeadlineUtc.Should().Be(now.AddMinutes(5));
    }
}
