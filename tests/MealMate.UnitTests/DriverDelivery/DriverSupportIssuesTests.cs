using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Storage;
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
        AddActiveDriverAccount(db, userId);
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
        AddActiveDriverAccount(db, userId);
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
        AddActiveDriverAccount(db, userId);
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
        AddActiveDriverAccount(db, userId);
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
        var foreignUserId = Guid.NewGuid(); AddActiveDriverAccount(db, foreignUserId);
        db.DriverProfiles.Add(new DriverProfile { UserId = foreignUserId, FullNameAr = "Another", FullNameEn = "Another" }); await db.SaveChangesAsync();
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
        AddActiveDriverAccount(db, userId);
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
        AddActiveDriverAccount(db, userId);
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

    [Theory]
    [InlineData("uploads/drivers/issue-evidence/evidence.webp")]
    public async Task PhotoBindingChecksOwnershipAndIsIdempotent(string key)
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var restaurantId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, RestaurantId = restaurantId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var issue = Issue(profile.Id, ComplaintStatus.Submitted, "photos"); issue.RestaurantId = restaurantId;
        AddActiveDriverAccount(db, userId);
        db.AddRange(profile, issue, new MealMate.Domain.Common.UploadFileRecord { StorageKey = key, UploadedByUserId = userId, ContentType = "image/webp", FileName = "evidence.webp", SizeBytes = 12 });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var storage = Substitute.For<IFileStorage>(); storage.ExistsAsync(key, Arg.Any<CancellationToken>()).Returns(true);
        storage.OpenReadAsync(key, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Stream>(new MemoryStream(WebpBytes(), writable: false)));
        var handler = new AddDriverSupportIssuePhotosHandler(db, user, NullLocalizedMessageProvider.Instance,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db), storage, CreateEvidenceScans(storage));

        var first = await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [key, key]), default);
        first.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        var retry = await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [key]), default);
        first.IsSuccess.Should().BeTrue(); retry.IsSuccess.Should().BeTrue();
        first.Value.Status.Should().Be("AwaitingReply"); first.Value.EvidenceUrls.Should().ContainSingle(); retry.Value.EvidenceUrls.Should().ContainSingle();
        db.ChangeTracker.Clear();
        var persistedPhotos = (await db.Complaints.Include(x => x.EvidencePhotos).SingleAsync(x => x.Id == issue.Id)).EvidencePhotos;
        persistedPhotos.Should().ContainSingle(); persistedPhotos.Single().Id.Should().NotBeEmpty();
        persistedPhotos.Single().StoragePath.Should().Be(key);
        persistedPhotos.Single().ComplaintId.Should().Be(issue.Id);

        db.ChangeTracker.Clear();
        var driverDetail = await new DriverSupportIssueQueries(db, user, NullLocalizedMessageProvider.Instance)
            .Handle(new GetDriverSupportIssueQuery(issue.Id), default);
        driverDetail.IsSuccess.Should().BeTrue();
        driverDetail.Value.EvidenceUrls.Should().Equal(CatalogImageStorage.ToContentUrl(key));
        var managerId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = managerId,
            UserName = $"manager-{managerId:N}",
            UserType = UserType.DeliveryManager,
            AccountStatus = AccountStatus.Active,
            RestaurantId = restaurantId
        });
        await db.SaveChangesAsync();
        var manager = Substitute.For<ICurrentUser>();
        manager.IsAuthenticated.Returns(true); manager.UserId.Returns(managerId);
        manager.UserType.Returns(UserType.DeliveryManager); manager.RestaurantId.Returns(restaurantId);
        var managerDetail = await new GetDispatcherProblemDetailsQueryHandler(db, NullLocalizedMessageProvider.Instance, manager)
            .Handle(new GetDispatcherProblemDetailsQuery(issue.Id), default);
        managerDetail.IsSuccess.Should().BeTrue();
        var managerPhoto = managerDetail.Value.EvidencePhotos.Should().ContainSingle().Subject;
        managerPhoto.Id.Should().Be(persistedPhotos.Single().Id);
        managerPhoto.Url.Should().Be(driverDetail.Value.EvidenceUrls.Single());
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
        AddActiveDriverAccount(db, owner);
        db.AddRange(profile, foreignProfile, issue, terminal, foreignIssue,
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = key, UploadedByUserId = otherUser, ContentType = "image/png", FileName = "foreign.png", SizeBytes = 10 },
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = mimeKey, UploadedByUserId = owner, ContentType = "application/pdf", FileName = "wrong-type.pdf", SizeBytes = 10 },
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = publicKey, UploadedByUserId = owner, ContentType = "image/png", FileName = "wrong-folder.png", SizeBytes = 10 });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(owner); user.UserType.Returns(UserType.Driver);
        var storage = Substitute.For<IFileStorage>(); storage.ExistsAsync(key, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new AddDriverSupportIssuePhotosHandler(db, user, NullLocalizedMessageProvider.Instance,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db), storage, CreateEvidenceScans(storage));
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [key]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [mimeKey]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [publicKey]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(terminal.Id, [key]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_ISSUE_TERMINAL");
        (await handler.Handle(new AddDriverSupportIssuePhotosCommand(foreignIssue.Id, [key]), default)).Error.Code.Should().Be("DRIVER_SUPPORT_ISSUE_NOT_FOUND");
    }

    [Theory]
    [InlineData("https://cdn.example/account/uploads/catalog/file.jpg")]
    [InlineData("https://cdn.example/file.jpg?folder=/uploads/drivers/issue-evidence/")]
    [InlineData("https://cdn.example/file.jpg#/uploads/drivers/issue-evidence/")]
    [InlineData("https://user@cdn.example/account/uploads/drivers/issue-evidence/file.jpg")]
    [InlineData("http://cdn.example/account/uploads/drivers/issue-evidence/file.jpg")]
    [InlineData("https://cdn.example/account/uploads/drivers/issue-evidence/../other.jpg")]
    [InlineData("https://cdn.example/account/uploads/drivers/issue-evidence/%2e%2e/other.jpg")]
    [InlineData("https://cdn.example/account/uploads/drivers/issue-evidence/%252e%252e/other.jpg")]
    [InlineData("https://cdn.example/account/uploads/drivers/issue-evidence/dir%2ffile.jpg")]
    [InlineData("https://cdn.example/account/uploads/drivers/issue-evidence/dir%5cfile.jpg")]
    [InlineData("https://cdn.example/account/uploads/drivers/issue-evidence/evidence.webp")]
    [InlineData("uploads/drivers/issue-evidence/../other.jpg")]
    public async Task PhotoBindingRejectsUnsafeOwnedKeyBeforeStorageAccess(string key)
    {
        await AssertRejectedUpload(key, owned: true, recordExists: true);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task PhotoBindingRejectsForeignOrUnrecordedAbsoluteKeyBeforeStorageAccess(bool owned, bool recordExists)
    {
        await AssertRejectedUpload("https://cdn.example/account/uploads/drivers/issue-evidence/file.jpg", owned, recordExists);
    }

    private static async Task AssertRejectedUpload(string key, bool owned, bool recordExists)
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var restaurantId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, RestaurantId = restaurantId, FullNameEn = "Driver", FullNameAr = "سائق" };
        var issue = Issue(profile.Id, ComplaintStatus.Submitted, "photos"); issue.RestaurantId = restaurantId;
        AddActiveDriverAccount(db, userId);
        db.AddRange(profile, issue);
        if (recordExists) db.Add(new MealMate.Domain.Common.UploadFileRecord { StorageKey = key, UploadedByUserId = owned ? userId : Guid.NewGuid(), ContentType = "image/jpeg", FileName = "file.jpg", SizeBytes = 10 });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var storage = Substitute.For<IFileStorage>();
        var handler = new AddDriverSupportIssuePhotosHandler(db, user, NullLocalizedMessageProvider.Instance,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db), storage, CreateEvidenceScans(storage));
        var result = await handler.Handle(new AddDriverSupportIssuePhotosCommand(issue.Id, [key]), default);
        result.Error.Code.Should().Be("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID");
        await storage.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await db.Set<ComplaintEvidencePhoto>().CountAsync()).Should().Be(0);
        (await db.OutboxMessages.CountAsync()).Should().Be(0);
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
        AddActiveDriverAccount(db, userId);
        db.AddRange(profile, issue,
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = missingKey, UploadedByUserId = userId, ContentType = "image/png", FileName = "missing.png", SizeBytes = 10 },
            new MealMate.Domain.Common.UploadFileRecord { StorageKey = fourthKey, UploadedByUserId = userId, ContentType = "image/png", FileName = "fourth.png", SizeBytes = 10 });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns(userId); user.UserType.Returns(UserType.Driver);
        var storage = Substitute.For<IFileStorage>(); storage.ExistsAsync(missingKey, Arg.Any<CancellationToken>()).Returns(false); storage.ExistsAsync(fourthKey, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new AddDriverSupportIssuePhotosHandler(db, user, NullLocalizedMessageProvider.Instance,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db), storage, CreateEvidenceScans(storage));
        storage.OpenReadAsync(fourthKey, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Stream>(new MemoryStream(PngBytes(), writable: false)));
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

    private static void AddActiveDriverAccount(AppDbContext db, Guid userId)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"driver-{userId:N}",
            AccountStatus = AccountStatus.Active,
            UserType = UserType.Driver
        });
    }

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

    private static MealMate.Application.Common.Storage.SupportEvidenceScanService CreateEvidenceScans(IFileStorage storage)
    {
        var scanner = Substitute.For<MealMate.Application.Common.Abstractions.IEvidenceMalwareScanner>();
        scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MealMate.Application.Common.Abstractions.EvidenceScanResult(
                MealMate.Application.Common.Abstractions.EvidenceScanVerdict.Clean, "test")));
        var receipts = Substitute.For<MealMate.Application.Common.Storage.ISupportEvidenceScanReceiptStore>();
        receipts.GetOrScanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<Func<CancellationToken, Task<MealMate.Application.Common.Abstractions.EvidenceScanResult>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<MealMate.Application.Common.Abstractions.EvidenceScanResult>>>()(call.Arg<CancellationToken>()));
        receipts.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<MealMate.Application.Common.Abstractions.EvidenceScanResult>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return new MealMate.Application.Common.Storage.SupportEvidenceScanService(storage, scanner, receipts);
    }

    private static byte[] WebpBytes() => [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];
    private static byte[] PngBytes() => [137, 80, 78, 71, 13, 10, 26, 10, 0, 0];

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
}
