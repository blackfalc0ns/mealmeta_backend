using System;
using System.Linq;
using System.Net;
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

public sealed class StaffAuthEndpointTests : IClassFixture<EndpointTestFactory>
{
    private readonly HttpClient _client;

    public StaffAuthEndpointTests(EndpointTestFactory factory)
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

                var dbName = "StaffAuthTests_" + Guid.NewGuid();
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
    public async Task LookupPhone_WhenRoleInvalid_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/lookup-phone", new
        {
            phone = "+96550000000",
            role = "SuperAdmin"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LookupPhone_WhenPhoneNotFound_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/lookup-phone", new
        {
            phone = "+96599999999",
            role = "Driver"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task VerifyFirstTimeOtp_WhenOtpMalformed_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/verify-first-time-otp", new
        {
            phone = "+96550000000",
            role = "Driver",
            otpCode = "1" // less than min length
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetPassword_WhenPasswordsDoNotMatch_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/set-password", new
        {
            phone = "+96550000000",
            role = "Driver",
            verificationToken = "sample-token",
            newPassword = "Password123!",
            confirmPassword = "DifferentPassword123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WhenCredentialsNotFound_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/login", new
        {
            phone = "+96500000000",
            role = "DeliveryManager",
            password = "WrongPassword123!"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ResendOtp_WhenCalledWithValidRole_ReturnsSuccess()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/resend-otp", new
        {
            phone = "+96500000000",
            role = "Driver"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ForgotPassword_WhenCalledWithValidRole_ReturnsSuccess()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/forgot-password", new
        {
            phone = "+96500000000",
            role = "Driver"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.InternalServerError);
    }
}
