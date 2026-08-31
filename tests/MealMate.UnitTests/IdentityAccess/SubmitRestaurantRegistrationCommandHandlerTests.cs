using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
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

public sealed class SubmitRestaurantRegistrationCommandHandlerTests
{
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    private static ICurrentUser CreateCurrentUser(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Restaurant);
        currentUser.IsInRole(Arg.Any<string>()).Returns(false);
        currentUser.HasPermission(Arg.Any<string>()).Returns(false);
        return currentUser;
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
        DocumentType.BranchPhoto,
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
    public async Task Submit_rejects_missing_required_template_field()
    {
        var userId = Guid.NewGuid();
        var registration = CreateValidRegistration(userId);
        registration.BankName = null;

        await using var db = CreateInMemoryContext();
        db.RestaurantRegistrations.Add(registration);
        db.Documents.AddRange(RequiredDocumentTypes.Select(type => CreateDocument(registration.Id, type)));
        await db.SaveChangesAsync();

        var handler = new SubmitRestaurantRegistrationCommandHandler(
            db,
            CreateCurrentUser(userId),
            Substitute.For<IAuditLogger>(),
            Substitute.For<IDateTimeProvider>());

        var result = await handler.Handle(
            new SubmitRestaurantRegistrationCommand(registration.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.RequiredField);
        result.Error.Description.Should().Contain("bankName");
    }

    [Fact]
    public async Task Submit_rejects_invalid_iban()
    {
        var userId = Guid.NewGuid();
        var registration = CreateValidRegistration(userId);
        registration.Iban = "GB29NWBK60161331926819";

        await using var db = CreateInMemoryContext();
        db.RestaurantRegistrations.Add(registration);
        db.Documents.AddRange(RequiredDocumentTypes.Select(type => CreateDocument(registration.Id, type)));
        await db.SaveChangesAsync();

        var handler = new SubmitRestaurantRegistrationCommandHandler(
            db,
            CreateCurrentUser(userId),
            Substitute.For<IAuditLogger>(),
            Substitute.For<IDateTimeProvider>());

        var result = await handler.Handle(
            new SubmitRestaurantRegistrationCommand(registration.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.InvalidFormat);
        result.Error.Description.Should().Contain("IBAN");
    }

    [Fact]
    public async Task Submit_accepts_valid_kuwait_iban()
    {
        var userId = Guid.NewGuid();
        var registration = CreateValidRegistration(userId);
        registration.Iban = "KW74CBKU0000000000001234567890";

        await using var db = CreateInMemoryContext();
        db.RestaurantRegistrations.Add(registration);
        db.Documents.AddRange(RequiredDocumentTypes.Select(type => CreateDocument(registration.Id, type)));
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var handler = new SubmitRestaurantRegistrationCommandHandler(
            db,
            CreateCurrentUser(userId),
            Substitute.For<IAuditLogger>(),
            clock);

        var result = await handler.Handle(
            new SubmitRestaurantRegistrationCommand(registration.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(VerificationStatus.Submitted);
        result.Value.Iban.Should().Be(registration.Iban);
    }
}
