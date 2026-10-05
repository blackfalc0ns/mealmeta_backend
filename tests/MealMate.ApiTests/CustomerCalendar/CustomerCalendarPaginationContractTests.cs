using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using ProgramEntity = MealMate.Domain.ProgramsSubscriptions.Program;
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

namespace MealMate.ApiTests.CustomerCalendar;

/// <summary>
/// HTTP binding contract for GET days/{date}/restaurants: optional page/pageSize query params,
/// the paged envelope metadata, and the camelCase wire names mobile clients read.
/// </summary>
public sealed class CustomerCalendarPaginationContractTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateTime Now = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 8, 29, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task WithoutPagingParams_ReturnsFullListAndSinglePageMetadata()
    {
        var (controller, subscriptionId) = await CreateControllerAsync(restaurantCount: 3);

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetDayRestaurants(subscriptionId, Day1, query: null, page: null, pageSize: null, CancellationToken.None));
        var dto = Assert.IsType<CustomerDayRestaurantsDto>(result.Value);

        Assert.Equal(3, dto.Restaurants.Count);
        Assert.Equal(1, dto.Page);
        Assert.Equal(3, dto.PageSize);
        Assert.Equal(3, dto.TotalCount);
        Assert.Equal(1, dto.TotalPages);
        Assert.False(dto.HasNextPage);
        Assert.False(dto.HasPreviousPage);
    }

    [Fact]
    public async Task WithPagingParams_SlicesEnvelopeAndReportsMetadata()
    {
        var (controller, subscriptionId) = await CreateControllerAsync(restaurantCount: 3);

        var page1 = Assert.IsType<OkObjectResult>(
            await controller.GetDayRestaurants(subscriptionId, Day1, query: null, page: 1, pageSize: 2, CancellationToken.None));
        var dto1 = Assert.IsType<CustomerDayRestaurantsDto>(page1.Value);
        Assert.Equal(2, dto1.Restaurants.Count);
        Assert.Equal(3, dto1.TotalCount);
        Assert.Equal(2, dto1.TotalPages);
        Assert.True(dto1.HasNextPage);

        var page2 = Assert.IsType<OkObjectResult>(
            await controller.GetDayRestaurants(subscriptionId, Day1, query: null, page: 2, pageSize: 2, CancellationToken.None));
        var dto2 = Assert.IsType<CustomerDayRestaurantsDto>(page2.Value);
        Assert.Single(dto2.Restaurants);
        Assert.True(dto2.HasPreviousPage);
    }

    [Fact]
    public async Task SearchParam_StillBoundAlongsidePaging()
    {
        var (controller, subscriptionId) = await CreateControllerAsync(restaurantCount: 3);

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetDayRestaurants(subscriptionId, Day1, query: "Pool R2", page: 1, pageSize: 2, CancellationToken.None));
        var dto = Assert.IsType<CustomerDayRestaurantsDto>(result.Value);

        Assert.Single(dto.Restaurants);
        Assert.Equal("Pool R2", dto.Restaurants[0].NameEn);
        Assert.Equal(1, dto.TotalCount);
    }

    [Fact]
    public void Envelope_SerializesCamelCaseMetadataForMobileClients()
    {
        var dto = new CustomerDayRestaurantsDto(
            Day1, 8, 8, [], Page: 1, PageSize: 20, TotalCount: 0, TotalPages: 0,
            HasNextPage: false, HasPreviousPage: false);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"serviceDate\"");
        json.Should().Contain("\"limit\"");
        json.Should().Contain("\"adminCeiling\"");
        json.Should().Contain("\"restaurants\"");
        json.Should().Contain("\"page\"");
        json.Should().Contain("\"pageSize\"");
        json.Should().Contain("\"totalCount\"");
        json.Should().Contain("\"totalPages\"");
        json.Should().Contain("\"hasNextPage\"");
        json.Should().Contain("\"hasPreviousPage\"");
    }

    private static async Task<(CustomerCalendarController Controller, Guid SubscriptionId)> CreateControllerAsync(int restaurantCount)
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
        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            RestaurantCount = restaurantCount,
            EligibilityCheckedAtUtc = Now
        };
        db.AddRange(program, bundle, subscription, pool);
        db.Add(new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = Day1,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = Deadline
        });
        for (var i = 0; i < restaurantCount; i++)
        {
            var restaurantId = Guid.NewGuid();
            var snapshot = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = pool.Id,
                SubscriptionId = subscription.Id,
                RestaurantId = restaurantId,
                ProgramId = subscription.ProgramId,
                BundleId = subscription.BundleId,
                RegionId = Guid.NewGuid(),
                Tier = RestaurantTier.Platinum,
                NameAr = $"مطعم مجموعة {i + 1}",
                NameEn = $"Pool R{i + 1}",
                IsInitiallySelected = i == 0,
                OfferingSnapshotJson = "{}",
                CapturedAtUtc = DateTime.UtcNow
            };
            db.Add(snapshot);
            var meals = JsonSerializer.Serialize(new List<object>
            {
                new { mealId = Guid.NewGuid(), nameAr = "فطور", nameEn = "Breakfast", descriptionAr = "فطور", descriptionEn = "Breakfast", slot = "breakfast", sortOrder = 1, imageUrl = (string?)null, calories = 400, proteinG = 30, carbsG = 20, fatG = 10, presentAllergenCodes = Array.Empty<string>(), mayContainAllergenCodes = Array.Empty<string>(), dietaryPreferenceCodes = Array.Empty<string>(), ingredients = Array.Empty<object>() },
                new { mealId = Guid.NewGuid(), nameAr = "غداء", nameEn = "Lunch", descriptionAr = "غداء", descriptionEn = "Lunch", slot = "lunch", sortOrder = 2, imageUrl = (string?)null, calories = 400, proteinG = 30, carbsG = 20, fatG = 10, presentAllergenCodes = Array.Empty<string>(), mayContainAllergenCodes = Array.Empty<string>(), dietaryPreferenceCodes = Array.Empty<string>(), ingredients = Array.Empty<object>() },
                new { mealId = Guid.NewGuid(), nameAr = "عشاء", nameEn = "Dinner", descriptionAr = "عشاء", descriptionEn = "Dinner", slot = "dinner", sortOrder = 3, imageUrl = (string?)null, calories = 400, proteinG = 30, carbsG = 20, fatG = 10, presentAllergenCodes = Array.Empty<string>(), mayContainAllergenCodes = Array.Empty<string>(), dietaryPreferenceCodes = Array.Empty<string>(), ingredients = Array.Empty<object>() }
            });
            db.Add(new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = snapshot.Id,
                SubscriptionId = subscription.Id,
                RestaurantId = restaurantId,
                ServiceDate = Day1,
                MenuPlanVersionId = Guid.NewGuid(),
                SlotRequirementsJson = "[{\"slot\":\"breakfast\",\"count\":1},{\"slot\":\"lunch\",\"count\":1},{\"slot\":\"dinner\",\"count\":1}]",
                MealsJson = meals,
                ContentHash = "hash",
                CapturedAtUtc = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);
        var engineUser = Substitute.For<ICurrentUser>();
        engineUser.UserId.Returns(customerId);
        engineUser.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(Now));

        var engine = new CustomerCalendarEngine(
            db, engineUser, clock,
            new RestaurantDiversificationCalculator(),
            new SubscriptionDayDispatchService(db, clock, new CustomerDeliveryAddressService(db, clock)));
        var handlers = new CustomerCalendarHandlers(engine);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerDayRestaurantsQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => handlers.Handle(call.ArgAt<GetCustomerDayRestaurantsQuery>(0), call.ArgAt<CancellationToken>(1)));

        var controller = new CustomerCalendarController(currentUser)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.RequestServices =
            new ServiceCollection().AddSingleton(sender).BuildServiceProvider();

        return (controller, subscription.Id);
    }
}
