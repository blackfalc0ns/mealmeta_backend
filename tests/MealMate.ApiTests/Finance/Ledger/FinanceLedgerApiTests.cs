using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Finance.Ledger;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.ApiTests.Finance.Ledger;

public sealed class FinanceLedgerApiFixture : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private string _connectionString = null!;
    public Guid EntityId { get; } = Guid.NewGuid();
    public Guid PeriodId { get; } = Guid.NewGuid();
    public Guid DebitAccountId { get; } = Guid.NewGuid();
    public Guid CreditAccountId { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _connectionString = _database.GetConnectionString();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.LegalEntities.Add(new LegalEntity
        {
            Id = EntityId, LegalNameAr = "كيان اختبار", LegalNameEn = "Ledger API Test",
            RegistrationNo = "LEDGER-API-" + EntityId.ToString("N"), Currency = "KWD", Status = LegalEntityStatus.Active,
        });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = PeriodId, LegalEntityId = EntityId, StartDate = new DateOnly(2026, 1, 1), EndDateExclusive = new DateOnly(2027, 1, 1),
        });
        db.ChartOfAccounts.AddRange(
            new ChartOfAccount { Id = DebitAccountId, LegalEntityId = EntityId, Code = "1000", NameAr = "أصل", NameEn = "Asset", AccountType = AccountType.Asset, NormalBalance = AccountNormalBalance.Debit, IsActive = true, IsPostable = true },
            new ChartOfAccount { Id = CreditAccountId, LegalEntityId = EntityId, Code = "3000", NameAr = "حقوق", NameEn = "Equity", AccountType = AccountType.Equity, NormalBalance = AccountNormalBalance.Credit, IsActive = true, IsPostable = true });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    public WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Seed:Identity", "false");
        builder.UseSetting("ConnectionStrings:MealMateDb", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = FinanceLedgerApiTests.Scheme;
                options.DefaultChallengeScheme = FinanceLedgerApiTests.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, FinanceLedgerApiTests.LedgerTestAuthHandler>(FinanceLedgerApiTests.Scheme, _ => { });
            foreach (var hosted in services.Where(x => x.ServiceType == typeof(IHostedService)).ToList()) services.Remove(hosted);
        });
    });

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options,
        NSubstitute.Substitute.For<IDomainEventDispatcher>());
}

public sealed class FinanceLedgerApiTests(FinanceLedgerApiFixture fixture) : IClassFixture<FinanceLedgerApiFixture>
{
    public const string Scheme = "FinanceLedgerTest";

    [Fact]
    public async Task HttpAuthenticationAndViewOnlyPermissionsAreEnforced()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/admin/finance/ledger/entities")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetActor(client, Guid.NewGuid(), PermissionCodes.LedgerView);
        var entities = await client.GetAsync("/api/v1/admin/finance/ledger/entities");
        entities.StatusCode.Should().Be(HttpStatusCode.OK);
        using var responseJson = JsonDocument.Parse(await entities.Content.ReadAsStringAsync());
        responseJson.RootElement.EnumerateArray().Should().ContainSingle(x => x.GetProperty("id").GetString() == fixture.EntityId.ToString());

        var create = await client.PostAsJsonAsync("/api/v1/admin/finance/ledger/sources", new { });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var approve = await client.PostAsJsonAsync($"/api/v1/admin/finance/ledger/sources/{Guid.NewGuid()}/approve",
            new { expectedFingerprint = "stale", expectedRowVersion = 1 });
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LedgerManagerCanConfigureARealAccountAndNonOverlappingPeriod()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        SetActor(client, Guid.NewGuid(), PermissionCodes.LedgerView, PermissionCodes.LedgerManage, PermissionCodes.LedgerPeriodsManage);
        var account = await client.PostAsJsonAsync("/api/v1/admin/finance/ledger/accounts", new
        {
            legalEntityId = fixture.EntityId, code = "5000", nameAr = "مصروف اختبار", nameEn = "Test expense",
            accountType = "Expense", normalBalance = "Debit", parentId = (Guid?)null, isPostable = true, requiresParty = false,
            effectiveFrom = "2027-01-01", effectiveToExclusive = (string?)null, reportingLineCode = (string?)null,
        });
        account.StatusCode.Should().Be(HttpStatusCode.OK, await account.Content.ReadAsStringAsync());
        using var accountJson = JsonDocument.Parse(await account.Content.ReadAsStringAsync());
        accountJson.RootElement.GetProperty("legalEntityId").GetGuid().Should().Be(fixture.EntityId);
        accountJson.RootElement.GetProperty("isPostable").GetBoolean().Should().BeTrue();

