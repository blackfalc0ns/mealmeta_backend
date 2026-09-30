using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class DriverRegistrationValidationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Submit_WhenNationalIdBelongsToAnotherApprovedDriver_ReturnsConflict()
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var existingDriverUserId = Guid.NewGuid();
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = existingDriverUserId,
            NationalId = "299010111111",
            LicenseNumber = "DL-ORIG-1",
            FullNameAr = "سائق أول",
            FullNameEn = "Driver One",
            VerificationStatus = VerificationStatus.Approved
        });

        var myUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = myUserId,
            Phone = "+96551111111",
            FullNameAr = "سائق جديد",
            FullNameEn = "New Driver",
            NationalId = "299010111111", // Duplicate
            LicenseNumber = "DL-NEW-1",
            ProfileImageStorageKey = "docs/photo.jpg",
            Status = VerificationStatus.Draft
        };
        db.DriverRegistrations.Add(reg);
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.NationalIdFront,
            StoragePath = "docs/nid.jpg",
            FileName = "nid.jpg",
            ContentType = "image/jpeg"
        });
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            StoragePath = "docs/dl.jpg",
            FileName = "dl.jpg",
            ContentType = "image/jpeg"
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(myUserId);
        var audit = Substitute.For<IAuditLogger>();

        var handler = new SubmitDriverRegistrationCommandHandler(db, currentUser, audit, clock);

        var result = await handler.Handle(new SubmitDriverRegistrationCommand(reg.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NationalIdAlreadyExists");
    }

    [Fact]
    public async Task Submit_WhenLicenseNumberBelongsToAnotherApprovedDriver_ReturnsConflict()
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var existingDriverUserId = Guid.NewGuid();
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = existingDriverUserId,
            NationalId = "299010111112",
            LicenseNumber = "DL-SHARED-9",
            FullNameAr = "سائق أول",
            FullNameEn = "Driver One",
            VerificationStatus = VerificationStatus.Approved
        });

        var myUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = myUserId,
            Phone = "+96551111112",
            FullNameAr = "سائق جديد",
            FullNameEn = "New Driver",
            NationalId = "299010199999",
            LicenseNumber = "DL-SHARED-9", // Duplicate license
            ProfileImageStorageKey = "docs/photo.jpg",
            Status = VerificationStatus.Draft
        };
        db.DriverRegistrations.Add(reg);
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.NationalIdFront,
            StoragePath = "docs/nid.jpg",
            FileName = "nid.jpg",
            ContentType = "image/jpeg"
        });
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            StoragePath = "docs/dl.jpg",
            FileName = "dl.jpg",
            ContentType = "image/jpeg"
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(myUserId);
        var audit = Substitute.For<IAuditLogger>();

        var handler = new SubmitDriverRegistrationCommandHandler(db, currentUser, audit, clock);

        var result = await handler.Handle(new SubmitDriverRegistrationCommand(reg.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.LicenseAlreadyExists");
    }

    [Fact]
    public async Task Submit_WhenNationalIdExpired_ReturnsValidationError()
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var myUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = myUserId,
            Phone = "+96551111113",
            FullNameAr = "سائق جديد",
            FullNameEn = "New Driver",
            NationalId = "299010199998",
            NationalIdExpiryUtc = clock.UtcNow.AddDays(-1), // Expired
            LicenseNumber = "DL-VALID-1",
            ProfileImageStorageKey = "docs/photo.jpg",
            Status = VerificationStatus.Draft
        };
        db.DriverRegistrations.Add(reg);
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.NationalIdFront,
            StoragePath = "docs/nid.jpg",
            FileName = "nid.jpg",
            ContentType = "image/jpeg"
        });
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            StoragePath = "docs/dl.jpg",
            FileName = "dl.jpg",
            ContentType = "image/jpeg"
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(myUserId);
        var audit = Substitute.For<IAuditLogger>();

        var handler = new SubmitDriverRegistrationCommandHandler(db, currentUser, audit, clock);

        var result = await handler.Handle(new SubmitDriverRegistrationCommand(reg.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.InvalidNationalId");
    }

    [Fact]
    public async Task Submit_WhenDriverUnder18YearsOld_ReturnsValidationError()
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var myUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = myUserId,
            Phone = "+96551111114",
            FullNameAr = "سائق قاصر",
            FullNameEn = "Minor Driver",
            NationalId = "299010199997",
            LicenseNumber = "DL-VALID-2",
            DateOfBirthUtc = clock.UtcNow.AddYears(-17), // 17 years old
            ProfileImageStorageKey = "docs/photo.jpg",
            Status = VerificationStatus.Draft
        };
        db.DriverRegistrations.Add(reg);
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.NationalIdFront,
            StoragePath = "docs/nid.jpg",
            FileName = "nid.jpg",
            ContentType = "image/jpeg"
        });
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            StoragePath = "docs/dl.jpg",
            FileName = "dl.jpg",
            ContentType = "image/jpeg"
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(myUserId);
        var audit = Substitute.For<IAuditLogger>();

        var handler = new SubmitDriverRegistrationCommandHandler(db, currentUser, audit, clock);

        var result = await handler.Handle(new SubmitDriverRegistrationCommand(reg.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.Underage");
    }

    [Fact]
    public async Task Resubmit_WhenNationalIdBelongsToAnotherApprovedDriver_ReturnsConflict()
    {
        await using var db = CreateDb();
        var myUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = otherUserId,
            NationalId = "299010188888",
            FullNameAr = "سائق معتمد",
            FullNameEn = "Approved Driver",
            VerificationStatus = VerificationStatus.Approved
        });

        var reg = new DriverRegistration
        {
            SubmittedByUserId = myUserId,
            Phone = "+96552222222",
            FullNameAr = "سائق مقدم الطلب",
            FullNameEn = "Applicant Driver",
            NationalId = "299010155555",
            Status = VerificationStatus.NeedsChanges,
            AdminApprovalStatus = VerificationStatus.NeedsChanges
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var messages = Substitute.For<ILocalizedMessageProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(myUserId);
        currentUser.Roles.Returns(new List<string> { AppRoles.Driver });

        var handler = new ResubmitDriverRegistrationCommandHandler(db, audit, clock, messages, notifier, currentUser, users);

        var result = await handler.Handle(new ResubmitDriverRegistrationCommand(
            reg.Id,
            NationalId: "299010188888"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NationalIdAlreadyExists");
    }

    [Fact]
    public async Task RegisterRestaurantDriver_WhenLicenseNumberAlreadyExists_ReturnsConflict()
    {
        await using var db = CreateDb();
        var restId = Guid.NewGuid();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = Guid.NewGuid(),
            NationalId = "299010111199",
            LicenseNumber = "DL-REST-DUP",
            FullNameAr = "سائق مسجل",
            FullNameEn = "Registered Driver",
            VerificationStatus = VerificationStatus.Approved
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restId);
        currentUser.Roles.Returns(new List<string> { AppRoles.RestaurantOwner });

        var audit = Substitute.For<IAuditLogger>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new RegisterRestaurantDriverCommandHandler(users, db, currentUser, audit, clock, notifier, DriverRegistrationTestStorage.Create());

        var result = await handler.Handle(new RegisterRestaurantDriverCommand(
            FullNameAr: "سائق جديد",
            FullNameEn: "New Driver",
            Phone: "+96554443322",
            Email: "driver@test.local",
            NationalId: "299010188811",
            NationalIdExpiry: clock.UtcNow.AddYears(1),
            VehiclePlate: "PLATE-99",
            VehicleModel: "Camry",
            VehicleType: "Car",
            LicenseNumber: "DL-REST-DUP",
            LicenseExpiry: clock.UtcNow.AddYears(1),
            VehicleLicenseExpiry: clock.UtcNow.AddYears(1),
            ContractExpiry: clock.UtcNow.AddYears(1),
            RestaurantId: restId,
            NationalIdFrontStorageKey: "docs/nid_f.jpg",
            NationalIdBackStorageKey: "docs/nid_b.jpg",
            DrivingLicenseFrontStorageKey: "docs/dl_f.jpg",
            DrivingLicenseBackStorageKey: "docs/dl_b.jpg",
            VehicleRegistrationStorageKey: "docs/veh.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("auth.license_number_already_registered");
    }

    [Fact]
    public async Task RegisterRestaurantDriver_WhenDriverUnder18YearsOld_ReturnsValidationError()
    {
        await using var db = CreateDb();
        var restId = Guid.NewGuid();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restId);
        currentUser.Roles.Returns(new List<string> { AppRoles.RestaurantOwner });

        var audit = Substitute.For<IAuditLogger>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new RegisterRestaurantDriverCommandHandler(users, db, currentUser, audit, clock, notifier, DriverRegistrationTestStorage.Create());

        var result = await handler.Handle(new RegisterRestaurantDriverCommand(
            FullNameAr: "سائق قاصر",
            FullNameEn: "Minor Driver",
            Phone: "+96554443323",
            Email: "minor@test.local",
            NationalId: "299010188812",
            NationalIdExpiry: clock.UtcNow.AddYears(1),
            VehiclePlate: "PLATE-98",
            VehicleModel: "Camry",
            VehicleType: "Car",
            LicenseNumber: "DL-MINOR-1",
            LicenseExpiry: clock.UtcNow.AddYears(1),
            VehicleLicenseExpiry: clock.UtcNow.AddYears(1),
            ContractExpiry: clock.UtcNow.AddYears(1),
            RestaurantId: restId,
            NationalIdFrontStorageKey: "docs/nid_f.jpg",
            NationalIdBackStorageKey: "docs/nid_b.jpg",
            DrivingLicenseFrontStorageKey: "docs/dl_f.jpg",
            DrivingLicenseBackStorageKey: "docs/dl_b.jpg",
            VehicleRegistrationStorageKey: "docs/veh.jpg",
            DateOfBirth: clock.UtcNow.AddYears(-17)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.Underage");
    }

    [Fact]
    public async Task AdminApprove_WhenDocumentsAreSubmitted_AtomicallyApprovesLatestDocuments()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var reg = new DriverRegistration
        {
            SubmittedByUserId = driverUserId,
            Phone = "+96555556666",
            FullNameAr = "سائق معتمد",
            FullNameEn = "Approved Driver",
            NationalId = "299010188833",
            LicenseNumber = "DL-APPROV-1",
            VehiclePlate = "KW-1111",
            NationalIdExpiryUtc = now.AddYears(1),
            LicenseExpiryUtc = now.AddYears(1),
            VehicleRegistrationExpiryUtc = now.AddYears(1),
            ProfileImageStorageKey = "docs/photo.jpg",
            Status = VerificationStatus.UnderReview,
            AdminApprovalStatus = VerificationStatus.Submitted
        };
        db.DriverRegistrations.Add(reg);

        var nidDoc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.NationalIdFront,
            StoragePath = "docs/nid.jpg",
            FileName = "nid.jpg",
            ContentType = "image/jpeg",
            ExpiresAtUtc = now.AddYears(1)
        };
        nidDoc.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = now.AddHours(-1)
        });
        db.Documents.Add(nidDoc);

        var dlDoc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            StoragePath = "docs/dl.jpg",
            FileName = "dl.jpg",
            ContentType = "image/jpeg",
            ExpiresAtUtc = now.AddYears(1)
        };
        dlDoc.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = now.AddHours(-1)
        });
        db.Documents.Add(dlDoc);

        var vehDoc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.VehicleRegistration,
            StoragePath = "docs/veh.jpg",
            FileName = "veh.jpg",
            ContentType = "image/jpeg",
            ExpiresAtUtc = now.AddYears(1)
        };
        vehDoc.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = now.AddHours(-1)
        });
        db.Documents.Add(vehDoc);

        await db.SaveChangesAsync();

        var userDto = new UserAccountDto(driverUserId, "driver@test.local", reg.Phone, reg.FullNameEn, UserType.Driver, AccountStatus.Registered, null, true, true, now, null, false);
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(driverUserId, Arg.Any<CancellationToken>())
            .Returns(userDto);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Roles.Returns(new List<string> { AppRoles.SuperAdmin });

        var audit = Substitute.For<IAuditLogger>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new AdminDriverDecisionCommandHandler(db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        // Act
        var result = await handler.Handle(new AdminDriverDecisionCommand(reg.Id, "approve", "Approved by Admin"), CancellationToken.None);

        // Assert: Approval succeeds and documents are atomically verified as Approved
        result.IsSuccess.Should().BeTrue();

        var reloadedNid = await db.Documents.Include(d => d.Verifications).FirstAsync(d => d.Id == nidDoc.Id);
        var latestNidVerif = reloadedNid.Verifications.OrderByDescending(v => v.CreatedAtUtc).First();
        latestNidVerif.Status.Should().Be(VerificationStatus.Approved);

        var reloadedDl = await db.Documents.Include(d => d.Verifications).FirstAsync(d => d.Id == dlDoc.Id);
        var latestDlVerif = reloadedDl.Verifications.OrderByDescending(v => v.CreatedAtUtc).First();
        latestDlVerif.Status.Should().Be(VerificationStatus.Approved);

        var reloadedVeh = await db.Documents.Include(d => d.Verifications).FirstAsync(d => d.Id == vehDoc.Id);
        var latestVehVerif = reloadedVeh.Verifications.OrderByDescending(v => v.CreatedAtUtc).First();
        latestVehVerif.Status.Should().Be(VerificationStatus.Approved);
    }
}
