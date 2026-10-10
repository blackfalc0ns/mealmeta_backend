using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MealMate.ApiTests.Restaurant;

public sealed class RestaurantBranchAuthorizationTests : IClassFixture<EndpointTestFactory>
{
    private readonly HttpClient _client;

    public RestaurantBranchAuthorizationTests(EndpointTestFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
        }).CreateClient();
    }

    [Fact]
    public void Controller_HasAuthorizeAttribute()
    {
        var authAttr = typeof(RestaurantBranchesController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull("RestaurantBranchesController must require authorization at class level.");
    }

    [Fact]
    public void SensitiveActions_DoNotHaveAllowAnonymousAttribute()
    {
        var sensitiveMethods = new[]
        {
            nameof(RestaurantBranchesController.List),
            nameof(RestaurantBranchesController.Create),
            nameof(RestaurantBranchesController.ToggleStatus),
            nameof(RestaurantBranchesController.ListInvites),
            nameof(RestaurantBranchesController.ListStaff),
            nameof(RestaurantBranchesController.GetPermissions),
            nameof(RestaurantBranchesController.UpdatePermission),
            nameof(RestaurantBranchesController.CreateRole),
            nameof(RestaurantBranchesController.DeleteRole),
            nameof(RestaurantBranchesController.SendInvite),
            nameof(RestaurantBranchesController.ResendInvite)
        };

        foreach (var methodName in sensitiveMethods)
        {
            var method = typeof(RestaurantBranchesController).GetMethod(methodName);
            method.Should().NotBeNull($"Method {methodName} must exist on controller.");
            var allowAnon = method!.GetCustomAttribute<AllowAnonymousAttribute>();
            allowAnon.Should().BeNull($"Method {methodName} must NOT allow anonymous access.");
        }
    }

    [Fact]
    public void AcceptInvite_RetainsAllowAnonymous()
    {
        var method = typeof(RestaurantBranchesController).GetMethod(nameof(RestaurantBranchesController.AcceptInvite));
        method.Should().NotBeNull();
        var allowAnon = method!.GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().NotBeNull("AcceptInvite must allow anonymous access using one-time token.");
    }

    [Fact]
    public async Task Unauthenticated_ListBranches_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/restaurant/branches");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_CreateBranch_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/restaurant/branches", new
        {
            restaurantRegistrationId = Guid.NewGuid(),
            nameAr = "فرع تجريبي",
            addressAr = "شارع تجريبي",
            governorateAr = "العاصمة",
            contactPhone = "99999999"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_ToggleBranch_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync($"/api/v1/restaurant/branches/{Guid.NewGuid()}/toggle", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_ListInvites_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/restaurant/branches/invites");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_ListStaff_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/restaurant/branches/staff");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_GetPermissions_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/restaurant/branches/permissions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_CreateRole_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/restaurant/branches/roles", new
        {
            restaurantRegistrationId = Guid.NewGuid(),
            nameAr = "دور تجريبي"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AcceptInvite_WithInvalidData_DoesNotReturnUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/restaurant/branches/invites/accept", new
        {
            email = "staff@example.com",
            inviteToken = "invalid-token",
            password = "Password123!"
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
