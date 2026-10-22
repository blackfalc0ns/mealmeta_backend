using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class RestaurantOperationsReviewTests
{
    private readonly Persistence.AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly RestaurantCapacityService _capacityService;
    private readonly RestaurantChangeReviewService _changeReviewService;
    private readonly ActivateRestaurantEmergencyOverrideCommandHandler _activateEmergencyHandler;
    private readonly ResolveRestaurantEmergencyOverrideCommandHandler _resolveEmergencyHandler;

    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _registrationId = Guid.NewGuid();

    public RestaurantOperationsReviewTests()
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

        _capacityService = new RestaurantCapacityService(_db, _currentUser, _clock);
        _changeReviewService = new RestaurantChangeReviewService(_db, _currentUser, _audit, _clock, _notifier);
        _activateEmergencyHandler = new ActivateRestaurantEmergencyOverrideCommandHandler(_db, _currentUser, _audit, _clock);
        _resolveEmergencyHandler = new ResolveRestaurantEmergencyOverrideCommandHandler(_db, _currentUser, _audit, _clock);
    }

    private async Task SeedApprovedRestaurantAsync()
    {
        var registration = new RestaurantRegistration
        {
            SubmittedByUserId = _ownerUserId,
            ResultingRestaurantId = _restaurantId,
            TradeNameAr = "مطعم العمليات",
            TradeNameEn = "Operations Restaurant",
            LegalNameAr = "شركة العمليات",
            LegalNameEn = "Operations Co",
            Status = VerificationStatus.Approved,
            ApprovedVersion = 1,
            CreatedAtUtc = _clock.UtcNow.AddDays(-10)
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(registration, _registrationId);

        var profile = new RestaurantProfile
        {
            TradeNameAr = "مطعم العمليات",
            TradeNameEn = "Operations Restaurant",
            LegalNameAr = "شركة العمليات",
            LegalNameEn = "Operations Co",
            ApprovedVersion = 1,
            CreatedAtUtc = _clock.UtcNow.AddDays(-10)
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(profile, _restaurantId);

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var baseCap = new DailyCapacity
        {
            RestaurantId = _restaurantId,
            ServiceDate = today,
            CapacityType = CapacityType.Base,
            MaxBoxes = 100,
            ReservedBoxes = 0,
            CreatedAtUtc = _clock.UtcNow.AddDays(-10)
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(baseCap, Guid.NewGuid());

        _db.RestaurantRegistrations.Add(registration);
        _db.RestaurantProfiles.Add(profile);
        _db.DailyCapacities.Add(baseCap);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task DirectBaseCapacityUpdate_ForApprovedRestaurant_ByOwner_IsRejectedWithReviewRequired()
    {
        await SeedApprovedRestaurantAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        var cmd = new UpdateBaseCapacityCommand(120, _restaurantId);
        var result = await _capacityService.UpdateBaseCapacityAsync(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("capacity.review_required");
    }

    [Fact]
    public async Task DirectScheduleUpdate_ForApprovedRestaurant_ByOwner_PersistsImmediately()
    {
        await SeedApprovedRestaurantAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        var scheduleList = new List<WeeklyDayScheduleInputDto>
        {
            new WeeklyDayScheduleInputDto((int)DayOfWeek.Monday, false)
        };
        var cmd = new UpdateWorkingDaysScheduleCommand(scheduleList, _restaurantId);
        var result = await _capacityService.UpdateWorkingDaysScheduleAsync(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WeeklyDays.Should().ContainSingle(x => x.DayOfWeek == (int)DayOfWeek.Monday && !x.IsWorkingDay);
        _db.RestaurantSettingsChangeRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task DecideChange_Approve_CapacityProposal_UpdatesApprovedVersionAndCapacities()
    {
        await SeedApprovedRestaurantAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var payload = new RestaurantCapacityProposalPayload(150, "Upgrading kitchen capacity");
        var submitCmd = new SubmitRestaurantChangeCommand(
            _restaurantId,
            RestaurantChangeCategory.Capacity,
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(new List<string> { "NewBaseCapacity" }));

        var submitResult = await _changeReviewService.SubmitChangeAsync(submitCmd, CancellationToken.None);
        submitResult.IsSuccess.Should().BeTrue();

        // Admin approves
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var decideCmd = new DecideRestaurantChangeCommand(
            submitResult.Value.Id,
            "Approve",
            "Approved expansion",
            1);

        var decideResult = await _changeReviewService.DecideChangeAsync(decideCmd, CancellationToken.None);
        decideResult.IsSuccess.Should().BeTrue();

        var profile = await _db.RestaurantProfiles.FindAsync(_restaurantId);
        profile!.ApprovedVersion.Should().Be(2);

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var cap = await _db.DailyCapacities.FirstOrDefaultAsync(c => c.RestaurantId == _restaurantId && c.ServiceDate >= today);
        cap!.MaxBoxes.Should().Be(150);
    }

    [Fact]
    public async Task EmergencyOverride_CannotIncreaseCapacity()
    {
        await SeedApprovedRestaurantAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var cmd = new ActivateRestaurantEmergencyOverrideCommand(
            _restaurantId,
            today,
            today.AddDays(2),
            ReducedCapacity: 200, // Higher than approved 100!
            Closed: false,
            Reason: "Attempted capacity hike");

        var result = await _activateEmergencyHandler.Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Emergency.CannotIncreaseCapacity");
    }

    [Fact]
    public async Task EmergencyOverride_ActivationAndResolution_SetsAndClearsBusyState()
    {
        await SeedApprovedRestaurantAsync();

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var activateCmd = new ActivateRestaurantEmergencyOverrideCommand(
            _restaurantId,
            today,
            today.AddDays(1),
            ReducedCapacity: 30,
            Closed: true,
            Reason: "Kitchen equipment malfunction");

        var activateResult = await _activateEmergencyHandler.Handle(activateCmd, CancellationToken.None);
        activateResult.IsSuccess.Should().BeTrue();

        // Verify BusyStatus is active immediately
        var busy = await _db.BusyStatuses.FirstOrDefaultAsync(b => b.RestaurantId == _restaurantId && b.IsBusy);
        busy.Should().NotBeNull();
        busy!.Reason.Should().Be("Kitchen equipment malfunction");

        // Now resolve the emergency
        var resolveCmd = new ResolveRestaurantEmergencyOverrideCommand(
            activateResult.Value.Id,
            "Maintenance completed, restored normal operation");

        var resolveResult = await _resolveEmergencyHandler.Handle(resolveCmd, CancellationToken.None);
        resolveResult.IsSuccess.Should().BeTrue();

        // Verify BusyStatus is cleared
        var busyAfter = await _db.BusyStatuses.FirstOrDefaultAsync(b => b.RestaurantId == _restaurantId && b.IsBusy);
        busyAfter.Should().BeNull();

        var overrideRecord = await _db.RestaurantEmergencyOverrides.FindAsync(activateResult.Value.Id);
        overrideRecord!.IsResolved.Should().BeTrue();
        overrideRecord.ResolutionReason.Should().Be("Maintenance completed, restored normal operation");
    }
}
