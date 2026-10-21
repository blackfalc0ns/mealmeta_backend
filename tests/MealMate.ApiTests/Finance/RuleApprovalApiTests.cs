using System.Reflection;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Authorization;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Finance.Governance;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MealMate.ApiTests.Finance;

public sealed class FinanceGovernanceApiFixture : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private string _connectionString = null!;
    public Guid LegalEntityId { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _connectionString = _database.GetConnectionString();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.LegalEntities.Add(new LegalEntity { Id = LegalEntityId, LegalNameAr = "اختبار", LegalNameEn = "Finance API Test", RegistrationNo = "API-F01", Currency = "KWD", Status = LegalEntityStatus.Active });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    public WebApplicationFactory<Program> CreateFactory()
    {
        var connectionString = _connectionString;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
            builder.UseSetting("ConnectionStrings:MealMateDb", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = RuleApprovalApiTests.Scheme; options.DefaultChallengeScheme = RuleApprovalApiTests.Scheme; })
                    .AddScheme<AuthenticationSchemeOptions, RuleApprovalApiTests.FinanceGovernanceTestAuthHandler>(RuleApprovalApiTests.Scheme, _ => { });
                foreach (var hosted in services.Where(x => x.ServiceType == typeof(IHostedService)).ToList()) services.Remove(hosted);
            });
        });
    }

    public async Task<Guid> SeedActiveLegalEntityAsync()
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.LegalEntities.Add(new LegalEntity { Id = id, LegalNameAr = "اختبار", LegalNameEn = "Finance API Test", RegistrationNo = "API-F01-" + id.ToString("N"), Currency = "KWD", Status = LegalEntityStatus.Active });
        await db.SaveChangesAsync();
        return id;
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, NSubstitute.Substitute.For<IDomainEventDispatcher>());
}

