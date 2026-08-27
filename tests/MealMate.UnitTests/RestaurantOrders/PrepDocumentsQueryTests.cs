using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOrders;

public class PrepDocumentsQueryTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task GetPrepDocuments_returns_invoice_box_and_meal_parts_for_owning_restaurant()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            ServiceDate = DateOnly.FromDateTime(now.Date),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            CreatedAtUtc = now,
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "MM-TEST-1",
            GeneratedAtUtc = now,
            CreatedAtUtc = now,
        });
        db.MealInvoices.Add(new MealInvoice
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = day.Id,
            ServiceDate = DateOnly.FromDateTime(now.Date),
            InvoiceNumber = "INV-1",
            TotalBoxes = 1,
            SnapshotJson = """{"embeddedLayoutJson":"[]","mealMateLogoUrl":"https://x/m.png"}""",
            BarcodeValue = "MM-TEST-1",
            CreatedAtUtc = now,
        });
        db.MealLabels.Add(new MealLabel
        {
            SubscriptionDayId = day.Id,
            Kind = MealLabelKind.Box,
            LabelPayloadJson = """{"embeddedLayoutJson":"[1]"}""",
            GeneratedAtUtc = now,
            CreatedAtUtc = now,
        });
        db.MealLabels.Add(new MealLabel
        {
            SubscriptionDayId = day.Id,
            Kind = MealLabelKind.Meal,
            MealId = mealId,
            LabelPayloadJson = """{"embeddedLayoutJson":"[2]"}""",
            GeneratedAtUtc = now,
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurantId);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var sut = new RestaurantOrderService(db, currentUser, clock);
        var result = await sut.GetPrepDocumentsAsync(new GetRestaurantPrepDocumentsQuery(day.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BarcodeValue.Should().Be("MM-TEST-1");
        result.Value.Invoice.Should().NotBeNull();
        result.Value.Invoice!.LayoutJson.Should().Be("[]");
        result.Value.BoxLabel.Should().NotBeNull();
        result.Value.MealLabels.Should().HaveCount(1);
        result.Value.MealLabels[0].MealId.Should().Be(mealId);
    }

    [Fact]
    public async Task GetPrepDocuments_rejects_other_restaurant()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            ServiceDate = DateOnly.FromDateTime(now.Date),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            CreatedAtUtc = now,
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(Guid.NewGuid());
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var sut = new RestaurantOrderService(db, currentUser, clock);
        var result = await sut.GetPrepDocumentsAsync(new GetRestaurantPrepDocumentsQuery(day.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("order.not_found");
    }
}
