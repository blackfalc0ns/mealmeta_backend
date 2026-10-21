using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using MealMate.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace MealMate.IntegrationTests.DriverDelivery;

public sealed class DriverSupportHandoverPostgresTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options;
        _db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task HandoverTransfersOnlyReceivedBox_AndPersistsMissingFollowupAndAudit()
    {
        var restaurantUserId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var originalUserId = Guid.NewGuid();
        var replacementUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            UserId = restaurantUserId, LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        var original = new DriverProfile { UserId = originalUserId, RestaurantId = restaurant.Id, FullNameAr = "السائق", FullNameEn = "Original", IsAvailable = true, VerificationStatus = VerificationStatus.Approved };
        var replacement = new DriverProfile { UserId = replacementUserId, RestaurantId = restaurant.Id, FullNameAr = "البديل", FullNameEn = "Replacement", IsAvailable = true, VerificationStatus = VerificationStatus.Approved };
        var trip = new Trip { RestaurantId = restaurant.Id, Code = "PG-HO-1", Status = TripStatus.InProgress, PlannedDate = new DateOnly(2026, 10, 20) };
        var receivedStop = new TripStop { TripId = trip.Id, OrderCode = "BOX-RECEIVED", CustomerMaskedId = "C1", ZoneNameAr = "حولي", ZoneNameEn = "Hawally", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", Status = TripStopStatus.PickedUp };
        var missingStop = new TripStop { TripId = trip.Id, OrderCode = "BOX-MISSING", CustomerMaskedId = "C2", ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", Status = TripStopStatus.PickedUp };
        var barcode = new MealMate.Domain.RestaurantOperations.MealBarcode { SubscriptionDayId = Guid.NewGuid(), BarcodeValue = "PG-BARCODE-1", Status = MealBarcodeStatus.Printed };
        receivedStop.MealBarcodeId = barcode.Id;
        var issue = new Complaint { CustomerUserId = customerUserId, RestaurantId = restaurant.Id, OrderId = receivedStop.Id, DriverProfileId = original.Id, Category = "VehicleBreakdown", Title = "Vehicle issue", Description = "Vehicle stopped", Status = ComplaintStatus.Submitted, Latitude = 29.3m, Longitude = 47.9m, SubmittedAtUtc = DateTime.UtcNow };
        _db.Users.AddRange(
            new ApplicationUser { Id = restaurantUserId, UserType = UserType.Restaurant, AccountStatus = AccountStatus.Active },
            new ApplicationUser { Id = managerUserId, UserType = UserType.DeliveryManager, AccountStatus = AccountStatus.Active, RestaurantId = restaurant.Id },
            new ApplicationUser { Id = originalUserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
            new ApplicationUser { Id = replacementUserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
            new ApplicationUser { Id = customerUserId, UserType = UserType.Customer, AccountStatus = AccountStatus.Active });
        _db.AddRange(restaurant, original, replacement, trip, receivedStop, missingStop, barcode, issue,
            new DriverAssignment { TripId = trip.Id, DriverProfileId = original.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow },
            new DriverWorkSession { DriverProfileId = replacement.Id, RestaurantId = restaurant.Id, StartedAtUtc = DateTime.UtcNow.AddHours(-1), Status = "Active" });
        await _db.SaveChangesAsync();

        var caller = Substitute.For<ICurrentUser>();
        caller.IsAuthenticated.Returns(true); caller.UserId.Returns(managerUserId); caller.UserType.Returns(UserType.DeliveryManager); caller.RestaurantId.Returns(restaurant.Id);
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(DateTime.UtcNow);
        issue.Category = "DriverReassignmentRequested";
        issue.ReassignmentTripStopId = receivedStop.Id;
        issue.ReassignmentReason = "VehicleBreakdown";
        await _db.SaveChangesAsync();
        // A lost send response can be recovered before a handover exists.
        caller.UserId.Returns(originalUserId); caller.UserType.Returns(UserType.Driver); caller.RestaurantId.Returns((Guid?)null);
        var recovery = new DriverReassignmentHandlers(_db, caller, NullLocalizedMessageProvider.Instance, clock,
            new PostgresApplicationTransactionExecutor(_db), new PostgresApplicationLockManager(_db));
        var pending = await recovery.Handle(new GetDriverReassignmentRequestsQuery(receivedStop.Id), default);
        pending.Value.Should().ContainSingle(); pending.Value[0].Status.Should().Be("ReassignmentRequested");
        pending.Value[0].HandoverId.Should().BeNull();
        caller.UserId.Returns(managerUserId); caller.UserType.Returns(UserType.DeliveryManager); caller.RestaurantId.Returns(restaurant.Id);
        async Task<Result<DriverReassignmentRequestDto>> AcceptFromFreshContextAsync(bool fail = false)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString());
            if (fail) options.AddInterceptors(new ThrowOnceOnSaveInterceptor());
            await using var context = new AppDbContext(options.Options, Substitute.For<IDomainEventDispatcher>());
            var decisionHandler = new DriverReassignmentHandlers(context, caller, NullLocalizedMessageProvider.Instance, clock,
                new PostgresApplicationTransactionExecutor(context), new PostgresApplicationLockManager(context));
            return await decisionHandler.Handle(new DecideDriverReassignmentCommand(issue.Id, "Accepted", "Reviewed by dispatch"), default);
        }
        Func<Task> failDecision = async () => await AcceptFromFreshContextAsync(true);
        await failDecision.Should().ThrowAsync<InvalidOperationException>();
        await using (var rolledBack = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>()))
        {
            (await rolledBack.Complaints.SingleAsync(x => x.Id == issue.Id)).ReassignmentDecision.Should().BeNull();
            (await rolledBack.AuditLogs.CountAsync(x => x.Action == "DriverReassignmentAccepted")).Should().Be(0);
        }
        var decisions = await Task.WhenAll(AcceptFromFreshContextAsync(), AcceptFromFreshContextAsync());
        decisions.Should().OnlyContain(x => x.IsSuccess);
        await using (var acceptedRead = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>()))
        {
            (await acceptedRead.AuditLogs.CountAsync(x => x.Action == "DriverReassignmentAccepted")).Should().Be(1);
            (await acceptedRead.DriverSupportHandovers.CountAsync()).Should().Be(0);
            (await acceptedRead.DriverAssignments.SingleAsync(x => x.TripId == trip.Id)).DriverProfileId.Should().Be(original.Id);
        }
        _db.ChangeTracker.Clear();
        async Task<Result<DriverSupportHandoverDto>> CreateFromFreshContextAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options;
            await using var context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
            var localClock = Substitute.For<IDateTimeProvider>(); localClock.UtcNow.Returns(DateTime.UtcNow);
            var localHandler = new DriverSupportHandoverHandlers(context, caller, NullLocalizedMessageProvider.Instance, localClock,
                new PostgresApplicationTransactionExecutor(context), new PostgresApplicationLockManager(context));
            return await localHandler.Handle(new CreateDriverSupportHandoverCommand(issue.Id, replacement.Id, "pg-test-1"), default);
        }
        var competingCreates = await Task.WhenAll(CreateFromFreshContextAsync(), CreateFromFreshContextAsync());
        competingCreates.Should().OnlyContain(x => x.IsSuccess);
        competingCreates.Select(x => x.Value.HandoverId).Distinct().Should().ContainSingle();
        var created = competingCreates[0];
        var handler = new DriverSupportHandoverHandlers(_db, caller, NullLocalizedMessageProvider.Instance, clock,
            new PostgresApplicationTransactionExecutor(_db), new PostgresApplicationLockManager(_db));
        var handoverId = created.Value.HandoverId;
        caller.UserId.Returns(replacementUserId); caller.UserType.Returns(UserType.Driver); caller.RestaurantId.Returns((Guid?)null);
        var hold = await handler.Handle(new ArriveDriverSupportHandoverCommand(handoverId, 29.3m, 47.9m), default);
        hold.IsSuccess.Should().BeTrue();
        (await handler.Handle(new ScanDriverSupportHandoverBoxCommand(handoverId, "PG-BARCODE-1", "Received"), default)).IsSuccess.Should().BeTrue();
        (await handler.Handle(new ScanDriverSupportHandoverBoxCommand(handoverId, "PG-BARCODE-1", "Received"), default)).IsSuccess.Should().BeTrue("repeating the same persisted receipt must be idempotent");
        (await handler.Handle(new ScanDriverSupportHandoverBoxCommand(handoverId, "BOX-MISSING", "Missing", "Box not present", "ManualCode"), default)).IsSuccess.Should().BeTrue();

        await using (var before = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>()))
        {
            // This manager was created as a restaurant account without a registration request.
            var notice = await before.DeliveryManagerNotifications.SingleAsync(x => x.SubjectId == handoverId);
            notice.UserId.Should().Be(managerUserId);
            (await before.OutboxMessages.CountAsync(x => x.EventType == DeliveryManagerSupportPushOutbox.EventType
                && x.AggregateId == notice.Id.ToString("D"))).Should().Be(1);
            (await before.DriverAssignments.CountAsync(x => x.TripId == trip.Id && x.Status == "Active")).Should().Be(1);
            (await before.TripStops.SingleAsync(x => x.Id == receivedStop.Id)).TripId.Should().Be(trip.Id);
        }
        var failureOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString())
            .AddInterceptors(new ThrowOnceOnSaveInterceptor()).Options;
        await using (var failureContext = new AppDbContext(failureOptions, Substitute.For<IDomainEventDispatcher>()))
        {
            var failureHandler = new DriverSupportHandoverHandlers(failureContext, caller, NullLocalizedMessageProvider.Instance,
                clock, new PostgresApplicationTransactionExecutor(failureContext), new PostgresApplicationLockManager(failureContext));
            Func<Task> failCompletion = async () => await failureHandler.Handle(new CompleteDriverSupportHandoverCommand(handoverId), default);
            await failCompletion.Should().ThrowAsync<InvalidOperationException>();
        }
        await using (var afterFailure = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>()))
        {
            (await afterFailure.DriverSupportHandovers.SingleAsync(x => x.Id == handoverId)).Status.Should().Be("Receiving");
            (await afterFailure.DriverAssignments.CountAsync(x => x.TripId == trip.Id && x.Status == "Active")).Should().Be(1);
            (await afterFailure.TripStops.SingleAsync(x => x.Id == receivedStop.Id)).TripId.Should().Be(trip.Id);
            (await afterFailure.Trips.CountAsync(x => x.Code.StartsWith("HO-") && x.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-5))).Should().Be(0);
        }
        async Task<Result<DriverSupportHandoverDto>> CompleteFromFreshContextAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options;
            await using var context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
            var localClock = Substitute.For<IDateTimeProvider>(); localClock.UtcNow.Returns(DateTime.UtcNow);
            var localHandler = new DriverSupportHandoverHandlers(context, caller, NullLocalizedMessageProvider.Instance, localClock,
                new PostgresApplicationTransactionExecutor(context), new PostgresApplicationLockManager(context));
            return await localHandler.Handle(new CompleteDriverSupportHandoverCommand(handoverId), default);
        }
        var simultaneousCompletions = await Task.WhenAll(CompleteFromFreshContextAsync(), CompleteFromFreshContextAsync());
        simultaneousCompletions.Should().OnlyContain(x => x.IsSuccess);
        simultaneousCompletions.Select(x => x.Value.ReplacementTripId).Distinct().Should().ContainSingle();
        var complete = simultaneousCompletions[0];
        complete.IsSuccess.Should().BeTrue(complete.Error.ToString());
        complete.Value.ReplacementTripId.Should().NotBeNull();
        var repeatedComplete = await CompleteFromFreshContextAsync();
        repeatedComplete.IsSuccess.Should().BeTrue("completion retry after a lost response must return the persisted result");
        repeatedComplete.Value.ReplacementTripId.Should().Be(complete.Value.ReplacementTripId);
        await _db.SaveChangesAsync();
        await using var fresh = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());
        (await fresh.TripStops.SingleAsync(x => x.Id == receivedStop.Id)).TripId.Should().Be(complete.Value.ReplacementTripId!.Value);
        (await fresh.TripStops.SingleAsync(x => x.Id == missingStop.Id)).TripId.Should().Be(trip.Id);
        (await fresh.DeliveryHolds.CountAsync(x => x.DriverSupportHandoverBoxId != null && x.Status == "Active")).Should().Be(1);
        (await fresh.DriverAssignments.CountAsync(x => x.TripId == complete.Value.ReplacementTripId && x.Status == "Active" && x.DriverProfileId == replacement.Id)).Should().Be(1);
        (await fresh.DriverSupportHandovers.SingleAsync(x => x.Id == handoverId)).Status.Should().Be("Completed");
        (await fresh.Trips.CountAsync(x => x.Code.StartsWith("HO-") && x.Notes == $"Support handover {handoverId:N}")).Should().Be(1);

        caller.UserId.Returns(managerUserId); caller.UserType.Returns(UserType.DeliveryManager); caller.RestaurantId.Returns(restaurant.Id);
        async Task<Result<DriverSupportHandoverDto>> ResolveFollowUpFromFreshContextAsync(string code)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options;
            await using var context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
            var localClock = Substitute.For<IDateTimeProvider>(); localClock.UtcNow.Returns(DateTime.UtcNow);
            var localHandler = new DriverSupportHandoverHandlers(context, caller, NullLocalizedMessageProvider.Instance, localClock,
                new PostgresApplicationTransactionExecutor(context), new PostgresApplicationLockManager(context));
            return await localHandler.Handle(new ResolveDriverSupportHandoverFollowUpCommand(
                handoverId, missingStop.Id, code, "Kitchen confirmed physical return"), default);
        }
        var wrongCode = await ResolveFollowUpFromFreshContextAsync("NOT-THE-MANIFEST");
        wrongCode.IsSuccess.Should().BeFalse("follow-up resolution must validate persisted box identity");
        (await ResolveFollowUpFromFreshContextAsync("BOX-MISSING")).IsSuccess.Should().BeTrue();
        (await ResolveFollowUpFromFreshContextAsync("BOX-MISSING")).IsSuccess.Should().BeTrue("retrying identical evidence must be safe");
        await _db.SaveChangesAsync();
        await using var afterFollowUp = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());
        var resolvedStop = await afterFollowUp.TripStops.SingleAsync(x => x.Id == missingStop.Id);
        resolvedStop.TripId.Should().Be(trip.Id);
        resolvedStop.Status.Should().Be(TripStopStatus.ReturnedToKitchen);
        var resolvedHold = await afterFollowUp.DeliveryHolds.SingleAsync(x => x.DriverSupportHandoverBoxId != null);
        resolvedHold.Status.Should().Be("Resolved");
        resolvedHold.ResolvedAtUtc.Should().NotBeNull();
        (await afterFollowUp.AuditLogs.CountAsync(x => x.Action == "SupportHandoverFollowUpReturnedToKitchen" && x.EntityId == resolvedHold.Id)).Should().Be(1);
        (await afterFollowUp.DriverSupportHandoverBoxes.SingleAsync(x => x.TripStopId == missingStop.Id)).Status.Should().Be("Missing", "returning the physical box does not rewrite the driver's original receipt evidence");
    }

    [Fact]
    public async Task SupportPushAndInboxCommitOrRollbackTogether()
    {
        var notification = new DeliveryManagerNotification
        {
            UserId = Guid.NewGuid(), RestaurantId = Guid.NewGuid(), Type = "driver-support-issue",
            TitleAr = "بلاغ", TitleEn = "Issue", BodyAr = "مشكلة الطريق", BodyEn = "Road issue",
            Section = "support", SentAtUtc = DateTime.UtcNow
        };
        var transaction = new PostgresApplicationTransactionExecutor(_db);
        Func<Task> failAfterSave = async () => await transaction.ExecuteResultAsync<bool>(async ct =>
        {
            DeliveryManagerSupportPushOutbox.Add(_db, notification);
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Injected failure after saving inbox and push.");
        }, default);
        await failAfterSave.Should().ThrowAsync<InvalidOperationException>();
        await using var fresh = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_database.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());
        (await fresh.DeliveryManagerNotifications.CountAsync()).Should().Be(0);
        (await fresh.OutboxMessages.CountAsync(x => x.EventType == DeliveryManagerSupportPushOutbox.EventType)).Should().Be(0);

        var successful = await new PostgresApplicationTransactionExecutor(fresh).ExecuteResultAsync<bool>(async ct =>
        {
            DeliveryManagerSupportPushOutbox.Add(fresh, notification);
            await fresh.SaveChangesAsync(ct);
            return Result.Success(true);
        }, default);
        successful.IsSuccess.Should().BeTrue();
        (await fresh.DeliveryManagerNotifications.CountAsync()).Should().Be(1);
        var push = await fresh.OutboxMessages.SingleAsync(x => x.EventType == DeliveryManagerSupportPushOutbox.EventType);
        push.AggregateId.Should().Be(notification.Id.ToString("D"));
        push.ProcessedAtUtc.Should().BeNull();
    }

    private sealed class ThrowOnceOnSaveInterceptor : SaveChangesInterceptor
    {
        private int _thrown;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _thrown, 1) == 0)
                throw new InvalidOperationException("Injected handover completion save failure.");
            return ValueTask.FromResult(result);
        }
    }
}
