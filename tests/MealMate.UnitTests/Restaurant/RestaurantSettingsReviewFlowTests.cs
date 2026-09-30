using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class RestaurantSettingsReviewFlowTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly RestaurantChangeReviewService _reviewService;
    private readonly RestaurantReviewCenterHandlers _reviewCenterHandlers;

    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _registrationId = Guid.NewGuid();
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();

    public RestaurantSettingsReviewFlowTests()
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

        _reviewService = new RestaurantChangeReviewService(_db, _currentUser, _audit, _clock, _notifier);
        _reviewCenterHandlers = new RestaurantReviewCenterHandlers(_db, _currentUser);

        SeedApprovedRestaurant();
    }

    private void SeedApprovedRestaurant()
    {
        var reg = new RestaurantRegistration
        {
            SubmittedByUserId = _ownerUserId,
            TradeNameAr = "مطعم الشاطئ المعتمد",
            TradeNameEn = "Approved Seaside",
            LegalNameAr = "شركة الشاطئ للتجارة",
            LegalNameEn = "Approved Seaside Co",
            ContactPhone = "96590000001",
            ContactEmail = "seaside@example.com",
            Iban = "KW81CBKU0000000000001234560101",
            BankAccountNumber = "1234560101",
            Status = VerificationStatus.Approved,
            ResultingRestaurantId = _restaurantId,
            ApprovedVersion = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(reg, _registrationId);
        _db.RestaurantRegistrations.Add(reg);

        var profile = new RestaurantProfile
        {
            UserId = _ownerUserId,
            TradeNameAr = "مطعم الشاطئ المعتمد",
            TradeNameEn = "Approved Seaside",
            LegalNameAr = "شركة الشاطئ للتجارة",
            LegalNameEn = "Approved Seaside Co",
            ContactPhone = "96590000001",
            ContactEmail = "seaside@example.com",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
            ApprovedVersion = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(profile, _restaurantId);
        _db.RestaurantProfiles.Add(profile);

        _db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static RestaurantSettingsDraftPayload CreateSamplePayload(string tradeNameAr, string tradeNameEn, string? iban = null) => new(
        LegalNameAr: "شركة الشاطئ للتجارة",
        LegalNameEn: "Approved Seaside Co",
        TradeNameAr: tradeNameAr,
        TradeNameEn: tradeNameEn,
        ContactPhone: "96590000001",
        ContactEmail: "seaside@example.com",
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
        BankBeneficiaryName: "شركة الشاطئ",
        Iban: iban ?? "KW81CBKU0000000000001234560101",
        BankAccountNumber: "1234560101",
        SwiftCode: "CBKUKWKW",
        Address: "شارع الخليج",
        CountryId: null,
        RegionId: null,
        Latitude: 29.3759m,
        Longitude: 47.9774m);

    [Fact]
    public async Task SubmitProposal_EnqueuesNotification_WithStableType_AndSafeDeepLink()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var payload = CreateSamplePayload("مطعم الشاطئ الذهبي الجديد", "Approved Seaside Golden");

        var command = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(new List<string> { "TradeNameAr", "TradeNameEn" }));

        var result = await _reviewService.SubmitChangeAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var notification = await _db.RestaurantNotifications
            .FirstOrDefaultAsync(n => n.Type == "restaurant-change.submitted" && n.SubjectId == result.Value.Id);

        notification.Should().NotBeNull();
        notification!.UserId.Should().Be(_ownerUserId);
        notification.DeepLink.Should().Be("/restaurant/settings");
        notification.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task DecideProposal_Approved_EnqueuesNotification_WithStableType_AndRedactsSensitiveBankData()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var payload = CreateSamplePayload("مطعم الشاطئ الحديث", "Approved Seaside Modern", iban: "KW81CBKU0000000000009999990101");

        var submitResult = await _reviewService.SubmitChangeAsync(new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(new List<string> { "TradeNameAr", "TradeNameEn", "Iban" })), CancellationToken.None);

        submitResult.IsSuccess.Should().BeTrue();

        // Admin approval
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var decideResult = await _reviewService.DecideChangeAsync(new DecideRestaurantChangeCommand(
            submitResult.Value.Id,
            "Approved",
            "Valid banking and commercial documents verified",
            ExpectedVersion: 1), CancellationToken.None);

        decideResult.IsSuccess.Should().BeTrue();

        var notification = await _db.RestaurantNotifications
            .FirstOrDefaultAsync(n => n.Type == "restaurant-change.approved" && n.SubjectId == submitResult.Value.Id);

        notification.Should().NotBeNull();
        notification!.UserId.Should().Be(_ownerUserId);
        notification.DeepLink.Should().Be("/restaurant/settings");
        notification.BodyAr.Should().NotContain("KW81CBKU0000000000009999990101");
        notification.BodyEn.Should().NotContain("KW81CBKU0000000000009999990101");

        // Verify version bumped on profile
        var profile = await _db.RestaurantProfiles.FindAsync(_restaurantId);
        profile!.ApprovedVersion.Should().Be(2);
        profile.TradeNameAr.Should().Be("مطعم الشاطئ الحديث");
    }

    [Fact]
    public async Task DecideProposal_NeedsChanges_EnqueuesNotification_AndAllowsCorrection()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var payload = CreateSamplePayload("مطعم الشاطئ الخاطئ", "Approved Seaside Mistake");

        var submitResult = await _reviewService.SubmitChangeAsync(new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(new List<string> { "TradeNameAr" })), CancellationToken.None);

        submitResult.IsSuccess.Should().BeTrue();

        // Admin returns NeedsChanges
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var decideResult = await _reviewService.DecideChangeAsync(new DecideRestaurantChangeCommand(
            submitResult.Value.Id,
            "NeedsChanges",
            "Trade name does not match license certificate",
            ExpectedVersion: 1), CancellationToken.None);

        decideResult.IsSuccess.Should().BeTrue();
        decideResult.Value.Status.Should().Be(VerificationStatus.NeedsChanges);

        var notification = await _db.RestaurantNotifications
            .FirstOrDefaultAsync(n => n.Type == "restaurant-change.needs-changes" && n.SubjectId == submitResult.Value.Id);

        notification.Should().NotBeNull();
        notification!.UserId.Should().Be(_ownerUserId);
        notification.BodyAr.Should().Contain("Trade name does not match license certificate");

        // Resubmit correction
        _currentUser.UserId.Returns(_ownerUserId);
        var correctedPayload = CreateSamplePayload("مطعم الشاطئ المصحح", "Approved Seaside Corrected");
        var resubmitResult = await _reviewService.SubmitChangeAsync(new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Profile,
            JsonSerializer.Serialize(correctedPayload),
            JsonSerializer.Serialize(new List<string> { "TradeNameAr" })), CancellationToken.None);

        resubmitResult.IsSuccess.Should().BeTrue();
        resubmitResult.Value.PayloadVersion.Should().Be(2);
        resubmitResult.Value.Status.Should().Be(VerificationStatus.Submitted);
    }

    [Fact]
    public async Task Notification_MarkAsRead_UpdatesIsReadStatus()
    {
        var notification = new RestaurantNotification
        {
            UserId = _ownerUserId,
            Type = "restaurant-change.approved",
            TitleAr = "تم الاعتماد",
            TitleEn = "Approved",
            BodyAr = "تم اعتماد التعديلات",
            BodyEn = "Changes approved",
            IsRead = false,
            SentAtUtc = DateTime.UtcNow
        };
        _db.RestaurantNotifications.Add(notification);
        await _db.SaveChangesAsync(CancellationToken.None);

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);

        var result = await _reviewCenterHandlers.Handle(
            new MarkRestaurantReviewNotificationReadCommand(notification.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updated = await _db.RestaurantNotifications.FindAsync(notification.Id);
        updated!.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task EmergencyOverride_ActivationAndResolution_EnqueuesNotifications_AndMaintainsAuditTrail()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var activateHandler = new ActivateRestaurantEmergencyOverrideCommandHandler(_db, _currentUser, _audit, _clock);
        var resolveHandler = new ResolveRestaurantEmergencyOverrideCommandHandler(_db, _currentUser, _audit, _clock);

        var activateCommand = new ActivateRestaurantEmergencyOverrideCommand(
            _restaurantId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            ReducedCapacity: 10,
            Closed: false,
            Reason: "Power generator breakdown");

        var activateResult = await activateHandler.Handle(activateCommand, CancellationToken.None);
        activateResult.IsSuccess.Should().BeTrue();

        var startNotification = await _db.RestaurantNotifications
            .FirstOrDefaultAsync(n => n.Type == "restaurant-emergency.started" && n.SubjectId == activateResult.Value.Id);
        startNotification.Should().NotBeNull();
        startNotification!.DeepLink.Should().Be("/restaurant/capacity");
        startNotification.BodyAr.Should().Contain("Power generator breakdown");

        // Resolve
        var resolveCommand = new ResolveRestaurantEmergencyOverrideCommand(
            activateResult.Value.Id,
            "Power fully restored");

        var resolveResult = await resolveHandler.Handle(resolveCommand, CancellationToken.None);
        resolveResult.IsSuccess.Should().BeTrue();

        var endNotification = await _db.RestaurantNotifications
            .FirstOrDefaultAsync(n => n.Type == "restaurant-emergency.ended" && n.SubjectId == activateResult.Value.Id);
        endNotification.Should().NotBeNull();
        endNotification!.DeepLink.Should().Be("/restaurant/capacity");
        endNotification.BodyAr.Should().Contain("Power fully restored");
    }
}
