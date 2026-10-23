using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminNotifications;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
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
}
