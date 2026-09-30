using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class RestaurantBranchChangeReviewTests
{
    private readonly Persistence.AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly CreateRestaurantBranchCommandHandler _createBranchHandler;
    private readonly ToggleBranchStatusCommandHandler _toggleBranchHandler;
    private readonly RestaurantChangeReviewService _changeReviewService;

    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _registrationId = Guid.NewGuid();

    public RestaurantBranchChangeReviewTests()
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

        _createBranchHandler = new CreateRestaurantBranchCommandHandler(_db, _currentUser);
        _toggleBranchHandler = new ToggleBranchStatusCommandHandler(_db, _currentUser);
        _changeReviewService = new RestaurantChangeReviewService(_db, _currentUser, _audit, _clock, _notifier);
    }

    private async Task SeedApprovedRestaurantAsync()
    {
        var registration = new RestaurantRegistration
        {
            SubmittedByUserId = _ownerUserId,
            ResultingRestaurantId = _restaurantId,
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            Status = VerificationStatus.Approved,
            ApprovedVersion = 1,
            CreatedAtUtc = _clock.UtcNow.AddDays(-10)
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(registration, _registrationId);

        var profile = new RestaurantProfile
        {
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "شركة الاختبار",
            LegalNameEn = "Test Company",
            ApprovedVersion = 1,
            CreatedAtUtc = _clock.UtcNow.AddDays(-10)
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(profile, _restaurantId);

        var mainBranch = new RestaurantBranch
        {
            RestaurantRegistrationId = _restaurantId,
            BranchCode = "MAIN-01",
            NameAr = "الفرع الرئيسي",
            NameEn = "Main Branch",
            AddressAr = "مدينة الكويت",
            AddressEn = "Kuwait City",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            ContactPhone = "96511111111",
            IsActive = true,
            IsPrimary = true,
            CreatedAtUtc = _clock.UtcNow.AddDays(-10)
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(mainBranch, Guid.NewGuid());

        _db.RestaurantRegistrations.Add(registration);
        _db.RestaurantProfiles.Add(profile);
        _db.RestaurantBranches.Add(mainBranch);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task DirectBranchCreate_ForApprovedRestaurant_ByOwner_IsRejectedWithReviewRequired()
    {
        await SeedApprovedRestaurantAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.Roles.Returns(new[] { "Owner" });
        _currentUser.IsInRole("Owner").Returns(true);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        var cmd = new CreateRestaurantBranchCommand(
            _restaurantId,
            "BR-02",
            "فرع السالمية",
            "Salmiya Branch",
            "شارع سالم المبارك",
            "Salem Al Mubarak St",
            "حولي",
            "Hawally",
            "96522222222");

        var result = await _createBranchHandler.Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("branch.review_required");
    }

    [Fact]
    public async Task DirectBranchToggle_ForApprovedRestaurant_ByOwner_IsRejectedWithReviewRequired()
    {
        await SeedApprovedRestaurantAsync();
        var branch = await _db.RestaurantBranches.FirstAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.Roles.Returns(new[] { "Owner" });
        _currentUser.IsInRole("Owner").Returns(true);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        var cmd = new ToggleBranchStatusCommand(branch.Id);
        var result = await _toggleBranchHandler.Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("branch.review_required");
    }

    [Fact]
    public async Task DecideChange_Approve_CreateBranch_CreatesActiveBranchAndIncrementsVersion()
    {
        await SeedApprovedRestaurantAsync();

        // 1. Submit branch proposal
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var payload = new RestaurantBranchProposalPayload(
            BranchAction.Create,
            null,
            "BR-SALMIYA",
            "فرع السالمية",
            "Salmiya Branch",
            "شارع سالم المبارك",
            "Salem Al Mubarak St",
            "حولي",
            "Hawally",
            "96522222222",
            IsPrimary: false);

        var submitCmd = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Branch,
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["NameAr"] = "فرع السالمية" }));

        var submitResult = await _changeReviewService.SubmitChangeAsync(submitCmd, CancellationToken.None);
        submitResult.IsSuccess.Should().BeTrue();

        // 2. Admin approves
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var decideCmd = new DecideRestaurantChangeCommand(
            submitResult.Value.Id,
            "Approve",
            "Looks good",
            1);

        var decideResult = await _changeReviewService.DecideChangeAsync(decideCmd, CancellationToken.None);
        decideResult.IsSuccess.Should().BeTrue();

        // Verify branch added to DB and version incremented
        var newBranch = await _db.RestaurantBranches.FirstOrDefaultAsync(b => b.BranchCode == "BR-SALMIYA");
        newBranch.Should().NotBeNull();
        newBranch!.NameAr.Should().Be("فرع السالمية");
        newBranch.IsActive.Should().BeTrue();

        var profile = await _db.RestaurantProfiles.FindAsync(_restaurantId);
        profile!.ApprovedVersion.Should().Be(2);
    }

    [Fact]
    public async Task DecideChange_Approve_DeactivateLastUsableBranch_ReturnsConflict()
    {
        await SeedApprovedRestaurantAsync();
        var existingBranch = await _db.RestaurantBranches.FirstAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var payload = new RestaurantBranchProposalPayload(
            BranchAction.Deactivate,
            existingBranch.Id,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            IsPrimary: false);

        var submitCmd = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Branch,
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["IsActive"] = "false" }));

        var submitResult = await _changeReviewService.SubmitChangeAsync(submitCmd, CancellationToken.None);
        submitResult.IsSuccess.Should().BeTrue();

        // Admin decides approve
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var decideCmd = new DecideRestaurantChangeCommand(
            submitResult.Value.Id,
            "Approve",
            "Approved",
            1);

        var decideResult = await _changeReviewService.DecideChangeAsync(decideCmd, CancellationToken.None);
        decideResult.IsFailure.Should().BeTrue();
        decideResult.Error.Code.Should().Be("Branch.CannotDeactivateLastBranch");
    }

    [Fact]
    public async Task DecideChange_Approve_SetPrimaryToForeignBranch_ReturnsConflict()
    {
        await SeedApprovedRestaurantAsync();

        // Create foreign branch belonging to another restaurant
        var foreignBranch = new RestaurantBranch
        {
            RestaurantRegistrationId = Guid.NewGuid(),
            BranchCode = "FOREIGN-01",
            NameAr = "فرع أجنبي",
            NameEn = "Foreign Branch",
            AddressAr = "العاصمة",
            AddressEn = "Capital",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            ContactPhone = "96599999999",
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(foreignBranch, Guid.NewGuid());
        _db.RestaurantBranches.Add(foreignBranch);
        await _db.SaveChangesAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var payload = new RestaurantBranchProposalPayload(
            BranchAction.SetPrimary,
            foreignBranch.Id,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            IsPrimary: true);

        var submitCmd = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Branch,
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["IsPrimary"] = "true" }));

        var submitResult = await _changeReviewService.SubmitChangeAsync(submitCmd, CancellationToken.None);
        submitResult.IsSuccess.Should().BeTrue();

        // Admin approves
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var decideCmd = new DecideRestaurantChangeCommand(
            submitResult.Value.Id,
            "Approve",
            "Approved",
            1);

        var decideResult = await _changeReviewService.DecideChangeAsync(decideCmd, CancellationToken.None);
        decideResult.IsFailure.Should().BeTrue();
        decideResult.Error.Code.Should().Be("Branch.ForeignBranchConflict");
    }
}
