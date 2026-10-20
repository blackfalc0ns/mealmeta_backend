using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using System.Globalization;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// Screens 08.01 / 08.02 contract tests: the driver report-issue and reassignment
/// endpoints must persist real records (Complaint + classification + evidence
/// photos), enforce assigned-driver ownership, validate the design's fixed
/// value sets and 250-character limits, accept optional coordinates as a pair,
/// and reject duplicate active reassignment requests.
/// </summary>
public class DriverIssueReassignmentContractTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (ICurrentUser CurrentUser, ILocalizedMessageProvider Messages) CreateMocks()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserType.Returns(UserType.Driver);
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.CurrentCulture.Returns(new CultureInfo("ar"));
        return (currentUser, messages);
    }

    private static IFileStorage ExistingStorage()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return storage;
    }

    private static DriverProfile SeedDriver(AppDbContext db, ICurrentUser currentUser)
    {
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق البلاغات",
            FullNameEn = "Issue Driver",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);
        db.Users.Add(new ApplicationUser { Id = driver.UserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
        currentUser.UserId.Returns(driver.UserId);
        return driver;
    }

    private static (Trip Trip, TripStop Stop) SeedAssignedStop(
        AppDbContext db, DriverProfile driver, TripStopStatus status = TripStopStatus.PickedUp, string code = "BX-0801")
    {
        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRP-0801",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress
        };
        db.Trips.Add(trip);
        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        });
        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = code,
            CustomerMaskedId = "CUST-08",
            CustomerName = "عميل البلاغ",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "2 وجبة",
            MealSummaryEn = "2 meals",
            SlotLabel = "09:20 ص",
            Status = status
        };
        db.TripStops.Add(stop);
        return (trip, stop);
    }

    private static UploadFileRecord SeedUpload(
        AppDbContext db, Guid ownerUserId, string key, string contentType = "image/jpeg")
    {
        var record = new UploadFileRecord
        {
            StorageKey = key,
            FileName = "evidence.jpg",
            ContentType = contentType,
            SizeBytes = 2048,
            UploadedByUserId = ownerUserId,
            UploadedAtUtc = DateTime.UtcNow
        };
        db.UploadFileRecords.Add(record);
        return record;
    }

    private static DriverDeliveryOrderCommandHandlers CreateHandler(
        AppDbContext db, ICurrentUser currentUser, ILocalizedMessageProvider messages, IFileStorage? storage = null)
        => new(db, currentUser, messages, fileStorage: storage,
            transactions: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());

    #region 08.01 Report issue

    [Fact]
    public async Task ReportIssue_MissingStop_ReturnsNotFound_AndSavesNothing()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        SeedDriver(db, currentUser);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            Guid.NewGuid(), IssueType: "DamagedBox", Description: "بوكس تالف"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("order.not_found");
        db.Complaints.Should().BeEmpty();
        db.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_AuthenticatedCallerWithoutDriverProfile_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (_, stop) = SeedAssignedStop(db, SeedDriver(db, currentUser));
        await db.SaveChangesAsync();

        // A signed-in caller whose identity resolves to no driver profile.
        currentUser.UserId.Returns(Guid.NewGuid());
        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id, IssueType: "DamagedBox", Description: "بوكس تالف"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("DRIVER_IDENTITY_MISMATCH");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_ForeignDriverWithoutActiveAssignment_IsForbidden()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (_, stop) = SeedAssignedStop(db, SeedDriver(db, currentUser));

        var foreign = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق آخر",
            FullNameEn = "Other Driver",
            IsAvailable = true
        };
        db.DriverProfiles.Add(foreign);
        db.Users.Add(new ApplicationUser { Id = foreign.UserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(foreign.UserId);
        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id, IssueType: "DamagedBox", Description: "بوكس تالف"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Orders.DeliveryForbidden");
        db.Complaints.Should().BeEmpty();
    }

    [Theory]
    [InlineData("test_issue")]
    [InlineData("damaged")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ReportIssue_IssueTypeOutsideAllowlist_IsRejected(string? issueType)
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id, IssueType: issueType!, Description: "بوكس تالف"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.issue.issue_type_invalid");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_UnknownSeverity_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id, IssueType: "DamagedBox", Description: "بوكس تالف", Severity: "Critical"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.issue.severity_invalid");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_DescriptionBeyond250_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id, IssueType: "DamagedBox", Description: new string('ا', 251)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.issue.description_too_long");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_PersistsRealComplaint_ClassificationSeverity_AndReturnsPersistedId()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (trip, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id,
            IssueType: "DamagedBox",
            Description: "الصندوق مكسور من الجانب الأيمن",
            Severity: "High"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var saved = await db.Complaints.Include(c => c.Classifications).SingleAsync();
        result.Value.Should().Be(saved.Id, "the response id must be the persisted complaint id");
        saved.Category.Should().Be("DamagedBox");
        saved.Status.Should().Be(ComplaintStatus.Submitted);
        saved.RestaurantId.Should().Be(trip.RestaurantId);
        saved.OrderId.Should().Be(stop.Id);
        saved.DriverProfileId.Should().Be(driver.Id);
        saved.SubmittedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        saved.Description.Should().Contain("الصندوق مكسور من الجانب الأيمن");

        var classification = saved.Classifications.Should().ContainSingle().Subject;
        classification.Severity.Should().Be("High");
        classification.CategoryCode.Should().Be("DamagedBox");

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.FailureReason.Should().Contain("DamagedBox");

        db.AuditLogs.Should().Contain(a => a.Action == "DriverReportedIssue" && a.EntityId == stop.Id);
    }

    [Fact]
    public async Task ReportIssue_BlankDescription_IsOptional_AndComposesFallback()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id, IssueType: "SevereDelay", Description: "   "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.Complaints.SingleAsync();
        saved.Description.Should().NotBeNullOrWhiteSpace();
        saved.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReportIssue_CaseInsensitiveValues_AreNormalizedToMachineValues()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id, IssueType: "damagedbox", Description: "تلف", Severity: "high"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.Complaints.Include(c => c.Classifications).SingleAsync();
        saved.Category.Should().Be("DamagedBox");
        saved.Classifications.Single().Severity.Should().Be("High");
    }

    [Fact]
    public async Task ReportIssue_LegacySingleKey_PersistsOneEvidencePhoto()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        SeedUpload(db, driver.UserId, "uploads/drivers/issue-evidence/legacy-1.jpg");
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages, ExistingStorage());

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id,
            IssueType: "DamagedBox",
            Description: "تلف",
            EvidencePhotoStorageKey: "uploads/drivers/issue-evidence/legacy-1.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.Complaints.Include(c => c.EvidencePhotos).SingleAsync();
        var photo = saved.EvidencePhotos.Should().ContainSingle().Subject;
        photo.StoragePath.Should().Be("uploads/drivers/issue-evidence/legacy-1.jpg");
        photo.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task ReportIssue_MultipleKeys_PersistUpToThree_MergedWithLegacyAndDeduped()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        SeedUpload(db, driver.UserId, "uploads/drivers/issue-evidence/a.jpg");
        SeedUpload(db, driver.UserId, "uploads/drivers/issue-evidence/b.png");
        SeedUpload(db, driver.UserId, "uploads/drivers/issue-evidence/c.webp");
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages, ExistingStorage());

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id,
            IssueType: "TrafficAccident",
            Description: "حادث",
            EvidencePhotoStorageKey: "uploads/drivers/issue-evidence/a.jpg",
            EvidencePhotoStorageKeys: new[]
            {
                "uploads/drivers/issue-evidence/a.jpg",
                "",
                "uploads/drivers/issue-evidence/b.png",
                "uploads/drivers/issue-evidence/c.webp"
            }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.Complaints.Include(c => c.EvidencePhotos).SingleAsync();
        saved.EvidencePhotos.Select(p => p.StoragePath).Should().BeEquivalentTo(new[]
        {
            "uploads/drivers/issue-evidence/a.jpg",
            "uploads/drivers/issue-evidence/b.png",
            "uploads/drivers/issue-evidence/c.webp"
        });
    }

    [Fact]
    public async Task ReportIssue_MoreThanThreeDistinctPhotos_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        foreach (var key in new[] { "1", "2", "3", "4" })
        {
            SeedUpload(db, driver.UserId, $"uploads/drivers/issue-evidence/{key}.jpg");
        }
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages, ExistingStorage());

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id,
            IssueType: "DamagedBox",
            Description: "تلف",
            EvidencePhotoStorageKeys: new[]
            {
                "uploads/drivers/issue-evidence/1.jpg",
                "uploads/drivers/issue-evidence/2.jpg",
                "uploads/drivers/issue-evidence/3.jpg",
                "uploads/drivers/issue-evidence/4.jpg"
            }), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.issue.photo_keys_limit");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_PhotoKeyWithoutUploadRecord_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages, ExistingStorage());

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id,
            IssueType: "DamagedBox",
            Description: "تلف",
            EvidencePhotoStorageKey: "uploads/drivers/issue-evidence/never-uploaded.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.issue.photo_not_found");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_PhotoOwnedByAnotherUser_IsForbidden()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        SeedUpload(db, Guid.NewGuid(), "uploads/drivers/issue-evidence/foreign.jpg");
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages, ExistingStorage());

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id,
            IssueType: "DamagedBox",
            Description: "تلف",
            EvidencePhotoStorageKey: "uploads/drivers/issue-evidence/foreign.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.issue.photo_forbidden");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportIssue_NonImagePhoto_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        SeedUpload(db, driver.UserId, "uploads/drivers/issue-evidence/doc.pdf", "application/pdf");
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages, ExistingStorage());

        var result = await handler.Handle(new ReportDriverIssueCommand(
            stop.Id,
            IssueType: "DamagedBox",
            Description: "تلف",
            EvidencePhotoStorageKey: "uploads/drivers/issue-evidence/doc.pdf"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.issue.photo_invalid_type");
        db.Complaints.Should().BeEmpty();
    }

    #endregion

    #region 08.02 Request reassignment

    [Fact]
    public async Task Reassignment_MissingStop_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            Guid.NewGuid(), Reason: "VehicleBreakdown", DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("order.not_found");
    }

    [Fact]
    public async Task Reassignment_FreeTextReason_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "تعطل محرك المركبة في الطريق", DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.reassignment.reason_invalid");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassignment_CaseInsensitiveReason_IsNormalized()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "vehiclebreakdown", DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.Complaints.SingleAsync();
        saved.Description.Should().Contain("VehicleBreakdown");
    }

    [Fact]
    public async Task Reassignment_ForeignDriverWithoutActiveAssignment_IsForbidden()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, SeedDriver(db, Substitute.For<ICurrentUser>()));
        await db.SaveChangesAsync();

        // A signed-in driver who holds no active assignment on the stop's trip.
        var foreignAuthUserId = Guid.NewGuid();
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = foreignAuthUserId,
            FullNameAr = "سائق آخر",
            FullNameEn = "Other Driver",
            IsAvailable = true
        });
        db.Users.Add(new ApplicationUser { Id = foreignAuthUserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
        await db.SaveChangesAsync();
        currentUser.UserId.Returns(foreignAuthUserId);
        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Orders.DeliveryForbidden");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassignment_FinalStopState_IsConflict()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver, TripStopStatus.Delivered);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.reassignment.stop_state_not_allowed");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassignment_DuplicateActiveRequest_IsConflict()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var first = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", DriverId: driver.Id), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var second = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", DriverId: driver.Id), CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be("driver.reassignment.request_already_active");
        second.Error!.Type.Should().Be(ErrorType.Conflict);
        db.Complaints.Count(c => c.Category == "DriverReassignmentRequested").Should().Be(1);
    }

    [Fact]
    public async Task Reassignment_ResolvedPriorRequest_AllowsNewRequest()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var first = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", DriverId: driver.Id), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Dispatcher resolved (or rejected) the first request.
        var prior = await db.Complaints.SingleAsync(c => c.Category == "DriverReassignmentRequested");
        prior.Status = ComplaintStatus.Resolved;
        await db.SaveChangesAsync();

        // The driver was re-assigned and got stuck again: a new request must work.
        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = stop.TripId,
            DriverProfileId = driver.Id,
            Status = "Active"
        });
        db.Entry(stop).State = EntityState.Modified;
        stop.Status = TripStopStatus.PickedUp;
        await db.SaveChangesAsync();

        var second = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "TrafficAccident", DriverId: driver.Id), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        db.Complaints.Count(c => c.Category == "DriverReassignmentRequested").Should().Be(2);
    }

    [Fact]
    public async Task Reassignment_NotesBeyond250_IsRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", Notes: new string('ن', 251), DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.reassignment.notes_too_long");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassignment_PartialCoordinates_AreRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", Latitude: 29.3m, DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Orders.CoordinatesPairRequired");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassignment_OutOfRangeCoordinates_AreRejected()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "VehicleBreakdown", Latitude: 91m, Longitude: 47.9m, DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Orders.CoordinatesOutOfRange");
        db.Complaints.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassignment_ValidRequest_PersistsComplaint_WithCoordinates_AndResetsStop()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (trip, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id,
            Reason: "VehicleBreakdown",
            Notes: "المركبة متوقفة أمام مجمع الأفنيوز",
            Latitude: 29.3000m,
            Longitude: 47.9300m,
            DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var saved = await db.Complaints.SingleAsync();
        result.Value.RequestId.Should().Be(saved.Id);
        saved.Category.Should().Be("DriverReassignmentRequested");
        saved.ReassignmentTripStopId.Should().Be(stop.Id);
        saved.ReassignmentReason.Should().Be("VehicleBreakdown");
        saved.ReassignmentNotes.Should().Be("المركبة متوقفة أمام مجمع الأفنيوز");
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(DateTime.UtcNow);
        var recoveryHandler = new DriverReassignmentHandlers(db, currentUser, messages, clock,
            new MealMate.Persistence.Common.PostgresApplicationTransactionExecutor(db),
            new MealMate.Persistence.Common.PostgresApplicationLockManager(db));
        var recovery = await recoveryHandler.Handle(new GetDriverReassignmentRequestsQuery(stop.Id), default);
        recovery.IsSuccess.Should().BeTrue();
        recovery.Value.Single().RequestId.Should().Be(result.Value.RequestId);
        recovery.Value.Single().Reason.Should().Be("VehicleBreakdown");
        recovery.Value.Single().Status.Should().Be("ReassignmentRequested");
        saved.Status.Should().Be(ComplaintStatus.Submitted);
        saved.DriverProfileId.Should().Be(driver.Id);
        saved.RestaurantId.Should().Be(trip.RestaurantId);
        saved.Latitude.Should().Be(29.3000m);
        saved.Longitude.Should().Be(47.9300m);
        saved.Description.Should().Contain("المركبة متوقفة أمام مجمع الأفنيوز");

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.PickedUp, "requesting reassignment preserves the original driver's custody until receipt completion");
        updatedStop.FailureReason.Should().Contain("VehicleBreakdown");

        var assignment = await db.DriverAssignments.SingleAsync();
        assignment.Status.Should().Be("Active", "the reporter remains the custodian while dispatch arranges handover");

        db.AuditLogs.Should().Contain(a => a.Action == "DriverRequestedReassignment" && a.EntityId == stop.Id);
    }

    [Fact]
    public async Task Reassignment_WithoutCoordinates_PersistsNullLocation()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var driver = SeedDriver(db, currentUser);
        var (_, stop) = SeedAssignedStop(db, driver);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, currentUser, messages);

        var result = await handler.Handle(new RequestReassignmentCommand(
            stop.Id, Reason: "DeviceFailure", DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.Complaints.SingleAsync();
        saved.Latitude.Should().BeNull();
        saved.Longitude.Should().BeNull();
    }

    #endregion
}
