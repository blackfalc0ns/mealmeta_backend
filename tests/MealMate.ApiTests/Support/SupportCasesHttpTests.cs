using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.Support;

public sealed class SupportCasesHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    public SupportCasesHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SupportRoutesRequireAuthentication()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/support/cases")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CustomerCanCreateAndReadOnlyOwnPersistedCases()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        using var owner = _fixture.CreateClientForUser(new TokenUser(ownerId, UserType.Customer, ["Customer"], []));
        using var other = _fixture.CreateClientForUser(new TokenUser(otherId, UserType.Customer, ["Customer"], []));

        var create = await owner.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "order", "Missing item", "An item was missing.", "customer-create-1"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<SupportCaseDto>();
        created.Should().NotBeNull();
        created!.RequesterUserId.Should().Be(ownerId);

        var mine = await owner.GetFromJsonAsync<SupportCasePageDto>("/api/v1/support/cases");
        mine!.TotalCount.Should().Be(1);
        var others = await other.GetFromJsonAsync<SupportCasePageDto>("/api/v1/support/cases");
        others!.TotalCount.Should().Be(0);
        (await other.GetAsync($"/api/v1/support/cases/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GenericCaseListRejectsOffsetOverflow()
    {
        using var customer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        var response = await customer.GetAsync("/api/v1/support/cases?pageNumber=2147483647&pageSize=2");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RestaurantStaffCanCloseAndReopenOwnCaseUsingUrlActionWithoutBodyAction()
    {
        var restaurantId = Guid.NewGuid();
        var supportCase = SupportCase.Create(SupportCaseChannel.Restaurant, SupportCaseKind.GeneralRequest,
            SupportCaseOwnerTeam.PlatformSupport, "account", "Restaurant request", "Help needed", Guid.NewGuid(),
            DateTime.UtcNow, restaurantId: restaurantId);
        supportCase.TryTransitionTo(SupportCaseStatus.InProgress).Should().BeTrue();
        supportCase.TryTransitionTo(SupportCaseStatus.Resolved).Should().BeTrue();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        { db.SupportCases.Add(supportCase); await db.SaveChangesAsync(); }
        using var staff = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Restaurant, ["RestaurantOwner"], [], restaurantId));
        var initial = await staff.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{supportCase.Id}");
        var close = await staff.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/actions/close",
            new { expectedVersion = initial!.Version, idempotencyKey = "staff-close-without-action", reason = "Confirmed fixed" });
        close.StatusCode.Should().Be(HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
        var closed = await close.Content.ReadFromJsonAsync<SupportCaseDto>();
        closed!.Status.Should().Be("Closed");
        var reopen = await staff.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/actions/reopen",
            new { action = "close", expectedVersion = closed.Version, idempotencyKey = "staff-reopen-url-action", reason = "Needs follow-up" });
        reopen.StatusCode.Should().Be(HttpStatusCode.OK, await reopen.Content.ReadAsStringAsync());
        (await reopen.Content.ReadFromJsonAsync<SupportCaseDto>())!.Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task GenericDriverRouteRejectsOperationalReportWithoutTouchingComplaintLifecycle()
    {
        using var driver = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Driver, ["Driver"], []));
        var response = await driver.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Driver, SupportCaseKind.OperationalReport, "delivery", "Late delivery", "Order was late.", "driver-op-1"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(SupportCaseProblemCodes.OperationalRouteRequired);
    }

    [Fact]
    public async Task RestaurantTaskViewExcludesRequesterComplaintTextAndNonTaskMessages()
    {
        var customerId = Guid.NewGuid();
        var restaurantOwnerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "order", "Private customer context", "Private complaint body", customerId,
                DateTime.UtcNow, restaurantId: restaurantId);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            var task = new SupportCaseTask { Id = taskId, SupportCaseId = caseId, Type = SupportCaseTaskType.RestaurantEvidence,
                RecipientType = SupportCaseTaskRecipientType.Restaurant, RestaurantId = restaurantId, Subject = "Confirm packing",
                Question = "Was this item packed?", CreatedByUserId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow };
            db.SupportCaseTasks.Add(task);
            db.SupportCaseMessages.AddRange(
                new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = customerId, Visibility = SupportCaseMessageVisibility.Participant,
                    Body = "Customer-only detail", CreatedAtUtc = DateTime.UtcNow },
                new SupportCaseMessage { SupportCaseId = caseId, SupportCaseTaskId = taskId, AuthorUserId = Guid.NewGuid(),
                    Visibility = SupportCaseMessageVisibility.Participant, Body = "Task-specific question", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var restaurant = _fixture.CreateClientForUser(new TokenUser(restaurantOwnerId, UserType.Restaurant, ["RestaurantOwner"], [], restaurantId));
        var response = await restaurant.GetAsync($"/api/v1/support/cases/{caseId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Was this item packed?");
        json.Should().Contain("Task-specific question");
        json.Should().NotContain("Private customer context");
        json.Should().NotContain("Private complaint body");
        json.Should().NotContain("Customer-only detail");
        json.Should().NotContain(customerId.ToString());
    }

    [Fact]
    public async Task RestaurantStaffCanReadOwnChannelCaseWithoutSeeingAnotherPartyTask()
    {
        var authorId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var supportCase = SupportCase.Create(SupportCaseChannel.Restaurant, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "restaurant", "Restaurant request subject", "Restaurant request details", authorId,
                DateTime.UtcNow, restaurantId: restaurantId);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            db.SupportCaseTasks.Add(new SupportCaseTask { Id = taskId, SupportCaseId = caseId, Type = SupportCaseTaskType.DeliveryAction,
                RecipientType = SupportCaseTaskRecipientType.DeliveryManager, RecipientUserId = Guid.NewGuid(), Subject = "Private dispatcher task",
                Question = "Private dispatcher question", CreatedByUserId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow });
            db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, SupportCaseTaskId = taskId,
                AuthorUserId = Guid.NewGuid(), Visibility = SupportCaseMessageVisibility.Participant,
                Body = "Private dispatcher message", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var staff = _fixture.CreateClientForUser(new TokenUser(staffId, UserType.Restaurant, ["RestaurantOwner"], [], restaurantId));
        var response = await staff.GetAsync($"/api/v1/support/cases/{caseId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Restaurant request subject");
        json.Should().Contain("Restaurant request details");
        json.Should().NotContain("Private dispatcher task");
        json.Should().NotContain("Private dispatcher question");
        json.Should().NotContain("Private dispatcher message");
    }

    [Fact]
    public async Task RestaurantStaffCanReplyToOwnChannelCaseButNotCustomerComplaintOnSameRestaurant()
    {
        var caseAuthor = Guid.NewGuid();
        var restaurantStaff = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var restaurantCaseId = Guid.NewGuid();
        var customerCaseId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var restaurantCase = SupportCase.Create(SupportCaseChannel.Restaurant, SupportCaseKind.GeneralRequest,
                SupportCaseOwnerTeam.PlatformSupport, "operations", "Restaurant request", "Restaurant team context", caseAuthor,
                DateTime.UtcNow, restaurantId: restaurantId);
            restaurantCase.Id = restaurantCaseId;
            var customerCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "order", "Customer request", "Customer private text", customerId,
                DateTime.UtcNow, restaurantId: restaurantId);
            customerCase.Id = customerCaseId;
            db.SupportCases.AddRange(restaurantCase, customerCase);
            db.SupportCaseTasks.Add(new SupportCaseTask { Id = taskId, SupportCaseId = customerCaseId,
                Type = SupportCaseTaskType.RestaurantEvidence, RecipientType = SupportCaseTaskRecipientType.Restaurant,
                RestaurantId = restaurantId, Subject = "Check item", Question = "Was it packed?",
                CreatedByUserId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var staff = _fixture.CreateClientForUser(new TokenUser(restaurantStaff, UserType.Restaurant, ["RestaurantOwner"], [], restaurantId));
        var ownCase = await staff.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{restaurantCaseId}");
        var ownReply = await staff.PostAsJsonAsync($"/api/v1/support/cases/{restaurantCaseId}/messages",
            new AddSupportMessageRequest("Restaurant staff follow-up", ownCase!.Version, "restaurant-channel-reply"));
        ownReply.StatusCode.Should().Be(HttpStatusCode.OK, await ownReply.Content.ReadAsStringAsync());

        var taskCase = await staff.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{customerCaseId}");
        taskCase.Should().NotBeNull();
        var unauthorizedReply = await staff.PostAsJsonAsync($"/api/v1/support/cases/{customerCaseId}/messages",
            new AddSupportMessageRequest("This must use the private task channel", taskCase!.Version, "customer-case-public-reply"));
        unauthorizedReply.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequesterDoesNotSeeRestaurantTaskThreadOrRecipientDetails()
    {
        var customerId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "customer-private-category", "Missing item", "Customer complaint text", customerId,
                DateTime.UtcNow, restaurantId: restaurantId);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            db.SupportCaseTasks.Add(new SupportCaseTask { Id = taskId, SupportCaseId = caseId, Type = SupportCaseTaskType.RestaurantEvidence,
                RecipientType = SupportCaseTaskRecipientType.Restaurant, RestaurantId = restaurantId, Subject = "Private restaurant task",
                Question = "Private restaurant question", CreatedByUserId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow });
            db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, SupportCaseTaskId = taskId,
                AuthorUserId = Guid.NewGuid(), Visibility = SupportCaseMessageVisibility.Participant,
                Body = "Restaurant private response", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var response = await customer.GetAsync($"/api/v1/support/cases/{caseId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("Private restaurant task");
        json.Should().NotContain("Private restaurant question");
        json.Should().NotContain("Restaurant private response");
        json.Should().Contain("customer-private-category");
    }

    [Fact]
    public async Task AdminCanAssignRestaurantAndDispatcherTasksSeparatelyAndDispatcherReplaySurvivesCompletion()
    {
        var customerId = Guid.NewGuid();
        var supportAdminId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        FixtureEntities testData;
        Guid restaurantId;
        var restaurantTaskId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            testData = await OrderLifecycleTestDataSeeder.SeedAsync(db, new DateOnly(2026, 10, 23));
            restaurantId = testData.RestaurantA.Id;
            customerId = testData.CustomerUser.Id;
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "private-category-label", "Private customer subject", "Private customer description", customerId,
                DateTime.UtcNow, restaurantId: restaurantId);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            db.SupportCaseTasks.Add(new SupportCaseTask { Id = restaurantTaskId, SupportCaseId = caseId,
                Type = SupportCaseTaskType.RestaurantEvidence, RecipientType = SupportCaseTaskRecipientType.Restaurant,
                RestaurantId = restaurantId, Subject = "Restaurant-only task", Question = "Confirm the item?",
                CreatedByUserId = supportAdminId, CreatedAtUtc = DateTime.UtcNow });
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new MealMate.Domain.IdentityAccess.UserScope { UserId = supportAdminId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(supportAdminId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        }

        var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var admin = _fixture.CreateClientForUser(new TokenUser(supportAdminId, UserType.Admin, ["SupportAdmin"],
            ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        var recipientsResponse = await admin.GetAsync($"/api/v1/support/cases/{caseId}/task-recipients");
        recipientsResponse.StatusCode.Should().Be(HttpStatusCode.OK, await recipientsResponse.Content.ReadAsStringAsync());
        var recipientRows = await recipientsResponse.Content.ReadFromJsonAsync<SupportCaseTaskRecipientDto[]>();
        recipientRows.Should().Contain(x => x.UserId == testData.DispatcherA.Id);

        var adminCase = await admin.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var invalidTask = await admin.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/create-party-task",
            new SupportCaseActionRequest("create-party-task", adminCase!.Version, "mismatched-task-recipient",
                TaskType: SupportCaseTaskType.DeliveryAction, RecipientType: SupportCaseTaskRecipientType.Restaurant,
                RestaurantId: restaurantId, TaskSubject: "Wrong type", TaskQuestion: "Not a restaurant review"));
        invalidTask.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var dispatcherTaskResponse = await admin.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/create-party-task",
            new SupportCaseActionRequest("create-party-task", adminCase!.Version, "parallel-dispatcher-task",
                TaskType: SupportCaseTaskType.DeliveryAction, RecipientType: SupportCaseTaskRecipientType.DeliveryManager,
                RestaurantId: restaurantId, RecipientUserId: testData.DispatcherA.Id,
                TaskSubject: "Dispatcher-only task", TaskQuestion: "Please investigate delivery status."));
        dispatcherTaskResponse.StatusCode.Should().Be(HttpStatusCode.OK, await dispatcherTaskResponse.Content.ReadAsStringAsync());
        var adminUpdated = await dispatcherTaskResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        adminUpdated!.Tasks.Should().HaveCount(2);
        var dispatcherTaskId = adminUpdated.Tasks.Single(x => x.Subject == "Dispatcher-only task").Id;

        var restaurant = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Restaurant, ["RestaurantOwner"], [], restaurantId));
        var restaurantView = await restaurant.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var privateCategoryFilter = await restaurant.GetFromJsonAsync<SupportCasePageDto>("/api/v1/support/cases?category=private-category-label");
        privateCategoryFilter!.Items.Should().NotContain(x => x.Id == caseId);
        restaurantView!.Category.Should().BeNull();
        System.Text.Json.JsonSerializer.Serialize(restaurantView).Should().NotContain("private-category-label");
        restaurantView.Subject.Should().Be("Restaurant-only task");
        restaurantView.Description.Should().BeNull();
        restaurantView.Tasks.Select(x => x.Id).Should().Equal(restaurantTaskId);
        restaurantView.Tasks.Should().NotContain(x => x.Subject == "Dispatcher-only task");

        var dispatcher = _fixture.CreateClientForUser(new TokenUser(testData.DispatcherA.Id, UserType.DeliveryManager,
            ["Dispatcher", "DeliveryManager"], [], restaurantId));
        var dispatcherView = await dispatcher.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        dispatcherView!.Category.Should().BeNull();
        System.Text.Json.JsonSerializer.Serialize(dispatcherView).Should().NotContain("private-category-label");
        dispatcherView.Subject.Should().Be("Dispatcher-only task");
        dispatcherView.Description.Should().BeNull();
        dispatcherView.Tasks.Select(x => x.Id).Should().Equal(dispatcherTaskId);
        dispatcherView.Tasks.Should().NotContain(x => x.Subject == "Restaurant-only task");
        var message = new AddSupportMessageRequest("Dispatcher checked the route.", dispatcherView.Version,
            "dispatcher-task-message", dispatcherTaskId);
        var sent = await dispatcher.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages", message);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var afterMessage = await sent.Content.ReadFromJsonAsync<SupportCaseDto>();
        var complete = await dispatcher.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/complete-party-task",
            new SupportCaseActionRequest("complete-party-task", afterMessage!.Version, "dispatcher-task-complete",
                TaskId: dispatcherTaskId, TaskResult: "Delivery status checked."));
        complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        var replay = await dispatcher.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages", message);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var replayed = await replay.Content.ReadFromJsonAsync<SupportCaseDto>();
        replayed!.Messages.Count(x => x.Body == "Dispatcher checked the route.").Should().Be(1);
        var newReply = await dispatcher.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages",
            message with { Body = "A new message after completion", IdempotencyKey = "dispatcher-completed-task-new-message" });
        newReply.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await dispatcher.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages",
            new AddSupportMessageRequest("Cannot reply to the restaurant task.", dispatcherView.Version, "dispatcher-wrong-task-message", restaurantTaskId)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await dispatcher.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/complete-party-task",
            new SupportCaseActionRequest("complete-party-task", dispatcherView.Version, "dispatcher-wrong-task-complete",
                TaskId: restaurantTaskId, TaskResult: "Unauthorized"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var currentCase = await admin.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var publicMessage = new AddSupportMessageRequest("The support team is reviewing this.", currentCase!.Version, "admin-public-reply");
        (await admin.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages", publicMessage)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages", publicMessage)).StatusCode.Should().Be(HttpStatusCode.OK);
        currentCase = await admin.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var privateNote = await admin.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/internal-notes",
            new AddSupportInternalNoteRequest("Internal-only investigation note.", currentCase!.Version, "admin-internal-note"));
        privateNote.StatusCode.Should().Be(HttpStatusCode.OK, await privateNote.Content.ReadAsStringAsync());
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var publicEvent = await db.SupportCaseEvents.SingleAsync(x => x.ActorUserId == supportAdminId && x.IdempotencyKey == "admin-public-reply");
            (await db.CustomerNotifications.CountAsync(x => x.UserId == customerId && x.SubjectId == publicEvent.Id)).Should().Be(1);
            (await db.CustomerNotifications.AnyAsync(x => x.UserId == customerId && x.BodyEn.Contains("Internal-only investigation note"))).Should().BeFalse();
            var latest = await admin.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
            var cancel = await admin.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/cancel-party-task",
                new SupportCaseActionRequest("cancel-party-task", latest!.Version, "admin-cancel-rest-task",
                    Reason: "The restaurant task is no longer needed.", TaskId: restaurantTaskId));
            cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
            var inbox = await db.CustomerNotifications.ToListAsync();
            inbox.Should().NotContain(x => x.SubjectId == Guid.Empty);
        }
        (await restaurant.GetAsync($"/api/v1/support/cases/{caseId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var manager = await db.Users.SingleAsync(x => x.Id == testData.DispatcherA.Id);
            manager.RestaurantId = testData.RestaurantB.Id;
            await db.SaveChangesAsync();
        }
        var movedDispatcher = _fixture.CreateClientForUser(new TokenUser(testData.DispatcherA.Id, UserType.DeliveryManager,
            ["Dispatcher", "DeliveryManager"], [], testData.RestaurantB.Id));
        (await movedDispatcher.GetAsync($"/api/v1/support/cases/{caseId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignedRestaurantCanCompleteItsPartyTaskWithCurrentVersion()
    {
        var customerId = Guid.NewGuid();
        var restaurantOwnerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "order", "Missing item", "Customer complaint", customerId,
                DateTime.UtcNow, restaurantId: restaurantId);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            db.SupportCaseTasks.Add(new SupportCaseTask { Id = taskId, SupportCaseId = caseId, Type = SupportCaseTaskType.RestaurantEvidence,
                RecipientType = SupportCaseTaskRecipientType.Restaurant, RestaurantId = restaurantId, Subject = "Confirm packing",
                Question = "Was it packed?", CreatedByUserId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var restaurant = _fixture.CreateClientForUser(new TokenUser(restaurantOwnerId, UserType.Restaurant, ["RestaurantOwner"], [], restaurantId));
        var detail = await restaurant.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        detail.Should().NotBeNull();
        var response = await restaurant.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/complete-party-task",
            new SupportCaseActionRequest("complete-party-task", detail!.Version, "task-completion-1", TaskId: taskId, TaskResult: "Packing confirmed"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("Completed");
    }

    [Fact]
    public async Task MessageReplayIsStableAndKeyReuseOrStaleVersionConflicts()
    {
        var actorId = Guid.NewGuid();
        using var customer = _fixture.CreateClientForUser(new TokenUser(actorId, UserType.Customer, ["Customer"], []));
        var create = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "order", "Missing item", "An item was missing.", "replay-case-1"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await create.Content.ReadFromJsonAsync<SupportCaseDto>();
        detail.Should().NotBeNull();
        var firstMessage = new AddSupportMessageRequest("Please check this.", detail!.Version, "message-key-1");
        var first = await customer.PostAsJsonAsync($"/api/v1/support/cases/{detail.Id}/messages", firstMessage);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await customer.PostAsJsonAsync($"/api/v1/support/cases/{detail.Id}/messages", firstMessage);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = await replay.Content.ReadFromJsonAsync<SupportCaseDto>();
        replayed!.Messages.Count(x => x.Body == "Please check this.").Should().Be(1);
        var reused = await customer.PostAsJsonAsync($"/api/v1/support/cases/{detail.Id}/messages", firstMessage with { Body = "Different payload" });
        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var stale = await customer.PostAsJsonAsync($"/api/v1/support/cases/{detail.Id}/messages",
            new AddSupportMessageRequest("Stale update", detail.Version, "message-key-2"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConcurrentSameKeyCreateAndMessageReplayExactlyOnce()
    {
        var actorId = Guid.NewGuid();
        using var customer = _fixture.CreateClientForUser(new TokenUser(actorId, UserType.Customer, ["Customer"], []));
        var request = new CreateSupportCaseRequest(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            "order", "Concurrent request", "Persist it once.", "concurrent-create-once");
        var createResponses = await Task.WhenAll(
            customer.PostAsJsonAsync("/api/v1/support/cases", request),
            customer.PostAsJsonAsync("/api/v1/support/cases", request));
        createResponses.Select(x => x.StatusCode).Should().OnlyContain(x => x == HttpStatusCode.Created);
        var createdCases = await Task.WhenAll(createResponses.Select(x => x.Content.ReadFromJsonAsync<SupportCaseDto>()));
        createdCases.Select(x => x!.Id).Distinct().Should().ContainSingle();
        var caseId = createdCases[0]!.Id;
        var details = await customer.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var message = new AddSupportMessageRequest("Concurrent message", details!.Version, "concurrent-message-once");
        var messageResponses = await Task.WhenAll(
            customer.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages", message),
            customer.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages", message));
        messageResponses.Select(x => x.StatusCode).Should().OnlyContain(x => x == HttpStatusCode.OK);
        var detail = await customer.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        detail!.Messages.Count(x => x.Body == "Concurrent message").Should().Be(1);
    }

    [Fact]
    public async Task SupportViewPermissionAloneCannotReplyEvenWhenCaseIsReadable()
    {
        var actorId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new MealMate.Domain.IdentityAccess.UserScope { UserId = actorId, AccessScopeId = scopeId });
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "account", "Locked account", "Cannot sign in", Guid.NewGuid(), DateTime.UtcNow);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(actorId, UserType.Admin, ["SupportAdmin"], ["support:view"], null, [scopeId]));
        }
        using var supportViewOnly = _fixture.CreateClientForUser(new TokenUser(actorId, UserType.Admin,
            ["SupportAdmin"], ["support:view"], null, [scopeId]));
        var response = await supportViewOnly.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages",
            new AddSupportMessageRequest("Not permitted", 1, "reply-1"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SupportManageWithoutReplyCannotPostParticipantTaskMessageButReplyOnlyCan()
    {
        var requesterId = Guid.NewGuid(); var manageOnlyId = Guid.NewGuid(); var replyOnlyId = Guid.NewGuid();
        var scopeId = Guid.NewGuid(); var caseId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "order", "Task permission test", "Private customer context", requesterId,
                DateTime.UtcNow, restaurantId: Guid.NewGuid());
            supportCase.Id = caseId; db.SupportCases.Add(supportCase);
            db.SupportCaseTasks.Add(new SupportCaseTask { Id = taskId, SupportCaseId = caseId, Type = SupportCaseTaskType.RestaurantEvidence,
                RecipientType = SupportCaseTaskRecipientType.Restaurant, RestaurantId = supportCase.RestaurantId!.Value,
                Subject = "Private task", Question = "Confirm this item.", CreatedByUserId = manageOnlyId, CreatedAtUtc = DateTime.UtcNow });
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.AddRange(new MealMate.Domain.IdentityAccess.UserScope { UserId = manageOnlyId, AccessScopeId = scopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = replyOnlyId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(manageOnlyId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.manage"], null, [scopeId]));
            await SupportTestIdentity.SeedAsync(db, new TokenUser(replyOnlyId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.reply"], null, [scopeId]));
        }
        using var manageOnly = _fixture.CreateClientForUser(new TokenUser(manageOnlyId, UserType.Admin, ["SupportAdmin"], ["support:view", "support.manage"], null, [scopeId]));
        using var replyOnly = _fixture.CreateClientForUser(new TokenUser(replyOnlyId, UserType.Admin, ["SupportAdmin"], ["support:view", "support.reply"], null, [scopeId]));
        var manageView = await manageOnly.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var denied = await manageOnly.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages",
            new AddSupportMessageRequest("Manage alone cannot send this.", manageView!.Version, "manage-only-task-message", taskId));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var replyView = await replyOnly.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var allowed = await replyOnly.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/messages",
            new AddSupportMessageRequest("Reply permission can send this.", replyView!.Version, "reply-only-task-message", taskId));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SupportManageWithoutReplyCanCompleteTaskAndRevokedManageCannotUseStaleToken()
    {
        var managerId = Guid.NewGuid();
        var revokedManagerId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var taskOneId = Guid.NewGuid();
        var taskTwoId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.AddRange(new MealMate.Domain.IdentityAccess.UserScope { UserId = managerId, AccessScopeId = scopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = revokedManagerId, AccessScopeId = scopeId });
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "order", "Task completion authorization", "Body", Guid.NewGuid(), DateTime.UtcNow);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            db.SupportCaseTasks.AddRange(
                new SupportCaseTask { Id = taskOneId, SupportCaseId = caseId, Type = SupportCaseTaskType.RestaurantEvidence,
                    RecipientType = SupportCaseTaskRecipientType.Restaurant, RestaurantId = Guid.NewGuid(), Subject = "Task one",
                    Question = "Confirm one", CreatedByUserId = managerId, CreatedAtUtc = DateTime.UtcNow },
                new SupportCaseTask { Id = taskTwoId, SupportCaseId = caseId, Type = SupportCaseTaskType.RestaurantEvidence,
                    RecipientType = SupportCaseTaskRecipientType.Restaurant, RestaurantId = Guid.NewGuid(), Subject = "Task two",
                    Question = "Confirm two", CreatedByUserId = managerId, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(managerId, UserType.Admin, ["SupportTaskManager"],
                ["support:view", "support.manage"], null, [scopeId]));
            await SupportTestIdentity.SeedAsync(db, new TokenUser(revokedManagerId, UserType.Admin, ["SupportTaskRevoked"],
                ["support:view", "support.manage"], null, [scopeId]));
        }

        using var manager = _fixture.CreateClientForUser(new TokenUser(managerId, UserType.Admin, ["SupportTaskManager"],
            ["support:view", "support.manage"], null, [scopeId]));
        using var revoked = _fixture.CreateClientForUser(new TokenUser(revokedManagerId, UserType.Admin, ["SupportTaskRevoked"],
            ["support:view", "support.manage"], null, [scopeId]));
        var managerCase = await manager.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var completed = await manager.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/complete-party-task",
            new SupportCaseActionRequest("complete-party-task", managerCase!.Version, "manage-complete-task",
                TaskId: taskOneId, TaskResult: "Confirmed"));
        completed.StatusCode.Should().Be(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());

        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var roleId = await db.UserRoles.Where(x => x.UserId == revokedManagerId).Select(x => x.RoleId).SingleAsync();
            var permissionId = await db.Permissions.Where(x => x.Code == "support.manage").Select(x => x.Id).SingleAsync();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            await db.SaveChangesAsync();
        }
        var revokedCase = await revoked.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var denied = await revoked.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/actions/complete-party-task",
            new SupportCaseActionRequest("complete-party-task", revokedCase!.Version, "revoked-manage-complete-task",
                TaskId: taskTwoId, TaskResult: "Should be denied"));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>());
        (await verify.SupportCaseTasks.SingleAsync(x => x.Id == taskTwoId)).Status.Should().Be(SupportCaseTaskStatus.Open);
    }

    [Fact]
    public async Task AssignmentTransferKeepsOwnerUntilDecisionAndReplaysAcceptWithoutDuplicateNotification()
    {
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.AddRange(new MealMate.Domain.IdentityAccess.UserScope { UserId = ownerId, AccessScopeId = scopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = targetId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(ownerId, UserType.Admin, ["TransferOwner"],
                ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
            await SupportTestIdentity.SeedAsync(db, new TokenUser(targetId, UserType.Admin, ["TransferTarget"],
                ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        }

        using var owner = _fixture.CreateClientForUser(new TokenUser(ownerId, UserType.Admin, ["TransferOwner"],
            ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        using var target = _fixture.CreateClientForUser(new TokenUser(targetId, UserType.Admin, ["TransferTarget"],
            ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        using var customer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        var create = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Transfer test", "Body", "transfer-owner-case"));
        var initial = await create.Content.ReadFromJsonAsync<SupportCaseDto>();
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var startedResponse = await owner.PostAsJsonAsync($"/api/v1/support/cases/{initial!.Id}/actions/start",
            new SupportCaseActionRequest("start", initial.Version, "transfer-start"));
        startedResponse.StatusCode.Should().Be(HttpStatusCode.OK, await startedResponse.Content.ReadAsStringAsync());
        var started = await startedResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        var requestResponse = await owner.PostAsJsonAsync($"/api/v1/support/cases/{initial.Id}/assignment-transfer-requests",
            new SupportCaseTransferRequest(targetId, "Move this case to the billing specialist.", started!.Version, "transfer-request"));
        requestResponse.StatusCode.Should().Be(HttpStatusCode.OK, await requestResponse.Content.ReadAsStringAsync());
        var pending = await requestResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        pending!.AssignedToUserId.Should().Be(ownerId);
        pending.PendingTransferToUserId.Should().Be(targetId);

        var acceptRequest = new SupportCaseTransferDecisionRequest(pending.Version, "transfer-accept");
        var acceptedResponse = await target.PostAsJsonAsync($"/api/v1/support/cases/{initial.Id}/assignment-transfer-requests/accept", acceptRequest);
        acceptedResponse.StatusCode.Should().Be(HttpStatusCode.OK, await acceptedResponse.Content.ReadAsStringAsync());
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        accepted!.AssignedToUserId.Should().Be(targetId);
        accepted.PendingTransferToUserId.Should().BeNull();

        var replayResponse = await target.PostAsJsonAsync($"/api/v1/support/cases/{initial.Id}/assignment-transfer-requests/accept", acceptRequest);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK, await replayResponse.Content.ReadAsStringAsync());
        var replayed = await replayResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        replayed!.AssignedToUserId.Should().Be(targetId);
        replayed.Version.Should().Be(accepted.Version);

        var requesterUpdate = new AddSupportMessageRequest("Additional requester detail", accepted.Version, "assigned-owner-update");
        var requesterMessage = await customer.PostAsJsonAsync($"/api/v1/support/cases/{initial.Id}/messages", requesterUpdate);
        requesterMessage.StatusCode.Should().Be(HttpStatusCode.OK, await requesterMessage.Content.ReadAsStringAsync());
        var messageCase = await requesterMessage.Content.ReadFromJsonAsync<SupportCaseDto>();
        var requesterReplay = await customer.PostAsJsonAsync($"/api/v1/support/cases/{initial.Id}/messages", requesterUpdate);
        requesterReplay.StatusCode.Should().Be(HttpStatusCode.OK, await requesterReplay.Content.ReadAsStringAsync());

        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>());
        var transferNotifications = await verify.AdminNotifications.Where(x => x.UserId == targetId || x.UserId == ownerId).ToListAsync();
        transferNotifications.Count(x => x.UserId == targetId && x.PayloadJson!.Contains("support.case.assignment.transfer-requested")).Should().Be(1);
        transferNotifications.Count(x => x.UserId == ownerId && x.PayloadJson!.Contains("support.case.assignment.transfer-accepted")).Should().Be(1);
        transferNotifications.Count(x => x.UserId == targetId && x.PayloadJson!.Contains("support.case.message")).Should().Be(1);
        transferNotifications.Should().NotContain(x => x.UserId == ownerId && x.PayloadJson!.Contains("support.case.message"));

        var createDecline = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Decline test", "Body", "transfer-decline-case"));
        var declineCase = await createDecline.Content.ReadFromJsonAsync<SupportCaseDto>();
        var declineStart = await owner.PostAsJsonAsync($"/api/v1/support/cases/{declineCase!.Id}/actions/start",
            new SupportCaseActionRequest("start", declineCase.Version, "decline-start"));
        var declineStarted = await declineStart.Content.ReadFromJsonAsync<SupportCaseDto>();
        var declineTransfer = await owner.PostAsJsonAsync($"/api/v1/support/cases/{declineCase.Id}/assignment-transfer-requests",
            new SupportCaseTransferRequest(targetId, "Request specialist review.", declineStarted!.Version, "decline-request"));
        var declinePending = await declineTransfer.Content.ReadFromJsonAsync<SupportCaseDto>();
        var blockedResolve = await owner.PostAsJsonAsync($"/api/v1/support/cases/{declineCase.Id}/actions/resolve",
            new SupportCaseActionRequest("resolve", declinePending!.Version, "resolve-while-transfer-pending", Reason: "Resolve after transfer."));
        blockedResolve.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var declineResponse = await target.PostAsJsonAsync($"/api/v1/support/cases/{declineCase.Id}/assignment-transfer-requests/decline",
            new SupportCaseTransferDecisionRequest(declinePending.Version, "decline-decision", "Keep with the current queue."));
        declineResponse.StatusCode.Should().Be(HttpStatusCode.OK, await declineResponse.Content.ReadAsStringAsync());
        var declined = await declineResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        declined!.AssignedToUserId.Should().Be(ownerId);
        declined.PendingTransferToUserId.Should().BeNull();
        var cancelTransfer = await owner.PostAsJsonAsync($"/api/v1/support/cases/{declineCase.Id}/assignment-transfer-requests",
            new SupportCaseTransferRequest(targetId, "Try a different handoff time.", declined.Version, "cancel-request"));
        var cancelPending = await cancelTransfer.Content.ReadFromJsonAsync<SupportCaseDto>();
        var cancelResponse = await owner.PostAsJsonAsync($"/api/v1/support/cases/{declineCase.Id}/assignment-transfer-requests/cancel",
            new SupportCaseTransferDecisionRequest(cancelPending!.Version, "cancel-decision"));
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK, await cancelResponse.Content.ReadAsStringAsync());
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        cancelled!.AssignedToUserId.Should().Be(ownerId);
        cancelled.PendingTransferToUserId.Should().BeNull();
        var resolveAfterCancel = await owner.PostAsJsonAsync($"/api/v1/support/cases/{declineCase.Id}/actions/resolve",
            new SupportCaseActionRequest("resolve", cancelled.Version, "resolve-after-cancel", Reason: "Transfer was cancelled."));
        resolveAfterCancel.StatusCode.Should().Be(HttpStatusCode.OK, await resolveAfterCancel.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InactiveAssignedOwnerWithPendingTransferCanBeTakenOverAndAudited()
    {
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var takeoverId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.AddRange(new MealMate.Domain.IdentityAccess.UserScope { UserId = ownerId, AccessScopeId = scopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = targetId, AccessScopeId = scopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = takeoverId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            foreach (var (id, role) in new[] { (ownerId, "InactiveOwner"), (targetId, "PendingTarget"), (takeoverId, "TakeoverOperator") })
                await SupportTestIdentity.SeedAsync(db, new TokenUser(id, UserType.Admin, [role],
                    ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        }

        var permissions = new[] { "support:view", "support.manage", "support.reply", "support.internal" };
        using var owner = _fixture.CreateClientForUser(new TokenUser(ownerId, UserType.Admin, ["InactiveOwner"], permissions, null, [scopeId]));
        using var target = _fixture.CreateClientForUser(new TokenUser(targetId, UserType.Admin, ["PendingTarget"], permissions, null, [scopeId]));
        using var takeover = _fixture.CreateClientForUser(new TokenUser(takeoverId, UserType.Admin, ["TakeoverOperator"], permissions, null, [scopeId]));
        using var customer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        var create = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Stranded owner", "Body", "takeover-case"));
        var initial = await create.Content.ReadFromJsonAsync<SupportCaseDto>();
        var start = await owner.PostAsJsonAsync($"/api/v1/support/cases/{initial!.Id}/actions/start",
            new SupportCaseActionRequest("start", initial.Version, "takeover-start"));
        var started = await start.Content.ReadFromJsonAsync<SupportCaseDto>();
        var transfer = await owner.PostAsJsonAsync($"/api/v1/support/cases/{initial.Id}/assignment-transfer-requests",
            new SupportCaseTransferRequest(targetId, "Specialist handoff pending.", started!.Version, "takeover-transfer"));
        var pending = await transfer.Content.ReadFromJsonAsync<SupportCaseDto>();
        pending!.PendingTransferToUserId.Should().Be(targetId);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var ownerUser = await db.Users.SingleAsync(x => x.Id == ownerId);
            ownerUser.AccountStatus = AccountStatus.Suspended;
            await db.SaveChangesAsync();
        }

        var takeoverCase = await takeover.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{initial.Id}");
        takeoverCase.Should().NotBeNull();
        takeoverCase!.CanTakeOverAssignment.Should().BeTrue();
        var response = await takeover.PostAsJsonAsync($"/api/v1/support/cases/{initial.Id}/actions/take-over-inactive-assignment",
            new SupportCaseActionRequest("take-over-inactive-assignment", takeoverCase.Version, "takeover-owner", Reason: "Previous owner is inactive."));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var taken = await response.Content.ReadFromJsonAsync<SupportCaseDto>();
        taken!.AssignedToUserId.Should().Be(takeoverId);
        taken.PendingTransferToUserId.Should().BeNull();
        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>());
        (await verify.SupportCaseEvents.AnyAsync(x => x.SupportCaseId == initial.Id && x.EventType == "take-over-inactive-assignment"
            && x.FromValue == ownerId.ToString("D") && x.ToValue == takeoverId.ToString("D"))).Should().BeTrue();
        (await verify.SupportCaseMessages.AnyAsync(x => x.SupportCaseId == initial.Id && x.Visibility == SupportCaseMessageVisibility.Internal
            && x.Body == "Previous owner is inactive.")).Should().BeTrue();

        var createScopeRevoked = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Revoked scope owner", "Body", "takeover-revoked-scope-case"));
        var scopeCase = await createScopeRevoked.Content.ReadFromJsonAsync<SupportCaseDto>();
        var scopeStart = await target.PostAsJsonAsync($"/api/v1/support/cases/{scopeCase!.Id}/actions/start",
            new SupportCaseActionRequest("start", scopeCase.Version, "revoked-scope-start"));
        var scopeStarted = await scopeStart.Content.ReadFromJsonAsync<SupportCaseDto>();
        var scopeTransfer = await target.PostAsJsonAsync($"/api/v1/support/cases/{scopeCase.Id}/assignment-transfer-requests",
            new SupportCaseTransferRequest(takeoverId, "Temporary backup coverage.", scopeStarted!.Version, "revoked-scope-transfer"));
        var scopePending = await scopeTransfer.Content.ReadFromJsonAsync<SupportCaseDto>();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.UserScopes.Remove(await db.UserScopes.SingleAsync(x => x.UserId == targetId && x.AccessScopeId == scopeId));
            await db.SaveChangesAsync();
        }
        var scopeTakeoverCase = await takeover.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{scopeCase.Id}");
        scopeTakeoverCase!.CanTakeOverAssignment.Should().BeTrue();
        var scopeTakeover = await takeover.PostAsJsonAsync($"/api/v1/support/cases/{scopeCase.Id}/actions/take-over-inactive-assignment",
            new SupportCaseActionRequest("take-over-inactive-assignment", scopeTakeoverCase.Version, "takeover-scope-owner", Reason: "Current owner lost case scope."));
        scopeTakeover.StatusCode.Should().Be(HttpStatusCode.OK, await scopeTakeover.Content.ReadAsStringAsync());
        var scopeTaken = await scopeTakeover.Content.ReadFromJsonAsync<SupportCaseDto>();
        scopeTaken!.AssignedToUserId.Should().Be(takeoverId);
        scopeTaken.PendingTransferToUserId.Should().BeNull();
    }

    [Fact]
    public async Task OpenDispatcherTaskCanBeReassignedToCurrentManagerAndRevokesOldManagerAccess()
    {
        var supportAdminId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        FixtureEntities testData;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            testData = await OrderLifecycleTestDataSeeder.SeedAsync(db, new DateOnly(2026, 10, 23));
            db.Users.Add(new MealMate.Persistence.Identity.ApplicationUser
            {
                Id = newManagerId, UserType = UserType.DeliveryManager, AccountStatus = AccountStatus.Active,
                RestaurantId = testData.RestaurantA.Id, UserName = $"support-reassign-{newManagerId:N}",
                NormalizedUserName = $"SUPPORT-REASSIGN-{newManagerId:N}",
            });
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new MealMate.Domain.IdentityAccess.UserScope { UserId = supportAdminId, AccessScopeId = scopeId });
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "delivery", "Dispatcher reassignment", "Body", testData.CustomerUser.Id,
                now, restaurantId: testData.RestaurantA.Id);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            db.SupportCaseTasks.Add(new SupportCaseTask
            {
                Id = taskId, SupportCaseId = caseId, Type = SupportCaseTaskType.DeliveryAction,
                RecipientType = SupportCaseTaskRecipientType.DeliveryManager, RecipientUserId = testData.DispatcherA.Id,
                RestaurantId = testData.RestaurantA.Id, Subject = "Investigate driver", Question = "Check the route.",
                CreatedByUserId = supportAdminId, CreatedAtUtc = now.AddHours(-3), DueAtUtc = now.AddHours(5),
            });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(supportAdminId, UserType.Admin, ["TaskReassignAdmin"],
                ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        }

        using var admin = _fixture.CreateClientForUser(new TokenUser(supportAdminId, UserType.Admin, ["TaskReassignAdmin"],
            ["support:view", "support.manage", "support.reply", "support.internal"], null, [scopeId]));
        using var oldManager = _fixture.CreateClientForUser(new TokenUser(testData.DispatcherA.Id, UserType.DeliveryManager,
            ["Dispatcher", "DeliveryManager"], [], testData.RestaurantA.Id));
        using var newManager = _fixture.CreateClientForUser(new TokenUser(newManagerId, UserType.DeliveryManager,
            ["Dispatcher", "DeliveryManager"], [], testData.RestaurantA.Id));
        var oldView = await oldManager.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        oldView.Should().NotBeNull();
        var originalTask = oldView!.Tasks.Single(x => x.Id == taskId);
        var adminView = await admin.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        var response = await admin.PostAsJsonAsync($"/api/v1/support/cases/{caseId}/tasks/{taskId}/reassign",
            new ReassignSupportTaskRequest(newManagerId, "The current dispatcher is unavailable.", adminView!.Version, "dispatcher-reassign"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = await response.Content.ReadFromJsonAsync<SupportCaseDto>();
        var movedTask = updated!.Tasks.Single(x => x.Id == taskId);
        movedTask.RecipientUserId.Should().Be(newManagerId);
        movedTask.CreatedAtUtc.Should().Be(originalTask.CreatedAtUtc);
        movedTask.DueAtUtc.Should().Be(originalTask.DueAtUtc);
        (await oldManager.GetAsync($"/api/v1/support/cases/{caseId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var newView = await newManager.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{caseId}");
        newView.Should().NotBeNull();
        newView!.Tasks.Single().Subject.Should().Be("Investigate driver");
        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>());
        (await verify.SupportCaseTasks.SingleAsync(x => x.Id == taskId)).RecipientUserId.Should().Be(newManagerId);
    }

    [Fact]
    public async Task CountryOnlyTriageWithoutRegionDoesNotGuessBetweenTenants()
    {
        var countryId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantScopedUser = Guid.NewGuid();
        var countryScopedUser = Guid.NewGuid();
        var tenantScopeId = Guid.NewGuid();
        var countryScopeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var supportCaseId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            db.Countries.Add(new MealMate.Domain.Foundation.Country { Id = countryId, Code = $"C{suffix[..2]}", NameAr = "بلد", NameEn = "Country", IsActive = true });
            db.Tenants.AddRange(
                new MealMate.Domain.Foundation.Tenant { Id = tenantA, CountryId = countryId, Code = $"tenant-a-{suffix}", NameAr = "مستأجر أ", NameEn = "Tenant A",
                    Domain = $"tenant-a-{suffix}.example.test", CurrencyCode = "KWD", TimeZoneId = "UTC", Status = MealMate.Domain.Enums.TenantStatus.Active, IsActive = true },
                new MealMate.Domain.Foundation.Tenant { Id = tenantB, CountryId = countryId, Code = $"tenant-b-{suffix}", NameAr = "مستأجر ب", NameEn = "Tenant B",
                    Domain = $"tenant-b-{suffix}.example.test", CurrencyCode = "KWD", TimeZoneId = "UTC", Status = MealMate.Domain.Enums.TenantStatus.Active, IsActive = true });
            db.AccessScopes.AddRange(
                new MealMate.Domain.IdentityAccess.AccessScope { Id = tenantScopeId, Name = "Tenant A only", ScopeType = AccessScopeType.Tenant, TenantId = tenantA },
                new MealMate.Domain.IdentityAccess.AccessScope { Id = countryScopeId, Name = "Country coverage", ScopeType = AccessScopeType.Country, CountryId = countryId });
            db.UserScopes.AddRange(new MealMate.Domain.IdentityAccess.UserScope { UserId = tenantScopedUser, AccessScopeId = tenantScopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = countryScopedUser, AccessScopeId = countryScopeId });
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest,
                SupportCaseOwnerTeam.PlatformSupport, "account", "Country triage", "Country known, region unavailable", requesterId,
                DateTime.UtcNow, triageCountryId: countryId);
            supportCase.Id = supportCaseId;
            db.SupportCases.Add(supportCase);
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(tenantScopedUser, UserType.Admin, ["TenantSupport"],
                ["support:view", "support.manage"], null, [tenantScopeId]));
            await SupportTestIdentity.SeedAsync(db, new TokenUser(countryScopedUser, UserType.Admin, ["CountrySupport"],
                ["support:view", "support.manage"], null, [countryScopeId]));
        }

        using var tenantOperator = _fixture.CreateClientForUser(new TokenUser(tenantScopedUser, UserType.Admin, ["TenantSupport"],
            ["support:view", "support.manage"], null, [tenantScopeId]));
        using var countryOperator = _fixture.CreateClientForUser(new TokenUser(countryScopedUser, UserType.Admin, ["CountrySupport"],
            ["support:view", "support.manage"], null, [countryScopeId]));
        (await tenantOperator.GetAsync($"/api/v1/support/cases/{supportCaseId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await countryOperator.GetAsync($"/api/v1/support/cases/{supportCaseId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await countryOperator.GetFromJsonAsync<SupportCasePageDto>("/api/v1/support/cases"))!.Items.Should().ContainSingle(x => x.Id == supportCaseId);
    }

    [Fact]
    public async Task CustomerProfileLocationDrivesRegionalTriageAndUnknownLocationUsesGlobalQueue()
    {
        var country = new MealMate.Domain.Foundation.Country
        {
            Code = $"{Guid.NewGuid():N}"[..3].ToUpperInvariant(), NameAr = "بلد", NameEn = "Country", DialCode = "+999", FlagEmoji = "🏳",
        };
        var tenant = new MealMate.Domain.Foundation.Tenant
        {
            CountryId = country.Id, Code = $"T{Guid.NewGuid():N}"[..8], NameAr = "مستأجر", NameEn = "Tenant",
            Domain = $"{Guid.NewGuid():N}.support.test", CurrencyCode = "KWD", TimeZoneId = "UTC",
            Status = MealMate.Domain.Enums.TenantStatus.Active, IsActive = true,
        };
        var parent = new MealMate.Domain.Foundation.Region
        {
            CountryId = country.Id, Code = $"P{Guid.NewGuid():N}"[..8], NameAr = "محافظة", NameEn = "Province",
            Type = MealMate.Domain.Enums.RegionType.Governorate,
        };
        var customerRegion = new MealMate.Domain.Foundation.Region
        {
            CountryId = country.Id, ParentRegionId = parent.Id, Code = $"R{Guid.NewGuid():N}"[..8], NameAr = "منطقة", NameEn = "Area",
            Type = MealMate.Domain.Enums.RegionType.Area,
        };
        var foreignRegion = new MealMate.Domain.Foundation.Region
        {
            CountryId = country.Id, Code = $"F{Guid.NewGuid():N}"[..8], NameAr = "أخرى", NameEn = "Other",
            Type = MealMate.Domain.Enums.RegionType.Governorate,
        };
        var locatedCustomerId = Guid.NewGuid();
        var unknownCustomerId = Guid.NewGuid();
        var parentAdminId = Guid.NewGuid();
        var foreignAdminId = Guid.NewGuid();
        var globalAdminId = Guid.NewGuid();
        var parentScopeId = Guid.NewGuid();
        var foreignScopeId = Guid.NewGuid();
        var globalScopeId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.Countries.Add(country);
            db.Tenants.Add(tenant);
            db.Regions.AddRange(parent, customerRegion, foreignRegion);
            db.CustomerProfiles.AddRange(
                new MealMate.Domain.IdentityAccess.CustomerProfile { UserId = locatedCustomerId, DefaultRegionId = customerRegion.Id },
                new MealMate.Domain.IdentityAccess.CustomerProfile { UserId = unknownCustomerId });
            db.AccessScopes.AddRange(
                new MealMate.Domain.IdentityAccess.AccessScope { Id = parentScopeId, Name = "Parent region", ScopeType = AccessScopeType.Region,
                    CountryId = country.Id, TenantId = tenant.Id, RegionId = parent.Id },
                new MealMate.Domain.IdentityAccess.AccessScope { Id = foreignScopeId, Name = "Other region", ScopeType = AccessScopeType.Region,
                    CountryId = country.Id, TenantId = tenant.Id, RegionId = foreignRegion.Id },
                new MealMate.Domain.IdentityAccess.AccessScope { Id = globalScopeId, Name = "Global triage", ScopeType = AccessScopeType.Global });
            db.UserScopes.AddRange(new MealMate.Domain.IdentityAccess.UserScope { UserId = parentAdminId, AccessScopeId = parentScopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = foreignAdminId, AccessScopeId = foreignScopeId },
                new MealMate.Domain.IdentityAccess.UserScope { UserId = globalAdminId, AccessScopeId = globalScopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(parentAdminId, UserType.Admin, ["RegionSupport"],
                ["support:view", "support.reply", "support.manage"], null, [parentScopeId]));
            await SupportTestIdentity.SeedAsync(db, new TokenUser(foreignAdminId, UserType.Admin, ["ForeignRegionSupport"],
                ["support:view", "support.reply"], null, [foreignScopeId]));
            await SupportTestIdentity.SeedAsync(db, new TokenUser(globalAdminId, UserType.Admin, ["GlobalSupport"],
                ["support:view", "support.reply", "support.manage"], null, [globalScopeId]));
        }

        using var locatedCustomer = _fixture.CreateClientForUser(new TokenUser(locatedCustomerId, UserType.Customer, ["Customer"], []));
        using var unknownCustomer = _fixture.CreateClientForUser(new TokenUser(unknownCustomerId, UserType.Customer, ["Customer"], []));
        using var parentAdmin = _fixture.CreateClientForUser(new TokenUser(parentAdminId, UserType.Admin, ["RegionSupport"],
            ["support:view", "support.reply", "support.manage"], null, [parentScopeId]));
        using var foreignAdmin = _fixture.CreateClientForUser(new TokenUser(foreignAdminId, UserType.Admin, ["ForeignRegionSupport"],
            ["support:view", "support.reply"], null, [foreignScopeId]));
        using var globalAdmin = _fixture.CreateClientForUser(new TokenUser(globalAdminId, UserType.Admin, ["GlobalSupport"],
            ["support:view", "support.reply", "support.manage"], null, [globalScopeId]));
        var knownResponse = await locatedCustomer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Located request", "Body", "located-triage-case"));
        knownResponse.StatusCode.Should().Be(HttpStatusCode.Created, await knownResponse.Content.ReadAsStringAsync());
        var knownCase = await knownResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        knownCase!.TriageCountryId.Should().BeNull();
        knownCase.TriageRegionId.Should().BeNull();
        var parentList = await parentAdmin.GetFromJsonAsync<SupportCasePageDto>("/api/v1/support/cases");
        parentList!.Items.Should().ContainSingle(x => x.Id == knownCase.Id);
        (await foreignAdmin.GetAsync($"/api/v1/support/cases/{knownCase.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var unknownResponse = await unknownCustomer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Unknown location", "Body", "unknown-triage-case"));
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.Created, await unknownResponse.Content.ReadAsStringAsync());
        var unknownCase = await unknownResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        unknownCase!.TriageStatus.Should().Be("GlobalTriage");
        unknownCase.TriageCountryId.Should().BeNull();
        (await parentAdmin.GetAsync($"/api/v1/support/cases/{unknownCase.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await globalAdmin.GetAsync($"/api/v1/support/cases/{unknownCase.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>());
        var persistedKnownCase = await verify.SupportCases.SingleAsync(x => x.Id == knownCase.Id);
        persistedKnownCase.TriageCountryId.Should().Be(country.Id);
        persistedKnownCase.TriageRegionId.Should().Be(customerRegion.Id);
        var notifications = await verify.AdminNotifications.Where(x => x.UserId == parentAdminId || x.UserId == foreignAdminId || x.UserId == globalAdminId).ToListAsync();
        notifications.Should().Contain(x => x.UserId == parentAdminId && x.PayloadJson!.Contains(knownCase.Id.ToString("D")));
        notifications.Should().NotContain(x => x.UserId == foreignAdminId);
        notifications.Should().Contain(x => x.UserId == globalAdminId && x.PayloadJson!.Contains(unknownCase.Id.ToString("D")));
    }

    [Fact]
    public async Task SupportViewWithoutScopeCannotReadGlobalCase()
    {
        var actorId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "account", "Locked account", "Cannot sign in", Guid.NewGuid(), DateTime.UtcNow);
            supportCase.Id = caseId;
            db.SupportCases.Add(supportCase);
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(actorId, UserType.Admin, ["SupportAdmin"], ["support:view"]));
        }
        using var support = _fixture.CreateClientForUser(new TokenUser(actorId, UserType.Admin, ["SupportAdmin"], ["support:view"]));
        (await support.GetAsync($"/api/v1/support/cases/{caseId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OwnedSubscriptionDayCanCreateCaseAndRoutePrivateRestaurantTask()
    {
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var dayId = Guid.NewGuid();
        var supportAdminId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = $"support-{Guid.NewGuid():N}", NameAr = "خطة", NameEn = "Plan" };
            var bundle = new Bundle { ProgramId = program.Id, Code = $"support-{Guid.NewGuid():N}", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
            var subscription = new Subscription { CustomerUserId = customerId, ProgramId = program.Id, BundleId = bundle.Id,
                Status = SubscriptionStatus.Active, PaidAmount = new Money(10m, "KWD"), StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31), DurationDays = 26 };
            var day = new SubscriptionDay { Id = dayId, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
                ServiceDate = new DateOnly(2026, 10, 23) };
            db.Programs.Add(program);
            db.Bundles.Add(bundle);
            db.Subscriptions.Add(subscription);
            db.SubscriptionDays.Add(day);
            db.RestaurantProfiles.Add(new MealMate.Domain.IdentityAccess.RestaurantProfile { Id = restaurantId,
                TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
                VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true });
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new MealMate.Domain.IdentityAccess.UserScope { UserId = supportAdminId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(supportAdminId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.manage", "support.reply"], null, [scopeId]));
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        using var unrelatedCustomer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        var unauthorizedResource = await unrelatedCustomer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "delivery", "Unowned resource", "Should not resolve.",
            "unowned-day-case", "SubscriptionDay", dayId));
        unauthorizedResource.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var create = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "delivery", "Missing delivery item", "The meal was missing.",
            "owned-day-case", "SubscriptionDay", dayId));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var customerCase = await create.Content.ReadFromJsonAsync<SupportCaseDto>();
        customerCase.Should().NotBeNull();
        customerCase!.RelatedResourceType.Should().Be("SubscriptionDay");
        customerCase.RelatedResourceId.Should().Be(dayId);

        using var support = _fixture.CreateClientForUser(new TokenUser(supportAdminId, UserType.Admin, ["SupportAdmin"],
            ["support:view", "support.manage", "support.reply"], null, [scopeId]));
        var adminCase = await support.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{customerCase.Id}");
        adminCase.Should().NotBeNull();
        adminCase!.RestaurantId.Should().Be(restaurantId);
        var taskResponse = await support.PostAsJsonAsync($"/api/v1/support/cases/{customerCase.Id}/actions/create-party-task",
            new SupportCaseActionRequest("create-party-task", adminCase.Version, "owned-day-task", TaskType: SupportCaseTaskType.RestaurantEvidence,
                RecipientType: SupportCaseTaskRecipientType.Restaurant, RestaurantId: restaurantId,
                TaskSubject: "Check packing", TaskQuestion: "Was this item packed?"));
        taskResponse.StatusCode.Should().Be(HttpStatusCode.OK, await taskResponse.Content.ReadAsStringAsync());
        var adminUpdated = await taskResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        var taskId = adminUpdated!.Tasks.Single().Id;

        using var restaurant = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Restaurant, ["RestaurantOwner"], [], restaurantId));
        var taskView = await restaurant.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{customerCase.Id}");
        taskView.Should().NotBeNull();
        taskView!.Subject.Should().Be("Check packing");
        taskView.Description.Should().BeNull();
        var messageResponse = await restaurant.PostAsJsonAsync($"/api/v1/support/cases/{customerCase.Id}/messages",
            new AddSupportMessageRequest("We packed it.", taskView.Version, "owned-day-task-message", taskId));
        messageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var restaurantUpdated = await messageResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        var completeResponse = await restaurant.PostAsJsonAsync($"/api/v1/support/cases/{customerCase.Id}/actions/complete-party-task",
            new SupportCaseActionRequest("complete-party-task", restaurantUpdated!.Version, "owned-day-task-complete",
                TaskId: taskId, TaskResult: "Packing confirmed"));
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var customerUpdated = await customer.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{customerCase.Id}");
        customerUpdated!.Tasks.Should().BeEmpty();
        customerUpdated.Messages.Should().NotContain(x => x.Body == "We packed it.");
    }

    [Fact]
    public async Task RequestInformationResolveCloseAndReopenFollowExplicitLifecycle()
    {
        var customerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.AccessScopes.Add(new MealMate.Domain.IdentityAccess.AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new MealMate.Domain.IdentityAccess.UserScope { UserId = adminId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(adminId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.manage", "support.reply"], null, [scopeId]));
        }
        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        using var admin = _fixture.CreateClientForUser(new TokenUser(adminId, UserType.Admin, ["SupportAdmin"],
            ["support:view", "support.manage", "support.reply"], null, [scopeId]));
        var created = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Need help", "Please help me.", "lifecycle-create"));
        var supportCase = await created.Content.ReadFromJsonAsync<SupportCaseDto>();
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var start = await admin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase!.Id}/actions/start",
            new { expectedVersion = supportCase.Version, idempotencyKey = "lifecycle-start" });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        var started = await start.Content.ReadFromJsonAsync<SupportCaseDto>();
        started!.Status.Should().Be("InProgress");

        var requestInfo = await admin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/actions/request-information",
            new SupportCaseActionRequest("request-information", started.Version, "lifecycle-info", Reason: "Please share the account email."));
        var waiting = await requestInfo.Content.ReadFromJsonAsync<SupportCaseDto>();
        waiting!.Status.Should().Be("WaitingOnRequester");
        var reply = await customer.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/messages",
            new AddSupportMessageRequest("I used customer@example.test.", waiting.Version, "lifecycle-reply"));
        var inProgress = await reply.Content.ReadFromJsonAsync<SupportCaseDto>();
        inProgress!.Status.Should().Be("InProgress");

        var resolve = await admin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/actions/resolve",
            new SupportCaseActionRequest("resolve", inProgress.Version, "lifecycle-resolve", Reason: "Account access restored."));
        var resolved = await resolve.Content.ReadFromJsonAsync<SupportCaseDto>();
        resolved!.Status.Should().Be("Resolved");
        var close = await customer.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/actions/close",
            new SupportCaseActionRequest("close", resolved.Version, "lifecycle-close"));
        var closed = await close.Content.ReadFromJsonAsync<SupportCaseDto>();
        closed!.Status.Should().Be("Closed");
        var closedReply = await customer.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/messages",
            new AddSupportMessageRequest("This should wait for an explicit reopen.", closed.Version, "lifecycle-closed-reply"));
        closedReply.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var reopen = await customer.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/actions/reopen",
            new SupportCaseActionRequest("reopen", closed.Version, "lifecycle-reopen", Reason: "The issue returned."));
        reopen.StatusCode.Should().Be(HttpStatusCode.OK);
        var reopened = await reopen.Content.ReadFromJsonAsync<SupportCaseDto>();
        reopened!.Status.Should().Be("InProgress");
        var reopenedReply = await customer.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/messages",
            new AddSupportMessageRequest("The issue returned.", reopened.Version, "lifecycle-reopened-reply"));
        reopenedReply.StatusCode.Should().Be(HttpStatusCode.OK, await reopenedReply.Content.ReadAsStringAsync());
    }
}
