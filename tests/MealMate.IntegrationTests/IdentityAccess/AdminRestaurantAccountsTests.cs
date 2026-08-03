using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
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
        account.TradeNameEn.Should().Be("New Restaurant Owner");
        account.OwnerName.Should().Be("New Restaurant Owner");
        account.ContactEmail.Should().Be("owner@restaurant.test");
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
        var handler = new GetAdminRestaurantProfileDetailQueryHandler(db);
        var result = await handler.Handle(
            new GetAdminRestaurantProfileDetailQuery(profile.Id),
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
        detail.Registration.Registration.ContactEmail.Should().Be("reg@test.com");
        detail.Registration.Registration.Address.Should().Be("Test Address, Block 1");

        detail.Registration.Documents.Should().ContainSingle();
        detail.Registration.Documents[0].FileName.Should().Be("cr.pdf");
        detail.Registration.Documents[0].DocumentType.Should().Be(DocumentType.CommercialLicense);
    }

    [Fact]
    public async Task Detail_returns_not_found_for_missing_profile()
    {
        await using var db = CreateDbContext();
        var handler = new GetAdminRestaurantProfileDetailQueryHandler(db);

        var result = await handler.Handle(
            new GetAdminRestaurantProfileDetailQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
