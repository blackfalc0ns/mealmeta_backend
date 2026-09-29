using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class RestaurantChangeReviewTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly RestaurantChangeReviewService _service;

    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _registrationId = Guid.NewGuid();
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();

    public RestaurantChangeReviewTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _currentUser = Substitute.For<ICurrentUser>();
        _audit = Substitute.For<IAuditLogger>();
        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        _notifier = Substitute.For<IRegistrationReviewNotifier>();

        _service = new RestaurantChangeReviewService(_db, _currentUser, _audit, _clock, _notifier);

        SeedApprovedRestaurant();
    }

    private void SeedApprovedRestaurant()
    {
        var reg = new RestaurantRegistration
        {
            SubmittedByUserId = _ownerUserId,
            TradeNameAr = "مطعم البرجر المعتمد",
            TradeNameEn = "Approved Burger",
            LegalNameAr = "شركة البرجر المعتمد",
            LegalNameEn = "Approved Burger Co",
            ContactPhone = "96590000001",
            ContactEmail = "burger@example.com",
            Status = VerificationStatus.Approved,
            ResultingRestaurantId = _restaurantId,
            ApprovedVersion = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        // Use reflection to set Id on registration for in-memory db setup
        typeof(BaseEntity).GetProperty("Id")!.SetValue(reg, _registrationId);
        _db.RestaurantRegistrations.Add(reg);

        var profile = new RestaurantProfile
        {
            UserId = _ownerUserId,
            TradeNameAr = "مطعم البرجر المعتمد",
            TradeNameEn = "Approved Burger",
            LegalNameAr = "شركة البرجر المعتمد",
            LegalNameEn = "Approved Burger Co",
            ContactPhone = "96590000001",
            ContactEmail = "burger@example.com",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
            ApprovedVersion = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(profile, _restaurantId);
        _db.RestaurantProfiles.Add(profile);

        _db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Submit_WhenNoChangesDetected_RejectsWithValidationError()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.Roles.Returns(new[] { "owner" });

        var command = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(new { TradeNameAr = "مطعم البرجر المعتمد" }),
            ChangedFieldsJson: "[]" // No fields changed!
        );

        var result = await _service.SubmitChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("Validation.NoChanges", "change.no_changes_detected");
    }

    [Fact]
    public async Task Submit_WhenOpenRequestAlreadyExists_ReturnsConflict()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.Roles.Returns(new[] { "owner" });

        var openRequest = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "اسم مقترح أول" }),
            SubmittedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(openRequest);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(new { TradeNameAr = "اسم مقترح ثان" }),
            ChangedFieldsJson: "[\"TradeNameAr\"]"
        );

        var result = await _service.SubmitChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf(MessageCodes.Auth.InvalidState, "change.already_open");
    }

    [Fact]
    public async Task Submit_WhenPreviousNeedsChanges_IncrementsPayloadVersion_AndRetainsPriorNotes()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.Roles.Returns(new[] { "owner" });

        var needsChangesRequest = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.NeedsChanges,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "اسم أول" }),
            AdminNotes = "يرجى تعديل الاسم التجاري",
            SubmittedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        _db.RestaurantSettingsChangeRequests.Add(needsChangesRequest);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(new { TradeNameAr = "اسم معدل جديد" }),
            ChangedFieldsJson: "[\"TradeNameAr\"]"
        );

        var result = await _service.SubmitChangeAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PayloadVersion.Should().Be(2);
        result.Value.Status.Should().Be(VerificationStatus.Submitted);
        result.Value.AdminNotes.Should().Be("يرجى تعديل الاسم التجاري");
    }

    [Fact]
    public async Task Submit_DoesNotModifyApprovedRegistrationOrProfileBeforeApproval()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.Roles.Returns(new[] { "owner" });

        var command = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(new { TradeNameAr = "اسم جديد مقترح لا يطبق فوراً" }),
            ChangedFieldsJson: "[\"TradeNameAr\"]"
        );

        var result = await _service.SubmitChangeAsync(command, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        // Check database: live approved data MUST NOT change!
        var profile = await _db.RestaurantProfiles.FirstAsync(p => p.Id == _restaurantId);
        profile.TradeNameAr.Should().Be("مطعم البرجر المعتمد");

        var reg = await _db.RestaurantRegistrations.FirstAsync(r => r.Id == _registrationId);
        reg.TradeNameAr.Should().Be("مطعم البرجر المعتمد");
    }

    [Fact]
    public async Task Decide_WhenUserLacksAdminApprovalPermission_ReturnsForbidden()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.Permissions.Returns(Array.Empty<string>());

        var change = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "اسم مقترح" }),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(change);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new DecideRestaurantChangeCommand(change.Id, "Approved", "معتمد", ExpectedVersion: 1);
        var result = await _service.DecideChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf(MessageCodes.Auth.Forbidden, "restaurant.forbidden");
    }

    [Fact]
    public async Task Decide_WhenRejectionReasonIsMissing_ReturnsValidationError()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.Permissions.Returns(new[] { PermissionCodes.RestaurantRegApprove });

        var change = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "اسم مقترح" }),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(change);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new DecideRestaurantChangeCommand(change.Id, "Rejected", Reason: null, ExpectedVersion: 1);
        var result = await _service.DecideChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Required);
    }

    [Fact]
    public async Task Decide_WhenExpectedVersionDoesNotMatchBaseVersion_ReturnsConflict()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.Permissions.Returns(new[] { PermissionCodes.RestaurantRegApprove });

        var change = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 2, // Proposal is on version 2
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "اسم مقترح" }),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(change);
        await _db.SaveChangesAsync(CancellationToken.None);

        // Admin passes ExpectedVersion = 1 (stale)
        var command = new DecideRestaurantChangeCommand(change.Id, "Approved", "معتمد", ExpectedVersion: 1);
        var result = await _service.DecideChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf(MessageCodes.Auth.InvalidState, "change.stale_version");
    }

    [Fact]
    public async Task Decide_WhenApprovedAggregateVersionChangedConcurrently_ReturnsConflict()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.Permissions.Returns(new[] { PermissionCodes.RestaurantRegApprove });

        // Update live registration version to 2 concurrently!
        var reg = await _db.RestaurantRegistrations.FirstAsync(r => r.Id == _registrationId);
        reg.ApprovedVersion = 2;
        await _db.SaveChangesAsync(CancellationToken.None);

        var change = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1, // Based on stale version 1
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "اسم مقترح" }),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(change);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new DecideRestaurantChangeCommand(change.Id, "Approved", "معتمد", ExpectedVersion: 1);
        var result = await _service.DecideChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf(MessageCodes.Auth.InvalidState, "change.stale_version");
    }

    [Fact]
    public async Task Decide_WhenDuplicateApprovalAttempted_ReturnsConflict()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.Permissions.Returns(new[] { PermissionCodes.RestaurantRegApprove });

        var change = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Approved, // Already approved!
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "اسم مقترح" }),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(change);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new DecideRestaurantChangeCommand(change.Id, "Approved", "معتمد ثانياً", ExpectedVersion: 1);
        var result = await _service.DecideChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf(MessageCodes.Auth.InvalidState, "change.already_decided");
    }

    [Fact]
    public async Task Decide_WhenApproved_AppliesChangesAndIncrementsApprovedVersionAtomically()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.Permissions.Returns(new[] { PermissionCodes.RestaurantRegApprove });

        var newPayload = new RestaurantSettingsDraftPayload(
            LegalNameAr: "شركة البرجر المعتمد الجديدة",
            LegalNameEn: "New Approved Burger Co",
            TradeNameAr: "برجر معتمد جديد",
            TradeNameEn: "New Approved Burger",
            ContactPhone: "96590000099",
            ContactEmail: "new@example.com",
            CommercialRegistrationNumber: "CR-12345",
            CommercialRegistrationExpiry: null,
            CommercialLicenseNumber: null,
            CommercialLicenseExpiry: null,
            FoodLicenseNumber: null,
            FoodLicenseExpiry: null,
            LegalEntityType: "wll",
            CompanyCivilId: null,
            OwnerName: null,
            AuthorizedSignatoryCivilId: null,
            AuthorizedSignatoryTitle: null,
            PrimaryContactName: null,
            PrimaryContactTitle: null,
            PrimaryContactPhone: null,
            PrimaryContactEmail: null,
            BankName: "بنك الكويت الوطني",
            BankBeneficiaryName: "شركة البرجر",
            Iban: "KW81NBOK0000000000001234567890",
            BankAccountNumber: "1234567890",
            SwiftCode: "NBOKKWKW",
            Address: "شارع الخليج",
            CountryId: null,
            RegionId: null);

        var change = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(newPayload),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(change);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new DecideRestaurantChangeCommand(change.Id, "Approved", "تم الاعتماد بنجاح", ExpectedVersion: 1);
        var result = await _service.DecideChangeAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify change record updated
        var updatedChange = await _db.RestaurantSettingsChangeRequests.FirstAsync(c => c.Id == change.Id);
        updatedChange.Status.Should().Be(VerificationStatus.Approved);
        updatedChange.ReviewedByUserId.Should().Be(_adminUserId);

        // Verify live profile updated and version incremented
        var updatedProfile = await _db.RestaurantProfiles.FirstAsync(p => p.Id == _restaurantId);
        updatedProfile.TradeNameAr.Should().Be("برجر معتمد جديد");
        updatedProfile.ApprovedVersion.Should().Be(2);

        // Verify live registration updated and version incremented
        var updatedReg = await _db.RestaurantRegistrations.FirstAsync(r => r.Id == _registrationId);
        updatedReg.TradeNameAr.Should().Be("برجر معتمد جديد");
        updatedReg.ApprovedVersion.Should().Be(2);
    }
}
