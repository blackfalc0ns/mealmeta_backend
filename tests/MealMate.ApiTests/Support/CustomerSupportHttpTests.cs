using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Support;

public sealed class CustomerSupportHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;

    public CustomerSupportHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CustomerSupportOptionsAreAuthenticatedBilingualFixedCatalogs()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/customer/support/options")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var driver = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Driver, ["Driver"], []));
        (await driver.GetAsync("/api/v1/customer/support/options")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Admin, ["SupportAdmin"], ["support:view"]));
        (await admin.GetAsync("/api/v1/customer/support/options")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var customer = NewCustomer();
        using var bilingualRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/support/options");
        bilingualRequest.Headers.Add("X-Bilingual", "true");
        var response = await customer.SendAsync(bilingualRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        ReadOptionValues(json.RootElement.GetProperty("issues"))
            .Should().BeEquivalentTo("FoodQuality", "MissingItems", "WrongItems", "DamagedPackaging", "DeliveryDelay",
                "NotDelivered", "Payment", "Subscription", "Account", "Other");
        ReadOptionValues(json.RootElement.GetProperty("suggestions"))
            .Should().BeEquivalentTo("Meals", "Delivery", "AppExperience", "Service", "Other");
        json.RootElement.GetProperty("issues")[0].GetProperty("labelAr").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("issues")[0].GetProperty("labelEn").GetString().Should().NotBeNullOrWhiteSpace();

        using var englishRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/support/options");
        englishRequest.Headers.Add("Accept-Language", "en");
        using var englishResponse = await customer.SendAsync(englishRequest);
        using var englishJson = JsonDocument.Parse(await englishResponse.Content.ReadAsStringAsync());
        englishJson.RootElement.GetProperty("issues")[0].GetProperty("label").GetString().Should().Be("Food quality");

        using var arabicRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/support/options");
        arabicRequest.Headers.Add("Accept-Language", "ar");
        using var arabicResponse = await customer.SendAsync(arabicRequest);
        using var arabicJson = JsonDocument.Parse(await arabicResponse.Content.ReadAsStringAsync());
        arabicJson.RootElement.GetProperty("issues")[0].GetProperty("label").GetString().Should().Be("جودة الطعام");
    }

    [Fact]
    public async Task CustomerCanCreateIdempotentIssueAndSuggestionAndListsOnlyMatchingOwnedCases()
    {
        var ownerId = Guid.NewGuid();
        using var customer = _fixture.CreateClientForUser(new TokenUser(ownerId, UserType.Customer, ["Customer"], []));
        using var other = NewCustomer();
        using var driver = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Driver, ["Driver"], []));

        var issueRequest = new { typeCode = "  missingitems ", subject = "Missing item", description = "One item was missing.", idempotencyKey = "customer-issue-one" };
        var issueResponse = await customer.PostAsJsonAsync("/api/v1/customer/support/issues", issueRequest);
        issueResponse.StatusCode.Should().Be(HttpStatusCode.Created, await issueResponse.Content.ReadAsStringAsync());
        using var issueJson = JsonDocument.Parse(await issueResponse.Content.ReadAsStringAsync());
        var issueId = issueJson.RootElement.GetProperty("id").GetGuid();
        issueJson.RootElement.GetProperty("kind").GetString().Should().Be("Complaint");
        issueJson.RootElement.GetProperty("category").GetString().Should().Be("customer.issue.MissingItems");

        var replay = await customer.PostAsJsonAsync("/api/v1/customer/support/issues", issueRequest);
        replay.StatusCode.Should().Be(HttpStatusCode.Created, await replay.Content.ReadAsStringAsync());
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        replayJson.RootElement.GetProperty("id").GetGuid().Should().Be(issueId);
        var changedReplay = await customer.PostAsJsonAsync("/api/v1/customer/support/issues", issueRequest with { subject = "Different subject" });
        changedReplay.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var suggestion = await customer.PostAsJsonAsync("/api/v1/customer/support/suggestions", new
        {
            typeCode = "Meals", subject = "More vegetarian meals", description = "Please add more options.", idempotencyKey = "customer-suggestion-one"
        });
        suggestion.StatusCode.Should().Be(HttpStatusCode.Created, await suggestion.Content.ReadAsStringAsync());
        using var suggestionJson = JsonDocument.Parse(await suggestion.Content.ReadAsStringAsync());
        var suggestionId = suggestionJson.RootElement.GetProperty("id").GetGuid();
        suggestionJson.RootElement.GetProperty("kind").GetString().Should().Be("GeneralRequest");
        suggestionJson.RootElement.GetProperty("category").GetString().Should().Be("customer.suggestion.Meals");

        var legacy = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "legacy-order", "Older issue", "Existing category is preserved.", "customer-legacy-support"));
        legacy.StatusCode.Should().Be(HttpStatusCode.Created, await legacy.Content.ReadAsStringAsync());

        var issues = await customer.GetFromJsonAsync<JsonElement>("/api/v1/customer/support/issues");
        issues.GetProperty("totalCount").GetInt32().Should().Be(2);
        var issueItems = issues.GetProperty("items").EnumerateArray().ToArray();
        issueItems.Should().ContainSingle(item => item.GetProperty("id").GetGuid() == issueId
            && item.GetProperty("typeCode").GetString() == "MissingItems");
        issueItems.Should().ContainSingle(item => item.GetProperty("category").GetString() == "legacy-order"
            && item.GetProperty("typeCode").ValueKind == JsonValueKind.Null);

        var suggestions = await customer.GetFromJsonAsync<JsonElement>("/api/v1/customer/support/suggestions");
        suggestions.GetProperty("totalCount").GetInt32().Should().Be(1);
        suggestions.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(suggestionId);

        var issueFilter = await customer.GetFromJsonAsync<JsonElement>("/api/v1/customer/support/issues?typeCode=MissingItems&pageNumber=1&pageSize=1");
        issueFilter.GetProperty("totalCount").GetInt32().Should().Be(1);
        issueFilter.GetProperty("items").GetArrayLength().Should().Be(1);
        issueFilter.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(issueId);
        var suggestionFilter = await customer.GetFromJsonAsync<JsonElement>("/api/v1/customer/support/suggestions?status=New&typeCode=Meals");
        suggestionFilter.GetProperty("totalCount").GetInt32().Should().Be(1);
        var invalidStatus = await customer.GetAsync("/api/v1/customer/support/issues?status=MadeUp");
        invalidStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var detail = await customer.GetAsync($"/api/v1/customer/support/suggestions/{suggestionId}"))
        {
            detail.StatusCode.Should().Be(HttpStatusCode.OK);
            using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            var version = detailJson.RootElement.GetProperty("version").GetUInt32();
            var suggestionReply = await customer.PostAsJsonAsync($"/api/v1/customer/support/suggestions/{suggestionId}/messages", new
            {
                body = "One more suggestion detail", expectedVersion = version, idempotencyKey = "customer-suggestion-reply"
            });
            suggestionReply.StatusCode.Should().Be(HttpStatusCode.OK, await suggestionReply.Content.ReadAsStringAsync());
        }

        (await other.GetAsync($"/api/v1/customer/support/issues/{issueId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await customer.GetAsync($"/api/v1/customer/support/suggestions/{issueId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await driver.GetAsync("/api/v1/customer/support/issues")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/customer/support/unknown")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CustomerCatalogCodesAreValidatedAcrossTypedAndGenericCreateRoutes()
    {
        using var customer = NewCustomer();
        var unknown = await customer.PostAsJsonAsync("/api/v1/customer/support/issues", new
        {
            typeCode = "MadeUp", subject = "Unknown", description = "Invalid", idempotencyKey = "customer-bad-code"
        });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var reservedUnknown = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "customer.issue.NotARealCode", "Unknown code", "Invalid", "customer-bad-generic-code"));
        reservedUnknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var reservedWrongKind = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "customer.issue.Payment", "Wrong kind", "Invalid", "customer-wrong-kind-code"));
        reservedWrongKind.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var suggestionPrefixWrongKind = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "customer.suggestion.Meals", "Wrong kind", "Invalid", "customer-wrong-kind-suggestion"));
        suggestionPrefixWrongKind.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidListFilter = await customer.GetAsync("/api/v1/customer/support/issues?typeCode=MadeUp");
        invalidListFilter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidPage = await customer.GetAsync("/api/v1/customer/support/issues?pageNumber=2147483647&pageSize=100");
        invalidPage.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CustomerCaseDetailAndSharedListDoNotExposePrivateTimingCycleText()
    {
        var customerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "customer.issue.Payment", "Payment support", "Please review this payment.",
            customerId, now);
        var cycle = SupportCaseTimingCycle.StartConfigured(supportCase.Id, 1, Guid.NewGuid(), 1, Guid.NewGuid(), now,
            now.AddMinutes(30), now.AddHours(4), TimeSpan.FromHours(2));
        cycle.Escalate("Private escalation reason", now.AddMinutes(1));
        cycle.SetUnrouted(true, "Private routing notes");
        supportCase.SetActiveTimingCycle(cycle.Id);
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.Add(supportCase);
            db.SupportCaseTimingCycles.Add(cycle);
            await db.SaveChangesAsync();
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var detail = await customer.GetFromJsonAsync<JsonElement>($"/api/v1/customer/support/issues/{supportCase.Id}");
        AssertCustomerTimingCycleRedacted(detail.GetProperty("activeTimingCycle"), now.AddHours(4), now.AddHours(2));

        var list = await customer.GetFromJsonAsync<JsonElement>("/api/v1/support/cases?kind=Complaint");
        var item = list.GetProperty("items").EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == supportCase.Id);
        AssertCustomerTimingCycleRedacted(item.GetProperty("activeTimingCycle"), now.AddHours(4), now.AddHours(2));
    }

    [Fact]
    public async Task CustomerCanReplyAndCloseOrReopenOnlyOwnTypedCases()
    {
        var customerId = Guid.NewGuid();
        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var createdResponse = await customer.PostAsJsonAsync("/api/v1/customer/support/issues", new
        {
            typeCode = "Payment", subject = "Payment question", description = "Please check this.", idempotencyKey = "customer-followup-create"
        });
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createdResponse.Content.ReadAsStringAsync());
        using var createdJson = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        var caseId = createdJson.RootElement.GetProperty("id").GetGuid();
        var initialVersion = createdJson.RootElement.GetProperty("version").GetUInt32();

        var taskReply = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{caseId}/messages", new
        {
            body = "Task spoof", expectedVersion = initialVersion, idempotencyKey = "customer-task-spoof", taskId = Guid.NewGuid()
        });
        taskReply.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var reply = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{caseId}/messages", new
        {
            body = "More information", expectedVersion = initialVersion, idempotencyKey = "customer-followup-reply"
        });
        reply.StatusCode.Should().Be(HttpStatusCode.OK, await reply.Content.ReadAsStringAsync());
        using var repliedJson = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        var currentVersion = repliedJson.RootElement.GetProperty("version").GetUInt32();
        currentVersion.Should().BeGreaterThan(initialVersion);

        var staleReply = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{caseId}/messages", new
        {
            body = "Stale reply", expectedVersion = initialVersion, idempotencyKey = "customer-stale-reply"
        });
        staleReply.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var resolvedCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "customer.issue.Payment", "Resolved request", "Resolved description", customerId, DateTime.UtcNow);
        resolvedCase.TryTransitionTo(SupportCaseStatus.InProgress).Should().BeTrue();
        resolvedCase.TryTransitionTo(SupportCaseStatus.Resolved).Should().BeTrue();
        await using (var db = _fixture.CreateDbContext())
        {
            db.SupportCases.Add(resolvedCase);
            await db.SaveChangesAsync();
        }
        var resolved = await customer.GetFromJsonAsync<JsonElement>($"/api/v1/customer/support/issues/{resolvedCase.Id}");
        var close = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{resolvedCase.Id}/actions/close", new
        {
            expectedVersion = resolved.GetProperty("version").GetUInt32(), idempotencyKey = "customer-close"
        });
        close.StatusCode.Should().Be(HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
        using var closedJson = JsonDocument.Parse(await close.Content.ReadAsStringAsync());
        closedJson.RootElement.GetProperty("status").GetString().Should().Be("Closed");

        var reopen = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{resolvedCase.Id}/actions/reopen", new
        {
            expectedVersion = closedJson.RootElement.GetProperty("version").GetUInt32(),
            idempotencyKey = "customer-reopen", reason = "The issue returned."
        });
        reopen.StatusCode.Should().Be(HttpStatusCode.OK, await reopen.Content.ReadAsStringAsync());
        using var reopenedJson = JsonDocument.Parse(await reopen.Content.ReadAsStringAsync());
        reopenedJson.RootElement.GetProperty("status").GetString().Should().Be("InProgress");

        var hiddenAction = await customer.PostAsJsonAsync($"/api/v1/customer/support/issues/{caseId}/actions/resolve", new
        {
            expectedVersion = currentVersion, idempotencyKey = "customer-forbidden-action", reason = "No."
        });
        hiddenAction.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient NewCustomer()
        => _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));

    private static string[] ReadOptionValues(JsonElement options)
        => options.EnumerateArray().Select(option => option.GetProperty("value").GetString()!).ToArray();

    private static void AssertCustomerTimingCycleRedacted(JsonElement cycle, DateTime resolutionDeadline, DateTime escalationDeadline)
    {
        cycle.GetProperty("isEscalated").GetBoolean().Should().BeTrue();
        cycle.GetProperty("isUnrouted").GetBoolean().Should().BeTrue();
        cycle.GetProperty("resolutionDeadlineUtc").GetDateTime().Should().BeCloseTo(resolutionDeadline, TimeSpan.FromSeconds(1));
        cycle.GetProperty("escalationDeadlineUtc").GetDateTime().Should().BeCloseTo(escalationDeadline, TimeSpan.FromSeconds(1));
        cycle.GetProperty("escalationReason").ValueKind.Should().Be(JsonValueKind.Null);
        cycle.GetProperty("notes").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
