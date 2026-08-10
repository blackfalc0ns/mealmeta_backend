using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public sealed class AdminRestaurantAccountsTests
{
    [Fact]
    public async Task List_includes_verified_restaurant_account_without_profile()
    {
        var userId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);
        var users = Substitute.For<IUserAccountService>();
        users.ListUsersByTypeAsync(UserType.Restaurant, Arg.Any<CancellationToken>())
            .Returns([
                new UserAccountDto(
                    userId,
                    "owner@restaurant.test",
                    "0500000000",
                    "New Restaurant Owner",
                    UserType.Restaurant,
                    AccountStatus.Registered,
                    null,
                    true,
                    false,
                    createdAt,
                    null),
            ]);

        await using var db = CreateDbContext();
        var handler = new ListAdminRestaurantsQueryHandler(db, users);

        var result = await handler.Handle(
            new ListAdminRestaurantsQuery(Page: 1, PageSize: 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle();

        var account = result.Value.Items[0];
        account.Id.Should().Be(userId);
        account.UserId.Should().Be(userId);
        account.ProfileId.Should().BeNull();
        account.RegistrationId.Should().BeNull();
        account.TradeNameEn.Should().Be("****wner");
        account.OwnerName.Should().Be("****wner");
        account.ContactEmail.Should().Be("****test");
        account.VerificationStatus.Should().Be(VerificationStatus.Draft);
        account.IsOperationallyActive.Should().BeFalse();

        (await db.RestaurantProfiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Detail_returns_profile_with_registration_and_documents()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = CreateDbContext();

        // Arrange: create a restaurant profile
        var profile = new RestaurantProfile
        {
            UserId = userId,
            LegalNameAr = "مطعم تيت",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "تيت",
            TradeNameEn = "Test",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = false,
            ContactPhone = "+96550000000",
            ContactEmail = "test@test.com",
            CreatedAtUtc = now,
            ModifiedAtUtc = now,
        };

        // Arrange: create a registration linked to the same user
        var registration = new MealMate.Domain.IdentityAccess.RestaurantRegistration
        {
            LegalNameAr = "مطعم تيت",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "تيت",
            TradeNameEn = "Test",
            ContactPhone = "+96550000000",
            ContactEmail = "reg@test.com",
            CommercialRegistrationNumber = "CR12345",
            OwnerName = "Test Owner",
            Address = "Test Address, Block 1",
            CountryId = Guid.NewGuid(),
            RegionId = Guid.NewGuid(),
            Status = VerificationStatus.Approved,
            ResultingRestaurantId = profile.Id,
            CreatedAtUtc = now,
            ModifiedAtUtc = now,
        };

        // Arrange: add a document for the registration
        var document = new Document
        {
            DocumentType = DocumentType.CommercialLicense,
            FileName = "cr.pdf",
            ContentType = "application/pdf",
            StoragePath = "documents/registrations/cr.pdf",
            SizeBytes = 1024,
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = registration.Id,
            CreatedAtUtc = now,
        };

        db.RestaurantProfiles.Add(profile);
        db.RestaurantRegistrations.Add(registration);
        db.Documents.Add(document);

        await db.SaveChangesAsync(CancellationToken.None);

        // Act
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserIdentityDto>());
        var handler = new GetAdminRestaurantProfileDetailQueryHandler(db, currentUser, users);
        var result = await handler.Handle(
            new GetAdminRestaurantProfileDetailQuery(profile.Id),
            CancellationToken.None);
        var revealed = await handler.Handle(
            new GetAdminRestaurantProfileDetailQuery(profile.Id, IncludeSensitive: true),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var detail = result.Value;
        detail.Profile.Should().NotBeNull();
        detail.Profile.Id.Should().Be(profile.Id);
        detail.Profile.LegalNameEn.Should().Be("Test Restaurant");
        detail.Profile.VerificationStatus.Should().Be(VerificationStatus.Approved);

        detail.Registration.Should().NotBeNull();
        detail.Registration!.Registration.Should().NotBeNull();
        detail.Registration.Registration.ContactEmail.Should().Be("****.com");
        detail.Registration.Registration.CommercialRegistrationNumber.Should().Be("****2345");
        detail.Registration.Registration.Address.Should().Be("****ck 1");
        detail.SensitiveDataMasked.Should().BeTrue();
        detail.CanRevealSensitive.Should().BeTrue();
        detail.Registration.SensitiveDataMasked.Should().BeTrue();
        detail.Registration.CanRevealSensitive.Should().BeTrue();

        detail.Registration.Documents.Should().ContainSingle();
        detail.Registration.Documents[0].FileName.Should().Be("cr.pdf");
        detail.Registration.Documents[0].DocumentType.Should().Be(DocumentType.CommercialLicense);

        revealed.IsSuccess.Should().BeTrue();
        revealed.Value.Registration!.Registration.ContactEmail.Should().Be("reg@test.com");
        revealed.Value.Registration!.Registration.CommercialRegistrationNumber.Should().Be("CR12345");
        revealed.Value.Registration!.Registration.Address.Should().Be("Test Address, Block 1");
        revealed.Value.SensitiveDataMasked.Should().BeFalse();
        revealed.Value.CanRevealSensitive.Should().BeTrue();
    }

    [Fact]
    public async Task Detail_returns_not_found_for_missing_profile()
    {
        await using var db = CreateDbContext();
        var currentUser = Substitute.For<ICurrentUser>();
        var users = Substitute.For<IUserAccountService>();
        var handler = new GetAdminRestaurantProfileDetailQueryHandler(db, currentUser, users);

        var result = await handler.Handle(
            new GetAdminRestaurantProfileDetailQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    [Fact]
    public async Task Detail_view_only_admin_cannot_reveal_sensitive_data()
    {
        var profile = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "Test",
            LegalNameEn = "Test",
            TradeNameAr = "Test",
            TradeNameEn = "Test",
            CommercialLicenseNumber = "CL12345",
            CreatedAtUtc = DateTime.UtcNow,
        };
        await using var db = CreateDbContext();
        db.RestaurantProfiles.Add(profile);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(true);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var users = Substitute.For<IUserAccountService>();
        var handler = new GetAdminRestaurantProfileDetailQueryHandler(db, currentUser, users);

        var result = await handler.Handle(
            new GetAdminRestaurantProfileDetailQuery(profile.Id, IncludeSensitive: true),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public void Registration_list_contract_excludes_contact_and_sensitive_fields()
    {
        var properties = typeof(RestaurantRegistrationSummaryDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var excluded in new[]
                 {
                     "ContactPhone",
                     "ContactEmail",
                     "CommercialRegistrationNumber",
                     "CommercialLicenseNumber",
                     "FoodLicenseNumber",
                     "CompanyCivilId",
                     "AuthorizedSignatoryCivilId",
                     "Iban",
                     "BankAccountNumber",
                     "SwiftCode",
                 })
        {
            properties.Should().NotContain(excluded);
        }
    }

    [Fact]
    public async Task Restaurant_registration_draft_fields_are_nullable_in_ef_model()
    {
        await using var db = CreateDbContext();
        var entityType = db.Model.FindEntityType(typeof(MealMate.Domain.IdentityAccess.RestaurantRegistration));
        entityType.Should().NotBeNull();

        foreach (var propertyName in new[]
                 {
                     nameof(MealMate.Domain.IdentityAccess.RestaurantRegistration.LegalNameAr),
                     nameof(MealMate.Domain.IdentityAccess.RestaurantRegistration.LegalNameEn),
                     nameof(MealMate.Domain.IdentityAccess.RestaurantRegistration.TradeNameAr),
                     nameof(MealMate.Domain.IdentityAccess.RestaurantRegistration.TradeNameEn),
                     nameof(MealMate.Domain.IdentityAccess.RestaurantRegistration.ContactPhone),
                     nameof(MealMate.Domain.IdentityAccess.RestaurantRegistration.ContactEmail),
                 })
        {
            entityType!.FindProperty(propertyName)!.IsNullable.Should().BeTrue(propertyName);
        }
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
