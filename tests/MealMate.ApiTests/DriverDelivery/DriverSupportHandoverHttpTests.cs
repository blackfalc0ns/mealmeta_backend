using System.Net;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.ApiTests.OrderLifecycle;

namespace MealMate.ApiTests.DriverDelivery;

public sealed class DriverSupportHandoverHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    public DriverSupportHandoverHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

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
