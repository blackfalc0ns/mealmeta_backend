using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public sealed class DriverSupportIssuesTests
{
    [Fact]
    public async Task ListUsesCurrentDriversProfileAndSeparatesContactingFromReviewCounts()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var other = new DriverProfile { UserId = Guid.NewGuid(), FullNameEn = "Other", FullNameAr = "آخر" };
        var review = Issue(profile.Id, ComplaintStatus.UnderReview, "review");
        var contact = Issue(profile.Id, ComplaintStatus.UnderReview, "contact");
        var submitted = Issue(profile.Id, ComplaintStatus.Submitted, "submitted");
        var foreign = Issue(other.Id, ComplaintStatus.Submitted, "foreign");
        db.AddRange(profile, other, review, contact, submitted, foreign,
            new AuditLog { EntityId = contact.Id, EntityType = nameof(Complaint), Action = "DriverSupportProgressUpdated", AfterJson = "{\"Stage\":\"ContactingCustomer\"}", OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var handler = new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance);

        var result = await handler.Handle(new GetDriverSupportIssuesQuery(), default);
        result.IsSuccess.Should().BeTrue();
        result.Value.AllCount.Should().Be(3);
        result.Value.UnderReviewCount.Should().Be(1);
        result.Value.AwaitingReplyCount.Should().Be(2);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Issues.Select(x => x.IssueId).Should().NotContain(foreign.Id);
    }

    [Fact]
    public async Task ListSearchAndPaginationKeepFilteredTotalSeparateFromPageSize()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var profile = new DriverProfile { UserId = userId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var first = Issue(profile.Id, ComplaintStatus.Submitted, "First"); first.Description = "unique search phrase";
        var second = Issue(profile.Id, ComplaintStatus.Submitted, "Second"); second.Description = "ordinary text";
        db.AddRange(profile, first, second); await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var handler = new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance);

        var searched = await handler.Handle(new GetDriverSupportIssuesQuery("All", "unique search phrase", 1, 1), default);
        var paged = await handler.Handle(new GetDriverSupportIssuesQuery("All", null, 1, 1), default);
        searched.Value.TotalCount.Should().Be(1); searched.Value.Issues.Should().ContainSingle(x => x.IssueId == first.Id);
        paged.Value.TotalCount.Should().Be(2); paged.Value.Issues.Should().ContainSingle();
    }

    [Fact]
    public async Task OrderCodeSearchUsesUniqueRestaurantScopedStopAndHidesAmbiguousLink()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var restaurantId = Guid.NewGuid(); var dailyOrderId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, RestaurantId = restaurantId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var trip = new Trip { RestaurantId = restaurantId, Code = "TRIP", PlannedDate = new DateOnly(2026, 10, 21) };
        var uniqueStop = Stop(trip.Id, dailyOrderId, "ORDER-ONLY");
        var firstAmbiguous = Stop(trip.Id, Guid.NewGuid(), "AMBIGUOUS-ONE");
        var secondAmbiguous = Stop(trip.Id, Guid.NewGuid(), "AMBIGUOUS-TWO");
        var uniqueIssue = Issue(profile.Id, ComplaintStatus.Submitted, "unique"); uniqueIssue.RestaurantId = restaurantId; uniqueIssue.OrderId = dailyOrderId;
        var ambiguousOrderId = Guid.NewGuid();
        firstAmbiguous.DailyOrderId = ambiguousOrderId; secondAmbiguous.DailyOrderId = ambiguousOrderId;
        var ambiguousIssue = Issue(profile.Id, ComplaintStatus.Submitted, "ambiguous"); ambiguousIssue.RestaurantId = restaurantId; ambiguousIssue.OrderId = ambiguousOrderId;
        db.AddRange(profile, trip, uniqueStop, firstAmbiguous, secondAmbiguous, uniqueIssue, ambiguousIssue); await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var handler = new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance);

        var unique = await handler.Handle(new GetDriverSupportIssuesQuery("All", "ORDER-ONLY"), default);
        var ambiguous = await handler.Handle(new GetDriverSupportIssueQuery(ambiguousIssue.Id), default);
        var hiddenSearch = await handler.Handle(new GetDriverSupportIssuesQuery("All", "AMBIGUOUS-ONE"), default);
        unique.Value.Issues.Should().ContainSingle(x => x.IssueId == uniqueIssue.Id && x.OrderCode == "ORDER-ONLY");
        ambiguous.Value.OrderCode.Should().BeNull(); hiddenSearch.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task DetailPreservesLegacyTimelineWithoutFabricatedProgressDates()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var profile = new DriverProfile { UserId = userId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var submittedAt = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        var issue = Issue(profile.Id, ComplaintStatus.Resolved, "legacy"); issue.SubmittedAtUtc = submittedAt;
        db.AddRange(profile, issue);
        db.AuditLogs.Add(new AuditLog { EntityId = issue.Id, EntityType = nameof(Complaint), Action = "SupportIssueResolved", Reason = "Done", OccurredAtUtc = submittedAt.AddHours(2) });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);

        var result = await new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance)
            .Handle(new GetDriverSupportIssueQuery(issue.Id), default);
        result.IsSuccess.Should().BeTrue();
        result.Value.Timeline.Single(x => x.Stage == "UnderReview").OccurredAtUtc.Should().BeNull();
        result.Value.Timeline.Single(x => x.Stage == "ContactingCustomer").OccurredAtUtc.Should().BeNull();
        result.Value.ResolvedAtUtc.Should().Be(submittedAt.AddHours(2));
        result.Value.CanContactSupport.Should().BeFalse();
        var foreignUserId = Guid.NewGuid(); db.DriverProfiles.Add(new DriverProfile { UserId = foreignUserId, FullNameAr = "Another", FullNameEn = "Another" }); await db.SaveChangesAsync();
        var foreignUser = Substitute.For<ICurrentUser>(); foreignUser.IsAuthenticated.Returns(true); foreignUser.UserId.Returns(foreignUserId); foreignUser.UserType.Returns(UserType.Driver);
        var foreign = await new DriverSupportIssueQueries(db, foreignUser, NullLocalizedMessageProvider.Instance).Handle(new GetDriverSupportIssueQuery(issue.Id), default);
        foreign.Error.Code.Should().Be("DRIVER_SUPPORT_ISSUE_NOT_FOUND");
    }

    [Fact]
    public async Task MissingAuthenticationIsForbidden()
    {
        await using var db = CreateDb();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(false);
        var result = await new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance)
            .Handle(new GetDriverSupportIssuesQuery(), default);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DRIVER_SCOPE_REQUIRED");
    }

    [Fact]
    public async Task EmptyDriverListReturnsZeroCounts()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.DriverProfiles.Add(new DriverProfile { UserId = userId, FullNameEn = "Driver", FullNameAr = "سائق" }); await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var result = await new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance).Handle(new GetDriverSupportIssuesQuery(), default);
        result.Value.AllCount.Should().Be(0); result.Value.UnderReviewCount.Should().Be(0); result.Value.AwaitingReplyCount.Should().Be(0);
        result.Value.ResolvedCount.Should().Be(0); result.Value.TotalCount.Should().Be(0); result.Value.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRejectsInvalidPagingAndExcludesUnsupportedCustomerComplaintCategory()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var customerComplaint = Issue(profile.Id, ComplaintStatus.Submitted, "customer"); customerComplaint.Category = "FoodQuality";
        db.AddRange(profile, customerComplaint); await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var handler = new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance);
        (await handler.Handle(new GetDriverSupportIssuesQuery("nonsense"), default)).Error.Code.Should().Be("DRIVER_SUPPORT_QUERY_INVALID");
        var valid = await handler.Handle(new GetDriverSupportIssuesQuery(), default);
        valid.Value.AllCount.Should().Be(0); valid.Value.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatcherProgressIsOrderedIdempotentAndPersisted()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = Guid.NewGuid(), RestaurantId = restaurantId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var issue = Issue(profile.Id, ComplaintStatus.Submitted, "progress"); issue.RestaurantId = restaurantId;
        var outsideIssue = Issue(profile.Id, ComplaintStatus.Submitted, "outside"); outsideIssue.RestaurantId = Guid.NewGuid();
        db.AddRange(profile, issue, outsideIssue); await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(Guid.NewGuid());
        user.UserType.Returns(UserType.DeliveryManager); user.RestaurantId.Returns(restaurantId);
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        var handler = new UpdateDriverSupportProgressHandler(db, user, NullLocalizedMessageProvider.Instance, clock,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));

        var review = await handler.Handle(new UpdateDriverSupportProgressCommand(issue.Id, "UnderReview"), default);
        var replay = await handler.Handle(new UpdateDriverSupportProgressCommand(issue.Id, "UnderReview"), default);
        var contact = await handler.Handle(new UpdateDriverSupportProgressCommand(issue.Id, "ContactingCustomer"), default);
        var contactReplay = await handler.Handle(new UpdateDriverSupportProgressCommand(issue.Id, "ContactingCustomer"), default);
        var reverse = await handler.Handle(new UpdateDriverSupportProgressCommand(issue.Id, "UnderReview"), default);
        review.IsSuccess.Should().BeTrue(); replay.Value.Idempotent.Should().BeTrue();
        contact.IsSuccess.Should().BeTrue(); contactReplay.Value.Idempotent.Should().BeTrue(); reverse.Error.Code.Should().Be("DRIVER_SUPPORT_PROGRESS_ORDER_INVALID");
        (await db.AuditLogs.CountAsync(x => x.Action == "DriverSupportProgressUpdated")).Should().Be(2);
        (await db.DriverNotifications.CountAsync(x => x.Type == "driver-support-progress")).Should().Be(2);
        (await db.OutboxMessages.CountAsync(x => x.EventType == "driver-support-issue-changed.v1")).Should().Be(2);
        (await db.Complaints.SingleAsync(x => x.Id == issue.Id)).Status.Should().Be(ComplaintStatus.UnderReview);
        user.RestaurantId.Returns(Guid.NewGuid());
        var outOfScope = await handler.Handle(new UpdateDriverSupportProgressCommand(outsideIssue.Id, "UnderReview"), default);
        outOfScope.Error.Code.Should().Be("DISPATCHER_SCOPE_FORBIDDEN");
    }

    [Fact]
    public async Task PhotoBindingChecksOwnershipAndIsIdempotent()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var restaurantId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, RestaurantId = restaurantId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var issue = Issue(profile.Id, ComplaintStatus.Submitted, "photos"); issue.RestaurantId = restaurantId;
        var key = "uploads/drivers/issue-evidence/evidence.webp";
        db.AddRange(profile, issue, new MealMate.Domain.Common.UploadFileRecord { StorageKey = key, UploadedByUserId = userId, ContentType = "image/webp", FileName = "evidence.webp", SizeBytes = 10 });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var storage = Substitute.For<IFileStorage>(); storage.ExistsAsync(key, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new AddDriverSupportIssuePhotosHandler(db, user, NullLocalizedMessageProvider.Instance,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db), storage);

        var first = await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [key, key]), default);
        var retry = await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [key]), default);
        first.IsSuccess.Should().BeTrue(); retry.IsSuccess.Should().BeTrue();
        first.Value.Status.Should().Be("AwaitingReply"); first.Value.EvidenceUrls.Should().ContainSingle(); retry.Value.EvidenceUrls.Should().ContainSingle();
        var persistedPhotos = (await db.Complaints.Include(x => x.EvidencePhotos).SingleAsync(x => x.Id == issue.Id)).EvidencePhotos;
        persistedPhotos.Should().ContainSingle(); persistedPhotos.Single().Id.Should().NotBeEmpty();
        (await db.OutboxMessages.CountAsync(x => x.EventType == "driver-support-issue-changed.v1")).Should().Be(1);
        (await db.AuditLogs.CountAsync(x => x.Action == "DriverSupportPhotosAdded")).Should().Be(1);
    }

    [Fact]
    public async Task PhotoBindingRejectsForeignUploadAndTerminalIssue()
    {
        await using var db = CreateDb();
        var owner = Guid.NewGuid(); var otherUser = Guid.NewGuid(); var restaurantId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = owner, RestaurantId = restaurantId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var foreignProfile = new DriverProfile { UserId = otherUser, RestaurantId = restaurantId, FullNameEn = "Other", FullNameAr = "آخر" };
        var issue = Issue(profile.Id, ComplaintStatus.Submitted, "photos"); issue.RestaurantId = restaurantId;
        var terminal = Issue(profile.Id, ComplaintStatus.Resolved, "closed"); terminal.RestaurantId = restaurantId;
        var foreignIssue = Issue(foreignProfile.Id, ComplaintStatus.Submitted, "foreign"); foreignIssue.RestaurantId = restaurantId;
        var key = "uploads/drivers/issue-evidence/foreign.png";
        var mimeKey = "uploads/drivers/issue-evidence/wrong-type.pdf";
        var publicKey = "uploads/catalog/wrong-folder.png";
        db.AddRange(profile, foreignProfile, issue, terminal, foreignIssue,
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = key, UploadedByUserId = otherUser, ContentType = "image/png", FileName = "foreign.png", SizeBytes = 10 },
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = mimeKey, UploadedByUserId = owner, ContentType = "application/pdf", FileName = "wrong-type.pdf", SizeBytes = 10 },
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = publicKey, UploadedByUserId = owner, ContentType = "image/png", FileName = "wrong-folder.png", SizeBytes = 10 });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(owner); user.UserType.Returns(UserType.Driver);
        var storage = Substitute.For<IFileStorage>(); storage.ExistsAsync(key, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new AddDriverSupportIssuePhotosHandler(db, user, NullLocalizedMessageProvider.Instance,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db), storage);
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [key]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [mimeKey]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [publicKey]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(terminal.Id, [key]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_ISSUE_TERMINAL");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(foreignIssue.Id, [key]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_ISSUE_NOT_FOUND");
    }

    [Fact]
    public async Task PhotoBindingRejectsMissingPhysicalFileAndFourthPhoto()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var restaurantId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, RestaurantId = restaurantId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var issue = Issue(profile.Id, ComplaintStatus.Submitted, "photos"); issue.RestaurantId = restaurantId;
        for (var i = 0; i < 3; i++) issue.EvidencePhotos.Add(new ComplaintEvidencePhoto { StoragePath = $"existing-{i}.png", ContentType = "image/png", UploadedAtUtc = DateTime.UtcNow });
        var missingKey = "uploads/drivers/issue-evidence/missing.png";
        var fourthKey = "uploads/drivers/issue-evidence/fourth.png";
        db.AddRange(profile, issue,
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = missingKey, UploadedByUserId = userId, ContentType = "image/png", FileName = "missing.png", SizeBytes = 10 },
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = fourthKey, UploadedByUserId = userId, ContentType = "image/png", FileName = "fourth.png", SizeBytes = 10 });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var storage = Substitute.For<IFileStorage>(); storage.ExistsAsync(missingKey, Arg.Any<CancellationToken>()).Returns(false); storage.ExistsAsync(fourthKey, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new AddDriverSupportIssuePhotosHandler(db, user, NullLocalizedMessageProvider.Instance,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db), storage);
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [missingKey]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_MISSING");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [fourthKey]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_LIMIT");
    }

    private static Complaint Issue(Guid driverId, ComplaintStatus status, string title) => new()
    {
        DriverProfileId = driverId,
        Category = "VehicleBreakdown",
        Title = title,
        Description = title,
        Status = status,
        SubmittedAtUtc = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc)
    };

    private static TripStop Stop(Guid tripId, Guid dailyOrderId, string orderCode) => new()
    {
        TripId = tripId,
        DailyOrderId = dailyOrderId,
        OrderCode = orderCode,
        CustomerMaskedId = "C1",
        ZoneNameAr = "المنطقة",
        ZoneNameEn = "Area",
        MealSummaryAr = "وجبة",
        MealSummaryEn = "Meal",
        SlotLabel = "AM"
    };

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
}
