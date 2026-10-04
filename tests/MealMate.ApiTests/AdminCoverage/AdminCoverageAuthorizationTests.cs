using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using MealMate.Application.Common.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.ApiTests.AdminCoverage;

public sealed class AdminCoverageAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scheme = "CoverageTest";
    private readonly HttpClient _client;

    public AdminCoverageAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = Scheme;
                    options.DefaultChallengeScheme = Scheme;
                }).AddScheme<AuthenticationSchemeOptions, CoverageTestAuthHandler>(Scheme, _ => { });
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Snapshot_RejectsUnauthenticatedRequest()
    {
        var response = await _client.GetAsync("/api/v1/admin/coverage/snapshot?countryCode=KW&serviceDate=2026-10-03");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Snapshot_RequiresRestaurantRegistrationViewPermission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/admin/coverage/snapshot?countryCode=KW&serviceDate=2026-10-03");
        request.Headers.Add("X-Coverage-Test-Auth", "admin-without-permission");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Trips_RequiresDedicatedCoverageOperationsPermission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/admin/coverage/trips?countryCode=KW&serviceDate=2026-10-03");
        request.Headers.Add("X-Coverage-Test-Auth", "admin-without-permission");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlannedRoute_RequiresDedicatedCoverageOperationsPermission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/admin/coverage/trips/{Guid.NewGuid()}/planned-route?countryCode=KW");
        request.Headers.Add("X-Coverage-Test-Auth", "admin-without-permission");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed class CoverageTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public CoverageTestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Coverage-Test-Auth", out var value)
                || value != "admin-without-permission")
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "CountryAdmin"),
                new Claim(AppClaimTypes.UserType, "Admin"),
            };
            var identity = new ClaimsIdentity(claims, AdminCoverageAuthorizationTests.Scheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), AdminCoverageAuthorizationTests.Scheme)));
        }
    }
}
