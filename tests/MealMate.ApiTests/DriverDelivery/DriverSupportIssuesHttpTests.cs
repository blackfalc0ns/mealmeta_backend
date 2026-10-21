using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.DriverDelivery;

public sealed class DriverSupportIssuesHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    public DriverSupportIssuesHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DriverCanReadOwnListAndDetailOverHttp()
    {
        var userId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = new Complaint
        {
            CustomerUserId = userId,
            DriverProfileId = profile.Id,
            Category = "VehicleBreakdown",
            Title = "Breakdown",
            Description = "Vehicle needs help",
            SubmittedAtUtc = DateTime.UtcNow
        };
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            db.AddRange(profile, issue); await db.SaveChangesAsync();
        }
        using var client = _fixture.CreateClientForUser(new TokenUser(userId, UserType.Driver, ["Driver"], []));
        var response = await client.GetAsync("/api/v1/driver/support/issues?status=All&pageNumber=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DriverSupportIssuesResponseDto>();
        body.Should().NotBeNull(); body!.AllCount.Should().Be(1); body.Issues.Should().ContainSingle(x => x.IssueId == issue.Id);
        var detail = await client.GetAsync($"/api/v1/driver/support/issues/{issue.Id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadAsStringAsync()).Should().Contain("Vehicle needs help");
    }

    [Fact]
    public async Task DriverIssueRoutesRequireDriverAuthenticationAndRole()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/driver/support/issues")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var customer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        (await customer.GetAsync("/api/v1/driver/support/issues")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
