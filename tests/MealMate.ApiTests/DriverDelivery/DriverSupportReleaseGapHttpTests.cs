using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.ApiTests.Support;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.DriverDelivery;

public sealed class DriverSupportReleaseGapHttpTests(OrderLifecycleEndToEndFixture fixture)
    : IClassFixture<OrderLifecycleEndToEndFixture>
{
    [Fact]
    public async Task OperationalDriverListRejectsOffsetOverflow()
    {
        var userId = Guid.NewGuid();
        var driver = new DriverProfile { UserId = userId, FullNameAr = "سائق", FullNameEn = "Driver" };
        await using (var db = OpenDb())
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            db.DriverProfiles.Add(driver);
            await db.SaveChangesAsync();
        }

        using var client = fixture.CreateClientForUser(new TokenUser(userId, UserType.Driver, ["Driver"], []));
        var response = await client.GetAsync("/api/v1/support/operational-issues?pageNumber=2147483647&pageSize=2");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InactiveDriverCannotReadOperationalOrUnifiedCasesWithStillValidToken()
    {
        var userId = Guid.NewGuid();
        var driver = new DriverProfile { UserId = userId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = new Complaint
        {
            CustomerUserId = userId, DriverProfileId = driver.Id, Category = "VehicleBreakdown",
            Title = "Breakdown", Description = "Vehicle needs help", SubmittedAtUtc = DateTime.UtcNow
        };
        fixture.EnableSupportEvidenceTestScanner();
        var evidenceBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
        using var storageScope = fixture.Factory.Services.CreateScope();
        var storage = storageScope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using var image = new MemoryStream(evidenceBytes);
        var stored = await storage.SaveAsync(image, "driver-evidence.png", "image/png", "uploads/drivers/issue-evidence");
        var operationalEvidenceId = Guid.NewGuid();
        issue.EvidencePhotos.Add(new ComplaintEvidencePhoto
        {
            Id = operationalEvidenceId, StoragePath = stored.StorageKey, ContentType = "image/png", UploadedAtUtc = DateTime.UtcNow
        });
        var supportCase = SupportCase.Create(SupportCaseChannel.Driver, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "delivery", "Driver request", "Driver case details", userId, DateTime.UtcNow);
        var caseMessage = new SupportCaseMessage
        {
            SupportCaseId = supportCase.Id, AuthorUserId = userId, Visibility = SupportCaseMessageVisibility.Participant,
            Body = "Driver evidence message", CreatedAtUtc = DateTime.UtcNow
        };

        await using (var db = OpenDb())
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            db.AddRange(driver, issue, supportCase, caseMessage);
            db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord
            {
                StorageKey = stored.StorageKey, FileName = stored.FileName, ContentType = "image/png", SizeBytes = stored.SizeBytes,
                UploadedByUserId = userId, UploadedAtUtc = DateTime.UtcNow, Scope = "drivers", Folder = "issue-evidence"
            });
            await db.SaveChangesAsync();
        }

        using var client = fixture.CreateClientForUser(new TokenUser(userId, UserType.Driver, ["Driver"], []));
        var operationalPath = $"/api/v1/support/operational-issues/{issue.Id}";
        var operationalEvidencePath = $"/api/v1/support/operational-issues/{issue.Id}/evidence/{operationalEvidenceId}";
        var casePath = $"/api/v1/support/cases/{supportCase.Id}";
        (await client.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(operationalEvidencePath)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/driver/support/issues/{issue.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(casePath)).StatusCode.Should().Be(HttpStatusCode.OK);
        var activeCase = await client.GetFromJsonAsync<SupportCaseDto>(casePath);
        using (var proofUpload = new MultipartFormDataContent())
        {
            var pdf = new ByteArrayContent("%PDF-1.7\nprivate driver support evidence\n"u8.ToArray());
            pdf.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            proofUpload.Add(pdf, "file", "driver-proof.pdf");
            var uploaded = await client.PostAsync("/api/v1/support/attachments", proofUpload);
            uploaded.StatusCode.Should().Be(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
            var attachment = await uploaded.Content.ReadFromJsonAsync<SupportCaseService.SupportCaseAttachmentUploadDto>();
            await using var db = OpenDb();
            var seededMessage = await db.SupportCaseMessages.SingleAsync(x => x.Id == caseMessage.Id);
            seededMessage.AttachmentKeys = [attachment!.StorageKey];
            await db.SaveChangesAsync();
        }
        var attachmentPath = $"/api/v1/support/cases/{supportCase.Id}/messages/{caseMessage.Id}/attachments/0";
        (await client.GetAsync(attachmentPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        int outboxBeforeInactiveOperations;
        int auditBeforeInactiveOperations;
        await using (var db = OpenDb())
        {
            outboxBeforeInactiveOperations = await db.OutboxMessages.CountAsync();
            auditBeforeInactiveOperations = await db.AuditLogs.CountAsync(x => x.EntityId == issue.Id);
        }

        await using (var db = OpenDb())
        {
            var account = await db.Users.SingleAsync(x => x.Id == userId);
            account.AccountStatus = AccountStatus.Suspended;
            await db.SaveChangesAsync();
        }

        (await client.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<OperationalSupportIssuePageDto>("/api/v1/support/operational-issues"))!
            .Items.Should().BeEmpty();
        (await client.GetAsync(operationalPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(operationalEvidencePath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/driver/support/issues/{issue.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/v1/support/operational-issues/{issue.Id}/escalate",
            new EscalateOperationalIssueRequest("Stale driver escalation", "inactive-driver-escalation")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/support/cases")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(casePath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var create = await client.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Driver, SupportCaseKind.GeneralRequest, "delivery", "Another request", "Body", "inactive-driver-case-create"));
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var message = await client.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/messages",
            new AddSupportMessageRequest("Stale driver reply", activeCase!.Version, "inactive-driver-message"));
        message.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var action = await client.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/actions/close",
            new SupportCaseActionRequest("close", activeCase.Version, "inactive-driver-action", "Stale token"));
        action.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(attachmentPath))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent("%PDF-1.4 evidence"u8.ToArray()), "file", "evidence.pdf");
        upload.Last().Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        (await client.PostAsync("/api/v1/support/attachments", upload)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var db = OpenDb())
        {
            (await db.OutboxMessages.CountAsync()).Should().Be(outboxBeforeInactiveOperations);
            (await db.AuditLogs.CountAsync(x => x.EntityId == issue.Id)).Should().Be(auditBeforeInactiveOperations);
        }

        await using (var db = OpenDb())
        {
            var account = await db.Users.SingleAsync(x => x.Id == userId);
            account.AccountStatus = AccountStatus.Active;
            var persistedDriver = await db.DriverProfiles.IgnoreQueryFilters().SingleAsync(x => x.Id == driver.Id);
            persistedDriver.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        (await client.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<OperationalSupportIssuePageDto>("/api/v1/support/operational-issues"))!
            .Items.Should().BeEmpty();
        (await client.GetAsync($"/api/v1/driver/support/issues?status=All")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/support/cases")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KeyedReportReplaysSameIdAcrossLostResponseAndAfterCustodyEnds()
    {
        Guid driverUserId;
        Guid driverProfileId;
        Guid secondDriverUserId;
        Guid stopId;
        Guid tripId;
        await using (var db = OpenDb())
        {
            var seeded = await OrderLifecycleTestDataSeeder.SeedAsync(db, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
            driverUserId = seeded.DriverUserA.Id;
            secondDriverUserId = seeded.DriverUserB.Id;
            driverProfileId = seeded.DriverProfileA.Id;
            var stop = await SeedAssignedStopAsync(db, seeded, driverUserId, attachDay: true);
            stopId = stop.Id;
            tripId = stop.TripId;
        }

        using var driver = fixture.CreateClientForUser(new TokenUser(driverUserId, UserType.Driver, ["Driver"], []));
        var path = $"/api/v1/driver/orders/{stopId}/report-issue";
        var body = new { issueType = "Other", description = "Vehicle needs attention", severity = "Medium", idempotencyKey = "driver-report-release-key" };
        var firstPending = driver.PostAsJsonAsync(path, body);
        var concurrentPending = driver.PostAsJsonAsync(path, body);
        await Task.WhenAll(firstPending, concurrentPending);
        var first = firstPending.Result;
        var concurrent = concurrentPending.Result;
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var issueId = await first.Content.ReadFromJsonAsync<Guid>();
        concurrent.StatusCode.Should().Be(HttpStatusCode.OK, await concurrent.Content.ReadAsStringAsync());
        (await concurrent.Content.ReadFromJsonAsync<Guid>()).Should().Be(issueId);

        using var unrelatedDriver = fixture.CreateClientForUser(new TokenUser(secondDriverUserId, UserType.Driver, ["Driver"], []));
        var unrelatedReplay = await unrelatedDriver.PostAsJsonAsync(path, body);
        unrelatedReplay.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var replay = await driver.PostAsJsonAsync(path, body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        (await replay.Content.ReadFromJsonAsync<Guid>()).Should().Be(issueId);
        (await driver.PostAsJsonAsync($"/api/v1/driver/orders/{Guid.NewGuid()}/report-issue", body))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await driver.PostAsJsonAsync(path, new { issueType = "Other", description = "Changed payload", severity = "Medium", idempotencyKey = "driver-report-release-key" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var db = OpenDb())
        {
            var trip = await db.Trips.SingleAsync(x => x.Id == tripId);
            trip.Status = TripStatus.Completed;
            var assignment = await db.DriverAssignments.SingleAsync(x => x.TripId == tripId && x.DriverProfileId == driverProfileId);
            assignment.Status = "Completed";
            await db.SaveChangesAsync();
        }
        var postCustodyReplay = await driver.PostAsJsonAsync(path, body);
        postCustodyReplay.StatusCode.Should().Be(HttpStatusCode.OK, await postCustodyReplay.Content.ReadAsStringAsync());
        (await postCustodyReplay.Content.ReadFromJsonAsync<Guid>()).Should().Be(issueId);

        await using (var db = OpenDb())
        {
            (await db.Complaints.CountAsync(x => x.DriverProfileId == driverProfileId && x.Category == "Other")).Should().Be(1);
            (await db.AuditLogs.CountAsync(x => x.Action == "DriverReportedIssue" && x.CorrelationId == "driver-report-release-key")).Should().Be(1);
            (await db.OutboxMessages.CountAsync(x => x.EventType == "driver-support-issue-changed.v1" && x.AggregateId == issueId.ToString("D"))).Should().Be(1);
        }
    }

    [Fact]
    public async Task ReportIssueWithoutKeyKeepsLegacyBareGuidResponse()
    {
        Guid driverUserId;
        Guid stopId;
        await using (var db = OpenDb())
        {
            var seeded = await OrderLifecycleTestDataSeeder.SeedAsync(db, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
            driverUserId = seeded.DriverUserA.Id;
            stopId = (await SeedAssignedStopAsync(db, seeded, driverUserId, attachDay: true)).Id;
        }

        using var driver = fixture.CreateClientForUser(new TokenUser(driverUserId, UserType.Driver, ["Driver"], []));
        var response = await driver.PostAsJsonAsync($"/api/v1/driver/orders/{stopId}/report-issue",
            new { issueType = "Other", description = "Legacy client", severity = "Medium" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<Guid>()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task OrphanStopsCannotCreateReportOrReassignmentRecords()
    {
        Guid driverUserId;
        Guid driverProfileId;
        Guid stopId;
        int outboxBefore;
        await using (var db = OpenDb())
        {
            var seeded = await OrderLifecycleTestDataSeeder.SeedAsync(db, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
            outboxBefore = await db.OutboxMessages.CountAsync();
            driverUserId = seeded.DriverUserA.Id;
            driverProfileId = seeded.DriverProfileA.Id;
            var stop = await SeedAssignedStopAsync(db, seeded, driverUserId, attachDay: false);
            stopId = stop.Id;
        }

        using var driver = fixture.CreateClientForUser(new TokenUser(driverUserId, UserType.Driver, ["Driver"], []));
        (await driver.PostAsJsonAsync($"/api/v1/driver/orders/{stopId}/report-issue",
            new { issueType = "Other", description = "No customer link", severity = "Medium" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await driver.PostAsJsonAsync($"/api/v1/driver/orders/{stopId}/request-reassignment",
            new { reason = "VehicleBreakdown" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var db = OpenDb())
        {
            (await db.Complaints.CountAsync(x => x.DriverProfileId == driverProfileId)).Should().Be(0);
            (await db.AuditLogs.CountAsync(x => x.EntityId == stopId && (x.Action == "DriverReportedIssue" || x.Action == "DriverReassignmentRequested"))).Should().Be(0);
            (await db.OutboxMessages.CountAsync()).Should().Be(outboxBefore);
            (await db.TripStops.SingleAsync(x => x.Id == stopId)).FailureReason.Should().BeNull();
        }
    }

    [Fact]
    public async Task LegacyStopWithoutDayUsesItsPersistedDailyOrderCustomerRelationship()
    {
        Guid driverUserId;
        Guid driverProfileId;
        Guid stopId;
        Guid customerUserId;
        await using (var db = OpenDb())
        {
            var seeded = await OrderLifecycleTestDataSeeder.SeedAsync(db, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
            driverUserId = seeded.DriverUserA.Id;
            driverProfileId = seeded.DriverProfileA.Id;
            customerUserId = seeded.CustomerUser.Id;
            var stop = await SeedAssignedStopAsync(db, seeded, driverUserId, attachDay: false, attachDailyOrder: true);
            stopId = stop.Id;
        }

        using var driver = fixture.CreateClientForUser(new TokenUser(driverUserId, UserType.Driver, ["Driver"], []));
        var response = await driver.PostAsJsonAsync($"/api/v1/driver/orders/{stopId}/report-issue",
            new { issueType = "Other", description = "Legacy linked daily order", severity = "Medium" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var issueId = await response.Content.ReadFromJsonAsync<Guid>();
        await using var verify = OpenDb();
        (await verify.Complaints.SingleAsync(x => x.Id == issueId)).CustomerUserId.Should().Be(customerUserId);
    }

    [Fact]
    public async Task ProvidedBlankOrOverlengthReportKeyIsRejected()
    {
        Guid driverUserId;
        Guid stopId;
        await using (var db = OpenDb())
        {
            var seeded = await OrderLifecycleTestDataSeeder.SeedAsync(db, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
            driverUserId = seeded.DriverUserA.Id;
            stopId = (await SeedAssignedStopAsync(db, seeded, driverUserId, attachDay: true)).Id;
        }

        using var driver = fixture.CreateClientForUser(new TokenUser(driverUserId, UserType.Driver, ["Driver"], []));
        var path = $"/api/v1/driver/orders/{stopId}/report-issue";
        (await driver.PostAsJsonAsync(path, new { issueType = "Other", idempotencyKey = "   " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await driver.PostAsJsonAsync(path, new { issueType = "Other", idempotencyKey = new string('k', 101) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectedComplaintCannotBeResolvedOrEmitResolutionEffects()
    {
        Guid issueId;
        Guid dispatcherId;
        var token = default(TokenUser)!;
        await using (var db = OpenDb())
        {
            var seeded = await OrderLifecycleTestDataSeeder.SeedAsync(db, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
            dispatcherId = seeded.DispatcherA.Id;
            var issue = new Complaint
            {
                CustomerUserId = seeded.CustomerUser.Id, DriverProfileId = seeded.DriverProfileA.Id,
                RestaurantId = seeded.RestaurantA.Id, Category = "VehicleBreakdown", Title = "Rejected issue",
                Description = "This issue was rejected.", Status = ComplaintStatus.Rejected, SubmittedAtUtc = DateTime.UtcNow
            };
            issueId = issue.Id;
            db.Complaints.Add(issue);
            token = new TokenUser(dispatcherId, UserType.DeliveryManager, ["Dispatcher"], [], seeded.RestaurantA.Id);
            await db.SaveChangesAsync();
        }

        int outboxBefore;
        await using (var db = OpenDb()) outboxBefore = await db.OutboxMessages.CountAsync();
        using var dispatcher = fixture.CreateClientForUser(token);
        var response = await dispatcher.PostAsJsonAsync($"/api/v1/dispatcher/support/issues/{issueId}/resolve",
            new { resolutionNotes = "Attempt to resolve a rejected complaint." });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());

        await using var verify = OpenDb();
        (await verify.Complaints.SingleAsync(x => x.Id == issueId)).Status.Should().Be(ComplaintStatus.Rejected);
        (await verify.AuditLogs.CountAsync(x => x.EntityId == issueId && x.Action == "SupportIssueResolved")).Should().Be(0);
        (await verify.OutboxMessages.CountAsync()).Should().Be(outboxBefore);
        (await verify.Set<DeliveryManagerNotification>().CountAsync(x => x.SubjectId == issueId)).Should().Be(0);
        (await verify.DriverNotifications.CountAsync(x => x.SubjectId == issueId)).Should().Be(0);
    }

    private static async Task<TripStop> SeedAssignedStopAsync(AppDbContext db, FixtureEntities seed, Guid driverUserId,
        bool attachDay, bool attachDailyOrder = false)
    {
        var date = seed.StartDate;
        var subscription = new Subscription
        {
            CustomerUserId = seed.CustomerUser.Id, ProgramId = seed.Program.Id, BundleId = seed.Bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 1, IncludeFriday = true, Status = SubscriptionStatus.Active,
            StartDate = date, EndDate = date, PaidAmount = new Money(1m, "KWD")
        };
        var day = new SubscriptionDay { SubscriptionId = subscription.Id, RestaurantId = seed.RestaurantA.Id, ServiceDate = date };
        var dailyOrder = attachDailyOrder ? new RestaurantDailyOrder
        {
            RestaurantId = seed.RestaurantA.Id, SubscriptionDayId = day.Id, ServiceDate = date,
            Status = OrderStatus.ReadyForPickup, BoxCount = 1, CreatedAtUtc = DateTime.UtcNow
        } : null;
        var trip = new Trip { RestaurantId = seed.RestaurantA.Id, Code = $"SUP-{Guid.NewGuid():N}"[..12], PlannedDate = date, Status = TripStatus.InProgress };
        var stop = new TripStop
        {
            TripId = trip.Id, DailyOrderId = dailyOrder?.Id, SubscriptionDayId = attachDay ? day.Id : null,
            OrderCode = $"BX-{Guid.NewGuid():N}"[..8], CustomerMaskedId = "CUST", CustomerName = "Customer",
            ZoneNameAr = "منطقة", ZoneNameEn = "Area", MealSummaryAr = "وجبة", MealSummaryEn = "Meal",
            SlotLabel = "AM", Status = TripStopStatus.PickedUp
        };
        db.AddRange(subscription, day, trip, stop, new DriverAssignment { TripId = trip.Id, DriverProfileId = seed.DriverProfileA.Id, Status = "Active" });
        if (dailyOrder is not null) db.RestaurantDailyOrders.Add(dailyOrder);
        await db.SaveChangesAsync();
        return stop;
    }

    private AppDbContext OpenDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options,
        Substitute.For<IDomainEventDispatcher>());
}
