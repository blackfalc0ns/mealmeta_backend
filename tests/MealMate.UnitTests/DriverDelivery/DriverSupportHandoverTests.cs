using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.DriverDelivery.Trips;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DriverSupportHandoverTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    private DriverSupportHandoverHandlers Handler()
    {
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(Now);
        return new(_db, _user, NullLocalizedMessageProvider.Instance, clock,
            new PostgresApplicationTransactionExecutor(_db), new PostgresApplicationLockManager(_db));
    }

    private async Task<(DriverProfile Original, DriverProfile Replacement, Trip Trip, TripStop Stop, Complaint Issue)> Seed()
    {
        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRIP-1", Status = TripStatus.InProgress, PlannedDate = new(2026, 10, 20) };
        var original = new DriverProfile { UserId = Guid.NewGuid(), RestaurantId = trip.RestaurantId, FullNameAr = "السائق", FullNameEn = "Original", IsAvailable = true };
        var replacement = new DriverProfile { UserId = Guid.NewGuid(), RestaurantId = trip.RestaurantId, FullNameAr = "البديل", FullNameEn = "Replacement", IsAvailable = true, VerificationStatus = VerificationStatus.Approved };
        var managerUserId = Guid.NewGuid();
        var customer = new ApplicationUser { Id = Guid.NewGuid(), UserType = UserType.Customer, AccountStatus = AccountStatus.Active };
        var program = new Program { Code = $"HANDOVER-{Guid.NewGuid():N}", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = $"HANDOVER-{Guid.NewGuid():N}", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var subscription = new Subscription
        {
            CustomerUserId = customer.Id, ProgramId = program.Id, BundleId = bundle.Id, SelectedTier = RestaurantTier.Basic,
            DurationDays = 1, IncludeFriday = true, Status = SubscriptionStatus.Active,
            StartDate = DateOnly.FromDateTime(Now), EndDate = DateOnly.FromDateTime(Now), PaidAmount = new Money(1m, "KWD")
        };
        var subscriptionDay = new SubscriptionDay { SubscriptionId = subscription.Id, RestaurantId = trip.RestaurantId, ServiceDate = subscription.StartDate };
        var barcode = new MealMate.Domain.RestaurantOperations.MealBarcode { SubscriptionDayId = subscriptionDay.Id, BarcodeValue = "BARCODE-1", Status = MealBarcodeStatus.Printed };
        var stop = new TripStop { TripId = trip.Id, SubscriptionDayId = subscriptionDay.Id, MealBarcodeId = barcode.Id, OrderCode = "BX-1", CustomerMaskedId = "C1", ZoneNameAr = "منطقة", ZoneNameEn = "Area", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", Status = TripStopStatus.Failed, FailureReason = "VehicleBreakdown" };
        var issue = new Complaint { OrderId = stop.Id, RestaurantId = trip.RestaurantId, DriverProfileId = original.Id, Category = "DriverReassignmentRequested", Title = "VehicleBreakdown", Description = "Breakdown", Latitude = 29.3m, Longitude = 47.9m, SubmittedAtUtc = Now };
        _db.AddRange(original, replacement, trip, stop, barcode, issue, customer, program, bundle, subscription, subscriptionDay,
            new ApplicationUser { Id = replacement.UserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
            new ApplicationUser { Id = original.UserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
            new ApplicationUser { Id = managerUserId, UserType = UserType.DeliveryManager, AccountStatus = AccountStatus.Active, RestaurantId = trip.RestaurantId },
            new DeliveryManagerRegistration { RestaurantId = trip.RestaurantId, FullName = "Dispatcher", Phone = "555", Email = "dispatch@example.test", NationalId = "test", Status = VerificationStatus.Approved, ResultingUserId = managerUserId },
            new DriverWorkSession { DriverProfileId = replacement.Id, RestaurantId = trip.RestaurantId, StartedAtUtc = Now.AddHours(-1), Status = "Active" },
            new DriverAssignment { TripId = trip.Id, DriverProfileId = original.Id, Status = "Active", AssignedAtUtc = Now.AddHours(-1) },
            new MealMate.Domain.RestaurantOperations.OrderHandover { DailyOrderId = Guid.NewGuid(), TripId = trip.Id, TripStopId = stop.Id, MealBarcodeId = barcode.Id, DriverProfileId = original.Id, HandedOverAtUtc = Now.AddHours(-1), Status = "Completed" });
        await _db.SaveChangesAsync();
        _user.IsAuthenticated.Returns(true); _user.UserId.Returns(Guid.NewGuid()); _user.UserType.Returns(UserType.DeliveryManager); _user.RestaurantId.Returns(trip.RestaurantId);
        return (original, replacement, trip, stop, issue);
    }

    [Fact]
    public async Task ReassignmentRecovery_ExistsBeforeHandover_AndDecisionPreservesCustody()
    {
        var s = await Seed();
        s.Stop.Status = TripStopStatus.PickedUp; await _db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(Now);
        var handler = new DriverReassignmentHandlers(_db, _user, NullLocalizedMessageProvider.Instance, clock,
            new PostgresApplicationTransactionExecutor(_db), new PostgresApplicationLockManager(_db));
        _user.UserId.Returns(s.Original.UserId); _user.UserType.Returns(UserType.Driver);
        var recovered = await handler.Handle(new GetDriverReassignmentRequestsQuery(s.Stop.Id), default);
        recovered.IsSuccess.Should().BeTrue(); recovered.Value.Should().ContainSingle();
        recovered.Value[0].Status.Should().Be("ReassignmentRequested");
        recovered.Value[0].HandoverId.Should().BeNull();
        _user.UserId.Returns(Guid.NewGuid()); _user.UserType.Returns(UserType.DeliveryManager);
        var resolver = new ResolveSupportIssueCommandHandler(_db, NullLocalizedMessageProvider.Instance,
            Substitute.For<IAuditLogger>(), _user, new PostgresApplicationTransactionExecutor(_db), new PostgresApplicationLockManager(_db));
        (await resolver.Handle(new ResolveSupportIssueCommand(s.Issue.Id, "Pending decision"), default))
            .Error.Code.Should().Be("REASSIGNMENT_DECISION_REQUIRED");
        var accepted = await handler.Handle(new DecideDriverReassignmentCommand(s.Issue.Id, "Accepted", "Reviewed"), default);
        accepted.IsSuccess.Should().BeTrue(); accepted.Value.Status.Should().Be("Accepted");
        (await _db.DriverSupportHandovers.CountAsync()).Should().Be(0);
        (await _db.DriverAssignments.SingleAsync()).DriverProfileId.Should().Be(s.Original.Id);
        (await handler.Handle(new DecideDriverReassignmentCommand(s.Issue.Id, "Rejected", "Different decision"), default)).IsFailure.Should().BeTrue();
        (await handler.Handle(new DecideDriverReassignmentCommand(s.Issue.Id, "Accepted", "Reviewed"), default)).IsSuccess.Should().BeTrue();
        (await _db.AuditLogs.CountAsync(x => x.Action == "DriverReassignmentAccepted")).Should().Be(1);
        (await resolver.Handle(new ResolveSupportIssueCommand(s.Issue.Id, "Issue handled without replacement"), default)).IsSuccess.Should().BeTrue();
        var closed = await handler.Handle(new GetDriverReassignmentRequestQuery(s.Issue.Id), default);
        closed.Value.Status.Should().Be("Closed"); closed.Value.Decision.Should().Be("Accepted");
    }

    [Fact]
    public async Task RejectedReassignment_CannotCreateHandover_AndOtherDriverCannotRead()
    {
        var s = await Seed();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(Now);
        var handler = new DriverReassignmentHandlers(_db, _user, NullLocalizedMessageProvider.Instance, clock,
            new PostgresApplicationTransactionExecutor(_db), new PostgresApplicationLockManager(_db));
        (await handler.Handle(new DecideDriverReassignmentCommand(s.Issue.Id, "Rejected", "No replacement needed"), default)).IsSuccess.Should().BeTrue();
        (await Handler().Handle(new CreateDriverSupportHandoverCommand(s.Issue.Id, s.Replacement.Id, "rejected"), default)).IsFailure.Should().BeTrue();
        _user.UserType.Returns(UserType.Driver); _user.UserId.Returns(s.Replacement.UserId);
        (await handler.Handle(new GetDriverReassignmentRequestQuery(s.Issue.Id), default)).IsFailure.Should().BeTrue();
        _user.UserId.Returns(s.Original.UserId);
        var read = await handler.Handle(new GetDriverReassignmentRequestQuery(s.Issue.Id), default);
        read.Value.Status.Should().Be("Rejected"); read.Value.DecisionNotes.Should().Be("No replacement needed");
    }

    [Fact]
    public async Task ReassignmentDecision_EnforcesScopeReasonAndTerminalCustody()
    {
        var s = await Seed();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(Now);
        var handler = new DriverReassignmentHandlers(_db, _user, NullLocalizedMessageProvider.Instance, clock,
            new PostgresApplicationTransactionExecutor(_db), new PostgresApplicationLockManager(_db));
        _user.RestaurantId.Returns(Guid.NewGuid());
        (await handler.Handle(new DecideDriverReassignmentCommand(s.Issue.Id, "Rejected", "Not in scope"), default)).Error.Code.Should().Be("REASSIGNMENT_FORBIDDEN");
        _user.RestaurantId.Returns(s.Trip.RestaurantId);
        (await handler.Handle(new DecideDriverReassignmentCommand(s.Issue.Id, "Rejected"), default)).Error.Code.Should().Be("REASSIGNMENT_DECISION_INVALID");
        s.Stop.Status = TripStopStatus.Delivered; await _db.SaveChangesAsync();
        (await handler.Handle(new DecideDriverReassignmentCommand(s.Issue.Id, "Accepted"), default)).Error.Code.Should().Be("REASSIGNMENT_CUSTODY_CHANGED");
        s.Issue.ReassignmentDecision.Should().BeNull();
        (await _db.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Assignment_PreservesCustody_AndReplayReturnsSameHandover()
    {
        var s = await Seed(); var handler = Handler();
        var request = new CreateDriverSupportHandoverCommand(s.Issue.Id, s.Replacement.Id, "attempt-1");
        var first = await handler.Handle(request, default); var replay = await handler.Handle(request, default);
        first.IsSuccess.Should().BeTrue(); replay.Value.HandoverId.Should().Be(first.Value.HandoverId);
        first.Value.Status.Should().Be("Assigned"); first.Value.PickupLatitude.Should().Be(29.3m);
        (await _db.DriverAssignments.SingleAsync()).DriverProfileId.Should().Be(s.Original.Id);
        s.Stop.Status.Should().Be(TripStopStatus.Failed);
        (await _db.DriverSupportHandovers.CountAsync()).Should().Be(1);
        (await _db.DeliveryManagerNotifications.CountAsync()).Should().Be(0, "an assignment without unresolved box evidence must not claim dispatcher follow-up is needed");
    }

    [Fact]
    public async Task DriverRecoveryList_RejectsInactivePersistedAccount()
    {
        var s = await Seed();
        var account = await _db.Users.SingleAsync(x => x.Id == s.Original.UserId);
        account.AccountStatus = AccountStatus.Suspended;
        await _db.SaveChangesAsync();
        _user.UserId.Returns(s.Original.UserId); _user.UserType.Returns(UserType.Driver);

        var result = await Handler().Handle(new GetMyDriverSupportHandoversQuery(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DRIVER_ACCOUNT_INACTIVE");
    }

    [Fact]
    public async Task CreateRestaurantTrip_CannotDuplicateBoxWithActiveSupportHold()
    {
        var restaurantId = Guid.NewGuid();
        var restaurantUserId = Guid.NewGuid();
        var sourceDriver = new DriverProfile { UserId = Guid.NewGuid(), RestaurantId = restaurantId, FullNameAr = "السائق", FullNameEn = "Source" };
        var targetDriver = new DriverProfile { UserId = Guid.NewGuid(), RestaurantId = restaurantId, FullNameAr = "سائق آخر", FullNameEn = "Target" };
        var trip = new Trip { RestaurantId = restaurantId, Code = "ORIGINAL", Status = TripStatus.InProgress, PlannedDate = new DateOnly(2026, 10, 20) };
        var day = new SubscriptionDay { SubscriptionId = Guid.NewGuid(), RestaurantId = restaurantId };
        var dayId = day.Id;
        var stop = new TripStop { TripId = trip.Id, SubscriptionDayId = dayId, OrderCode = "BOX-HOLD", CustomerMaskedId = "C1", ZoneNameAr = "منطقة", ZoneNameEn = "Area", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", Status = TripStopStatus.PickedUp };
        _db.AddRange(sourceDriver, targetDriver, trip, stop, day,
            new DriverAssignment { TripId = trip.Id, DriverProfileId = sourceDriver.Id, Status = "Active" },
            new DeliveryHold { TripId = trip.Id, TripStopId = stop.Id, SubscriptionDayId = dayId, Reason = "support follow-up", HeldAtUtc = Now, Status = "Active" });
        await _db.SaveChangesAsync();
        var caller = Substitute.For<ICurrentUser>();
        caller.IsAuthenticated.Returns(true); caller.UserId.Returns(Guid.NewGuid()); caller.UserType.Returns(UserType.DeliveryManager); caller.RestaurantId.Returns(restaurantId);
        var create = new CreateRestaurantTripCommandHandler(_db, caller, DriverDeliveryTestConcurrency.Transactions(), DriverDeliveryTestConcurrency.Locks());
        var result = await create.Handle(new CreateRestaurantTripCommand(restaurantId, targetDriver.Id,
            new[] { new CreateTripStopInput("BOX-HOLD", "C1", null, null, "منطقة", "Area", "وجبة", "Meal", 1, "AM", SubscriptionDayId: dayId) }), default);
        result.IsFailure.Should().BeTrue(); result.Error.Code.Should().Be("SUPPORT_CUSTODY_BLOCKED");
        (await _db.Trips.CountAsync()).Should().Be(1);
        (await _db.TripStops.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Complete_RequiresArrivalAndBarcode_AndTransfersOnlyOnce()
    {
        var s = await Seed(); var handler = Handler();
        var created = await handler.Handle(new CreateDriverSupportHandoverCommand(s.Issue.Id, s.Replacement.Id, "attempt-2"), default);
        var id = created.Value.HandoverId;
        _user.UserId.Returns(s.Replacement.UserId); _user.UserType.Returns(UserType.Driver);
        (await handler.Handle(new CompleteDriverSupportHandoverCommand(id), default)).IsFailure.Should().BeTrue();
        (await handler.Handle(new ArriveDriverSupportHandoverCommand(id, 29.3m, 47.9m), default)).IsSuccess.Should().BeTrue();
        (await handler.Handle(new ScanDriverSupportHandoverBoxCommand(id, "WRONG"), default)).IsFailure.Should().BeTrue();
        (await handler.Handle(new ScanDriverSupportHandoverBoxCommand(id, "BARCODE-1"), default)).IsSuccess.Should().BeTrue();
        var completed = await handler.Handle(new CompleteDriverSupportHandoverCommand(id), default);
        completed.IsSuccess.Should().BeTrue(); completed.Value.Status.Should().Be("Completed");
        (await handler.Handle(new CompleteDriverSupportHandoverCommand(id), default)).IsSuccess.Should().BeTrue();
        completed.Value.ReplacementTripId.Should().NotBeNull();
        completed.Value.ReplacementTripId!.Value.Should().NotBe(s.Trip.Id);
        (await _db.DriverAssignments.CountAsync(x => x.TripId == s.Trip.Id && x.Status == "Active")).Should().Be(0);
        (await _db.DriverAssignments.SingleAsync(x => x.TripId == completed.Value.ReplacementTripId && x.Status == "Active")).DriverProfileId.Should().Be(s.Replacement.Id);
        s.Stop.TripId.Should().Be(completed.Value.ReplacementTripId!.Value);
        s.Stop.Status.Should().Be(TripStopStatus.PickedUp);
        s.Issue.DriverProfileId.Should().Be(s.Original.Id, "the issue preserves the reporting driver's identity");
    }

    [Fact]
    public async Task DispatcherCanResolveMissingBoxOnlyWithExactManifestCodeAndConfirmedKitchenReturn()
    {
        var s = await Seed(); var handler = Handler();
        var created = await handler.Handle(new CreateDriverSupportHandoverCommand(s.Issue.Id, s.Replacement.Id, "missing-return"), default);
        var id = created.Value.HandoverId;
        _user.UserId.Returns(s.Replacement.UserId); _user.UserType.Returns(UserType.Driver);
        await handler.Handle(new ArriveDriverSupportHandoverCommand(id, 29.3m, 47.9m), default);
        (await handler.Handle(new ScanDriverSupportHandoverBoxCommand(id, "BX-1", "Missing", "Customer confirmed the box was left at kitchen", "ManualCode"), default)).IsSuccess.Should().BeTrue();
        var completed = await handler.Handle(new CompleteDriverSupportHandoverCommand(id), default);
        completed.IsSuccess.Should().BeTrue(); completed.Value.ReceivedCount.Should().Be(0);
        _user.UserId.Returns(Guid.NewGuid()); _user.UserType.Returns(UserType.DeliveryManager); _user.RestaurantId.Returns(s.Trip.RestaurantId);

        var rejected = await handler.Handle(new ResolveDriverSupportHandoverFollowUpCommand(id, s.Stop.Id, "WRONG", "Confirmed return to kitchen"), default);
        rejected.IsFailure.Should().BeTrue(); rejected.Error.Code.Should().Be("FOLLOW_UP_CODE_MISMATCH");
        var resolved = await handler.Handle(new ResolveDriverSupportHandoverFollowUpCommand(id, s.Stop.Id, "BX-1", "Confirmed return to kitchen"), default);

        resolved.IsSuccess.Should().BeTrue();
        s.Stop.Status.Should().Be(TripStopStatus.ReturnedToKitchen);
        var box = await _db.DriverSupportHandoverBoxes.Include(x => x.FollowUp).SingleAsync(x => x.HandoverId == id);
        box.Status.Should().Be("Missing", "receipt evidence remains immutable");
        box.FollowUp!.Status.Should().Be("Resolved");
        box.FollowUp.ResolvedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task UnrelatedDriver_CannotReadOrConfirm()
    {
        var s = await Seed(); var handler = Handler();
        var created = await handler.Handle(new CreateDriverSupportHandoverCommand(s.Issue.Id, s.Replacement.Id, "attempt-3"), default);
        _user.UserId.Returns(Guid.NewGuid()); _user.UserType.Returns(UserType.Driver);
        (await handler.Handle(new GetDriverSupportHandoverQuery(created.Value.HandoverId), default)).IsFailure.Should().BeTrue();
        (await handler.Handle(new CompleteDriverSupportHandoverCommand(created.Value.HandoverId), default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DispatcherFollowUpNotification_IsCreatedOnlyForMissingOrDamagedReceipt()
    {
        var s = await Seed(); var handler = Handler();
        // Restaurant approval can create this real manager account without a registration request.
        _db.DeliveryManagerRegistrations.RemoveRange(_db.DeliveryManagerRegistrations);
        await _db.SaveChangesAsync();
        var created = await handler.Handle(new CreateDriverSupportHandoverCommand(s.Issue.Id, s.Replacement.Id, "attempt-notify"), default);
        _user.UserId.Returns(s.Replacement.UserId); _user.UserType.Returns(UserType.Driver);
        (await handler.Handle(new ArriveDriverSupportHandoverCommand(created.Value.HandoverId, 29.3m, 47.9m), default)).IsSuccess.Should().BeTrue();
        (await _db.DeliveryManagerNotifications.CountAsync()).Should().Be(0);
        var receipt = await handler.Handle(new ScanDriverSupportHandoverBoxCommand(created.Value.HandoverId, "BX-1", "Missing", "Box not present", "ManualCode"), default);
        receipt.IsSuccess.Should().BeTrue();
        (await _db.DeliveryManagerNotifications.CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DelayReport_RequiresActiveAssignmentAndPersistsDispatcherComplaint()
    {
        var s = await Seed();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(s.Replacement.UserId);
        user.UserType.Returns(UserType.Driver);
        var driverCommands = new DriverDeliveryOrderCommandHandlers(_db, user, NullLocalizedMessageProvider.Instance,
            transactions: new PostgresApplicationTransactionExecutor(_db), locks: new PostgresApplicationLockManager(_db));

        var denied = await driverCommands.Handle(new ReportDeliveryDelayCommand(s.Stop.Id, 22, "Road closure"), default);
        denied.IsFailure.Should().BeTrue();
        (await _db.Complaints.CountAsync()).Should().Be(1, "an unrelated driver must not create support data");

        user.UserId.Returns(s.Original.UserId);
        _db.DriverAssignments.Add(new DriverAssignment { TripId = s.Trip.Id, DriverProfileId = s.Original.Id, Status = "Active", AssignedAtUtc = Now });
        await _db.SaveChangesAsync();
        var accepted = await driverCommands.Handle(new ReportDeliveryDelayCommand(s.Stop.Id, 22, "Road closure"), default);
        accepted.IsSuccess.Should().BeTrue();
        var complaint = await _db.Complaints.SingleAsync(c => c.Category == "SevereDelay");
        complaint.Category.Should().Be("SevereDelay");
        complaint.DriverProfileId.Should().Be(s.Original.Id);
        complaint.Description.Should().Contain("22");
        (await _db.Complaints.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task LegacyAssignment_CannotBypassAnActiveHandover()
    {
        var s = await Seed();
        _db.DriverSupportHandovers.Add(new DriverSupportHandover
        {
            ComplaintId = s.Issue.Id, TripId = s.Trip.Id, OriginalDriverProfileId = s.Original.Id,
            ReplacementDriverProfileId = s.Replacement.Id, RestaurantId = s.Trip.RestaurantId,
            RequestedByUserId = Guid.NewGuid(), IdempotencyKey = "existing-active", Status = "Arrived",
            PickupLatitude = 29.3m, PickupLongitude = 47.9m, PickupLocationSource = "Complaint", AssignedAtUtc = Now,
            Boxes = { new DriverSupportHandoverBox { TripStopId = s.Stop.Id, ManifestCode = "BX-1", Status = "Pending" } }
        });
        await _db.SaveChangesAsync();
        var dispatcher = Substitute.For<ICurrentUser>();
        dispatcher.IsAuthenticated.Returns(true); dispatcher.UserId.Returns(Guid.NewGuid());
        dispatcher.UserType.Returns(UserType.Admin);
        var handler = new AssignBoxToDriverCommandHandler(_db, NullLocalizedMessageProvider.Instance,
            currentUser: dispatcher, txExecutor: new PostgresApplicationTransactionExecutor(_db),
            locks: new PostgresApplicationLockManager(_db));

        var result = await handler.Handle(new AssignBoxToDriverCommand(s.Stop.Id, s.Replacement.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("HANDOVER_PENDING");
        (await _db.DriverAssignments.CountAsync(x => x.TripId == s.Trip.Id && x.Status == "Active")).Should().Be(1);
        (await _db.DriverAssignments.SingleAsync(x => x.TripId == s.Trip.Id && x.Status == "Active")).DriverProfileId.Should().Be(s.Original.Id);
    }
}
