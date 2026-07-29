using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class CustomerDriverSelfServiceTests
{
    [Fact]
    public async Task Get_customer_profile_is_scoped_to_current_user()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using var db = CreateDbContext(context =>
        {
            context.CustomerProfiles.Add(CreateCustomerProfile(currentUserId, "ar"));
            context.CustomerProfiles.Add(CreateCustomerProfile(otherUserId, "en"));
        });
        var currentUser = CurrentUser(currentUserId);
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(currentUserId, Arg.Any<CancellationToken>())
            .Returns(CustomerAccount(currentUserId));

        var handler = new GetCustomerProfileQueryHandler(db, currentUser, users);
        var result = await handler.Handle(new GetCustomerProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(currentUserId);
        result.Value.PreferredLanguage.Should().Be("ar");
    }

    [Fact]
    public async Task Update_customer_profile_changes_only_current_users_profile()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var currentProfile = CreateCustomerProfile(currentUserId, "ar");
        var otherProfile = CreateCustomerProfile(otherUserId, "en");
        await using var db = CreateDbContext(context =>
        {
            context.CustomerProfiles.Add(currentProfile);
            context.CustomerProfiles.Add(otherProfile);
        });
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(currentUserId, Arg.Any<CancellationToken>())
            .Returns(CustomerAccount(currentUserId));
        var audit = Substitute.For<IAuditLogger>();
        var handler = new UpdateCustomerProfileCommandHandler(
            db,
            CurrentUser(currentUserId),
            users,
            audit);

        var result = await handler.Handle(
            new UpdateCustomerProfileCommand(
                "Updated Customer",
                "en",
                null,
                "Updated address",
                29.3759m,
                47.9774m,
                null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        currentProfile.PreferredLanguage.Should().Be("en");
        currentProfile.DeliveryAddressLine.Should().Be("Updated address");
        otherProfile.PreferredLanguage.Should().Be("en");
        otherProfile.DeliveryAddressLine.Should().BeNull();
        await users.Received(1).UpdateFullNameAsync(
            currentUserId,
            "Updated Customer",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_customer_profile_image_rejects_unsupported_content_type()
    {
        var storage = Substitute.For<IFileStorage>();
        var handler = new UploadCustomerProfileImageCommandHandler(
            Substitute.For<IAppDbContext>(),
            CurrentUser(Guid.NewGuid()),
            Substitute.For<IUserAccountService>(),
            storage,
            Substitute.For<IAuditLogger>());
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not-an-image"));

        var result = await handler.Handle(
            new UploadCustomerProfileImageCommand("profile.svg", "image/svg+xml", stream),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await storage.DidNotReceive().SaveAsync(
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_customer_profile_image_is_idempotent_when_no_image_exists()
    {
        var currentUserId = Guid.NewGuid();
        await using var db = CreateDbContext(context =>
            context.CustomerProfiles.Add(CreateCustomerProfile(currentUserId, "ar")));
        var storage = Substitute.For<IFileStorage>();
        var audit = Substitute.For<IAuditLogger>();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(currentUserId, Arg.Any<CancellationToken>())
            .Returns(CustomerAccount(currentUserId));
        var handler = new DeleteCustomerProfileImageCommandHandler(
            db,
            CurrentUser(currentUserId),
            users,
            storage,
            audit);

        var result = await handler.Handle(
            new DeleteCustomerProfileImageCommand(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await storage.DidNotReceive().DeleteAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await audit.DidNotReceive().LogAsync(
            Arg.Any<AuditEntry>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_driver_registration_is_allowed_for_owner_in_draft()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateDriverRegistration(ownerId, VerificationStatus.Draft);
        await using var db = CreateDbContext(context =>
            context.DriverRegistrations.Add(registration));
        var handler = new UpdateDriverRegistrationCommandHandler(
            db,
            CurrentUser(ownerId),
            Substitute.For<IAuditLogger>());

        var result = await handler.Handle(
            DriverUpdate(registration.Id, vehiclePlate: "NEW-123"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        registration.VehiclePlate.Should().Be("NEW-123");
    }

    [Fact]
    public async Task Update_driver_registration_is_forbidden_for_another_user()
    {
        var registration = CreateDriverRegistration(Guid.NewGuid(), VerificationStatus.Draft);
        await using var db = CreateDbContext(context =>
            context.DriverRegistrations.Add(registration));
        var handler = new UpdateDriverRegistrationCommandHandler(
            db,
            CurrentUser(Guid.NewGuid()),
            Substitute.For<IAuditLogger>());

        var result = await handler.Handle(
            DriverUpdate(registration.Id, vehiclePlate: "NEW-123"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
        registration.VehiclePlate.Should().Be("OLD-001");
    }

    [Fact]
    public async Task Update_driver_registration_conflicts_after_submission()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateDriverRegistration(ownerId, VerificationStatus.Submitted);
        await using var db = CreateDbContext(context =>
            context.DriverRegistrations.Add(registration));
        var handler = new UpdateDriverRegistrationCommandHandler(
            db,
            CurrentUser(ownerId),
            Substitute.For<IAuditLogger>());

        var result = await handler.Handle(
            DriverUpdate(registration.Id, vehiclePlate: "NEW-123"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
        registration.VehiclePlate.Should().Be("OLD-001");
    }

    [Fact]
    public async Task Update_driver_registration_rejects_missing_current_user()
    {
        var registration = CreateDriverRegistration(Guid.NewGuid(), VerificationStatus.Draft);
        await using var db = CreateDbContext(context =>
            context.DriverRegistrations.Add(registration));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new UpdateDriverRegistrationCommandHandler(
            db,
            currentUser,
            Substitute.For<IAuditLogger>());

        var result = await handler.Handle(
            DriverUpdate(registration.Id, vehiclePlate: "NEW-123"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
        registration.VehiclePlate.Should().Be("OLD-001");
    }

    [Fact]
    public async Task Driver_document_upload_uses_encrypted_documents_folder()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateDriverRegistration(ownerId, VerificationStatus.Draft);
        await using var db = CreateDbContext(context =>
            context.DriverRegistrations.Add(registration));
        var storage = Substitute.For<IFileStorage>();
        storage.SaveAsync(
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new StoredFile("documents/Registration/key.enc", "id.pdf", "application/pdf", 3, "hash"));
        var handler = new UploadDocumentCommandHandler(
            db,
            storage,
            Substitute.For<IAuditLogger>(),
            Substitute.For<IDateTimeProvider>(),
            CurrentUser(ownerId));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf"));

        var result = await handler.Handle(
            new UploadDocumentCommand(
                DocumentOwnerType.Registration,
                registration.Id,
                DocumentType.NationalId,
                "id.pdf",
                "application/pdf",
                stream),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await storage.Received(1).SaveAsync(
            Arg.Any<Stream>(),
            "id.pdf",
            "application/pdf",
            Arg.Is<string>(folder => folder.StartsWith("documents/", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    private static UpdateDriverRegistrationCommand DriverUpdate(
        Guid id,
        string? vehiclePlate = null) =>
        new(
            id,
            null,
            null,
            null,
            null,
            null,
            null,
            vehiclePlate,
            null,
            null,
            null);

    private static CustomerProfile CreateCustomerProfile(Guid userId, string language) =>
        new()
        {
            UserId = userId,
            PreferredLanguage = language,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static DriverRegistration CreateDriverRegistration(
        Guid ownerId,
        VerificationStatus status) =>
        new()
        {
            SubmittedByUserId = ownerId,
            FullNameAr = "سائق",
            FullNameEn = "Driver",
            Phone = "0500000000",
            VehiclePlate = "OLD-001",
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static UserAccountDto CustomerAccount(Guid userId) =>
        new(
            userId,
            "customer@example.com",
            "0500000000",
            "Customer",
            UserType.Customer,
            AccountStatus.Active,
            null,
            true,
            true,
            DateTime.UtcNow,
            null);

    private static ICurrentUser CurrentUser(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(Arg.Any<string>()).Returns(false);
        return currentUser;
    }

    private static AppDbContext CreateDbContext(Action<AppDbContext> seed)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        seed(db);
        db.SaveChanges();
        return db;
    }
}