        var period = await client.PostAsJsonAsync("/api/v1/admin/finance/ledger/periods", new
        { legalEntityId = fixture.EntityId, startDate = "2027-01-01", endDateExclusive = "2028-01-01" });
        period.StatusCode.Should().Be(HttpStatusCode.OK, await period.Content.ReadAsStringAsync());
        using var periodJson = JsonDocument.Parse(await period.Content.ReadAsStringAsync());
        periodJson.RootElement.GetProperty("state").GetString().Should().Be("Open");
        periodJson.RootElement.GetProperty("closeVersion").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task RealSourceWorkflowRejectsSelfApprovalAndStaleDecisionsThenIndependentCheckerPosts()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var makerId = Guid.NewGuid();
        SetActor(client, makerId, PermissionCodes.LedgerManage, PermissionCodes.LedgerApprove, PermissionCodes.LedgerView);
        var created = await client.PostAsJsonAsync("/api/v1/admin/finance/ledger/sources", new
        {
            legalEntityId = fixture.EntityId,
            type = "Adjustment",
            businessDate = "2026-10-21",
            reason = "Accrual adjustment approved by finance",
            evidenceReference = "evidence://ledger/api-adjustment-1",
            lines = new object[]
            {
                new { accountId = fixture.DebitAccountId, debit = 10m, credit = (decimal?)null, partyId = (Guid?)null, partyType = (string?)null, memo = "Debit" },
                new { accountId = fixture.CreditAccountId, debit = (decimal?)null, credit = 10m, partyId = (Guid?)null, partyType = (string?)null, memo = "Credit" },
            },
            originalJournalEntryId = (Guid?)null,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var sourceId = createdJson.RootElement.GetProperty("id").GetGuid();
        var fingerprint = createdJson.RootElement.GetProperty("fingerprint").GetString()!;
        var rowVersion = createdJson.RootElement.GetProperty("rowVersion").GetInt64();

        var submittedResponse = await client.PostAsJsonAsync($"/api/v1/admin/finance/ledger/sources/{sourceId}/submit",
            new { expectedFingerprint = fingerprint, expectedRowVersion = rowVersion });
        submittedResponse.StatusCode.Should().Be(HttpStatusCode.OK, await submittedResponse.Content.ReadAsStringAsync());
        using var submittedJson = JsonDocument.Parse(await submittedResponse.Content.ReadAsStringAsync());
        var submittedFingerprint = submittedJson.RootElement.GetProperty("fingerprint").GetString()!;
        var submittedVersion = submittedJson.RootElement.GetProperty("rowVersion").GetInt64();

        var ownApproval = await client.PostAsJsonAsync($"/api/v1/admin/finance/ledger/sources/{sourceId}/approve",
            new { expectedFingerprint = submittedFingerprint, expectedRowVersion = submittedVersion });
        ownApproval.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        SetActor(client, Guid.NewGuid(), PermissionCodes.LedgerManage, PermissionCodes.LedgerApprove, PermissionCodes.LedgerView);
        var staleHash = await client.PostAsJsonAsync($"/api/v1/admin/finance/ledger/sources/{sourceId}/approve",
            new { expectedFingerprint = "stale", expectedRowVersion = submittedVersion });
        staleHash.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var staleVersion = await client.PostAsJsonAsync($"/api/v1/admin/finance/ledger/sources/{sourceId}/approve",
            new { expectedFingerprint = submittedFingerprint, expectedRowVersion = submittedVersion + 1 });
        staleVersion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var approval = await client.PostAsJsonAsync($"/api/v1/admin/finance/ledger/sources/{sourceId}/approve",
            new { expectedFingerprint = submittedFingerprint, expectedRowVersion = submittedVersion });
        approval.StatusCode.Should().Be(HttpStatusCode.OK, await approval.Content.ReadAsStringAsync());

        var post = await client.PostAsync($"/api/v1/admin/finance/ledger/sources/{sourceId}/process-approved", null);
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        using var postedJson = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        var journalId = postedJson.RootElement.GetProperty("journalEntryId").GetGuid();
        var detailResponse = await client.GetAsync($"/api/v1/admin/finance/ledger/journals/{journalId}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync());
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        detail.RootElement.GetProperty("sourceDocumentId").GetGuid().Should().Be(sourceId);
        detail.RootElement.GetProperty("evidenceReference").GetString().Should().Be("evidence://ledger/api-adjustment-1");
        detail.RootElement.GetProperty("lines").GetArrayLength().Should().Be(2);

        SetActor(client, Guid.NewGuid(), PermissionCodes.LedgerView, PermissionCodes.LedgerManage, PermissionCodes.LedgerApprove);
        client.DefaultRequestHeaders.Remove("X-Ledger-Test-Tenant");
        client.DefaultRequestHeaders.Add("X-Ledger-Test-Tenant", Guid.NewGuid().ToString());
        var otherTenant = await client.GetAsync($"/api/v1/admin/finance/ledger/journals/{journalId}");
        otherTenant.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        SetActor(client, Guid.NewGuid(), PermissionCodes.LedgerView, PermissionCodes.LedgerManage, PermissionCodes.LedgerApprove);
        client.DefaultRequestHeaders.Remove("X-Ledger-Test-UserType");
        client.DefaultRequestHeaders.Add("X-Ledger-Test-UserType", "Restaurant");
        var restaurant = await client.GetAsync($"/api/v1/admin/finance/ledger/journals/{journalId}");
        restaurant.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    private void SetActor(HttpClient client, Guid actorId, params string[] permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Ledger-Test-Actor");
        client.DefaultRequestHeaders.Remove("X-Ledger-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Ledger-Test-Actor", actorId.ToString());
        client.DefaultRequestHeaders.Add("X-Ledger-Test-Permissions", string.Join(',', permissions));
        client.DefaultRequestHeaders.Remove("X-Ledger-Test-Tenant");
        client.DefaultRequestHeaders.Add("X-Ledger-Test-Tenant", fixture.EntityId.ToString());
        client.DefaultRequestHeaders.Remove("X-Ledger-Test-UserType");
        client.DefaultRequestHeaders.Add("X-Ledger-Test-UserType", "Admin");
    }

    public sealed class LedgerTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Ledger-Test-Permissions", out var permissions))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Request.Headers["X-Ledger-Test-Actor"].ToString()),
                new(AppClaimTypes.UserType, Request.Headers["X-Ledger-Test-UserType"].ToString()),
                new(AppClaimTypes.TenantId, Request.Headers["X-Ledger-Test-Tenant"].ToString()),
            };
            claims.AddRange(permissions.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim(AppClaimTypes.Permission, permission)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, FinanceLedgerApiTests.Scheme)), FinanceLedgerApiTests.Scheme)));
        }
    }
}
