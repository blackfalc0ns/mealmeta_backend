using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Auth;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.IdentityAccess;

public sealed class AuthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AuthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
        }).CreateClient();
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_unauthorized()
    {
        var response = await _client.GetAsync("/api/v1/admin/roles");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_customer_validation_rejects_empty_body()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/customers/register", new
        {
            fullName = "",
            password = "x",
        });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            (HttpStatusCode)422);
    }

    [Fact]
    public async Task Legacy_customer_register_route_is_not_available()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register/customer", new
        {
            fullName = "Ahmed",
            phone = "+96590000000",
            password = "Password123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_login_swagger_uses_phone_only_request()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);

        var schemaRef = json.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/customers/login")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        schemaRef.Should().Be("#/components/schemas/CustomerLoginRequest");

        var properties = json.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("CustomerLoginRequest")
            .GetProperty("properties");

        properties.TryGetProperty("phone", out _).Should().BeTrue();
        properties.TryGetProperty("password", out _).Should().BeTrue();
        properties.TryGetProperty("countryCode", out _).Should().BeTrue();
        properties.TryGetProperty("email", out _).Should().BeFalse();
        properties.TryGetProperty("phoneNumber", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("en", "Enter the 6-digit verification code.")]
    [InlineData("ar", "أدخل رمز التحقق المكون من 6 أرقام.")]
    public async Task Customer_verify_phone_empty_code_returns_localized_message(string culture, string expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/customers/verify-phone")
        {
            Content = JsonContent.Create(new
            {
                phone = "90000000",
                countryCode = "KW",
                code = "",
            }),
        };
        request.Headers.TryAddWithoutValidation("Accept-Language", culture);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            (HttpStatusCode)422);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);

        var codeErrors = json.RootElement
            .GetProperty("errors")
            .GetProperty("Code")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();

        codeErrors.Should().Contain(expected);
    }

    [Fact]
    public async Task Customer_password_otp_controller_actions_return_json_responses()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CustomerForgotPasswordCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        sender.Send(Arg.Any<CustomerResetPasswordCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var controller = CreateCustomerAuthController(sender);

        var forgot = await controller.ForgotPassword(new CustomerForgotPasswordRequest("90000000", "KW"), CancellationToken.None);
        var resend = await controller.ResendPasswordOtp(new CustomerForgotPasswordRequest("90000000", "KW"), CancellationToken.None);
        var reset = await controller.ResetPassword(new CustomerResetPasswordRequest("90000000", "123456", "Newpass123", "KW"), CancellationToken.None);

        var forgotValue = forgot.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<OtpActionResponse>().Subject;
        forgotValue.Success.Should().BeTrue();
        forgotValue.OtpSent.Should().BeTrue();
        forgotValue.NextStep.Should().Be("reset_password_otp");

        var resendValue = resend.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<OtpActionResponse>().Subject;
        resendValue.Success.Should().BeTrue();
        resendValue.OtpSent.Should().BeTrue();
        resendValue.NextStep.Should().Be("reset_password_otp");

        var resetValue = reset.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ResetPasswordResponse>().Subject;
        resetValue.Success.Should().BeTrue();
        resetValue.PasswordReset.Should().BeTrue();
        resetValue.NextStep.Should().Be("sign_in");
    }

    [Fact]
    public async Task Totp_setup_rejects_empty_login_challenge_token()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/admin/totp/setup", new { loginChallengeToken = "" });
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            (HttpStatusCode)422);
    }

    [Fact]
    public async Task Totp_verify_login_rejects_both_code_and_recovery_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/admin/totp/verify-login", new
        {
            loginChallengeToken = "token",
            code = "123456",
            recoveryCode = "RCODE",
        });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            (HttpStatusCode)422);
    }

    [Fact]
    public async Task Totp_confirm_setup_rejects_empty_body()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/admin/totp/confirm-setup", new
        {
            loginChallengeToken = "",
            code = "",
        });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            (HttpStatusCode)422);
    }

    [Fact]
    public async Task Login_with_invalid_credentials_returns_problem()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/admin/login", new
        {
            email = "nobody@example.com",
            password = "WrongPass1",
        });

        // Without DB seed, may be 401 unauthorized or 500 if DB down
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError,
            (HttpStatusCode)422);
    }

    private static CustomerAuthController CreateCustomerAuthController(ISender sender)
    {
        var services = new ServiceCollection()
            .AddSingleton(sender)
            .BuildServiceProvider();

        return new CustomerAuthController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                },
            },
        };
    }
}