public sealed class RuleApprovalApiTests : IClassFixture<FinanceGovernanceApiFixture>
{
    public const string Scheme = "FinanceGovernanceTest";
    private readonly FinanceGovernanceApiFixture _fixture;
    public RuleApprovalApiTests(FinanceGovernanceApiFixture fixture) => _fixture = fixture;
    [Fact]
    public void GovernanceEndpointsHaveDistinctViewManageAndApprovalPermissions()
    {
        var type = typeof(AdminFinanceGovernanceController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/admin/finance/governance");
        PermissionOf(type, nameof(AdminFinanceGovernanceController.List)).Should().Be(PermissionCodes.FinanceGovernanceView);
        PermissionOf(type, nameof(AdminFinanceGovernanceController.Get)).Should().Be(PermissionCodes.FinanceGovernanceView);
        PermissionOf(type, nameof(AdminFinanceGovernanceController.Simulate)).Should().Be(PermissionCodes.FinanceGovernanceView);
        PermissionOf(type, nameof(AdminFinanceGovernanceController.Create)).Should().Be(PermissionCodes.FinanceGovernanceManage);
        PermissionOf(type, nameof(AdminFinanceGovernanceController.Submit)).Should().Be(PermissionCodes.FinanceGovernanceManage);
        PermissionOf(type, nameof(AdminFinanceGovernanceController.Approve)).Should().Be(PermissionCodes.FinanceGovernanceApprove);
        PermissionOf(type, nameof(AdminFinanceGovernanceController.Reject)).Should().Be(PermissionCodes.FinanceGovernanceApprove);
    }

    [Fact]
    public void DecisionsRequireExpectedFingerprintAndRowVersion()
    {
        typeof(FinanceRuleDecisionRequest).GetProperties().Select(x => x.Name)
            .Should().Contain(["ExpectedFingerprint", "ExpectedRowVersion"]);
        typeof(FinanceRuleRejectionRequest).GetProperties().Select(x => x.Name)
            .Should().Contain(["ExpectedFingerprint", "ExpectedRowVersion", "Reason"]);
    }

    [Fact]
    public async Task GovernanceEndpointsRequireAuthenticationAndSeparatePermissions()
    {
        using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();
        var unauthenticated = await client.GetAsync("/api/v1/admin/finance/governance/rules");
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/finance/governance/rules") { Content = JsonContent.Create(new { }) };
        create.Headers.Add("X-Finance-Test-Permissions", PermissionCodes.FinanceGovernanceView);
        (await client.SendAsync(create)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/finance/governance/rules/{Guid.NewGuid()}/approve") { Content = JsonContent.Create(new { expectedFingerprint = "hash", expectedRowVersion = 1 }) };
        approve.Headers.Add("X-Finance-Test-Permissions", PermissionCodes.FinanceGovernanceView);
        (await client.SendAsync(approve)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatorCannotApproveOwnPayloadApiReturnsForbidden()
    {
        using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();
        var actor = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Finance-Test-Actor", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Finance-Test-Permissions", PermissionCodes.FinanceGovernanceManage + "," + PermissionCodes.FinanceGovernanceApprove + "," + PermissionCodes.FinanceGovernanceView);
        var created = await CreateRuleAsync(client, "api-self-" + Guid.NewGuid().ToString("N"));
        var submitted = await client.PostAsync($"/api/v1/admin/finance/governance/rules/{created}/submit", JsonContent.Create(new { }));
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        var submission = await submitted.Content.ReadFromJsonAsync<FinancialRuleSubmitResult>();
        submission.Should().NotBeNull();

        var ownApproval = await client.PostAsJsonAsync($"/api/v1/admin/finance/governance/rules/{created}/approve", new { expectedFingerprint = submission!.SubmissionFingerprint, expectedRowVersion = submission.RowVersion });
        ownApproval.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Remove("X-Finance-Test-Actor");
        client.DefaultRequestHeaders.Add("X-Finance-Test-Actor", Guid.NewGuid().ToString());
        var checkerApproval = await client.PostAsJsonAsync($"/api/v1/admin/finance/governance/rules/{created}/approve", new { expectedFingerprint = submission.SubmissionFingerprint, expectedRowVersion = submission.RowVersion });
        checkerApproval.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StaleDecisionAndEnumWireValuesAreEnforcedOverHttp()
    {
        using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Finance-Test-Actor", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Finance-Test-Permissions", PermissionCodes.FinanceGovernanceManage + "," + PermissionCodes.FinanceGovernanceApprove + "," + PermissionCodes.FinanceGovernanceView);
        var created = await CreateRuleAsync(client, "api-wire-" + Guid.NewGuid().ToString("N"));
        var submitted = await client.PostAsync($"/api/v1/admin/finance/governance/rules/{created}/submit", JsonContent.Create(new { }));
        var submission = (await submitted.Content.ReadFromJsonAsync<FinancialRuleSubmitResult>())!;
        client.DefaultRequestHeaders.Remove("X-Finance-Test-Actor");
        client.DefaultRequestHeaders.Add("X-Finance-Test-Actor", Guid.NewGuid().ToString());
        var staleHash = await client.PostAsJsonAsync($"/api/v1/admin/finance/governance/rules/{created}/approve", new { expectedFingerprint = "stale", expectedRowVersion = submission.RowVersion });
        staleHash.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var staleVersion = await client.PostAsJsonAsync($"/api/v1/admin/finance/governance/rules/{created}/approve", new { expectedFingerprint = submission.SubmissionFingerprint, expectedRowVersion = submission.RowVersion + 1 });
        staleVersion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var approval = await client.PostAsJsonAsync($"/api/v1/admin/finance/governance/rules/{created}/approve", new { expectedFingerprint = submission.SubmissionFingerprint, expectedRowVersion = submission.RowVersion });
        approval.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await client.GetAsync("/api/v1/admin/finance/governance/rules");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var record = json.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetString() == created);
        record.GetProperty("status").GetString().Should().Be("Approved");
        record.GetProperty("scopeType").GetString().Should().Be("Global");
    }

    private async Task<string> CreateRuleAsync(HttpClient client, string versionCode)
    {
        var legalEntityId = await _fixture.SeedActiveLegalEntityAsync();
        var response = await client.PostAsJsonAsync("/api/v1/admin/finance/governance/rules", new
        {
            legalEntityId, ruleKey = "CANCELLATION", versionCode,
            scopeType = "Global", scopeEntityId = (Guid?)null, schemaVersion = 1,
            payloadJson = "{\"minimumExecutedDays\":4,\"minimumUnexecutedExclusive\":2}",
            reason = "API test rule", effectiveFromUtc = "2026-10-20T00:00:00Z", effectiveToUtc = (DateTime?)null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("ruleVersionId").GetString()!;
    }

    private static string PermissionOf(Type controller, string methodName) =>
        controller.GetMethod(methodName)!.GetCustomAttribute<HasPermissionAttribute>()!.Policy!
            .Replace(PermissionPolicyProvider.PolicyPrefix, string.Empty, StringComparison.Ordinal);

    public sealed class FinanceGovernanceTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Finance-Test-Permissions", out var values))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Request.Headers["X-Finance-Test-Actor"].ToString() is { Length: > 0 } actor ? actor : Guid.NewGuid().ToString()),
                new(AppClaimTypes.UserType, "Admin"),
            };
            claims.AddRange(values.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim(AppClaimTypes.Permission, permission)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, RuleApprovalApiTests.Scheme)), RuleApprovalApiTests.Scheme)));
        }
    }
}
