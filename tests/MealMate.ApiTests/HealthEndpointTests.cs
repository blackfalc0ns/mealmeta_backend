using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MealMate.ApiTests;

/// <summary>
/// Verifies the API host boots and the request pipeline responds. Does not require
/// a database (health checks are only executed when their endpoint is hit).
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task UnknownRoute_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/this-route-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
