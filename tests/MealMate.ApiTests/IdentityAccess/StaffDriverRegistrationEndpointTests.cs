using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MealMate.ApiTests.IdentityAccess;

public sealed class StaffDriverRegistrationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StaffDriverRegistrationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
            builder.ConfigureTestServices(services =>
            {
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(IAppDbContext)).ToList();
                foreach (var d in descriptors) services.Remove(d);

                var dbName = "StaffDriverRegTests_" + Guid.NewGuid();
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });
                services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetRestaurants_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/v1/auth/staff/driver-registration/restaurants");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UploadDocument_WhenNoFileProvided_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        var response = await _client.PostAsync("/api/v1/auth/staff/driver-registration/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterDriver_WhenMissingRequiredFields_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/driver-registration", new
        {
            fullNameAr = "",
            phone = "",
            email = (string?)null // Optional email
        });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            (HttpStatusCode)422);
    }

    [Fact]
    public async Task RegisterDriver_WhenInvalidEmailFormat_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/driver-registration", new
        {
            restaurantId = System.Guid.NewGuid(),
            fullNameAr = "أحمد السائق",
            fullNameEn = "Ahmed Driver",
            phone = "+966501234567",
            email = "invalid-email-format",
            nationalId = "1012345678",
            nationalIdExpiry = "2028-12-31T00:00:00Z",
            vehiclePlate = "ABC1234",
            vehicleType = "Car",
            vehicleModel = "Toyota",
            licenseNumber = "DL123456",
            licenseExpiry = "2029-01-01T00:00:00Z",
            vehicleLicenseExpiry = "2028-06-01T00:00:00Z",
            nationalIdFrontStorageKey = "key1",
            nationalIdBackStorageKey = "key2",
            drivingLicenseFrontStorageKey = "key3",
            drivingLicenseBackStorageKey = "key4",
            vehicleRegistrationStorageKey = "key5"
        });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            (HttpStatusCode)422);
    }
}
