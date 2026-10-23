using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Application.Features.VoiceCalls.Queries;
using MealMate.Application.Features.VoiceCalls.V2.Commands;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

/// <summary>
/// The v2 call display surface gives call participants the driver/order context
/// their call screen renders (name, image URL, role, delivery address, order
/// data) without exposing the customer's phone number.
/// </summary>
public class VoiceCallV2CallDisplayTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public async Task Display_ServesCustomerIdentityToBothParticipants_InBothDirections(
        bool customerCalls, bool customerReads, bool withImage)
    {
        await using var db = CreateDb();
        var (driver, customer, stopId, call) = SeedCall(db);
        db.Users.Add(new MealMate.Persistence.Identity.ApplicationUser
        {
            Id = customer, FullName = "أحمد العميل", UserName = "customer-display",
            PhoneNumber = "+96551112222"
        });
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = customer,
            ProfileImageStorageKey = withImage ? "customers/أحمد photo.jpg" : null
        });
        db.CustomerProfiles.Add(new CustomerProfile
            { UserId = Guid.NewGuid(), ProfileImageStorageKey = "customers/other.jpg" });
        if (customerCalls)
        {
            call = new CallSession(stopId, customer, CallParticipantType.Customer,
                driver, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow);
            db.CallSessions.Add(call);
        }
        await db.SaveChangesAsync();
        var result = await CreateHandler(db).Handle(new(call.Id, customerReads ? customer : driver), default);
        result.IsSuccess.Should().BeTrue();
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            result.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        var root = json.RootElement;
        root.GetProperty("customerName").GetString().Should().Be("أحمد العميل");
        var expectedImage = withImage
            ? "/api/v1/uploads/content?key=" + Uri.EscapeDataString("customers/أحمد photo.jpg") : null;
        root.GetProperty("customerImageUrl").GetString().Should().Be(expectedImage);
        result.Value.DriverName.Should().Be("Mohamed Ali");
        result.Value.DeliveryAddress.Should().Be("السالمية، شارع البحر، عمارة 12");
        result.Value.DeliveryZone.Should().Be("Salmiya");
        root.GetRawText().Should().NotContain("+96551112222").And.NotContain("phoneNumber");
    }

    [Fact]
    public async Task Display_MissingCustomerAccountAndProfile_DoesNotFabricateIdentity()
    {
        await using var db = CreateDb();
        var (driver, _, _, call) = SeedCall(db);
        var result = await CreateHandler(db).Handle(new(call.Id, driver), default);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            result.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        json.RootElement.GetProperty("customerName").GetString().Should().BeEmpty();
        json.RootElement.GetProperty("customerImageUrl").GetString().Should().BeNull();
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (Guid DriverUserId, Guid CustomerUserId, Guid StopId, CallSession Call) SeedCall(
        AppDbContext db,
        bool withDriver = true)
    {
        var driverUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-TEST-1", Status = TripStatus.InProgress };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "MM-2026-0001",
            CustomerMaskedId = "CUST-****",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            FullAddress = "السالمية، شارع البحر، عمارة 12",
            MealSummaryAr = "وجبة كيتو غداء",
            MealSummaryEn = "Keto lunch meal",
            SlotLabel = "12:00 - 15:00",
            BoxCount = 2,
            Status = TripStopStatus.PickedUp,
        };
        db.TripStops.Add(stop);

        if (withDriver)
        {
            var driver = new DriverProfile
            {
                UserId = driverUserId,
                FullNameAr = "محمد علي",
                FullNameEn = "Mohamed Ali",
                VehicleType = "دراجة نارية",
                VehiclePlate = "12345",
                ProfileImageStorageKey = "uploads/drivers/profile/driver-1.jpg",
            };
            db.DriverProfiles.Add(driver);
            db.DriverAssignments.Add(new DriverAssignment
            {
                TripId = trip.Id,
                DriverProfileId = driver.Id,
                Status = "Active",
                AssignedAtUtc = DateTime.UtcNow,
            });
        }

        var call = new CallSession(
            stop.Id,
            driverUserId,
            CallParticipantType.Driver,
            customerUserId,
            CallParticipantType.Customer,
            Guid.NewGuid(),
            DateTime.UtcNow);
        db.CallSessions.Add(call);
        db.SaveChanges();

        return (driverUserId, customerUserId, stop.Id, call);
    }

    private static VoiceCallV2CallDisplayQueryHandler CreateHandler(AppDbContext db) =>
        new(db, NullLocalizedMessageProvider.Instance);

    [Fact]
    public async Task ReceiverCustomer_GetsDriverRoleAddressAndOrderData_WithoutPhone()
    {
        await using var db = CreateDb();
        var (_, customerUserId, stopId, call) = SeedCall(db);

        var result = await CreateHandler(db).Handle(
            new VoiceCallV2CallDisplayQuery(call.Id, customerUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var display = result.Value;
        display.CallId.Should().Be(call.Id);
        display.TripStopId.Should().Be(stopId);
        display.DriverName.Should().Be("Mohamed Ali");
        display.DriverImageUrl.Should().Be(
            $"/api/v1/uploads/content?key={Uri.EscapeDataString("uploads/drivers/profile/driver-1.jpg")}");
        display.DriverRole.Should().Be("Delivery Driver");
        display.VehicleType.Should().Be("دراجة نارية");
        display.PlateNumber.Should().Be("12345");
        display.DeliveryAddress.Should().Be("السالمية، شارع البحر، عمارة 12");
        display.DeliveryZone.Should().Be("Salmiya");
        display.OrderCode.Should().Be("MM-2026-0001");
        display.MealSummary.Should().Be("Keto lunch meal");
        display.SlotLabel.Should().Be("12:00 - 15:00");
        display.BoxCount.Should().Be(2);
        display.StopStatus.Should().Be("PickedUp");
    }

    [Fact]
    public async Task CallerDriver_GetsSameDisplay()
    {
        await using var db = CreateDb();
        var (driverUserId, _, _, call) = SeedCall(db);

        var result = await CreateHandler(db).Handle(
            new VoiceCallV2CallDisplayQuery(call.Id, driverUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DriverName.Should().Be("Mohamed Ali");
        result.Value.OrderCode.Should().Be("MM-2026-0001");
    }

    [Fact]
    public async Task NonParticipantAndUnknownCall_FailWithGenericNotFound()
    {
        await using var db = CreateDb();
        var (_, _, _, call) = SeedCall(db);
        var handler = CreateHandler(db);
        var strangerId = Guid.NewGuid();

        var strangerResult = await handler.Handle(
            new VoiceCallV2CallDisplayQuery(call.Id, strangerId), CancellationToken.None);
        var unknownResult = await handler.Handle(
            new VoiceCallV2CallDisplayQuery(Guid.NewGuid(), strangerId), CancellationToken.None);

        strangerResult.IsSuccess.Should().BeFalse();
        strangerResult.Error.Should().Be(VoiceCallErrors.TripStopNotFound());
        unknownResult.IsSuccess.Should().BeFalse();
        unknownResult.Error.Should().Be(VoiceCallErrors.TripStopNotFound());
    }

    [Fact]
    public async Task MissingDriverAssignment_ReturnsOrderDataWithEmptyDriverFields()
    {
        await using var db = CreateDb();
        var (_, customerUserId, _, call) = SeedCall(db, withDriver: false);

        var result = await CreateHandler(db).Handle(
            new VoiceCallV2CallDisplayQuery(call.Id, customerUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DriverName.Should().BeEmpty();
        result.Value.DriverImageUrl.Should().BeNull();
        result.Value.DriverRole.Should().Be("Delivery Driver");
        result.Value.DeliveryAddress.Should().Be("السالمية، شارع البحر، عمارة 12");
        result.Value.OrderCode.Should().Be("MM-2026-0001");
    }

    [Fact]
    public async Task LegacyStopWithoutFullAddress_FallsBackToLocalizedZone()
    {
        await using var db = CreateDb();
        var (_, customerUserId, _, call) = SeedCall(db, withDriver: false);
        var stop = await db.TripStops.SingleAsync(s => s.Id == call.TripStopId);
        stop.FullAddress = null;
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new VoiceCallV2CallDisplayQuery(call.Id, customerUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DeliveryAddress.Should().Be("Salmiya");
        result.Value.DeliveryZone.Should().Be("Salmiya");
    }
}
