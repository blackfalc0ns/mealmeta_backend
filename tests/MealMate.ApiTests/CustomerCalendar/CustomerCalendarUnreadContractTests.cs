using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text.Json;
using FluentAssertions;
using ProgramEntity = MealMate.Domain.ProgramsSubscriptions.Program;

namespace MealMate.ApiTests.CustomerCalendar;

/// <summary>
/// Contract for the unread indicator on GET calendar: the two new fields serialize
/// camelCase and the legacy response shape is untouched. GET never marks reads.
/// </summary>
public sealed class CustomerCalendarUnreadContractTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateTime Now = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 8, 29, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CalendarResponse_SerializesUnreadFieldsCamelCase_AndKeepsLegacyShape()
    {
        var (controller, _, _, subscriptionId) = await CreateControllerAsync();

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetCalendar(subscriptionId, CancellationToken.None));
        var dto = Assert.IsType<CustomerCalendarScreenDto>(result.Value);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // New fields, camelCase exactly as the mobile client reads them.
        root.TryGetProperty("hasUnreadNotifications", out var hasUnread).Should().BeTrue();
        root.TryGetProperty("unreadNotificationsCount", out var unreadCount).Should().BeTrue();
        hasUnread.ValueKind.Should().Be(JsonValueKind.False);
        unreadCount.ValueKind.Should().Be(JsonValueKind.Number);

        // Legacy calendar shape unchanged: same top-level members as before the feature.
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[]
        {
            "subscriptionId", "programId", "bundleId", "requiredCount",
            "slotRequirements", "isFulfillmentReady", "days",
            "hasUnreadNotifications", "unreadNotificationsCount"
        });
    }

    [Fact]
    public async Task GetCalendar_DoesNotMutateReadState()
    {
        var (controller, db, userId, subscriptionId2) = await CreateControllerAsync();
        db.CustomerNotifications.Add(new MealMate.Domain.IdentityAccess.CustomerNotification
        {
            UserId = userId,
            EventKey = "order.confirmed_by_restaurant",
            SubjectId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(),
            TitleAr = "تم تأكيد طلبك",
            TitleEn = "Order Confirmed",
            BodyAr = "المطعم أكد وجبتك.",
            BodyEn = "The restaurant confirmed your meal.",
            DeepLink = "mealmate://orders",
            CreatedAtUtc = Now,
        });
        await db.SaveChangesAsync();

        await controller.GetCalendar(subscriptionId2, CancellationToken.None);
        await controller.GetCalendar(subscriptionId2, CancellationToken.None);

        db.Entry(await db.CustomerNotifications.SingleAsync()).Reload();
        (await db.CustomerNotifications.SingleAsync()).ReadAtUtc.Should().BeNull();
    }

    private static async Task<(CustomerCalendarController Controller, AppDbContext Db, Guid UserId, Guid SubscriptionId)> CreateControllerAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var program = new ProgramEntity
        {
            Code = "WL", NameAr = "وزن", NameEn = "Weight",
            Status = ProgramStatus.Active, IsActive = true
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "3M", NameAr = "3 وجبات", NameEn = "3 meals",
            MealsPerDay = 3, BreakfastCount = 1, LunchCount = 1, DinnerCount = 1, SnackCount = 0,
            Status = ProgramStatus.Active, IsActive = true
        };
        var subscription = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 6,
            IncludeFriday = false,
            StartDate = Day1,
            EndDate = Day1,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(25m, "KWD"),
            FulfillmentCompletedAtUtc = Now.AddDays(-1)
        };
        db.AddRange(program, bundle, subscription);
        db.Add(new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = Day1,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = Deadline
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(Now));

        var engine = new CustomerCalendarEngine(
            db, currentUser, clock,
            new RestaurantDiversificationCalculator());
        var handlers = new CustomerCalendarHandlers(engine);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerCalendarQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => handlers.Handle(call.ArgAt<GetCustomerCalendarQuery>(0), call.ArgAt<CancellationToken>(1)));

        var controller = new CustomerCalendarController(currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.RequestServices =
            new ServiceCollection().AddSingleton(sender).BuildServiceProvider();

        return (controller, db, customerId, subscription.Id);
    }
}
