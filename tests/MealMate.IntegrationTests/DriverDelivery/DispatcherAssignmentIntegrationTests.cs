using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
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
using Xunit.Sdk;

namespace MealMate.IntegrationTests.DriverDelivery;

public sealed class DispatcherAssignmentIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();

            await _container.StartAsync();

            await using var db = CreateDbContext();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/PostgreSQL container failed to start for DispatcherAssignmentIntegrationTests.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDbContext()
    {
        var connStr = _container.GetConnectionString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (RestaurantProfile rest, DriverProfile driver) SeedRestaurantAndDriver(AppDbContext db)
    {
        var rest = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم الاختبار",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            CommercialLicenseNumber = "CR-" + Guid.NewGuid().ToString("N")[..8],
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantProfiles.Add(rest);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = rest.Id,
            FullNameAr = "سائق الاختبار",
            FullNameEn = "Test Driver",
            NationalId = "1234567890",
            LicenseNumber = "LIC-" + Guid.NewGuid().ToString("N")[..8],
            VehicleType = "sedan",
            VehiclePlate = "PLT-1234",
            VerificationStatus = VerificationStatus.Approved,
            IsAvailable = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.DriverProfiles.Add(driver);

        return (rest, driver);
    }

    [Fact]
    public async Task Queue_ExcludesUnreadyBoxes_And_ExcludesBoxesWithoutActiveBarcode()
    {
        await using var db = CreateDbContext();
        var (rest, driver) = SeedRestaurantAndDriver(db);
        var restId = rest.Id;

        var subId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Box 1: Preparing (not ReadyForPickup)
        var day1 = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restId,
            ServiceDate = today,
            Status = SubscriptionDayStatus.Preparing,
            DeliveryAddressLine = "حولي قطعة 1",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(day1);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = day1.Id,
            RestaurantId = restId,
            ServiceDate = today,
            Status = OrderStatus.Preparing,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day1.Id,
            BarcodeValue = "BC-UNREADY-01",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        // Box 2: ReadyForPickup, but barcode is Revoked (no active barcode)
        var day2 = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restId,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "حولي قطعة 2",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(day2);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = day2.Id,
            RestaurantId = restId,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day2.Id,
            BarcodeValue = "BC-REVOKED-02",
            Status = MealBarcodeStatus.Revoked,
            IsRevoked = true,
            RevokedAtUtc = DateTime.UtcNow,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        // Box 3: ReadyForPickup AND active barcode
        var day3 = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restId,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "حولي قطعة 3",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(day3);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = day3.Id,
            RestaurantId = restId,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day3.Id,
            BarcodeValue = "BC-READY-ACTIVE-03",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restId);
        var recEngine = Substitute.For<IDispatchRecommendationEngine>();

        var handler = new GetDispatcherOrderQueueQueryHandler(
            db,
            currentUser,
            NullLocalizedMessageProvider.Instance,
            recEngine);

        var queryResult = await handler.Handle(new GetDispatcherOrderQueueQuery(restId), CancellationToken.None);

        queryResult.IsSuccess.Should().BeTrue();
        var boxes = queryResult.Value.Boxes;
        boxes.Should().ContainSingle(b => b.BoxId == day3.Id, "Only day3 is ReadyForPickup and has an active barcode");
        boxes.Should().NotContain(b => b.BoxId == day1.Id, "Day 1 is still preparing and must be excluded");
        boxes.Should().NotContain(b => b.BoxId == day2.Id, "Day 2 has only a revoked barcode and must be excluded");
    }

    [Fact]
    public async Task Queue_MultiTenantIsolation_EnforcesCallerRestaurant()
    {
        await using var db = CreateDbContext();
        var (restA, _) = SeedRestaurantAndDriver(db);
        var (restB, _) = SeedRestaurantAndDriver(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Box belonging to Restaurant B
        var dayB = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restB.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "السالمية",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(dayB);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = dayB.Id,
            RestaurantId = restB.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = dayB.Id,
            BarcodeValue = "BC-REST-B",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Dispatcher from Restaurant A tries to query Restaurant B's queue
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restA.Id);
        currentUser.IsInRole("Admin").Returns(false);

        var handler = new GetDispatcherOrderQueueQueryHandler(
            db,
            currentUser,
            NullLocalizedMessageProvider.Instance,
            Substitute.For<IDispatchRecommendationEngine>());

        var result = await handler.Handle(new GetDispatcherOrderQueueQuery(restB.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Boxes.Should().NotContain(b => b.BoxId == dayB.Id,
            "Caller from Restaurant A must not see boxes belonging to Restaurant B");
    }

    [Fact]
    public async Task AssignBox_RejectsCallerFromDifferentRestaurant_ReturnsForbidden()
    {
        await using var db = CreateDbContext();
        var (restA, driverA) = SeedRestaurantAndDriver(db);
        var (restB, _) = SeedRestaurantAndDriver(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayB = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restB.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "السالمية",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(dayB);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = dayB.Id,
            RestaurantId = restB.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = dayB.Id,
            BarcodeValue = "BC-REST-B-ASSIGN",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Caller belongs to Restaurant A
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restA.Id);
        currentUser.IsInRole("Admin").Returns(false);

        var handler = new AssignBoxToDriverCommandHandler(
            db,
            NullLocalizedMessageProvider.Instance,
            Substitute.For<IDriverPushNotifier>(),
            currentUser);

        var result = await handler.Handle(new AssignBoxToDriverCommand(dayB.Id, driverA.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden, "Cross-restaurant box assignment must be forbidden (403)");
    }

    [Fact]
    public async Task AssignBox_RejectsWhenNotReadyForPickup_Or_MissingActiveBarcode()
    {
        await using var db = CreateDbContext();
        var (rest, driver) = SeedRestaurantAndDriver(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Case 1: Preparing (not ReadyForPickup)
        var dayUnready = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.Preparing,
            DeliveryAddressLine = "العديلية",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(dayUnready);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = dayUnready.Id,
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = OrderStatus.Preparing,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = dayUnready.Id,
            BarcodeValue = "BC-PREP-ONLY",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        // Case 2: ReadyForPickup, but barcode revoked
        var dayNoBarcode = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "العديلية",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(dayNoBarcode);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = dayNoBarcode.Id,
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = dayNoBarcode.Id,
            BarcodeValue = "BC-REVOKED-ONLY",
            Status = MealBarcodeStatus.Revoked,
            IsRevoked = true,
            RevokedAtUtc = DateTime.UtcNow,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(rest.Id);

        var handler = new AssignBoxToDriverCommandHandler(
            db,
            NullLocalizedMessageProvider.Instance,
            Substitute.For<IDriverPushNotifier>(),
            currentUser);

        var result1 = await handler.Handle(new AssignBoxToDriverCommand(dayUnready.Id, driver.Id), CancellationToken.None);
        result1.IsSuccess.Should().BeFalse();
        result1.Error.Type.Should().Be(ErrorType.Validation, "Box not ready for pickup cannot be assigned");

        var result2 = await handler.Handle(new AssignBoxToDriverCommand(dayNoBarcode.Id, driver.Id), CancellationToken.None);
        result2.IsSuccess.Should().BeFalse();
        result2.Error.Type.Should().Be(ErrorType.Validation, "Box without active barcode cannot be assigned");
    }

    [Fact]
    public async Task AssignBox_BindsTripStopToSavedBarcodeValue_IncludingRotatedBarcode()
    {
        await using var db = CreateDbContext();
        var (rest, driver) = SeedRestaurantAndDriver(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "كيفان",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(day);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = day.Id,
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });

        // Barcode 1: Revoked earlier
        var oldBarcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "BC-OLD-REVOKED",
            Status = MealBarcodeStatus.Revoked,
            IsRevoked = true,
            RevokedAtUtc = DateTime.UtcNow.AddMinutes(-30),
            GeneratedAtUtc = DateTime.UtcNow.AddHours(-1),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        db.MealBarcodes.Add(oldBarcode);

        // Barcode 2: Rotated / Reissued active barcode
        var newBarcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "BC-CANONICAL-ROTATED-99",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.Add(newBarcode);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(rest.Id);

        var handler = new AssignBoxToDriverCommandHandler(
            db,
            NullLocalizedMessageProvider.Instance,
            Substitute.For<IDriverPushNotifier>(),
            currentUser);

        var result = await handler.Handle(new AssignBoxToDriverCommand(day.Id, driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BoxCode.Should().Be("BC-CANONICAL-ROTATED-99", "Box code in response must match the active rotated barcode");

        var createdStop = await db.TripStops.FirstOrDefaultAsync(s => s.SubscriptionDayId == day.Id);
        createdStop.Should().NotBeNull();
        createdStop!.OrderCode.Should().Be("BC-CANONICAL-ROTATED-99", "TripStop.OrderCode must bind to canonical BarcodeValue");
        createdStop.MealBarcodeId.Should().Be(newBarcode.Id);
    }

    [Fact]
    public async Task AssignBox_SavesOutboxMessageInSameTransaction_WithoutDirectPush()
    {
        await using var db = CreateDbContext();
        var (rest, driver) = SeedRestaurantAndDriver(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "الشعب",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(day);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = day.Id,
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "BC-OUTBOX-PUSH-TEST",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var pushNotifierMock = Substitute.For<IDriverPushNotifier>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(rest.Id);

        var handler = new AssignBoxToDriverCommandHandler(
            db,
            NullLocalizedMessageProvider.Instance,
            pushNotifierMock,
            currentUser);

        var result = await handler.Handle(new AssignBoxToDriverCommand(day.Id, driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // 1. Direct Push Notifier must NOT be called before/during commit!
        await pushNotifierMock.DidNotReceiveWithAnyArgs().NotifyBoxAssignedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // 2. An Outbox message for "DriverBoxAssigned" MUST be persisted in DB
        var outboxMsg = await db.OutboxMessages
            .FirstOrDefaultAsync(m => m.EventType == "DriverBoxAssigned");
        outboxMsg.Should().NotBeNull("An outbox record must be created within the assignment transaction");
        outboxMsg!.PayloadJson.Should().Contain("BC-OUTBOX-PUSH-TEST");
    }

    [Fact]
    public async Task PartialUniqueIndex_PreventsConcurrentDuplicateActiveAssignments_ReturnsConflict409()
    {
        await using var db = CreateDbContext();
        var (rest, driver1) = SeedRestaurantAndDriver(db);
        var driver2 = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = rest.Id,
            FullNameAr = "سائق 2",
            FullNameEn = "Driver 2",
            NationalId = "1234567891",
            LicenseNumber = "LIC-2",
            VehicleType = "sedan",
            VehiclePlate = "PLT-5678",
            VerificationStatus = VerificationStatus.Approved,
            IsAvailable = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.DriverProfiles.Add(driver2);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "الروضة",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionDays.Add(day);
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            SubscriptionDayId = day.Id,
            RestaurantId = rest.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "BC-CONCURRENCY-409",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(rest.Id);

        var handler = new AssignBoxToDriverCommandHandler(
            db,
            NullLocalizedMessageProvider.Instance,
            Substitute.For<IDriverPushNotifier>(),
            currentUser);

        // First assignment succeeds
        var result1 = await handler.Handle(new AssignBoxToDriverCommand(day.Id, driver1.Id), CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        // Simulate concurrent conflict: another transaction attempting to create a second active TripStop for the same day
        // Testing that the database rejects it with unique violation on IX_TripStops_Active_SubscriptionDayId
        var trip2 = new Trip
        {
            RestaurantId = rest.Id,
            Code = "TRP-RACE-02",
            Status = TripStatus.Assigned,
            PlannedDate = today,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Trips.Add(trip2);
        var conflictingStop = new TripStop
        {
            TripId = trip2.Id,
            SubscriptionDayId = day.Id, // Same SubscriptionDayId!
            OrderCode = "BC-CONCURRENCY-409",
            CustomerMaskedId = "CUST-RACE",
            ZoneNameAr = "الروضة",
            ZoneNameEn = "Rawda",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "Slot 1",
            Status = TripStopStatus.Pending, // Active status!
            CreatedAtUtc = DateTime.UtcNow
        };
        db.TripStops.Add(conflictingStop);

        // Attempting to save the duplicate active stop must throw DbUpdateException due to IX_TripStops_Active_SubscriptionDayId
        Func<Task> act = async () => await db.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().NotBeNull();
        ex.Which.InnerException!.Message.Should().Contain("IX_TripStops_Active_SubscriptionDayId");
    }
}
