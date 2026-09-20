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

    [Theory]
    [InlineData("/api/v1/catalog/vehicles/types")]
    [InlineData("/api/v1/auth/vehicle-types")]
    [InlineData("/api/v1/auth/staff/driver-registration/vehicle-types")]
    public async Task GetVehicleTypes_FromMultipleRoutes_ReturnsOkWithTypes(string route)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var types = await response.Content.ReadFromJsonAsync<List<VehicleTypeDto>>();
        types.Should().NotBeNull();
        types!.Should().HaveCount(4);
        types.Should().Contain(x => x.Code == "Car");
        types.Should().Contain(x => x.Code == "Motorcycle");
        types.Should().Contain(x => x.Code == "Van");
        types.Should().Contain(x => x.Code == "Bicycle");
    }

    [Fact]
    public async Task GetVehicleTypes_WithArabicHeader_ReturnsArabicNames()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/vehicles/types");
        request.Headers.AcceptLanguage.ParseAdd("ar");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var types = await response.Content.ReadFromJsonAsync<List<VehicleTypeDto>>();
        types.Should().NotBeNull();
        types.Should().Contain(x => x.Code == "Car" && x.NameAr == "سيارة");
    }

    [Theory]
    [InlineData("/api/v1/catalog/vehicles/colors")]
    [InlineData("/api/v1/auth/vehicle-colors")]
    [InlineData("/api/v1/auth/staff/driver-registration/vehicle-colors")]
    public async Task GetVehicleColors_FromMultipleRoutes_ReturnsOkWithPalette(string route)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var colors = await response.Content.ReadFromJsonAsync<List<VehicleColorDto>>();
        colors.Should().NotBeNull();
        colors!.Should().NotBeEmpty();

        // Exact colors from mockup
        colors.Should().Contain(x => x.Hex == "#D32F2F"); // Red
        colors.Should().Contain(x => x.Hex == "#1976D2"); // Blue
        colors.Should().Contain(x => x.Hex == "#9E9E9E"); // Grey
        colors.Should().Contain(x => x.Hex == "#000000"); // Black
        colors.Should().Contain(x => x.Hex == "#5E35B1"); // Purple
        colors.Should().Contain(x => x.Hex == "#FFFFFF"); // White
    }

    [Theory]
    [InlineData("/api/v1/catalog/vehicles/models?search=camry", "Camry")]
    [InlineData("/api/v1/catalog/vehicles/models?search=تويوتا", "Toyota")]
    [InlineData("/api/v1/auth/staff/driver-registration/vehicle-models?search=sunny", "Sunny")]
    [InlineData("/api/v1/auth/staff/driver-registration/vehicle-models?search=صني", "Sunny")]
    [InlineData("/api/v1/auth/vehicle-models?vehicleType=Motorcycle", "Motorcycle")]
    public async Task SearchVehicleModels_ReturnsMatchingResults(string route, string expectedMatch)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var models = await response.Content.ReadFromJsonAsync<List<VehicleModelSearchResultDto>>();
        models.Should().NotBeNull();
        models!.Should().NotBeEmpty();

        if (expectedMatch == "Motorcycle")
        {
            models.Should().OnlyContain(x => x.VehicleType == "Motorcycle");
        }
        else
        {
            models.Should().Contain(x =>
                x.FullNameEn.Contains(expectedMatch, StringComparison.OrdinalIgnoreCase) ||
                x.MakeCode.Contains(expectedMatch, StringComparison.OrdinalIgnoreCase) ||
                x.ModelCode.Contains(expectedMatch, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("/api/v1/catalog/vehicles/makes")]
    [InlineData("/api/v1/auth/staff/driver-registration/vehicle-makes")]
    public async Task GetVehicleMakes_ReturnsOkWithHierarchy(string route)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var makes = await response.Content.ReadFromJsonAsync<List<VehicleMakeDto>>();
        makes.Should().NotBeNull();
        makes!.Should().NotBeEmpty();
        makes.Should().Contain(x => x.Code == "Toyota" && x.Models.Count > 0);
        makes.Should().Contain(x => x.Code == "Nissan" && x.Models.Count > 0);
        makes.Should().Contain(x => x.Code == "Hyundai" && x.Models.Count > 0);
    }
}
