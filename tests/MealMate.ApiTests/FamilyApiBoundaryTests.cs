using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.ApiTests.FamilyManager;

public sealed class FamilyApiBoundaryTests
{
    [Fact]
    public async Task Soft_deleted_pending_access_cannot_hide_the_setup_gate_from_an_existing_session()
    {
        await using var fx = await Fixture.Create();
        fx.Access.RequiresPasswordSetup = true;
        fx.Access.IsDeleted = true;
        await fx.Db.SaveChangesAsync();
        using var response = await fx.Get(fx.Actor, $"members/{fx.Member.Id}/capabilities");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("family.password_setup_required", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_member_session_is_blocked_during_setup_in_both_languages_but_can_reach_anonymous_setup(bool removed)
    {
        await using var fx = await Fixture.Create();
        fx.Access.RequiresPasswordSetup = true;
        fx.Access.RemovedAtUtc = removed ? DateTime.UtcNow : null;
        await fx.Db.SaveChangesAsync();
        var details = new List<string>();
        foreach (var language in new[] { "ar", "en", "ar" })
        {
            using var response = await fx.Get(fx.Actor, $"members/{fx.Member.Id}/capabilities", language);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("family.password_setup_required", body.GetProperty("code").GetString());
            details.Add(body.GetProperty("detail").GetString()!);
        }
        Assert.Equal(details[0], details[2]);
        Assert.NotEqual(details[0], details[1]);
        using var setup = await fx.Get(fx.Actor, "setup-probe");
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        using var manager = await fx.Get(fx.Manager, $"members/{fx.Member.Id}/capabilities");
        Assert.Equal(HttpStatusCode.OK, manager.StatusCode);
    }

    [Fact]
    public async Task Member_session_context_exposes_own_identity_and_effective_capabilities_only()
    {
        await using var fx = await Fixture.Create();
        using var response = await fx.Get(fx.Actor, "me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contexts = await response.Content.ReadFromJsonAsync<JsonElement>();
        var own = Assert.Single(contexts.EnumerateArray());
        Assert.Equal(fx.Group.Id, own.GetProperty("groupId").GetGuid());
        Assert.Equal(fx.Member.Id, own.GetProperty("memberId").GetGuid());
        Assert.False(own.GetProperty("isManager").GetBoolean());
        var capabilities = own.GetProperty("capabilities").EnumerateArray().ToList();
        Assert.True(capabilities.Single(x => x.GetProperty("action").GetString() == "ViewCalendar").GetProperty("allowed").GetBoolean());
        Assert.False(capabilities.Single(x => x.GetProperty("action").GetString() == "EditDailyMeals").GetProperty("allowed").GetBoolean());
        Assert.False(own.TryGetProperty("quotedFinalPrice", out _));
        Assert.False(own.TryGetProperty("members", out _));

        using var outsider = await fx.Get(fx.Outsider, "me");
        Assert.Equal(HttpStatusCode.OK, outsider.StatusCode);
        Assert.Empty((await outsider.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        using var manager = await fx.Get(fx.Manager, "me");
        Assert.Equal(HttpStatusCode.OK, manager.StatusCode);
        Assert.True(Assert.Single((await manager.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()).GetProperty("isManager").GetBoolean());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Member_session_context_denies_paused_or_disabled_access(bool separate, bool paused)
    {
        await using var fx = await Fixture.Create();
        fx.Access.HasSeparateAccount = separate;
        fx.Access.IsPaused = paused;
        await fx.Db.SaveChangesAsync();
        using var response = await fx.Get(fx.Actor, "me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Existing_sessions_and_outsiders_are_denied_with_ar_en_ar_localized_reason()
    {
        await using var fx = await Fixture.Create();
        var details = new List<string>();
        foreach (var language in new[] { "ar", "en", "ar" })
        {
            using var response = await fx.Get(fx.Outsider, $"members/{fx.Member.Id}/capabilities", language);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("family.not_member", body.GetProperty("code").GetString());
            details.Add(body.GetProperty("detail").GetString()!);
        }
        Assert.Equal(details[0], details[2]);
        Assert.NotEqual(details[0], details[1]);
        Assert.DoesNotContain("family.not_member", details[0]);
        foreach (var pause in new[] { true, false })
        {
            fx.Access.IsPaused = pause;
            fx.Access.HasSeparateAccount = pause;
            await fx.Db.SaveChangesAsync();
            using var response = await fx.Get(fx.Actor, $"members/{fx.Member.Id}/capabilities");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var manager = await fx.Get(fx.Manager, $"members/{fx.Member.Id}/capabilities");
            Assert.Equal(HttpStatusCode.OK, manager.StatusCode);
        }
    }

    [Fact]
    public async Task Capabilities_roster_and_permission_catalog_expose_real_supported_contracts()
    {
        await using var fx = await Fixture.Create();
        using var capabilities = await fx.Get(fx.Manager, $"members/{fx.Member.Id}/capabilities");
        var rows = await capabilities.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var row in rows.EnumerateArray())
        {
            var unsupported = new[] { "UnitExchange", "ChangeProgram", "EditMealsAndDays", "ChangeDeliveryTime" }
                .Contains(row.GetProperty("action").GetString());
            Assert.Equal(!unsupported, row.GetProperty("allowed").GetBoolean());
            if (!unsupported) Assert.Equal(JsonValueKind.Null, row.GetProperty("reasonCode").ValueKind);
            else Assert.Equal("family.action_not_available", row.GetProperty("reasonCode").GetString());
        }
        using var own = await fx.Get(fx.Actor, $"members/{fx.Member.Id}/capabilities");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var catalog = await fx.Get(fx.Manager, $"members/{fx.Member.Id}/permissions");
        var permissions = await catalog.Content.ReadFromJsonAsync<JsonElement>();
        var items = permissions.GetProperty("groups").EnumerateArray().SelectMany(x => x.GetProperty("permissions").EnumerateArray()).ToList();
        Assert.Equal(19, items.Count);
        Assert.All(items, x => { Assert.NotEqual(x.GetProperty("labelAr").GetString(), x.GetProperty("labelEn").GetString());
            Assert.DoesNotContain("family.permission.", x.GetProperty("labelEn").GetString()); });
        foreach (var status in new[] { FamilyGroupStatus.Draft, FamilyGroupStatus.CheckoutPending, FamilyGroupStatus.Active })
        {
            fx.Group.Status = status;
            await fx.Db.SaveChangesAsync();
            using var home = await fx.Get(fx.Manager, "home");
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            var body = await home.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(body.GetProperty("todaySchedule").EnumerateArray());
            var member = body.GetProperty("members").EnumerateArray().Single();
            Assert.Equal("Unsubscribed", member.GetProperty("lifecycle").GetProperty("displayStatus").GetString());
        }
        using var access = await fx.Get(fx.Manager, $"members/{fx.Member.Id}/access");
        var account = await access.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, account.GetProperty("generatedPassword").ValueKind);
        Assert.Equal(JsonValueKind.Null, account.GetProperty("inviteDeepLink").ValueKind);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Manager { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid Outsider { get; } = Guid.NewGuid();
        public AppDbContext Db { get; private set; } = null!;
        public FamilyGroup Group { get; private set; } = null!;
        public FamilyMember Member { get; private set; } = null!;
        public FamilyMemberAccess Access { get; private set; } = null!;
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        public static async Task<Fixture> Create()
        {
            var fx = new Fixture();
            fx.Db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                Substitute.For<IDomainEventDispatcher>());
            fx.Group = new() { ManagerCustomerUserId = fx.Manager, Name = "Family", Status = FamilyGroupStatus.Active };
            fx.Member = new() { FamilyGroupId = fx.Group.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
                RelationshipCode = "child", NutritionalGoalCode = "maintain", SetupStatus = FamilyMemberSetupStatus.Ready };
            fx.Group.Members.Add(fx.Member);
            fx.Access = new() { FamilyMemberId = fx.Member.Id, LinkedCustomerUserId = fx.Actor, HasSeparateAccount = true };
            fx.Db.AddRange(fx.Group, fx.Access, new FamilyMemberPermissions { FamilyMemberId = fx.Member.Id, ViewCalendar = true });
            await fx.Db.SaveChangesAsync();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(CustomerFamilyManagerController).Assembly);
            builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(FamilyManagerHandlers).Assembly));
            builder.Services.AddMealMateLocalization();
            builder.Services.AddSingleton<IAppDbContext>(fx.Db);
            builder.Services.AddSingleton(Substitute.For<IDateTimeProvider>());
            builder.Services.AddSingleton(Substitute.For<IUserAccountService>());
            var pricing = Substitute.For<IFamilyPricingSettingsReader>();
            pricing.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
            builder.Services.AddSingleton(pricing);
            builder.Services.AddScoped<IFamilyActionPolicy, FamilyActionPolicy>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser>(services => {
                var user = Substitute.For<ICurrentUser>();
                var id = services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier);
                user.UserId.Returns(Guid.Parse(id!));
                user.UserType.Returns(UserType.Customer);
                return user;
            });
            builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
            builder.Services.AddAuthorization();
            fx._app = builder.Build();
            fx._app.UseMealMateLocalization();
            fx._app.UseAuthentication();
            fx._app.UseAuthorization();
            fx._app.UseMiddleware<MealMate.Api.Middleware.FamilyPasswordSetupMiddleware>();
            fx._app.MapControllers();
            fx._app.MapGet("/api/v1/customer/family/setup-probe", () => Results.Ok()).AllowAnonymous();
            await fx._app.StartAsync();
            fx._client = fx._app.GetTestClient();
            return fx;
        }
        public Task<HttpResponseMessage> Get(Guid actor, string route, string language = "en")
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/family/" + route);
            request.Headers.Add("X-Fixture-Actor", actor.ToString());
            request.Headers.Add("Accept-Language", language);
            return _client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); await Db.DisposeAsync(); }
    }
    public sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Fixture-Actor"].ToString())], "fixture");
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), "fixture")));
        }
    }
}
