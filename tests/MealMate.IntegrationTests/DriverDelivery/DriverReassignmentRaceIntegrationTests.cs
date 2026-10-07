using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.DriverDelivery;

/// <summary>
/// Real-PostgreSQL guard tests for the driver reassignment request (screen 08.02):
/// the partial unique index IX_Complaints_Active_ReassignmentRequest must stop
/// parallel duplicate submissions that both passed the handler pre-check, while a
/// resolved prior request must not block a new one.
/// </summary>
public sealed class DriverReassignmentRaceIntegrationTests : IAsyncLifetime
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
            throw new XunitException("Docker/PostgreSQL container failed to start for DriverReassignmentRaceIntegrationTests.", ex);
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
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static DriverDeliveryOrderCommandHandlers CreateHandler(AppDbContext db)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        return new DriverDeliveryOrderCommandHandlers(db, currentUser, NullLocalizedMessageProvider.Instance);
    }

    private async Task<(DriverProfile Driver, TripStop Stop)> SeedAssignedStopAsync(AppDbContext db)
    {
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق التزامن",
            FullNameEn = "Race Driver",
            VerificationStatus = VerificationStatus.Approved,
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRP-RACE",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress
        };
        db.Trips.Add(trip);

        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        });

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-RACE-1",
            CustomerMaskedId = "CUST-RACE",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "1 وجبة",
            MealSummaryEn = "1 meal",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        return (driver, stop);
    }

    [Fact]
    public async Task ParallelDuplicateRequests_OnlyOneCreatesAnActiveRequest()
    {
        await using var db = CreateDbContext();
        var (driver, stop) = await SeedAssignedStopAsync(db);
        var stopId = stop.Id;
        var driverId = driver.Id;

        // Two parallel submissions, each with its own context, both pass the
        // handler's pre-check before either commits — the database decides.
        var firstTask = Task.Run(async () =>
        {
            await using var ctx = CreateDbContext();
            var handler = CreateHandler(ctx);
            return await handler.Handle(new RequestReassignmentCommand(
                stopId, Reason: "VehicleBreakdown", DriverId: driverId), CancellationToken.None);
        });
        var secondTask = Task.Run(async () =>
        {
            await using var ctx = CreateDbContext();
            var handler = CreateHandler(ctx);
            return await handler.Handle(new RequestReassignmentCommand(
                stopId, Reason: "TrafficAccident", DriverId: driverId), CancellationToken.None);
        });

        await Task.WhenAll(firstTask, secondTask);
        var results = new[] { await firstTask, await secondTask };

        results.Count(r => r.IsSuccess).Should().Be(1, "exactly one parallel request may create the active reassignment record");
        results.Count(r => !r.IsSuccess && r.Error!.Code == "driver.reassignment.request_already_active").Should().Be(1);

        await using var verify = CreateDbContext();
        (await verify.Complaints.CountAsync(c => c.Category == "DriverReassignmentRequested"))
            .Should().Be(1, "the partial unique index keeps one active request per order identity");
    }

    [Fact]
    public async Task ResolvedPriorRequest_AllowsANewRequest_OnPostgres()
    {
        await using var db = CreateDbContext();
        var (driver, stop) = await SeedAssignedStopAsync(db);

        var handler = CreateHandler(db);
        var first = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", DriverId: driver.Id), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var prior = await db.Complaints.SingleAsync(c => c.Category == "DriverReassignmentRequested");
        prior.Status = ComplaintStatus.Resolved;
        await db.SaveChangesAsync();

        // Re-assigned and stuck again: the resolved row is excluded by the index
        // filter, so the second request must persist.
        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = stop.TripId,
            DriverProfileId = driver.Id,
            Status = "Active"
        });
        stop.Status = TripStopStatus.PickedUp;
        await db.SaveChangesAsync();

        var second = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "TrafficAccident", DriverId: driver.Id), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        (await db.Complaints.CountAsync(c => c.Category == "DriverReassignmentRequested")).Should().Be(2);
        second.Value.RequestId.Should().NotBe(first.Value.RequestId);
    }
}
