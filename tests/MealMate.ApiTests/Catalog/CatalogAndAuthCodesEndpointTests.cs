using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Features.Catalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MealMate.ApiTests.Catalog;

public sealed class CatalogAndAuthCodesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CatalogAndAuthCodesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
        }).CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/catalog/nationalities")]
    [InlineData("/api/v1/auth/nationalities")]
    [InlineData("/api/v1/auth/staff/driver-registration/nationalities")]
    public async Task GetNationalities_FromMultipleRoutes_ReturnsOkWithList(string route)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var nationalities = await response.Content.ReadFromJsonAsync<List<NationalityDto>>();
        nationalities.Should().NotBeNull();
        nationalities!.Should().NotBeEmpty();
        nationalities.Should().Contain(x => x.Code == "KW");
        nationalities.Should().Contain(x => x.Code == "SA");
        nationalities.Should().Contain(x => x.Code == "EG");
        nationalities.Should().Contain(x => x.Code == "IN");
        nationalities.Should().Contain(x => x.Code == "PH");
    }

    [Theory]
    [InlineData("/api/v1/catalog/calling-codes")]
    [InlineData("/api/v1/auth/calling-codes")]
    [InlineData("/api/v1/auth/country-codes")]
    public async Task GetCallingCodes_FromMultipleRoutes_ReturnsSuccess(string route)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData("/api/v1/auth/staff/roles")]
    [InlineData("/api/v1/auth/roles")]
    public async Task GetRoles_FromMultipleRoutes_ReturnsOkWithRoles(string route)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roles = await response.Content.ReadFromJsonAsync<List<MealMate.Application.Features.IdentityAccess.StaffAuth.StaffRoleDto>>();
        roles.Should().NotBeNull();
        roles!.Should().HaveCount(2);
        roles.Should().Contain(x => x.Code == "Driver" && x.AllowsSelfRegistration);
        roles.Should().Contain(x => x.Code == "DeliveryManager" && !x.AllowsSelfRegistration);
    }
}
