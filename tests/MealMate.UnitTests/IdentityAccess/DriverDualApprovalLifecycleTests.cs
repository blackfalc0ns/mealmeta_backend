using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class DriverDualApprovalLifecycleTests
{
    private readonly AppDbContext _db;
    private readonly IUserAccountService _users = Substitute.For<IUserAccountService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ILocalizedMessageProvider _messages = Substitute.For<ILocalizedMessageProvider>();
    private readonly IDriverRegistrationNotificationService _notifier = Substitute.For<IDriverRegistrationNotificationService>();

    public DriverDualApprovalLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _db = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc));
        _messages.CurrentCulture.Returns(new CultureInfo("ar"));
        _messages.GetMessage(Arg.Any<string>(), Arg.Any<string?>()).Returns("Success");
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    [Fact]
    public async Task AdminDecision_Approve_ShouldSucceedForIndependentDriverAndCopyAllRegulatoryFields()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = userId,
            RestaurantId = null, // Independent fleet driver
            FullNameAr = "خالد العتيبي",
            FullNameEn = "Khaled Al-Otaibi",
            Phone = "+96599112233",
            NationalId = "294010112345",
            NationalIdExpiryUtc = new DateTime(2028, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            LicenseNumber = "DL-445566",
            LicenseExpiryUtc = new DateTime(2027, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            VehiclePlate = "KW-8899",
            VehicleType = "Car",
            VehicleModel = "Toyota Prius",
            VehicleYear = 2024,
            VehicleColor = "Black",
            IsVehicleOwned = true,
            VehicleRegistrationExpiryUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ContractExpiryUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            Nationality = "Kuwaiti",
            DateOfBirthUtc = new DateTime(1994, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RegistrationSource = "MobileApp",
            RestaurantApprovalStatus = VerificationStatus.Approved,
            AdminApprovalStatus = VerificationStatus.Submitted,
            Status = VerificationStatus.UnderReview
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new AdminDriverDecisionCommandHandler(
            _db, _users, _currentUser, _audit, _clock, _notifier);

        // Act
        var result = await handler.Handle(
            new AdminDriverDecisionCommand(reg.Id, "approve", "Approved by Admin"),
            CancellationToken.None);
        await _db.SaveChangesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        reg.Status.Should().Be(VerificationStatus.Approved);
        reg.ResultingDriverProfileId.Should().NotBeNull();

        var profile = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.Id == reg.ResultingDriverProfileId!.Value);
        profile.Should().NotBeNull();
        profile!.NationalIdExpiryUtc.Should().Be(reg.NationalIdExpiryUtc);
        profile.LicenseExpiryUtc.Should().Be(reg.LicenseExpiryUtc);
        profile.VehicleRegistrationExpiryUtc.Should().Be(reg.VehicleRegistrationExpiryUtc);
        profile.ContractExpiryUtc.Should().Be(reg.ContractExpiryUtc);
        profile.VehicleYear.Should().Be(2024);
        profile.VehicleColor.Should().Be("Black");
        profile.IsVehicleOwned.Should().BeTrue();
        profile.Nationality.Should().Be("Kuwaiti");
        profile.DateOfBirthUtc.Should().Be(reg.DateOfBirthUtc);

        await _users.Received(1).UpdateAccountStatusAsync(userId, AccountStatus.Active, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminDecision_Reject_ShouldSetUserAccountStatusToRejected()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = userId,
            FullNameAr = "سائق مرفوض",
            FullNameEn = "Rejected Driver",
            Phone = "+96599119911",
            Status = VerificationStatus.UnderReview
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new AdminDriverDecisionCommandHandler(
            _db, _users, _currentUser, _audit, _clock, _notifier);

        // Act
        var result = await handler.Handle(
            new AdminDriverDecisionCommand(reg.Id, "reject", "Documents expired"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        reg.Status.Should().Be(VerificationStatus.Rejected);
        reg.AdminApprovalStatus.Should().Be(VerificationStatus.Rejected);

        await _users.Received(1).UpdateAccountStatusAsync(userId, AccountStatus.Suspended, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resubmit_AfterAdminRequestedChanges_ShouldPreserveRestaurantApprovalAndNotifyAdmin()
    {
        // Arrange
        var restId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            RestaurantId = restId,
            FullNameAr = "سائق تعديل",
            FullNameEn = "Driver Needs Changes",
            Phone = "+96599223344",
            RestaurantApprovalStatus = VerificationStatus.Approved,
            AdminApprovalStatus = VerificationStatus.NeedsChanges,
            Status = VerificationStatus.NeedsChanges,
            ChangeRequestNotes = "Please re-upload driving license"
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new ResubmitDriverRegistrationCommandHandler(
            _db, _audit, _clock, _messages, _notifier);

        var command = new ResubmitDriverRegistrationCommand(
            RegistrationId: reg.Id,
            LicenseNumber: "NEW-DL-9988",
            DrivingLicenseFrontStorageKey: "new_lic_front.jpg");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        reg.RestaurantApprovalStatus.Should().Be(VerificationStatus.Approved, "Restaurant had already approved; Admin requested changes, so restaurant approval should NOT be reset");
        reg.AdminApprovalStatus.Should().Be(VerificationStatus.Submitted);
        reg.Status.Should().Be(VerificationStatus.Submitted);

        await _notifier.Received(1).NotifyAdminsDriverApplicationReadyForConfirmationAsync(
            reg.Id,
            restId,
            Arg.Any<string>(),
            reg.FullNameAr,
            reg.Phone,
            Arg.Any<CancellationToken>());
    }
}
