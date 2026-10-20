using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DriverOperationalEligibilityTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Theory]
    [InlineData(VerificationStatus.Draft)]
    [InlineData(VerificationStatus.Submitted)]
    [InlineData(VerificationStatus.UnderReview)]
    [InlineData(VerificationStatus.Rejected)]
    [InlineData(VerificationStatus.NeedsChanges)]
    public async Task CheckEligibilityAsync_WhenDriverNotApproved_ReturnsForbidden(VerificationStatus status)
    {
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            VerificationStatus = status,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(1),
            VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddYears(1)
        };

        var result = await DriverOperationalEligibility.CheckEligibilityAsync(
            db, null, driver, DateTime.UtcNow, isArabic: true);

        result.IsSuccess.Should().BeFalse();
        var expectedCode = status == VerificationStatus.Rejected ? "Driver.Rejected" : "Driver.NotApproved";
        result.Error.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task CheckEligibilityAsync_WhenUserSuspended_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = userId,
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(1),
            VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddYears(1)
        };

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "driver@mealmate.com", "+96555112233", "Driver Name",
            UserType.Driver, AccountStatus.Suspended, null, true, true, DateTime.UtcNow, null));

        var result = await DriverOperationalEligibility.CheckEligibilityAsync(
            db, users, driver, DateTime.UtcNow, isArabic: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.AccountSuspended");
    }

    [Fact]
    public async Task CheckEligibilityAsync_WhenLicenseExpired_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = now.AddDays(-1),
            NationalIdExpiryUtc = now.AddYears(1),
            VehicleRegistrationExpiryUtc = now.AddYears(1)
        };

        var result = await DriverOperationalEligibility.CheckEligibilityAsync(
            db, null, driver, now, isArabic: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.LicenseExpired");
    }

    [Fact]
    public async Task CheckEligibilityAsync_WhenNationalIdExpired_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = now.AddYears(1),
            NationalIdExpiryUtc = now.AddDays(-2),
            VehicleRegistrationExpiryUtc = now.AddYears(1)
        };

        var result = await DriverOperationalEligibility.CheckEligibilityAsync(
            db, null, driver, now, isArabic: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NationalIdExpired");
    }

    [Fact]
    public async Task CheckEligibilityAsync_WhenAllValidAndApproved_ReturnsSuccess()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = now.AddYears(1),
            NationalIdExpiryUtc = now.AddYears(1),
            VehicleRegistrationExpiryUtc = now.AddYears(1)
        };

        var result = await DriverOperationalEligibility.CheckEligibilityAsync(
            db, null, driver, now, isArabic: true);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckEligibilityAsync_WhenUserNotActive_ReturnsForbidden_AccountNotActive()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = userId,
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(1),
            VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddYears(1)
        };

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "driver@mealmate.com", "+96555112233", "Driver Name",
            UserType.Driver, AccountStatus.Registered, null, true, true, DateTime.UtcNow, null));

        var result = await DriverOperationalEligibility.CheckEligibilityAsync(
            db, users, driver, DateTime.UtcNow, isArabic: true, requireApproved: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.AccountNotActive");
    }

    [Fact]
    public async Task StartDriverShift_WhenDriverNotApproved_FailsWithEligibilityError()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = userId,
            FullNameAr = "سائق تجربة",
            FullNameEn = "Test Driver",
            VerificationStatus = VerificationStatus.UnderReview,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1)
        };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.IsAuthenticated.Returns(true);
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new DriverShiftCommandHandlers(db, currentUser, messages);

        var result = await handler.Handle(new StartDriverShiftCommand(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NotApproved");
    }

    [Fact]
    public async Task AssignBoxToDriver_WhenDriverNotApproved_FailsWithEligibilityError()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            FullNameAr = "سائق غير معتمد",
            FullNameEn = "Unapproved Driver",
            VerificationStatus = VerificationStatus.UnderReview,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1)
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            Code = "TRP-ELIG-01",
            Status = TripStatus.Planned,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-ELIG-01",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            CustomerMaskedId = "C-01",
            SlotLabel = "Slot 1",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new AssignBoxToDriverCommandHandler(db, messages, txExecutor: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());

        var result = await handler.Handle(new AssignBoxToDriverCommand(stop.Id, driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NotApproved");
    }

    [Fact]
    public async Task BulkAssignBoxes_WhenDriverNotApproved_FailsWithEligibilityError()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            FullNameAr = "سائق غير معتمد دفعة",
            FullNameEn = "Unapproved Bulk Driver",
            VerificationStatus = VerificationStatus.Submitted,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1)
        };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new BulkAssignBoxesCommandHandler(db, messages, txExecutor: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());

        var result = await handler.Handle(new BulkAssignBoxesCommand([Guid.NewGuid()], driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NotApproved");
    }
}
