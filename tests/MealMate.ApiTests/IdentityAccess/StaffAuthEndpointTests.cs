using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MealMate.ApiTests.IdentityAccess;

public sealed class StaffAuthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StaffAuthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Seed:Identity", "false");
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

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResendOtp_WhenCalledWithValidRole_ReturnsSuccess()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/resend-otp", new
        {
            phone = "+96500000000",
            role = "Driver"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ForgotPassword_WhenCalledWithValidRole_ReturnsSuccess()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/staff/forgot-password", new
        {
            phone = "+96500000000",
            role = "Driver"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
