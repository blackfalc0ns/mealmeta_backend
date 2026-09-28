using System.Collections.Concurrent;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace MealMate.IntegrationTests.Concurrency;

public sealed class ConcurrentPickupRaceTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        await _container.StartAsync();

        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task ConcurrentDriverPickup_ExactlyOneSucceeds()
    {
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var serviceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        Guid dailyOrderId;
        Guid subscriptionDayId;
        Guid tripId;
        Guid driverProfileId;
        string validationToken;

        // Arrange & Seed
        await using (var db = CreateDb())
        {
            var restaurant = new RestaurantProfile
            {
                UserId = Guid.NewGuid(),
                LegalNameAr = "مطعم السباق",
                LegalNameEn = "Race Restaurant",
                TradeNameAr = "مطعم السباق",
                TradeNameEn = "Race Restaurant"
            };
            db.RestaurantProfiles.Add(restaurant);
            restaurantId = restaurant.Id;

            var driver = new DriverProfile
            {
                UserId = driverUserId,
                RestaurantId = restaurantId,
                FullNameAr = "سائق السباق",
                FullNameEn = "Race Driver",
                VehicleType = "car",
                VehiclePlate = "RACE-1",
                VerificationStatus = VerificationStatus.Approved,
                IsAvailable = true
            };
            db.DriverProfiles.Add(driver);
            driverProfileId = driver.Id;

            var day = new SubscriptionDay
            {
                SubscriptionId = Guid.NewGuid(),
                RestaurantId = restaurantId,
                ServiceDate = serviceDate,
                Status = SubscriptionDayStatus.Preparing
            };
            db.SubscriptionDays.Add(day);
            subscriptionDayId = day.Id;

            var order = new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = subscriptionDayId,
                ServiceDate = serviceDate,
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            };
            db.RestaurantDailyOrders.Add(order);
            dailyOrderId = order.Id;

            var trip = new Trip
            {
                RestaurantId = restaurantId,
                Code = "TRP-RACE-01",
                Status = TripStatus.Assigned,
                PlannedDate = serviceDate
            };
            db.Trips.Add(trip);
            tripId = trip.Id;

            var assignment = new DriverAssignment
            {
                TripId = trip.Id,
                DriverProfileId = driver.Id,
                Status = "Active",
                AssignedAtUtc = DateTime.UtcNow
            };
            db.DriverAssignments.Add(assignment);

            var barcode = new MealBarcode
            {
                SubscriptionDayId = subscriptionDayId,
                BarcodeValue = $"MM-{serviceDate:yyyyMMdd}-{subscriptionDayId:N}",
                Status = MealBarcodeStatus.Generated,
                GeneratedAtUtc = DateTime.UtcNow
            };
            db.MealBarcodes.Add(barcode);

            var stop = new TripStop
            {
                TripId = trip.Id,
                SubscriptionDayId = subscriptionDayId,
                DailyOrderId = dailyOrderId,
                MealBarcodeId = barcode.Id,
                OrderCode = "#BX-RACE-01",
                CustomerMaskedId = "CUST-RACE",
                ZoneNameAr = "الشرق",
                ZoneNameEn = "Sharq",
                MealSummaryAr = "وجبة",
                MealSummaryEn = "Meal",
                SlotLabel = "12:00 م",
                BoxCount = 1,
                Status = TripStopStatus.Pending
            };
            db.TripStops.Add(stop);

            await db.SaveChangesAsync();

            // Perform pre-validation to issue validation token
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(driverUserId);

            var validateHandler = new ValidatePickupBarcodeCommandHandler(db, currentUser, NullLocalizedMessageProvider.Instance);
            var validateResult = await validateHandler.Handle(
                new ValidatePickupBarcodeCommand(barcode.BarcodeValue),
                CancellationToken.None);

            validateResult.IsSuccess.Should().BeTrue();
            validationToken = validateResult.Value.ValidationToken;

            var activeValidation = await db.PickupValidations.SingleAsync(v => v.BoxId == subscriptionDayId);
            var photo = new PickupConditionPhoto
            {
                PickupValidationId = activeValidation.Id,
                TripId = tripId,
                BoxId = subscriptionDayId,
                DriverProfileId = driverProfileId,
                StorageKey = "uploads/race-photo.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 2048,
                UploadedAtUtc = DateTime.UtcNow
            };
            db.PickupConditionPhotos.Add(photo);
            await db.SaveChangesAsync();
        }

        // Act: Execute two concurrent pickup confirmation requests
        var results = new ConcurrentBag<(bool IsSuccess, string? ErrorCode)>();
        var barrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            await using var db = CreateDb();
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(driverUserId);

            var handler = new ConfirmPickupBoxCommandHandler(
                db,
                currentUser,
                NullLocalizedMessageProvider.Instance,
                new PostgresApplicationTransactionExecutor(db));

            barrier.SignalAndWait();

            try
            {
                var res = await handler.Handle(new ConfirmPickupBoxCommand(
                    BoxId: subscriptionDayId,
                    ValidationToken: validationToken,
                    ConditionPhotoStorageKey: "uploads/race-photo.jpg",
                    Latitude: 29.33m,
                    Longitude: 47.97m,
                    IdempotencyKey: Guid.NewGuid().ToString()), CancellationToken.None);

                results.Add((res.IsSuccess, res.Error?.Code));
            }
            catch (Exception ex)
            {
                results.Add((false, ex.GetType().Name));
            }
        });

        var task2 = Task.Run(async () =>
        {
            await using var db = CreateDb();
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(driverUserId);

            var handler = new ConfirmPickupBoxCommandHandler(
                db,
                currentUser,
                NullLocalizedMessageProvider.Instance,
                new PostgresApplicationTransactionExecutor(db));

            barrier.SignalAndWait();

            try
            {
                var res = await handler.Handle(new ConfirmPickupBoxCommand(
                    BoxId: subscriptionDayId,
                    ValidationToken: validationToken,
                    ConditionPhotoStorageKey: "uploads/race-photo.jpg",
                    Latitude: 29.33m,
                    Longitude: 47.97m,
                    IdempotencyKey: Guid.NewGuid().ToString()), CancellationToken.None);

                results.Add((res.IsSuccess, res.Error?.Code));
            }
            catch (Exception ex)
            {
                results.Add((false, ex.GetType().Name));
            }
        });

        await Task.WhenAll(task1, task2);

        // Assert
        results.Should().HaveCount(2);
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        successCount.Should().Be(1, "Exactly one concurrent pickup confirmation must succeed");
        failureCount.Should().Be(1, "The competing concurrent pickup confirmation must fail");

        // Verify Database Invariant: Exactly ONE OrderHandover record exists in PostgreSQL
        await using (var db = CreateDb())
        {
            var handovers = await db.OrderHandovers
                .Where(h => h.DailyOrderId == dailyOrderId)
                .ToListAsync();

            handovers.Should().HaveCount(1, "PostgreSQL database must strictly guarantee exactly one OrderHandover per order attempt");
            handovers.Single().Status.Should().Be("Completed");
            handovers.Single().AttemptNumber.Should().Be(1);
        }
    }
}
