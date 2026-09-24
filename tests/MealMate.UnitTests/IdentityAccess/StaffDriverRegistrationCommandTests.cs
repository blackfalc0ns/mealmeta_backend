using System;
using System.IO;
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
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class StaffDriverRegistrationCommandTests
{
    private readonly AppDbContext _db;
    private readonly IUserAccountService _users = Substitute.For<IUserAccountService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ILocalizedMessageProvider _messages = Substitute.For<ILocalizedMessageProvider>();
    private readonly IDriverRegistrationNotificationService _notifier = Substitute.For<IDriverRegistrationNotificationService>();

    public StaffDriverRegistrationCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _db = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc));
        _messages.CurrentCulture.Returns(new System.Globalization.CultureInfo("ar"));
        _messages.GetMessage(Arg.Any<string>(), Arg.Any<string?>()).Returns("Success");
    }

    [Fact]
    public void Validator_ShouldRequirePasswordAndAllowNullRestaurantId()
    {
        // Arrange
        var commandWithoutPassword = new StaffDriverSelfRegistrationCommand(
            RestaurantId: null,
            FullNameAr: "أحمد الشمري",
            FullNameEn: "Ahmed Al-Shammari",
            Phone: "+96590001122",
            Email: null,
            Password: "", // empty
            NationalId: "293041512345",
            NationalIdExpiry: DateTime.UtcNow.AddYears(2),
            DateOfBirth: null,
            Nationality: "Kuwaiti",
            VehicleType: "Car",
            VehicleModel: "Toyota Camry",
            VehiclePlate: "12345",
            VehicleYear: 2023,
            VehicleColor: "Silver",
            IsVehicleOwned: true,
            LicenseNumber: "DL12345",
            LicenseExpiry: DateTime.UtcNow.AddYears(1),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            ContractExpiry: null,
            NationalIdFrontStorageKey: "front.jpg",
            NationalIdBackStorageKey: "back.jpg",
            DrivingLicenseFrontStorageKey: "lic_front.jpg",
            DrivingLicenseBackStorageKey: "lic_back.jpg",
            VehicleRegistrationStorageKey: "veh.jpg");

        var validator = new StaffDriverSelfRegistrationCommandValidator();

        // Act
        var result = validator.Validate(commandWithoutPassword);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(commandWithoutPassword.Password));
    }

    [Fact]
    public async Task Handle_SelfRegistration_ShouldUseProvidedPasswordAndAllowIndependentDriver()
    {
        // Arrange
        CreateUserRequest? capturedCreateUserRequest = null;
        _users.CreateAsync(Arg.Do<CreateUserRequest>(r => capturedCreateUserRequest = r), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserAccountDto(
                Guid.NewGuid(),
                "driver@mealmate.com",
                "+96590001122",
                "Ahmed Al-Shammari",
                UserType.Driver,
                AccountStatus.Registered,
                null,
                false,
                true,
                DateTime.UtcNow,
                null,
                true)));

        var handler = new StaffDriverSelfRegistrationCommandHandler(
            _users, _db, _audit, _clock, _messages, _notifier);

        var command = new StaffDriverSelfRegistrationCommand(
            RestaurantId: null, // Independent fleet driver
            FullNameAr: "أحمد الشمري",
            FullNameEn: "Ahmed Al-Shammari",
            Phone: "+96590001122",
            Email: "driver@mealmate.com",
            Password: "SecureDriverPass123!",
            NationalId: "293041512345",
            NationalIdExpiry: DateTime.UtcNow.AddYears(2),
            DateOfBirth: null,
            Nationality: "Kuwaiti",
            VehicleType: "Car",
            VehicleModel: "Toyota Camry",
            VehiclePlate: "12345",
            VehicleYear: 2023,
            VehicleColor: "Silver",
            IsVehicleOwned: true,
            LicenseNumber: "DL12345",
            LicenseExpiry: DateTime.UtcNow.AddYears(1),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            ContractExpiry: null,
            NationalIdFrontStorageKey: "front.jpg",
            NationalIdBackStorageKey: "back.jpg",
            DrivingLicenseFrontStorageKey: "lic_front.jpg",
            DrivingLicenseBackStorageKey: "lic_back.jpg",
            VehicleRegistrationStorageKey: "veh.jpg");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedCreateUserRequest.Should().NotBeNull();
        capturedCreateUserRequest!.Password.Should().Be("SecureDriverPass123!");
        capturedCreateUserRequest.RestaurantId.Should().BeNull();
    }

    [Fact]
    public async Task SubmitDriverRegistration_ShouldSucceed_WhenDocumentsAreFrontAndBackSpecific()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);

        var reg = new DriverRegistration
        {
            SubmittedByUserId = userId,
            FullNameAr = "محمد عبد الله",
            FullNameEn = "Mohammed Abdullah",
            Phone = "+96590008899",
            ProfileImageStorageKey = "profile.jpg",
            Status = VerificationStatus.Draft
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        // Documents saved with specific front/back types
        _db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.NationalIdFront,
            FileName = "nid_front.jpg",
            ContentType = "image/jpeg",
            StoragePath = "nid_front.jpg"
        });
        _db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            FileName = "lic_front.jpg",
            ContentType = "image/jpeg",
            StoragePath = "lic_front.jpg"
        });
        await _db.SaveChangesAsync();

        var handler = new SubmitDriverRegistrationCommandHandler(_db, _currentUser, _audit);

        // Act
        var result = await handler.Handle(new SubmitDriverRegistrationCommand(reg.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        reg.Status.Should().Be(VerificationStatus.Submitted);
    }
}
