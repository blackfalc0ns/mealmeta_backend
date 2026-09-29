using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class RestaurantRegistrationOutboxTests
{
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    private static readonly DocumentType[] RequiredDocumentTypes =
    [
        DocumentType.CommercialRegistration,
        DocumentType.CommercialLicense,
        DocumentType.FoodLicense,
        DocumentType.AuthorizedSignatoryId,
        DocumentType.AuthorizedSignatoryAuthorization,
        DocumentType.BankIbanProof,
        DocumentType.ProfileImage,
        DocumentType.BranchLicense,
    ];

    private static Document CreateDocument(Guid ownerId, DocumentType type) => new()
    {
        OwnerType = DocumentOwnerType.Registration,
        OwnerId = ownerId,
        DocumentType = type,
        FileName = $"{type}.pdf",
        ContentType = type is DocumentType.ProfileImage or DocumentType.BranchPhoto ? "image/png" : "application/pdf",
        StoragePath = "path",
        SizeBytes = 1,
        CreatedAtUtc = DateTime.UtcNow,
        Verifications =
        [
            new DocumentVerification
            {
                Status = VerificationStatus.Submitted,
                CreatedAtUtc = DateTime.UtcNow,
            }
        ]
    };

    private static RestaurantRegistration CreateValidRegistration(Guid userId) => new()
    {
        SubmittedByUserId = userId,
        Status = VerificationStatus.Draft,
        LegalNameAr = "اسم الشركة",
        LegalNameEn = "Company Name",
        TradeNameAr = "المطعم",
        TradeNameEn = "Restaurant",
        ContactPhone = "50000000",
        ContactEmail = "restaurant@test.com",
        CommercialRegistrationNumber = "12345",
        CommercialRegistrationExpiry = new DateOnly(2030, 1, 1),
        CommercialLicenseNumber = "CL-123",
        CommercialLicenseExpiry = new DateOnly(2030, 1, 1),
        FoodLicenseNumber = "FL-123",
        FoodLicenseExpiry = new DateOnly(2030, 1, 1),
        LegalEntityType = "Company",
        CompanyCivilId = "123456789012",
        Address = "Kuwait City",
        Latitude = 29.3407455m,
        Longitude = 48.0838975m,
        OwnerName = "Owner",
        AuthorizedSignatoryCivilId = "987654321012",
        AuthorizedSignatoryTitle = "Manager",
        PrimaryContactName = "Contact",
        PrimaryContactTitle = "Coordinator",
        PrimaryContactPhone = "51111111",
        PrimaryContactEmail = "contact@test.com",
        BankName = "Bank",
        BankBeneficiaryName = "Beneficiary",
        Iban = "KW74CBKU0000000000001234567890",
        BankAccountNumber = "1234567890",
        SettlementCurrency = "KWD",
        DeliveryByRestaurantConfirmed = true,
        DataResponsibilityConfirmed = true,
        CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task SubmitRestaurantRegistration_Adds_RestaurantRegistrationSubmitted_OutboxMessage()
    {
        var userId = Guid.NewGuid();
        var registration = CreateValidRegistration(userId);

        await using var db = CreateInMemoryContext();
        db.RestaurantRegistrations.Add(registration);
        db.Documents.AddRange(RequiredDocumentTypes.Select(type => CreateDocument(registration.Id, type)));
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Restaurant);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        var handler = new SubmitRestaurantRegistrationCommandHandler(
            db,
            currentUser,
            Substitute.For<IAuditLogger>(),
            clock);

        var result = await handler.Handle(
            new SubmitRestaurantRegistrationCommand(registration.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventType == "RestaurantRegistrationSubmitted");
        outbox.Should().NotBeNull("Expected OutboxMessage with EventType 'RestaurantRegistrationSubmitted'");
        outbox!.AggregateType.Should().Be("RestaurantRegistration");
        outbox.AggregateId.Should().Be(registration.Id.ToString());

        using var doc = JsonDocument.Parse(outbox.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("RegistrationId").GetGuid().Should().Be(registration.Id);
        root.GetProperty("RestaurantName").GetString().Should().Be("المطعم");
        root.GetProperty("SubmittedByUserId").GetGuid().Should().Be(userId);
    }

    [Fact]
    public async Task AdminDecision_Approve_Adds_RestaurantRegistrationApproved_OutboxMessage()
    {
        var userId = Guid.NewGuid();
        var registration = CreateValidRegistration(userId);
        registration.Status = VerificationStatus.Submitted;

        await using var db = CreateInMemoryContext();
        db.RestaurantRegistrations.Add(registration);
        foreach (var fieldKey in RegistrationFieldKeys.RequiredForApproval(registration))
        {
            var review = new RestaurantRegistrationFieldReview
            {
                RegistrationId = registration.Id,
                FieldKey = fieldKey,
                Status = VerificationStatus.Approved,
                CreatedAtUtc = DateTime.UtcNow
            };
            registration.FieldReviews.Add(review);
            db.RestaurantRegistrationFieldReviews.Add(review);
        }

        foreach (var type in RequiredDocumentTypes)
        {
            var documentItem = CreateDocument(registration.Id, type);
            documentItem.Verifications.Clear();
            documentItem.Verifications.Add(new DocumentVerification { Status = VerificationStatus.Approved, CreatedAtUtc = DateTime.UtcNow });
            db.Documents.Add(documentItem);
        }
        await db.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(adminId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);

        var users = Substitute.For<IUserAccountService>();
        users.GetRolesAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<string>());
        users.AddToRoleAsync(userId, AppRoles.RestaurantOwner, Arg.Any<CancellationToken>()).Returns(MealMate.Application.Common.Models.Result.Success());
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealMate.Application.Common.Models.Result.Success(new UserAccountDto(
                Guid.NewGuid(), "dm@test.com", "+96500000000", "DM", UserType.DeliveryManager, AccountStatus.Registered, null, false, false, DateTime.UtcNow, null)));

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        var handler = new AdminRestaurantDecisionCommandHandler(
            db,
            users,
            currentUser,
            Substitute.For<IAuditLogger>(),
            clock);

        var result = await handler.Handle(
            new AdminRestaurantDecisionCommand(registration.Id, "approve", "Approved"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventType == "RestaurantRegistrationApproved");
        outbox.Should().NotBeNull("Expected OutboxMessage with EventType 'RestaurantRegistrationApproved'");
        outbox!.AggregateType.Should().Be("RestaurantRegistration");
        outbox.AggregateId.Should().Be(registration.Id.ToString());

        using var doc = JsonDocument.Parse(outbox.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("RegistrationId").GetGuid().Should().Be(registration.Id);
        root.GetProperty("OwnerUserId").GetGuid().Should().Be(userId);
    }

    [Fact]
    public async Task AdminDecision_Reject_Adds_RestaurantRegistrationRejected_OutboxMessage()
    {
        var userId = Guid.NewGuid();
        var registration = CreateValidRegistration(userId);
        registration.Status = VerificationStatus.Submitted;

        await using var db = CreateInMemoryContext();
        db.RestaurantRegistrations.Add(registration);
        await db.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(adminId);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        var handler = new AdminRestaurantDecisionCommandHandler(
            db,
            Substitute.For<IUserAccountService>(),
            currentUser,
            Substitute.For<IAuditLogger>(),
            clock);

        var result = await handler.Handle(
            new AdminRestaurantDecisionCommand(registration.Id, "reject", "Invalid licenses"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventType == "RestaurantRegistrationRejected");
        outbox.Should().NotBeNull("Expected OutboxMessage with EventType 'RestaurantRegistrationRejected'");
        outbox!.AggregateType.Should().Be("RestaurantRegistration");
        outbox.AggregateId.Should().Be(registration.Id.ToString());

        using var doc = JsonDocument.Parse(outbox.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("RegistrationId").GetGuid().Should().Be(registration.Id);
        root.GetProperty("OwnerUserId").GetGuid().Should().Be(userId);
        root.GetProperty("Reason").GetString().Should().Be("Invalid licenses");
    }

    [Fact]
    public async Task AdminDecision_RequestChanges_Adds_RestaurantRegistrationChangesRequested_OutboxMessage()
    {
        var userId = Guid.NewGuid();
        var registration = CreateValidRegistration(userId);
        registration.Status = VerificationStatus.Submitted;

        await using var db = CreateInMemoryContext();
        db.RestaurantRegistrations.Add(registration);
        await db.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(adminId);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        var handler = new AdminRestaurantDecisionCommandHandler(
            db,
            Substitute.For<IUserAccountService>(),
            currentUser,
            Substitute.For<IAuditLogger>(),
            clock);

        var result = await handler.Handle(
            new AdminRestaurantDecisionCommand(registration.Id, "request-changes", "Please update commercial license"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventType == "RestaurantRegistrationChangesRequested");
        outbox.Should().NotBeNull("Expected OutboxMessage with EventType 'RestaurantRegistrationChangesRequested'");
        outbox!.AggregateType.Should().Be("RestaurantRegistration");
        outbox.AggregateId.Should().Be(registration.Id.ToString());

        using var doc = JsonDocument.Parse(outbox.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("RegistrationId").GetGuid().Should().Be(registration.Id);
        root.GetProperty("OwnerUserId").GetGuid().Should().Be(userId);
        root.GetProperty("Reason").GetString().Should().Be("Please update commercial license");
    }
}
