using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.Support;

public sealed class SupportCaseNotificationHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;

    public SupportCaseNotificationHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PublicCustomerAndDriverUpdates_CreateOneTransactionalPushOutboxRowPerInboxOnReplay()
    {
        var supportAdminId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        FixtureEntities data;
        SupportCase customerCase;
        SupportCase driverCase;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString).Options, Substitute.For<IDomainEventDispatcher>()))
        {
            data = await OrderLifecycleTestDataSeeder.SeedAsync(db, new DateOnly(2026, 10, 23));
            customerCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "order", "Customer update", "Private customer case text",
                data.CustomerUser.Id, DateTime.UtcNow);
            driverCase = SupportCase.Create(SupportCaseChannel.Driver, SupportCaseKind.Complaint,
                SupportCaseOwnerTeam.PlatformSupport, "delivery", "Driver update", "Private driver case text",
                data.DriverUserA.Id, DateTime.UtcNow);
            db.SupportCases.AddRange(customerCase, driverCase);
            db.AccessScopes.Add(new AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new UserScope { UserId = supportAdminId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(supportAdminId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.reply"], null, [scopeId]));
        }

        using var admin = _fixture.CreateClientForUser(new TokenUser(supportAdminId, UserType.Admin,
            ["SupportAdmin"], ["support:view", "support.reply"], null, [scopeId]));
        foreach (var (supportCase, actorId, channelName) in new[]
                 { (customerCase, data.CustomerUser.Id, "customer"), (driverCase, data.DriverUserA.Id, "driver") })
        {
            var details = await admin.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{supportCase.Id}");
            details.Should().NotBeNull();
            var request = new AddSupportMessageRequest("We are reviewing your support request.", details!.Version,
                $"push-outbox-{channelName}");
            var first = await admin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/messages", request);
            first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
            var replay = await admin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/messages", request);
            replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());

            await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.ConnectionString).Options, Substitute.For<IDomainEventDispatcher>());
            var supportEvent = await verify.SupportCaseEvents.SingleAsync(item => item.SupportCaseId == supportCase.Id
                && item.IdempotencyKey == request.IdempotencyKey);
            Guid notificationId = supportCase.Channel == SupportCaseChannel.Customer
                ? await verify.CustomerNotifications.Where(item => item.UserId == actorId && item.SubjectId == supportEvent.Id)
                    .Select(item => item.Id).SingleAsync()
                : await verify.DriverNotifications.Where(item => item.UserId == actorId && item.SubjectId == supportEvent.Id)
                    .Select(item => item.Id).SingleAsync();
            var outboxRows = await verify.OutboxMessages.Where(item => item.EventType == "unified-support-case-push.v1"
                && item.DeduplicationKey == $"unified-support-case-push:{notificationId:N}").ToListAsync();
            outboxRows.Should().ContainSingle();
            var payload = JsonDocument.Parse(outboxRows[0].PayloadJson).RootElement;
            payload.GetProperty("notificationId").GetGuid().Should().Be(notificationId);
            payload.GetProperty("audience").GetString().Should().Be(channelName);
        }
    }
}
