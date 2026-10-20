using System.Net;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Net.Http.Json;

namespace MealMate.ApiTests.DriverDelivery;

public sealed class DriverSupportHandoverHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    public DriverSupportHandoverHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DriverCanRecoverPendingRequest_AndReadPersistedRejectionOverHttp()
    {
        var driverUser = Guid.NewGuid(); var customerUser = Guid.NewGuid(); var adminUser = Guid.NewGuid();
        var driver = new DriverProfile { UserId = driverUser, FullNameAr = "السائق", FullNameEn = "Driver" };
        var issue = new Complaint { CustomerUserId = customerUser, DriverProfileId = driver.Id,
            Category = "DriverReassignmentRequested", Title = "Pending legacy request", Description = "Lost response recovery",
            SubmittedAtUtc = DateTime.UtcNow };
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            db.Users.AddRange(new ApplicationUser { Id = driverUser, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = customerUser, UserType = UserType.Customer, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = adminUser, UserType = UserType.Admin, AccountStatus = AccountStatus.Active });
            db.AddRange(driver, issue); await db.SaveChangesAsync();
        }
        using var driverClient = _fixture.CreateClientForUser(new TokenUser(driverUser, UserType.Driver, ["Driver"], []));
        var recovery = await driverClient.GetFromJsonAsync<DriverReassignmentRequestDto[]>("/api/v1/driver/reassignment-requests");
        recovery.Should().ContainSingle(x => x.RequestId == issue.Id && x.Status == "ReassignmentRequested" && x.HandoverId == null);
        using var admin = _fixture.CreateClientForUser(new TokenUser(adminUser, UserType.Admin, ["Admin"], []));
        var path = $"/api/v1/dispatcher/reassignment-requests/{issue.Id}/reject";
        var rejected = await admin.PostAsJsonAsync(path, new { notes = "Replacement not needed" });
        rejected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync(path, new { notes = "Replacement not needed" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await driverClient.GetFromJsonAsync<DriverReassignmentRequestDto>($"/api/v1/driver/reassignment-requests/{issue.Id}");
        detail!.Status.Should().Be("Rejected"); detail.DecisionNotes.Should().Be("Replacement not needed");
        detail.DecidedByUserId.Should().Be(adminUser); detail.DecidedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task DriverReassignmentRecovery_RequiresAuthentication()
    {
        using var client = _fixture.Factory.CreateClient();
        (await client.GetAsync("/api/v1/driver/reassignment-requests")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DispatcherReassignmentDecision_RejectsDriverRole()
    {
        using var client = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Driver, ["Driver"], []));
        using var body = new StringContent("{\"notes\":\"Not authorized\"}", System.Text.Encoding.UTF8, "application/json");
        (await client.PostAsync($"/api/v1/dispatcher/reassignment-requests/{Guid.NewGuid()}/reject", body))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DriverHandoverRecoveryRoute_RequiresDriverAuthentication()
    {
        using var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/v1/driver/support-handovers?status=active");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DriverHandoverRecoveryRoute_RejectsAuthenticatedWrongRole()
    {
        using var client = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        var response = await client.GetAsync("/api/v1/driver/support-handovers?status=active");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DispatcherHandoverList_RequiresRestaurantScope()
    {
        using var client = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.DeliveryManager, ["DeliveryManager"], []));
        var response = await client.GetAsync("/api/v1/dispatcher/support-handovers");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
