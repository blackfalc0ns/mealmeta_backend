using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Application.Features.UnifiedSupport.Timing;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MealMate.ApiTests.Support;

public sealed class SupportTimingHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;

    public SupportTimingHttpTests(OrderLifecycleEndToEndFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TimingRoutesRequireAuthentication()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        var response = await anonymous.GetAsync("/api/v1/support/timing-policies");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NonAdminCannotPublishTimingPolicy()
    {
        var customerId = Guid.NewGuid();
        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));

        var request = new PublishSupportTimingPolicyRequest(
            "Customer Policy",
            "Description",
            "Unauthorized publish attempt",
            Guid.NewGuid().ToString("N"),
            1,
            [
                new CreateSupportTimingRuleDto(
                    SupportCaseChannel.Customer,
                    SupportCasePriority.P1,
                    "General",
                    30,
                    240,
                    60,
                    false,
                    1)
            ]);

        var response = await customer.PostAsJsonAsync("/api/v1/support/timing-policies/publish", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PublishedPolicyRequiresPersistedSuperAdminRoleEvenWhenTokenClaimsSuperAdmin()
    {
        var actorId = Guid.NewGuid();
        var persistedIdentity = new TokenUser(actorId, UserType.Admin, ["SupportOperator"],
            [PermissionCodes.SupportManage, PermissionCodes.SupportView]);
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            await SupportTestIdentity.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), persistedIdentity);
        }

        using var staleSuperAdmin = _fixture.CreateClientForUser(new TokenUser(actorId, UserType.Admin,
            [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]));
        var request = new PublishSupportTimingPolicyRequest(
            "Rejected stale role",
            null,
            "Role must come from persistence",
            Guid.NewGuid().ToString("N"),
            0,
            [new CreateSupportTimingRuleDto(SupportCaseChannel.Customer, null, null, 30, 240, 60, false, 1)]);

        var response = await staleSuperAdmin.PostAsJsonAsync("/api/v1/support/timing-policies/publish", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SupportViewerWithoutPersistedSupportTimingRoleCannotReadPolicyCatalog()
    {
        var actorId = Guid.NewGuid();
        var persistedIdentity = new TokenUser(actorId, UserType.Admin, [$"PolicyDenied{actorId:N}"], []);
        Guid policyId;
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SupportTestIdentity.SeedAsync(db, persistedIdentity);
            var version = (await db.SupportTimingPolicies.MaxAsync(policy => (int?)policy.Version) ?? 0) + 1;
            var policy = SupportTimingPolicy.Create(version, "Policy access test", null, Guid.NewGuid(), "Test fixture",
                Guid.NewGuid().ToString("N"), DateTime.UtcNow);
            policy.AddRule(SupportCaseChannel.Customer, null, null, TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5), false, 0);
            policyId = policy.Id;
            db.SupportTimingPolicies.Add(policy);
            await db.SaveChangesAsync();
        }

        using var supportViewer = _fixture.CreateClientForUser(new TokenUser(actorId, UserType.Admin,
            [$"PolicyDenied{actorId:N}"], [PermissionCodes.SupportView]));
        var responses = await Task.WhenAll(
            supportViewer.GetAsync("/api/v1/support/timing-policies"),
            supportViewer.GetAsync("/api/v1/support/timing-policies/active"),
            supportViewer.GetAsync($"/api/v1/support/timing-policies/{policyId:D}"));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PersistedSupportViewerCanReadActiveAndHistoryPolicies()
    {
        var viewerId = Guid.NewGuid();
        var viewer = new TokenUser(viewerId, UserType.Admin, [$"PolicyViewer{viewerId:N}"], [PermissionCodes.SupportView]);
        Guid policyId;
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SupportTestIdentity.SeedAsync(db, viewer);
            var version = (await db.SupportTimingPolicies.MaxAsync(policy => (int?)policy.Version) ?? 0) + 1;
            var policy = SupportTimingPolicy.Create(version, "Policy access test", null, Guid.NewGuid(), "Test fixture",
                Guid.NewGuid().ToString("N"), DateTime.UtcNow);
            policy.AddRule(SupportCaseChannel.Customer, null, null, TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5), false, 0);
            policyId = policy.Id;
            db.SupportTimingPolicies.Add(policy);
            await db.SaveChangesAsync();
        }
        using var supportViewer = _fixture.CreateClientForUser(viewer);
        var responses = await Task.WhenAll(
            supportViewer.GetAsync("/api/v1/support/timing-policies"),
            supportViewer.GetAsync("/api/v1/support/timing-policies/active"),
            supportViewer.GetAsync($"/api/v1/support/timing-policies/{policyId:D}"));
        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentSuperAdminPublicationsForTheSameNextVersionReturnOneConflict()
    {
        var superAdminA = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin],
            [PermissionCodes.SupportManage, PermissionCodes.SupportView]);
        var superAdminB = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin],
            [PermissionCodes.SupportManage, PermissionCodes.SupportView]);
        int expectedVersion;
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SupportTestIdentity.SeedAsync(db, superAdminA);
            await SupportTestIdentity.SeedAsync(db, superAdminB);
            expectedVersion = (await db.SupportTimingPolicies.MaxAsync(policy => (int?)policy.Version) ?? 0) + 1;
        }

        using var adminA = _fixture.CreateClientForUser(superAdminA);
        using var adminB = _fixture.CreateClientForUser(superAdminB);
        var requests = new[]
        {
            new PublishSupportTimingPolicyRequest("Concurrent policy A", null, "Concurrent publication test",
                Guid.NewGuid().ToString("N"), expectedVersion,
                [new CreateSupportTimingRuleDto(null, null, null, 10, 20, 5, false, 0)]),
            new PublishSupportTimingPolicyRequest("Concurrent policy B", null, "Concurrent publication test",
                Guid.NewGuid().ToString("N"), expectedVersion,
                [new CreateSupportTimingRuleDto(null, null, null, 10, 20, 5, false, 0)]),
        };

        var responses = await Task.WhenAll(
            adminA.PostAsJsonAsync("/api/v1/support/timing-policies/publish", requests[0]),
            adminB.PostAsJsonAsync("/api/v1/support/timing-policies/publish", requests[1]));

        responses.Select(response => response.StatusCode).Should().ContainSingle(status => status == HttpStatusCode.OK);
        responses.Select(response => response.StatusCode).Should().ContainSingle(status => status == HttpStatusCode.Conflict);
        await using var verify = _fixture.CreateDbContext();
        (await verify.SupportTimingPolicies.CountAsync(policy => policy.Version == expectedVersion)).Should().Be(1);
        (await verify.SupportTimingPolicies.CountAsync(policy => policy.IsActive)).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentPostgresWorkersEscalateOneCaseOnce()
    {
        var admin = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]);
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
            await SupportTestIdentity.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), admin);

        var startedAt = DateTime.UtcNow.AddHours(-5);
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "order", "Concurrent timing", "Please review this case.", Guid.NewGuid(),
            startedAt);
        var cycle = SupportCaseTimingCycle.StartConfigured(supportCase.Id, 1, Guid.NewGuid(), 1, Guid.NewGuid(), startedAt,
            startedAt.AddHours(1), startedAt.AddHours(4), TimeSpan.FromHours(2));
        supportCase.SetActiveTimingCycle(cycle.Id);
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.Add(supportCase);
            db.SupportCaseTimingCycles.Add(cycle);
            await db.SaveChangesAsync();
        }

        var services = _fixture.Factory.Services;
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        await using var workerScope1 = scopeFactory.CreateAsyncScope();
        await using var workerScope2 = scopeFactory.CreateAsyncScope();
        var worker1 = new SupportTimingProcessor(
            workerScope1.ServiceProvider.GetRequiredService<IAppDbContext>(), scopeFactory, TimeProvider.System,
            workerScope1.ServiceProvider.GetRequiredService<ILogger<SupportTimingProcessor>>());
        var worker2 = new SupportTimingProcessor(
            workerScope2.ServiceProvider.GetRequiredService<IAppDbContext>(), scopeFactory, TimeProvider.System,
            workerScope2.ServiceProvider.GetRequiredService<ILogger<SupportTimingProcessor>>());

        var results = await Task.WhenAll(worker1.ProcessTimingTickAsync(), worker2.ProcessTimingTickAsync());

        results.Sum(x => x.EscalatedCount).Should().Be(1);
        await using var verify = _fixture.CreateDbContext();
        (await verify.SupportCaseEvents.CountAsync(x => x.SupportCaseId == supportCase.Id && x.EventType == "timing.escalated"))
            .Should().Be(1);
        (await verify.AdminNotifications.Where(x => x.UserId == admin.UserId && x.TitleEn == "Support Case Escalated")
            .Select(x => x.PayloadJson).ToListAsync())
            .Count(payload => payload is not null && payload.Contains(supportCase.Id.ToString("D"), StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public async Task EscalationWaitsForSnapshottedThresholdAndSkipsAnsweredAndLegacyCycles()
    {
        var admin = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]);
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
            await SupportTestIdentity.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), admin);

        var now = DateTime.UtcNow;
        var early = CreateCycleCase(now.AddHours(-3), now.AddHours(-2), now.AddHours(4), TimeSpan.FromHours(4));
        var threshold = CreateCycleCase(now.AddHours(-5), now.AddHours(-4), now.AddHours(5), TimeSpan.FromHours(4));
        var answered = CreateCycleCase(now.AddHours(-5), now.AddHours(-3), now.AddHours(5), TimeSpan.FromHours(4));
        answered.Cycle.RecordFirstResponse(answered.Cycle.StartedAtUtc.AddHours(3));
        var legacy = CreateCycleCase(now.AddHours(-5), now.AddHours(-3), now.AddHours(-1), null);
        legacy.Cycle.MarkFirstResponseBreached();

        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.AddRange(early.Case, threshold.Case, answered.Case, legacy.Case);
            db.SupportCaseTimingCycles.AddRange(early.Cycle, threshold.Cycle, answered.Cycle, legacy.Cycle);
            await db.SaveChangesAsync();
        }

        var services = _fixture.Factory.Services;
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        await using var workerScope = scopeFactory.CreateAsyncScope();
        var worker = new SupportTimingProcessor(workerScope.ServiceProvider.GetRequiredService<IAppDbContext>(),
            scopeFactory, TimeProvider.System, workerScope.ServiceProvider.GetRequiredService<ILogger<SupportTimingProcessor>>());
        var result = await worker.ProcessTimingTickAsync();

        result.EscalatedCount.Should().Be(1);
        await using var verify = _fixture.CreateDbContext();
        (await verify.SupportCaseTimingCycles.SingleAsync(x => x.Id == early.Cycle.Id)).IsEscalated.Should().BeFalse();
        (await verify.SupportCaseTimingCycles.SingleAsync(x => x.Id == threshold.Cycle.Id)).IsEscalated.Should().BeTrue();
        var answeredCycle = await verify.SupportCaseTimingCycles.SingleAsync(x => x.Id == answered.Cycle.Id);
        answeredCycle.IsEscalated.Should().BeFalse();
        answeredCycle.IsFirstResponseBreached.Should().BeTrue();
        answeredCycle.FirstResponseAtUtc.Should().NotBeNull();
        (await verify.SupportCaseTimingCycles.SingleAsync(x => x.Id == legacy.Cycle.Id)).IsEscalated.Should().BeFalse();
        (await verify.SupportCaseTimingCycles.SingleAsync(x => x.Id == legacy.Cycle.Id)).IsResolutionBreached.Should().BeTrue();
    }

    [Fact]
    public async Task WorkerAndResolveRaceSerializeEffectsUnderSharedCaseLock()
    {
        var admin = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]);
        await using (var seed = _fixture.Factory.Services.CreateAsyncScope())
            await SupportTestIdentity.SeedAsync(seed.ServiceProvider.GetRequiredService<AppDbContext>(), admin);
        var startedAt = DateTime.UtcNow.AddHours(-5);
        var timedCase = CreateCycleCase(startedAt, startedAt.AddHours(1), startedAt.AddHours(2), TimeSpan.FromHours(3));
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.Add(timedCase.Case);
            db.SupportCaseTimingCycles.Add(timedCase.Cycle);
            await db.SaveChangesAsync();
        }

        using var client = _fixture.CreateClientForUser(admin);
        var version = await ReadVersionAsync(timedCase.Case.Id);
        var start = await client.PostAsJsonAsync($"/api/v1/support/cases/{timedCase.Case.Id}/actions/start",
            new SupportCaseActionRequest("start", version, Guid.NewGuid().ToString("N")));
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        version = await ReadVersionAsync(timedCase.Case.Id);
        var services = _fixture.Factory.Services;
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        await using var workerScope = scopeFactory.CreateAsyncScope();
        var worker = new SupportTimingProcessor(workerScope.ServiceProvider.GetRequiredService<IAppDbContext>(),
            scopeFactory, TimeProvider.System, workerScope.ServiceProvider.GetRequiredService<ILogger<SupportTimingProcessor>>());
        var resolveTask = client.PostAsJsonAsync($"/api/v1/support/cases/{timedCase.Case.Id}/actions/resolve",
            new SupportCaseActionRequest("resolve", version, Guid.NewGuid().ToString("N"), Reason: "Resolved during timing tick."));
        var tickTask = worker.ProcessTimingTickAsync();
        await Task.WhenAll(resolveTask, tickTask);
        var resolve = await resolveTask;
        if (resolve.StatusCode == HttpStatusCode.Conflict)
        {
            version = await ReadVersionAsync(timedCase.Case.Id);
            resolve = await client.PostAsJsonAsync($"/api/v1/support/cases/{timedCase.Case.Id}/actions/resolve",
                new SupportCaseActionRequest("resolve", version, Guid.NewGuid().ToString("N"), Reason: "Resolved during timing tick."));
        }
        resolve.StatusCode.Should().Be(HttpStatusCode.OK, await resolve.Content.ReadAsStringAsync());

        var tick = await worker.ProcessTimingTickAsync();
        tick.EscalatedCount.Should().Be(0);
        await using var verify = _fixture.CreateDbContext();
        var cycleState = await verify.SupportCaseTimingCycles.SingleAsync(x => x.Id == timedCase.Cycle.Id);
        var escalationCount = await verify.SupportCaseEvents.CountAsync(
            x => x.SupportCaseId == timedCase.Case.Id && x.EventType == "timing.escalated");
        escalationCount.Should().Be(cycleState.IsEscalated ? 1 : 0);
        var notificationCount = (await verify.AdminNotifications.Where(x => x.UserId == admin.UserId
                && x.TitleEn == "Support Case Escalated").Select(x => x.PayloadJson).ToListAsync())
            .Count(payload => payload is not null && payload.Contains(timedCase.Case.Id.ToString("D"), StringComparison.Ordinal));
        notificationCount.Should().Be(cycleState.IsEscalated ? 1 : 0);
    }

    private async Task<uint> ReadVersionAsync(Guid caseId)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.SupportCases.Where(x => x.Id == caseId).Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
    }

    private static (SupportCase Case, SupportCaseTimingCycle Cycle) CreateCycleCase(
        DateTime startedAtUtc, DateTime firstResponseDeadlineUtc, DateTime resolutionDeadlineUtc, TimeSpan? threshold)
    {
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "order", "Threshold timing", "Please review this case.", Guid.NewGuid(),
            startedAtUtc);
        var cycle = SupportCaseTimingCycle.StartConfigured(supportCase.Id, 1, Guid.NewGuid(), 1, Guid.NewGuid(), startedAtUtc,
            firstResponseDeadlineUtc, resolutionDeadlineUtc, threshold);
        supportCase.SetActiveTimingCycle(cycle.Id);
        return (supportCase, cycle);
    }

    [Fact]
    public async Task FirstResponseRequiresAnOutwardPlatformReplyRatherThanAckOrPartyTaskReply()
    {
        var adminId = Guid.NewGuid();
        var admin = new TokenUser(adminId, UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage, PermissionCodes.SupportReply]);
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            await SupportTestIdentity.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), admin);
        }

        var requesterId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var acknowledgedCase = CreateTimedCase(requesterId, restaurantId);
        var taskReplyCase = CreateTimedCase(requesterId, restaurantId);
        var taskId = Guid.NewGuid();
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.AddRange(acknowledgedCase.Case, taskReplyCase.Case);
            db.SupportCaseTimingCycles.AddRange(acknowledgedCase.Cycle, taskReplyCase.Cycle);
            db.SupportCaseTasks.Add(new SupportCaseTask
            {
                Id = taskId,
                SupportCaseId = taskReplyCase.Case.Id,
                Type = SupportCaseTaskType.RestaurantEvidence,
                RecipientType = SupportCaseTaskRecipientType.Restaurant,
                RestaurantId = restaurantId,
                Subject = "Confirm packing",
                Question = "Was the item packed?",
                CreatedByUserId = adminId,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var adminClient = _fixture.CreateClientForUser(admin);
        await using var versionDb = _fixture.CreateDbContext();
        var acknowledgedVersion = await versionDb.SupportCases.Where(x => x.Id == acknowledgedCase.Case.Id)
            .Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
        var taskReplyVersion = await versionDb.SupportCases.Where(x => x.Id == taskReplyCase.Case.Id)
            .Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
        var acknowledge = await adminClient.PostAsJsonAsync($"/api/v1/support/cases/{acknowledgedCase.Case.Id}/actions/acknowledge",
            new SupportCaseActionRequest("acknowledge", acknowledgedVersion, Guid.NewGuid().ToString("N")));
        acknowledge.StatusCode.Should().Be(HttpStatusCode.OK);

        using var restaurant = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Restaurant,
            ["RestaurantOwner"], [], restaurantId));
        var partyReply = await restaurant.PostAsJsonAsync($"/api/v1/support/cases/{taskReplyCase.Case.Id}/messages",
            new AddSupportMessageRequest("The item was packed.", taskReplyVersion, Guid.NewGuid().ToString("N"), taskId));
        partyReply.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = _fixture.CreateDbContext();
        (await verify.SupportCaseTimingCycles.Where(x => x.Id == acknowledgedCase.Cycle.Id)
            .Select(x => x.FirstResponseAtUtc).SingleAsync()).Should().BeNull();
        (await verify.SupportCaseTimingCycles.Where(x => x.Id == taskReplyCase.Cycle.Id)
            .Select(x => x.FirstResponseAtUtc).SingleAsync()).Should().BeNull();

        var requestInfoCase = CreateTimedCase(Guid.NewGuid(), restaurantId);
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.Add(requestInfoCase.Case);
            db.SupportCaseTimingCycles.Add(requestInfoCase.Cycle);
            await db.SaveChangesAsync();
        }
        await using var requestInfoVersionDb = _fixture.CreateDbContext();
        var requestInfoVersion = await requestInfoVersionDb.SupportCases.Where(x => x.Id == requestInfoCase.Case.Id)
            .Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
        var started = await adminClient.PostAsJsonAsync($"/api/v1/support/cases/{requestInfoCase.Case.Id}/actions/start",
            new SupportCaseActionRequest("start", requestInfoVersion, Guid.NewGuid().ToString("N")));
        started.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var afterStartVersionDb = _fixture.CreateDbContext();
        var afterStartVersion = await afterStartVersionDb.SupportCases.Where(x => x.Id == requestInfoCase.Case.Id)
            .Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
        var requestInfo = await adminClient.PostAsJsonAsync($"/api/v1/support/cases/{requestInfoCase.Case.Id}/actions/request-information",
            new SupportCaseActionRequest("request-information", afterStartVersion, Guid.NewGuid().ToString("N"),
                Reason: "Please share the order reference."));
        requestInfo.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyRequestInfo = _fixture.CreateDbContext();
        (await verifyRequestInfo.SupportCaseTimingCycles.Where(x => x.Id == requestInfoCase.Cycle.Id)
            .Select(x => x.FirstResponseAtUtc).SingleAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task PersistedScopedSupportReplyOperatorRecordsFirstResponse()
    {
        var scopeId = Guid.NewGuid();
        var operatorUser = new TokenUser(Guid.NewGuid(), UserType.Admin, ["SupportOperator"],
            [PermissionCodes.SupportView, PermissionCodes.SupportReply], ScopeIds: [scopeId]);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.AccessScopes.Add(new AccessScope
            {
                Id = scopeId,
                Name = "Global support test scope",
                ScopeType = AccessScopeType.Global,
                IsActive = true,
            });
            await seed.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(seed, operatorUser);
        }

        var timedCase = CreateTimedCase(Guid.NewGuid(), Guid.Empty);
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.Add(timedCase.Case);
            db.SupportCaseTimingCycles.Add(timedCase.Cycle);
            await db.SaveChangesAsync();
        }

        using var client = _fixture.CreateClientForUser(operatorUser);
        await using var versionDb = _fixture.CreateDbContext();
        var version = await versionDb.SupportCases.Where(x => x.Id == timedCase.Case.Id)
            .Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/support/cases/{timedCase.Case.Id}/messages",
            new AddSupportMessageRequest("We are reviewing this case.", version, Guid.NewGuid().ToString("N")));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await using var verify = _fixture.CreateDbContext();
        (await verify.SupportCaseTimingCycles.Where(x => x.Id == timedCase.Cycle.Id)
            .Select(x => x.FirstResponseAtUtc).SingleAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task RestaurantTaskReplyDoesNotRecordPlatformFirstResponse()
    {
        var admin = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]);
        await using (var seed = _fixture.Factory.Services.CreateAsyncScope())
            await SupportTestIdentity.SeedAsync(seed.ServiceProvider.GetRequiredService<AppDbContext>(), admin);
        var restaurantId = Guid.NewGuid();
        var timedCase = CreateTimedCase(Guid.NewGuid(), restaurantId);
        var taskId = Guid.NewGuid();
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.Add(timedCase.Case);
            db.SupportCaseTimingCycles.Add(timedCase.Cycle);
            db.SupportCaseTasks.Add(new SupportCaseTask
            {
                Id = taskId,
                SupportCaseId = timedCase.Case.Id,
                Type = SupportCaseTaskType.RestaurantEvidence,
                RecipientType = SupportCaseTaskRecipientType.Restaurant,
                RestaurantId = restaurantId,
                Subject = "Confirm packing",
                Question = "Was the item packed?",
                CreatedByUserId = admin.UserId,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var restaurant = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Restaurant,
            ["RestaurantOwner"], [], restaurantId));
        var response = await restaurant.PostAsJsonAsync($"/api/v1/support/cases/{timedCase.Case.Id}/messages",
            new AddSupportMessageRequest("The item was packed.", await ReadVersionAsync(timedCase.Case.Id),
                Guid.NewGuid().ToString("N"), taskId));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await using var verify = _fixture.CreateDbContext();
        (await verify.SupportCaseTimingCycles.Where(x => x.Id == timedCase.Cycle.Id)
            .Select(x => x.FirstResponseAtUtc).SingleAsync()).Should().BeNull();
    }

    private static (SupportCase Case, SupportCaseTimingCycle Cycle) CreateTimedCase(Guid requesterId, Guid restaurantId)
    {
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "order", "Timing response", "Please review this order.", requesterId,
            DateTime.UtcNow, restaurantId: restaurantId == Guid.Empty ? null : restaurantId);
        var now = DateTime.UtcNow;
        var cycle = SupportCaseTimingCycle.StartConfigured(supportCase.Id, 1, Guid.NewGuid(), 1, Guid.NewGuid(), now,
            now.AddHours(1), now.AddHours(4));
        supportCase.SetActiveTimingCycle(cycle.Id);
        return (supportCase, cycle);
    }

    [Fact]
    public async Task SuperAdminCanPublishAndRetrieveActivePolicy()
    {
        var superAdminId = Guid.NewGuid();
        var superAdminTokenUser = new TokenUser(
            superAdminId, UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]);

        int expectedVersion;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SupportTestIdentity.SeedAsync(db, superAdminTokenUser);
            expectedVersion = (await db.SupportTimingPolicies.MaxAsync(policy => (int?)policy.Version) ?? 0) + 1;
        }

        using var superAdmin = _fixture.CreateClientForUser(superAdminTokenUser);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        var request = new PublishSupportTimingPolicyRequest(
            "Platform Standard SLA",
            "Launch timing policy",
            "Launch readiness requirement",
            idempotencyKey,
            expectedVersion,
            [
                new CreateSupportTimingRuleDto(
                    null,
                    SupportCasePriority.P0,
                    null,
                    15,
                    60,
                    30,
                    true,
                    1),
                new CreateSupportTimingRuleDto(
                    null,
                    null,
                    null,
                    60,
                    1440,
                    240,
                    false,
                    99)
            ]);

        var publishResponse = await superAdmin.PostAsJsonAsync("/api/v1/support/timing-policies/publish", request);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var policyDto = await publishResponse.Content.ReadFromJsonAsync<SupportTimingPolicyDto>();
        policyDto.Should().NotBeNull();
        policyDto!.Name.Should().Be("Platform Standard SLA");
        policyDto.IsActive.Should().BeTrue();
        policyDto.Rules.Should().HaveCount(2);

        // Fetch active policy
        var activeResponse = await superAdmin.GetAsync("/api/v1/support/timing-policies/active");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeDto = await activeResponse.Content.ReadFromJsonAsync<SupportTimingPolicyDto>();
        activeDto.Should().NotBeNull();
        activeDto!.Id.Should().Be(policyDto.Id);

        // Idempotency replay
        var replayResponse = await superAdmin.PostAsJsonAsync("/api/v1/support/timing-policies/publish", request);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayDto = await replayResponse.Content.ReadFromJsonAsync<SupportTimingPolicyDto>();
        replayDto!.Id.Should().Be(policyDto.Id);

        var changedVersion = await superAdmin.PostAsJsonAsync("/api/v1/support/timing-policies/publish",
            request with { ExpectedVersion = expectedVersion + 1 });
        changedVersion.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var otherAdmin = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]);
        await using (var seed = _fixture.CreateDbContext()) await SupportTestIdentity.SeedAsync(seed, otherAdmin);
        using var otherClient = _fixture.CreateClientForUser(otherAdmin);
        (await otherClient.PostAsJsonAsync("/api/v1/support/timing-policies/publish", request))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvalidSecondRuleLeavesActivePolicyAndVersionHistoryUnchanged()
    {
        var admin = new TokenUser(Guid.NewGuid(), UserType.Admin, [AppRoles.SuperAdmin], [PermissionCodes.SupportManage]);
        int expectedVersion;
        await using (var db = _fixture.CreateDbContext())
        {
            await SupportTestIdentity.SeedAsync(db, admin);
            expectedVersion = (await db.SupportTimingPolicies.MaxAsync(policy => (int?)policy.Version) ?? 0) + 1;
            var baseline = SupportTimingPolicy.Create(expectedVersion, "Existing active", null, Guid.NewGuid(), "Baseline",
                Guid.NewGuid().ToString("N"), DateTime.UtcNow);
            baseline.AddRule(SupportCaseChannel.Customer, null, null, TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5), false, 0);
            db.SupportTimingPolicies.Add(baseline);
            await db.SaveChangesAsync();
            expectedVersion++;
        }
        using var client = _fixture.CreateClientForUser(admin);
        var request = new PublishSupportTimingPolicyRequest("Invalid candidate", null, "Must not deactivate active version",
            Guid.NewGuid().ToString("N"), expectedVersion,
            [
                new CreateSupportTimingRuleDto(null, null, null, 5, 15, 5, false, 0),
                new CreateSupportTimingRuleDto(null, null, null, 20, 10, 5, false, 1),
            ]);

        var response = await client.PostAsJsonAsync("/api/v1/support/timing-policies/publish", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        await using var verify = _fixture.CreateDbContext();
        (await verify.SupportTimingPolicies.CountAsync(policy => policy.Version == expectedVersion)).Should().Be(0);
        (await verify.SupportTimingPolicies.SingleAsync(policy => policy.Version == expectedVersion - 1))
            .IsActive.Should().BeTrue();
    }
}
