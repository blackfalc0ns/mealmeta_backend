using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.ApiTests.Support;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.ApiTests.DriverDelivery;

public sealed class UnifiedDriverSupportHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    private readonly SupportEvidenceTestScanner _scanner;
    public UnifiedDriverSupportHttpTests(OrderLifecycleEndToEndFixture fixture)
    {
        _fixture = fixture;
        _scanner = fixture.EnableSupportEvidenceTestScanner();
        _scanner.Reset();
    }

    [Fact]
    public async Task DriverOperationalQueueUsesOwnSupportedComplaintsOnly()
    {
        var userId = Guid.NewGuid();
        var foreignUserId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var foreignProfile = new DriverProfile { UserId = foreignUserId, FullNameAr = "آخر", FullNameEn = "Other" };
        var own = Complaint(profile.Id, userId, "VehicleBreakdown");
        var unsupported = Complaint(profile.Id, userId, "FoodQuality");
        var foreign = Complaint(foreignProfile.Id, foreignUserId, "VehicleBreakdown");
        await SeedAsync(userId, foreignUserId, profile, foreignProfile, own, unsupported, foreign);

        using var client = _fixture.CreateClientForUser(new TokenUser(userId, UserType.Driver, ["Driver"], []));
        var response = await client.GetAsync("/api/v1/support/operational-issues");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<OperationalSupportIssuePageDto>();
        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle(x => x.Id == own.Id);
        (await client.GetAsync($"/api/v1/support/operational-issues/{foreign.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EscalationIsAuditedIdempotentlyWithoutChangingOperationalComplaint()
    {
        var userId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = Complaint(profile.Id, userId, "TrafficAccident");
        var originalStatus = issue.Status;
        await SeedAsync(userId, null, profile, null, issue);
        using var client = _fixture.CreateClientForUser(new TokenUser(userId, UserType.Driver, ["Driver"], []));
        var path = $"/api/v1/support/operational-issues/{issue.Id}/escalate";
        var body = new EscalateOperationalIssueRequest("Dispatcher follow-up needed", Guid.NewGuid().ToString("N"));

        var first = await client.PostAsJsonAsync(path, body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await client.PostAsJsonAsync(path, body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(path, body with { Reason = "Different reason" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = OpenDb();
        var persisted = await db.Complaints.SingleAsync(x => x.Id == issue.Id);
        persisted.Status.Should().Be(originalStatus);
        (await db.AuditLogs.CountAsync(x => x.EntityType == nameof(Complaint) && x.EntityId == issue.Id
            && x.Action == "UnifiedSupportOperationalIssueEscalated")).Should().Be(1);
    }

    [Fact]
    public async Task OperationalIssueEndpointsRequireAuthenticationAndSupportedRole()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var customer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        (await customer.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.PostAsJsonAsync($"/api/v1/support/operational-issues/{Guid.NewGuid()}/escalate",
            new EscalateOperationalIssueRequest("no", "key"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminOperationalQueueRequiresSupportPermissionAndValidatedScope()
    {
        var driverId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = driverId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = Complaint(profile.Id, driverId, "VehicleBreakdown");
        await SeedAsync(driverId, null, profile, null, issue);

        using var ordinaryAdmin = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Admin, ["Admin"], []));
        (await ordinaryAdmin.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var supportAdminId = Guid.NewGuid();
        var supportToken = new TokenUser(supportAdminId, UserType.Admin,
            ["SupportAdmin"], ["support:view"]);
        await using (var db = OpenDb()) await SupportTestIdentity.SeedAsync(db, supportToken);
        using var unscopedSupport = _fixture.CreateClientForUser(supportToken);
        var response = await unscopedSupport.GetAsync("/api/v1/support/operational-issues");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<OperationalSupportIssuePageDto>();
        page!.Items.Should().NotContain(x => x.Id == issue.Id);
    }

    [Fact]
    public async Task DisabledPersistedSuperAdminLosesOperationalQueueAndEvidenceAccessWithOldToken()
    {
        var adminId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = driverId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = Complaint(profile.Id, driverId, "VehicleBreakdown");
        var image = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
        var evidenceId = Guid.NewGuid();
        using var scope = _fixture.Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using var content = new MemoryStream(image);
        var stored = await storage.SaveAsync(content, "evidence.png", "image/png", "uploads/drivers/issue-evidence");
        await using (var db = OpenDb())
        {
            db.Users.Add(new ApplicationUser { Id = driverId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            db.DriverProfiles.Add(profile);
            db.Complaints.Add(issue);
            db.Set<ComplaintEvidencePhoto>().Add(new ComplaintEvidencePhoto
                { Id = evidenceId, ComplaintId = issue.Id, StoragePath = stored.StorageKey, ContentType = "image/png", UploadedAtUtc = DateTime.UtcNow });
            db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord { StorageKey = stored.StorageKey,
                FileName = "evidence.png", ContentType = "image/png", SizeBytes = image.Length, UploadedByUserId = driverId,
                UploadedAtUtc = DateTime.UtcNow, Scope = "drivers", Folder = "issue-evidence" });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(adminId, UserType.Admin, ["SuperAdmin"], []));
        }

        using var oldToken = _fixture.CreateClientForUser(new TokenUser(adminId, UserType.Admin, ["SuperAdmin"], []));
        (await oldToken.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.OK);
        var evidencePath = $"/api/v1/support/operational-issues/{issue.Id}/evidence/{evidenceId}";
        (await oldToken.GetAsync(evidencePath)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = OpenDb())
        {
            var persistedAdmin = await db.Users.SingleAsync(x => x.Id == adminId);
            persistedAdmin.AccountStatus = AccountStatus.Suspended;
            await db.SaveChangesAsync();
        }
        (await oldToken.GetAsync("/api/v1/support/operational-issues")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await oldToken.GetAsync(evidencePath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokedSupportManageCannotMutateOperationalIssueWithOldToken()
    {
        var adminId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = driverId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = Complaint(profile.Id, driverId, "VehicleBreakdown");
        issue.RestaurantId = Guid.NewGuid();
        var token = new TokenUser(adminId, UserType.Admin, ["OperationalManageRevoked"], ["support:view", "support.manage"], null, [scopeId]);
        await using (var db = OpenDb())
        {
            db.Users.Add(new ApplicationUser { Id = driverId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            db.DriverProfiles.Add(profile);
            db.Complaints.Add(issue);
            db.AccessScopes.Add(new AccessScope { Id = scopeId, Name = "Operational support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new UserScope { UserId = adminId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, token);
        }
        using var oldToken = _fixture.CreateClientForUser(token);
        (await oldToken.PostAsJsonAsync($"/api/v1/support/operational-issues/{issue.Id}/progress",
            new OperationalSupportProgressRequest("UnderReview", "Before revocation"))).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = OpenDb())
        {
            var roleId = await db.UserRoles.Where(x => x.UserId == adminId).Select(x => x.RoleId).SingleAsync();
            var permissionId = await db.Permissions.Where(x => x.Code == "support.manage").Select(x => x.Id).SingleAsync();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            await db.SaveChangesAsync();
        }
        (await oldToken.PostAsJsonAsync($"/api/v1/support/operational-issues/{issue.Id}/progress",
            new OperationalSupportProgressRequest("Resolved", "Stale manage claim"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SupportCaseCanReferenceAuthorizedDriverComplaintAndResolutionWaitsForItsOutcome()
    {
        var customerId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = driverId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = Complaint(profile.Id, driverId, "VehicleBreakdown");
        await using (var db = OpenDb())
        {
            db.Users.Add(new ApplicationUser { Id = customerId, UserType = UserType.Customer, AccountStatus = AccountStatus.Active });
            db.Users.Add(new ApplicationUser { Id = driverId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            db.Add(profile);
            db.Complaints.Add(issue);
            await db.SaveChangesAsync();
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var create = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, "delivery", "Delivery help", "Please investigate",
            Guid.NewGuid().ToString("N")));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var supportCase = await create.Content.ReadFromJsonAsync<SupportCaseDto>();
        supportCase.Should().NotBeNull();

        var superAdminId = Guid.NewGuid();
        var superAdminToken = new TokenUser(superAdminId, UserType.Admin, ["SuperAdmin"], []);
        await using (var db = OpenDb()) await SupportTestIdentity.SeedAsync(db, superAdminToken);
        using var superAdmin = _fixture.CreateClientForUser(superAdminToken);
        var started = await superAdmin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase!.Id}/actions/start",
            new SupportCaseActionRequest("start", supportCase.Version, Guid.NewGuid().ToString("N")));
        var inProgress = await started.Content.ReadFromJsonAsync<SupportCaseDto>();
        var link = await superAdmin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/links",
            new AddSupportCaseLinkRequest(SupportCaseLinkRelation.OperationalSource, "DriverComplaint", issue.Id,
                inProgress!.Version, Guid.NewGuid().ToString("N")));
        link.StatusCode.Should().Be(HttpStatusCode.OK);
        var linked = await link.Content.ReadFromJsonAsync<SupportCaseDto>();
        linked!.Links.Should().ContainSingle(x => x.ResourceType == "DriverComplaint" && x.ResourceId == issue.Id);
        var blocked = await superAdmin.PostAsJsonAsync($"/api/v1/support/cases/{linked.Id}/actions/resolve",
            new SupportCaseActionRequest("resolve", linked.Version, Guid.NewGuid().ToString("N"), "Waiting for operation"));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var db = OpenDb())
        {
            var persistedIssue = await db.Complaints.SingleAsync(x => x.Id == issue.Id);
            persistedIssue.Status = ComplaintStatus.Resolved;
            await db.SaveChangesAsync();
        }
        var afterSourceOutcome = await customer.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{linked.Id}");
        afterSourceOutcome!.Status.Should().Be("InProgress", "a source outcome does not silently close or resolve its support case");
        var resolved = await superAdmin.PostAsJsonAsync($"/api/v1/support/cases/{linked.Id}/actions/resolve",
            new SupportCaseActionRequest("resolve", linked.Version, Guid.NewGuid().ToString("N"), "Operational issue resolved"));
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolvedCase = await resolved.Content.ReadFromJsonAsync<SupportCaseDto>();
        (await customer.PostAsJsonAsync($"/api/v1/support/cases/{linked.Id}/actions/close",
            new SupportCaseActionRequest("close", resolvedCase!.Version, Guid.NewGuid().ToString("N"), "Confirmed")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SupportCaseCannotLinkDriverComplaintFromDifferentRestaurant()
    {
        var customerId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var caseRestaurantId = Guid.NewGuid();
        var issueRestaurantId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = driverId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = Complaint(profile.Id, driverId, "VehicleBreakdown");
        issue.RestaurantId = issueRestaurantId;
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "delivery", "Help", "Please investigate", customerId,
            DateTime.UtcNow, restaurantId: caseRestaurantId);
        await using (var db = OpenDb())
        {
            db.Users.Add(new ApplicationUser { Id = customerId, UserType = UserType.Customer, AccountStatus = AccountStatus.Active });
            db.Users.Add(new ApplicationUser { Id = driverId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            var caseRestaurant = Restaurant(caseRestaurantId); var issueRestaurant = Restaurant(issueRestaurantId);
            db.Users.AddRange(new ApplicationUser { Id = caseRestaurant.UserId, UserType = UserType.Restaurant, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = issueRestaurant.UserId, UserType = UserType.Restaurant, AccountStatus = AccountStatus.Active });
            db.RestaurantProfiles.Add(caseRestaurant); db.RestaurantProfiles.Add(issueRestaurant);
            db.Add(profile);
            db.Add(issue);
            db.Add(supportCase);
            await db.SaveChangesAsync();
        }
        var superAdminId = Guid.NewGuid();
        var superAdminToken = new TokenUser(superAdminId, UserType.Admin, ["SuperAdmin"], []);
        await using (var db = OpenDb()) await SupportTestIdentity.SeedAsync(db, superAdminToken);
        using var superAdmin = _fixture.CreateClientForUser(superAdminToken);
        var response = await superAdmin.PostAsJsonAsync($"/api/v1/support/cases/{supportCase.Id}/links",
            new AddSupportCaseLinkRequest(SupportCaseLinkRelation.OperationalSource, "DriverComplaint", issue.Id,
                1, Guid.NewGuid().ToString("N")));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegionScopedSupportAdminSeesDescendantRestaurantsIncludingPausedOnesOnly()
    {
        var adminId = Guid.NewGuid();
        var country = new Country { Code = Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(), NameAr = "بلد", NameEn = "Country",
            DialCode = "+999", FlagEmoji = "🏳" };
        var tenant = new Tenant { CountryId = country.Id, Code = $"T{Guid.NewGuid():N}"[..8], NameAr = "مستأجر", NameEn = "Tenant",
            Domain = $"{Guid.NewGuid():N}.example.invalid", CurrencyCode = "KWD", TimeZoneId = "UTC", Status = TenantStatus.Active, IsActive = true };
        var parent = new Region { CountryId = country.Id, Code = $"P{Guid.NewGuid():N}"[..8], NameAr = "محافظة", NameEn = "Governorate", Type = RegionType.Governorate };
        var child = new Region { CountryId = country.Id, ParentRegionId = parent.Id, Code = $"C{Guid.NewGuid():N}"[..8], NameAr = "منطقة", NameEn = "Area", Type = RegionType.Area };
        var unrelated = new Region { CountryId = country.Id, Code = $"U{Guid.NewGuid():N}"[..8], NameAr = "أخرى", NameEn = "Other", Type = RegionType.Governorate };
        var pausedRestaurantId = Guid.NewGuid();
        var unrelatedRestaurantId = Guid.NewGuid();
        var pausedRestaurant = Restaurant(pausedRestaurantId);
        pausedRestaurant.CountryId = country.Id; pausedRestaurant.PrimaryRegionId = child.Id;
        pausedRestaurant.IsArchived = true; pausedRestaurant.IsOperationallyActive = false;
        var unrelatedRestaurant = Restaurant(unrelatedRestaurantId);
        unrelatedRestaurant.CountryId = country.Id; unrelatedRestaurant.PrimaryRegionId = unrelated.Id;
        var driver1Id = Guid.NewGuid(); var driver2Id = Guid.NewGuid();
        var profile1 = new DriverProfile { UserId = driver1Id, FullNameAr = "سائق", FullNameEn = "Driver" };
        var profile2 = new DriverProfile { UserId = driver2Id, FullNameAr = "آخر", FullNameEn = "Other" };
        var visibleIssue = Complaint(profile1.Id, driver1Id, "VehicleBreakdown"); visibleIssue.RestaurantId = pausedRestaurantId;
        var outsideIssue = Complaint(profile2.Id, driver2Id, "VehicleBreakdown"); outsideIssue.RestaurantId = unrelatedRestaurantId;
        var scope = new MealMate.Domain.IdentityAccess.AccessScope { Name = "Parent region support", ScopeType = AccessScopeType.Region,
            CountryId = country.Id, TenantId = tenant.Id, RegionId = parent.Id };

        await using (var db = OpenDb())
        {
            db.Users.AddRange(new ApplicationUser { Id = adminId, UserType = UserType.Admin, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = driver1Id, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = driver2Id, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = pausedRestaurant.UserId, UserType = UserType.Restaurant, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = unrelatedRestaurant.UserId, UserType = UserType.Restaurant, AccountStatus = AccountStatus.Active });
            db.Countries.Add(country); db.Tenants.Add(tenant); db.Regions.AddRange(parent, child, unrelated);
            db.RestaurantProfiles.AddRange(pausedRestaurant, unrelatedRestaurant);
            db.DriverProfiles.AddRange(profile1, profile2); db.Complaints.AddRange(visibleIssue, outsideIssue);
            db.AccessScopes.Add(scope); db.UserScopes.Add(new MealMate.Domain.IdentityAccess.UserScope { UserId = adminId, AccessScopeId = scope.Id });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(adminId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.manage"], ScopeIds: [scope.Id]));
        }

        using var support = _fixture.CreateClientForUser(new TokenUser(adminId, UserType.Admin, ["SupportAdmin"], ["support:view", "support.manage"],
            ScopeIds: [scope.Id]));
        var response = await support.GetFromJsonAsync<OperationalSupportIssuePageDto>("/api/v1/support/operational-issues");
        response!.Items.Should().ContainSingle(x => x.Id == visibleIssue.Id);
        response.Items.Should().NotContain(x => x.Id == outsideIssue.Id);
        (await support.PostAsJsonAsync($"/api/v1/support/operational-issues/{visibleIssue.Id}/progress",
            new OperationalSupportProgressRequest("UnderReview", "Scoped review"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeliveryManagerOperationalActionsAreScopedAndUseExistingGuardedHandlers()
    {
        var restaurantId = Guid.NewGuid();
        var otherRestaurantId = Guid.NewGuid();
        var managerId = Guid.NewGuid(); var driverId = Guid.NewGuid(); var otherDriverId = Guid.NewGuid();
        var restaurant = Restaurant(restaurantId); var otherRestaurant = Restaurant(otherRestaurantId);
        var manager = new ApplicationUser { Id = managerId, UserType = UserType.DeliveryManager, AccountStatus = AccountStatus.Active, RestaurantId = restaurantId };
        var driver = new DriverProfile { UserId = driverId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var otherDriver = new DriverProfile { UserId = otherDriverId, FullNameAr = "آخر", FullNameEn = "Other" };
        var issue = Complaint(driver.Id, driverId, "VehicleBreakdown"); issue.RestaurantId = restaurantId;
        var outside = Complaint(otherDriver.Id, otherDriverId, "VehicleBreakdown"); outside.RestaurantId = otherRestaurantId;
        await using (var db = OpenDb())
        {
            db.Users.AddRange(manager, new ApplicationUser { Id = restaurant.UserId, UserType = UserType.Restaurant, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = otherRestaurant.UserId, UserType = UserType.Restaurant, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = driverId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active },
                new ApplicationUser { Id = otherDriverId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
            db.RestaurantProfiles.AddRange(restaurant, otherRestaurant); db.DriverProfiles.AddRange(driver, otherDriver);
            db.Complaints.AddRange(issue, outside); await db.SaveChangesAsync();
        }
        using var deliveryManager = _fixture.CreateClientForUser(new TokenUser(managerId, UserType.DeliveryManager,
            ["DeliveryManager"], [], restaurantId));
        var path = $"/api/v1/support/operational-issues/{issue.Id}";
        var progress = await deliveryManager.PostAsJsonAsync($"{path}/progress", new OperationalSupportProgressRequest("UnderReview", "Checked"));
        progress.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deliveryManager.PostAsJsonAsync($"/api/v1/support/operational-issues/{outside.Id}/progress",
            new OperationalSupportProgressRequest("UnderReview", "Cross restaurant"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var viewOnly = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Admin, ["SupportAdmin"], ["support:view"]));
        (await viewOnly.PostAsJsonAsync($"/api/v1/support/operational-issues/{issue.Id}/progress",
            new OperationalSupportProgressRequest("UnderReview", "Read only"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationalEvidenceRequiresIssueScopeAndNeverExposesStorageKeys()
    {
        var userId = Guid.NewGuid();
        var profile = new DriverProfile { UserId = userId, FullNameAr = "سائق", FullNameEn = "Driver" };
        var issue = Complaint(profile.Id, userId, "VehicleBreakdown");
        await SeedAsync(userId, null, profile, null, issue);
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
        using var scope = _fixture.Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using var content = new MemoryStream(bytes);
        var stored = await storage.SaveAsync(content, "evidence.png", "image/png", "uploads/drivers/issue-evidence");
        var evidenceId = Guid.NewGuid();
        await using (var db = OpenDb())
        {
            db.Set<ComplaintEvidencePhoto>().Add(new ComplaintEvidencePhoto { Id = evidenceId, ComplaintId = issue.Id,
                StoragePath = stored.StorageKey, ContentType = "image/png", UploadedAtUtc = DateTime.UtcNow });
            db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord { StorageKey = stored.StorageKey,
                FileName = "evidence.png", ContentType = "image/png", SizeBytes = bytes.Length,
                UploadedByUserId = userId, UploadedAtUtc = DateTime.UtcNow, Scope = "drivers", Folder = "issue-evidence" });
            await db.SaveChangesAsync();
        }
        using var driver = _fixture.CreateClientForUser(new TokenUser(userId, UserType.Driver, ["Driver"], []));
        var path = $"/api/v1/support/operational-issues/{issue.Id}/evidence/{evidenceId}";
        var read = await driver.GetAsync(path);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        read.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var foreign = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Driver, ["Driver"], []));
        (await foreign.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var detail = await driver.GetAsync($"/api/v1/support/operational-issues/{issue.Id}");
        var json = await detail.Content.ReadAsStringAsync();
        json.Should().Contain(path).And.NotContain(stored.StorageKey);
    }

    private async Task SeedAsync(Guid userId, Guid? foreignUserId, DriverProfile profile,
        DriverProfile? foreignProfile, params Complaint[] issues)
    {
        await using var db = OpenDb();
        db.Users.Add(new ApplicationUser { Id = userId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
        if (foreignUserId.HasValue)
            db.Users.Add(new ApplicationUser { Id = foreignUserId.Value, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
        db.Add(profile);
        if (foreignProfile is not null) db.Add(foreignProfile);
        db.AddRange(issues);
        await db.SaveChangesAsync();
    }

    private AppDbContext OpenDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.ConnectionString).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static MealMate.Domain.IdentityAccess.RestaurantProfile Restaurant(Guid id) => new()
    {
        Id = id, UserId = Guid.NewGuid(), TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
        VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
    };

    private static Complaint Complaint(Guid profileId, Guid driverUserId, string category) => new()
    {
        CustomerUserId = driverUserId,
        DriverProfileId = profileId,
        Category = category,
        Title = category,
        Description = "Operational report",
        SubmittedAtUtc = DateTime.UtcNow,
    };
}
