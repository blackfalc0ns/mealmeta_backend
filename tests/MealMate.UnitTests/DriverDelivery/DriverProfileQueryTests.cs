using System.Globalization;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.DriverDelivery;

public sealed class DriverProfileQueryTests
{
    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static async Task<(bool Success, JsonElement Payload)> Query(AppDbContext db, Guid? userId, string culture = "en", bool isDriver = true)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(userId.HasValue);
        user.UserId.Returns(userId);
        user.IsInRole("Driver").Returns(isDriver);
        var accounts = Substitute.For<IUserAccountService>();
        var storage = Substitute.For<IFileStorage>();
        storage.GetReadUrlAsync("avatar.jpg", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example/avatar.jpg");
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.CurrentCulture.Returns(CultureInfo.GetCultureInfo(culture));
        messages.GetMessage(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<object[]>())
            .Returns(x => x.ArgAt<string?>(1) ?? x.ArgAt<string>(0));
        var handler = new GetDriverProfileQueryHandler(db, user, accounts, storage, messages);
        var result = await handler.Handle(new GetDriverProfileQuery(), CancellationToken.None);
        object payload = result.IsSuccess ? result.Value : result.Error;
        return (result.IsSuccess, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("ar", "أحمد", "فرع الشرق")]
    [InlineData("en", "Ahmed", "Sharq branch")]
    public async Task Profile_ReturnsOwnedDataAndLocalizedNames(string culture, string name, string branchName)
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed",
            VehicleColor = "White", VehicleYear = 2024, VehicleModel = "Honda", ProfileImageStorageKey = "avatar.jpg",
            ActivatedAtUtc = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc) };
        var other = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "آخر", FullNameEn = "Other" };
        db.DriverProfiles.AddRange(driver, other);
        var registration = new DriverRegistration { ResultingDriverProfileId = driver.Id, Phone = "+96550000001",
            FullNameAr = driver.FullNameAr, FullNameEn = driver.FullNameEn };
        db.DriverRegistrations.Add(registration);
        db.DriverRatings.AddRange(new DriverRating { DriverProfileId = driver.Id, Score = 4 },
            new DriverRating { DriverProfileId = driver.Id, Score = 5 }, new DriverRating { DriverProfileId = other.Id, Score = 1 });
        db.DriverWorkSessions.Add(new DriverWorkSession { DriverProfileId = driver.Id, Status = "Active", StartedAtUtc = DateTime.UtcNow });
        var branch = new RestaurantBranch { NameAr = "فرع الشرق", NameEn = "Sharq branch", IsActive = true,
            BranchCode = "SH", AddressAr = "الشرق", AddressEn = "Sharq", GovernorateAr = "العاصمة",
            GovernorateEn = "Capital", ContactPhone = "+96550000002" };
        db.RestaurantBranches.Add(branch);
        db.BranchStaffAssignments.Add(new BranchStaffAssignment { UserId = driver.UserId, RestaurantBranchId = branch.Id, IsActive = true, RoleId = "Driver" });
        db.SupportTickets.AddRange(new SupportTicket { CreatedByUserId = driver.UserId, Subject = "Tyre", Body = "Flat tyre", Status = "Open", Priority = "High", CreatedAtUtc = DateTime.UtcNow },
            new SupportTicket { CreatedByUserId = other.UserId, Subject = "Private", Body = "Other driver", Status = "Open", Priority = "Low", CreatedAtUtc = DateTime.UtcNow.AddMinutes(1) });
        await db.SaveChangesAsync();

        var (success, p) = await Query(db, driver.UserId, culture);
        Assert.True(success);
        Assert.Equal(name, p.GetProperty("fullName").GetString());
        Assert.Equal("+96550000001", p.GetProperty("phoneNumber").GetString());
        Assert.True(p.GetProperty("isOnline").GetBoolean());
        Assert.Equal(4.5m, p.GetProperty("averageRating").GetDecimal());
        Assert.Equal(2, p.GetProperty("reviewsCount").GetInt32());
        Assert.Equal(2024, p.GetProperty("vehicle").GetProperty("vehicleYear").GetInt32());
        Assert.Equal("White", p.GetProperty("vehicle").GetProperty("vehicleColor").GetString());
        Assert.Equal(branchName, p.GetProperty("assignment").GetProperty("branchName").GetString());
        Assert.Equal("Tyre", p.GetProperty("latestSupportTicket").GetProperty("subject").GetString());
        Assert.Equal("https://storage.example/avatar.jpg", p.GetProperty("profileImageUrl").GetString());
        Assert.Equal("2025-01-02T00:00:00Z", p.GetProperty("joinedAtUtc").GetString());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("acceptanceRatePercent").ValueKind);
    }

    [Fact]
    public async Task EmptyHistory_ReturnsNullMetricsAndEmptyDocuments()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();
        var (success, p) = await Query(db, driver.UserId);
        Assert.True(success);
        Assert.False(p.GetProperty("isOnline").GetBoolean());
        Assert.Equal(0, p.GetProperty("totalOrders").GetInt32());
        Assert.Equal(0, p.GetProperty("reviewsCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("averageRating").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("latestSupportTicket").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("driverRank").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("assignment").ValueKind);
        Assert.Empty(p.GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task Documents_UseLatestReviewAndExpiryWithoutLeakingOtherOwners()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);
        var doc = new Document { OwnerType = DocumentOwnerType.Driver, OwnerId = driver.Id,
            DocumentType = DocumentType.DrivingLicense, FileName = "license.jpg", ContentType = "image/jpeg", StoragePath = "license.jpg",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-2) };
        doc.Verifications.Add(new DocumentVerification { Status = VerificationStatus.Approved, CreatedAtUtc = DateTime.UtcNow.AddDays(-10) });
        doc.Verifications.Add(new DocumentVerification { Status = VerificationStatus.Rejected, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) });
        db.Documents.AddRange(doc, new Document { OwnerType = DocumentOwnerType.Driver, OwnerId = Guid.NewGuid(),
            DocumentType = DocumentType.NationalId, FileName = "other.jpg", ContentType = "image/jpeg", StoragePath = "other.jpg" });
        await db.SaveChangesAsync();
        var (success, p) = await Query(db, driver.UserId);
        Assert.True(success);
        var d = Assert.Single(p.GetProperty("documents").EnumerateArray());
        Assert.Equal("Rejected", d.GetProperty("verificationStatus").GetString());
        Assert.Equal("Expired", d.GetProperty("status").GetString());
        Assert.True(d.GetProperty("requiresRenewal").GetBoolean());
        Assert.True(d.GetProperty("daysUntilExpiry").GetInt32() < 0);
    }

    [Fact]
    public async Task UnauthenticatedOrMissingDriver_DoesNotReturnAnotherProfile()
    {
        await using var db = CreateDb();
        db.DriverProfiles.Add(new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "آخر", FullNameEn = "Other" });
        await db.SaveChangesAsync();
        var anonymous = await Query(db, null);
        Assert.False(anonymous.Success);
        Assert.Equal(4, anonymous.Payload.GetProperty("type").GetInt32());
        var missing = await Query(db, Guid.NewGuid());
        Assert.False(missing.Success);
        Assert.Equal(2, missing.Payload.GetProperty("type").GetInt32());
        var wrongRole = await Query(db, Guid.NewGuid(), isDriver: false);
        Assert.False(wrongRole.Success);
        Assert.Equal(5, wrongRole.Payload.GetProperty("type").GetInt32());
    }

    [Fact]
    public async Task TotalOrders_AttributesDeliveredStopsBeforeReassignmentAndDeduplicatesHistory()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);
        var trip = new Trip { Code = "TR1" };
        var deliveredAt = DateTime.UtcNow.AddDays(-2);
        trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driver.Id, AssignedAtUtc = deliveredAt.AddHours(-2), Status = "Assigned" });
        trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driver.Id, AssignedAtUtc = deliveredAt.AddHours(-1), Status = "Active" });
        trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = Guid.NewGuid(), AssignedAtUtc = deliveredAt.AddHours(1), Status = "Active" });
        trip.Stops.Add(new TripStop { OrderCode = "BOX1", CustomerMaskedId = "C1", ZoneNameAr = "الشرق", ZoneNameEn = "Sharq",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", Status = TripStopStatus.Delivered, DeliveredAtUtc = deliveredAt });
        trip.Stops.Add(new TripStop { OrderCode = "BOX2", CustomerMaskedId = "C2", ZoneNameAr = "الشرق", ZoneNameEn = "Sharq",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", Status = TripStopStatus.Pending });
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        var result = await Query(db, driver.UserId);
        Assert.True(result.Success);
        Assert.Equal(1, result.Payload.GetProperty("totalOrders").GetInt32());
    }
}
