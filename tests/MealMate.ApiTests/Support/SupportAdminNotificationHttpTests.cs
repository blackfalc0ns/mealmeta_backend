using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.AdminNotifications;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.Support;

public sealed class SupportAdminNotificationHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    public SupportAdminNotificationHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AdminNotificationsAreRecipientScopedAndCustomerCannotReadBroadcasts()
    {
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var targetedA = new AdminNotification { UserId = adminA, TitleAr = "A", TitleEn = "A", IsRead = false, SentAtUtc = DateTime.UtcNow };
        var targetedB = new AdminNotification { UserId = adminB, TitleAr = "B", TitleEn = "B", IsRead = false, SentAtUtc = DateTime.UtcNow };
        var legacyBroadcast = new AdminNotification { UserId = null, TitleAr = "Legacy", TitleEn = "Legacy", IsRead = false, SentAtUtc = DateTime.UtcNow };
        await using (var db = _fixture.CreateDbContext())
        {
            db.AdminNotifications.AddRange(targetedA, targetedB, legacyBroadcast);
            await db.SaveChangesAsync();
        }

        using var clientA = _fixture.CreateClientForUser(new TokenUser(adminA, UserType.Admin, ["Admin"], []));
        using var clientB = _fixture.CreateClientForUser(new TokenUser(adminB, UserType.Admin, ["Admin"], []));
        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));

        var responseA = await clientA.GetFromJsonAsync<AdminNotificationsListResponse>("/api/v1/admin/notifications");
        responseA.Should().NotBeNull();
        responseA!.TotalCount.Should().Be(2);
        responseA.Items.Select(x => x.TitleEn).Should().Contain(["A", "Legacy"]).And.NotContain("B");

        (await customer.GetAsync("/api/v1/admin/notifications")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.PostAsync("/api/v1/admin/notifications/mark-all-read", content: null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clientB.PostAsync($"/api/v1/admin/notifications/{targetedA.Id}/read", content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await clientA.PostAsync("/api/v1/admin/notifications/mark-all-read", content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verify = _fixture.CreateDbContext();
        (await verify.AdminNotifications.SingleAsync(x => x.Id == targetedA.Id)).IsRead.Should().BeTrue();
        (await verify.AdminNotifications.SingleAsync(x => x.Id == targetedB.Id)).IsRead.Should().BeFalse();
        (await verify.AdminNotifications.SingleAsync(x => x.Id == legacyBroadcast.Id)).IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task CustomerReopenNotifiesOnlyAssignedEligibleOperatorAndReplayDoesNotDuplicate()
    {
        var assignedOperatorId = Guid.NewGuid();
        var unassignedOperatorId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var permissions = new[] { PermissionCodes.SupportView, PermissionCodes.SupportReply };
        var assignedOperator = new TokenUser(assignedOperatorId, UserType.Admin, ["SupportAdmin"], permissions, null, [scopeId]);
        var unassignedOperator = new TokenUser(unassignedOperatorId, UserType.Admin, ["SupportAdmin"], permissions, null, [scopeId]);
        SupportCase[] cases;
        await using (var db = _fixture.CreateDbContext())
        {
            var fixtureData = await OrderLifecycleTestDataSeeder.SeedAsync(db, new DateOnly(2026, 10, 23));
            db.AccessScopes.Add(new AccessScope
            {
                Id = scopeId,
                Name = "Customer reopen notification scope",
                ScopeType = AccessScopeType.Global,
                IsActive = true,
            });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, assignedOperator);
            await SupportTestIdentity.SeedAsync(db, unassignedOperator);

            var now = DateTime.UtcNow;
            cases = [
                NewAssignedTerminalCase(customerId, assignedOperatorId, fixtureData.RestaurantA.Id, now, close: false),
                NewAssignedTerminalCase(customerId, assignedOperatorId, fixtureData.RestaurantA.Id, now.AddSeconds(1), close: true),
            ];
            db.SupportCases.AddRange(cases);
            await db.SaveChangesAsync();
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        foreach (var (supportCase, suffix) in cases.Zip(["resolved", "closed"]))
        {
            var detail = await customer.GetFromJsonAsync<SupportCaseDto>(
                $"/api/v1/customer/support/issues/{supportCase.Id:D}");
            detail.Should().NotBeNull();
            var request = new { expectedVersion = detail!.Version, idempotencyKey = $"customer-reopen-notify-{suffix}", reason = "Please reopen this case." };
            var first = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{supportCase.Id:D}/actions/reopen", request);
            first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
            var retry = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{supportCase.Id:D}/actions/reopen", request);
            retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());

            await using var verify = _fixture.CreateDbContext();
            var supportEvent = await verify.SupportCaseEvents.SingleAsync(item => item.SupportCaseId == supportCase.Id
                && item.IdempotencyKey == request.idempotencyKey);
            var operatorNotifications = await verify.AdminNotifications
                .Where(item => item.UserId == assignedOperatorId || item.UserId == unassignedOperatorId)
                .ToListAsync();
            var eventNotifications = operatorNotifications.Where(item =>
            {
                if (item.PayloadJson is null) return false;
                using var notificationPayload = JsonDocument.Parse(item.PayloadJson);
                return notificationPayload.RootElement.GetProperty("supportEventId").GetGuid() == supportEvent.Id;
            }).ToList();
            eventNotifications.Should().ContainSingle(item => item.UserId == assignedOperatorId);
            eventNotifications.Should().NotContain(item => item.UserId == unassignedOperatorId);
            using var payload = JsonDocument.Parse(eventNotifications.Single().PayloadJson!);
            payload.RootElement.GetProperty("eventKey").GetString().Should().Be("support.case.reopened");
            payload.RootElement.GetProperty("supportEventId").GetGuid().Should().Be(supportEvent.Id);
            payload.RootElement.GetProperty("deepLink").GetString()
                .Should().Be($"/admin/support/complaints?caseId={supportCase.Id:D}");
        }
    }

    private static SupportCase NewAssignedTerminalCase(Guid customerId, Guid assignedOperatorId, Guid restaurantId,
        DateTime createdAtUtc, bool close)
    {
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "customer.issue.Payment", "Payment review", "Customer requested help.",
            customerId, createdAtUtc, restaurantId: restaurantId);
        supportCase.TryTransitionTo(SupportCaseStatus.InProgress).Should().BeTrue();
        supportCase.TryTransitionTo(SupportCaseStatus.Resolved).Should().BeTrue();
        if (close) supportCase.TryTransitionTo(SupportCaseStatus.Closed).Should().BeTrue();
        supportCase.Assign(assignedOperatorId, customerId, createdAtUtc);
        return supportCase;
    }
}
