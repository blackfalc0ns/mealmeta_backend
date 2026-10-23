using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Application.Features.UnifiedSupport.Timing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ComplaintsSupport;

public sealed class SupportTimingServiceAndWorkerTests
{
    private static IApplicationTransactionExecutor DirectTransactions()
    {
        var executor = Substitute.For<IApplicationTransactionExecutor>();
        executor.ExecuteResultAsync(Arg.Any<Func<CancellationToken, Task<Result<SupportTimingPolicyDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result<SupportTimingPolicyDto>>>>()(call.Arg<CancellationToken>()));
        return executor;
    }

    private static ServiceProvider CreateProcessorServices(
        AppDbContext db,
        ICurrentUser currentUser,
        IUnitOfWork? unitOfWork = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        services.AddSingleton<IUnitOfWork>(unitOfWork ?? db);
        services.AddSingleton<IApplicationLockManager>(Substitute.For<IApplicationLockManager>());
        services.AddSingleton(currentUser);
        services.AddSingleton<IApplicationTransactionExecutor, InlineTransactionExecutor>();
        return services.BuildServiceProvider();
    }

    private sealed class InlineTransactionExecutor : IApplicationTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static ICurrentUser CreateCurrentUserMock(Guid? userId, string? role = null, params string[] permissions)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        if (!userId.HasValue)
        {
            currentUser.IsAuthenticated.Returns(false);
            return currentUser;
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId.Value);
        currentUser.UserType.Returns(UserType.Admin);

        var rolesSet = role != null ? new HashSet<string> { role } : new HashSet<string>();
        currentUser.Roles.Returns(rolesSet);
        currentUser.IsInRole(Arg.Any<string>()).Returns(callInfo => rolesSet.Contains(callInfo.Arg<string>()));

        var permsSet = permissions.ToHashSet();
        currentUser.Permissions.Returns(permsSet);
        currentUser.HasPermission(Arg.Any<string>()).Returns(callInfo => permsSet.Contains(callInfo.Arg<string>()));

        return currentUser;
    }

    private static async Task SeedOperatorAsync(
        AppDbContext db,
        Guid userId,
        string roleName,
        string? permissionCode = null,
        AccessScopeType? scopeType = null,
        Guid? countryId = null,
        Guid? restaurantId = null)
    {
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"op_{userId:N}@mealmate.com",
            UserType = UserType.Admin,
            AccountStatus = AccountStatus.Active,
        };
        db.Users.Add(user);

        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant(),
        };
        db.Roles.Add(role);

        var userRole = new IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = role.Id,
        };
        db.UserRoles.Add(userRole);

        if (!string.IsNullOrEmpty(permissionCode))
        {
            var permission = new Permission
            {
                Id = Guid.NewGuid(),
                Code = permissionCode,
                NameAr = permissionCode,
                NameEn = permissionCode,
                Module = "Support",
                IsActive = true,
            };
            db.Permissions.Add(permission);
            var rolePermission = new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                PermissionId = permission.Id,
            };
            db.RolePermissions.Add(rolePermission);
        }

        if (scopeType.HasValue)
        {
            var scope = new AccessScope
            {
                Id = Guid.NewGuid(),
                Name = $"Scope_{scopeType}",
                ScopeType = scopeType.Value,
                CountryId = countryId,
                RestaurantId = restaurantId,
                IsActive = true,
            };
            db.AccessScopes.Add(scope);

            var userScope = new UserScope
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccessScopeId = scope.Id,
            };
            db.UserScopes.Add(userScope);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PublishPolicy_ReusingIdempotencyKeyWithDifferentPayload_ReturnsConflict()
    {
        await using var db = CreateDb();
        var adminUserId = Guid.NewGuid();
        await SeedOperatorAsync(db, adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportManage);

        var existingPolicy = SupportTimingPolicy.Create(
            version: 1,
            name: "Initial SLA Policy",
            description: "Initial description",
            publishedByUserId: adminUserId,
            publicationReason: "First release",
            idempotencyKey: "test-idempotency-key",
            publishedAtUtc: DateTime.UtcNow);

        existingPolicy.AddRule(
            SupportCaseChannel.Customer,
            SupportCasePriority.P0,
            "Billing",
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(60),
            TimeSpan.FromMinutes(30),
            requiresImmediateAck: true,
            evaluationOrder: 1);

        db.SupportTimingPolicies.Add(existingPolicy);
        await db.SaveChangesAsync();

        var currentUser = CreateCurrentUserMock(adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportManage);

        var service = new SupportTimingPolicyService(
            db,
            Substitute.For<IUnitOfWork>(),
            DirectTransactions(),
            currentUser,
            TimeProvider.System,
            Substitute.For<IApplicationLockManager>());

        // Request reuses same key but with different Name and Reason
        var differentRequest = new PublishSupportTimingPolicyRequest(
            Name: "Completely Different SLA Name",
            Description: "Initial description",
            PublicationReason: "Different reason",
            IdempotencyKey: "test-idempotency-key",
            ExpectedVersion: 2,
            Rules: new List<CreateSupportTimingRuleDto>
            {
                new(SupportCaseChannel.Customer, SupportCasePriority.P0, "Billing", 15, 60, 30, true, 1)
            });

        var result = await service.PublishPolicyAsync(differentRequest);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(SupportCaseProblemCodes.IdempotencyKeyReused);
    }

    [Fact]
    public async Task PublishPolicy_ReplayingExactSamePayload_ReturnsSuccessWithSamePolicy()
    {
        await using var db = CreateDb();
        var adminUserId = Guid.NewGuid();
        await SeedOperatorAsync(db, adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportManage);

        var existingPolicy = SupportTimingPolicy.Create(
            version: 1,
            name: "Initial SLA Policy",
            description: "Initial description",
            publishedByUserId: adminUserId,
            publicationReason: "First release",
            idempotencyKey: "replay-key-1",
            publishedAtUtc: DateTime.UtcNow);

        existingPolicy.AddRule(
            SupportCaseChannel.Customer,
            SupportCasePriority.P0,
            "Billing",
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(60),
            TimeSpan.FromMinutes(30),
            requiresImmediateAck: true,
            evaluationOrder: 1);

        db.SupportTimingPolicies.Add(existingPolicy);
        await db.SaveChangesAsync();

        var currentUser = CreateCurrentUserMock(adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportManage);

        var service = new SupportTimingPolicyService(
            db,
            Substitute.For<IUnitOfWork>(),
            DirectTransactions(),
            currentUser,
            TimeProvider.System,
            Substitute.For<IApplicationLockManager>());

        // Request with exact same fields
        var exactRequest = new PublishSupportTimingPolicyRequest(
            Name: "Initial SLA Policy",
            Description: "Initial description",
            PublicationReason: "First release",
            IdempotencyKey: "replay-key-1",
            ExpectedVersion: 1,
            Rules: new List<CreateSupportTimingRuleDto>
            {
                new(SupportCaseChannel.Customer, SupportCasePriority.P0, "Billing", 15, 60, 30, true, 1)
            });

        var result = await service.PublishPolicyAsync(exactRequest);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(existingPolicy.Id);
        result.Value.Name.Should().Be("Initial SLA Policy");
    }

    [Fact]
    public async Task PublishPolicy_WhenCallerLacksSupportManagePermission_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var nonAdminUserId = Guid.NewGuid();
        await SeedOperatorAsync(db, nonAdminUserId, "SupportViewer", PermissionCodes.SupportView);

        var currentUser = CreateCurrentUserMock(nonAdminUserId, "SupportViewer", PermissionCodes.SupportView);

        var service = new SupportTimingPolicyService(
            db,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IApplicationTransactionExecutor>(),
            currentUser,
            TimeProvider.System,
            Substitute.For<IApplicationLockManager>());

        var request = new PublishSupportTimingPolicyRequest(
            Name: "Unauthorized SLA",
            Description: "Desc",
            PublicationReason: "Reason",
            IdempotencyKey: "unauth-key",
            ExpectedVersion: 1,
            Rules: new List<CreateSupportTimingRuleDto>
            {
                new(SupportCaseChannel.Customer, SupportCasePriority.P0, "Billing", 15, 60, 30, true, 1)
            });

        var result = await service.PublishPolicyAsync(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(SupportCaseProblemCodes.AccessDenied);
    }

    [Fact]
    public async Task GetCyclesForCase_RejectsUnauthenticatedCaller()
    {
        await using var db = CreateDb();
        var currentUser = CreateCurrentUserMock(null);

        var service = new SupportTimingPolicyService(
            db,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IApplicationTransactionExecutor>(),
            currentUser,
            TimeProvider.System,
            Substitute.For<IApplicationLockManager>());

        var result = await service.GetCyclesForCaseAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(SupportCaseProblemCodes.AuthenticationRequired);
    }

    [Fact]
    public async Task GetCyclesForCase_WhenCaseDoesNotExist_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var adminUserId = Guid.NewGuid();
        await SeedOperatorAsync(db, adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportView);

        var currentUser = CreateCurrentUserMock(adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportView);

        var service = new SupportTimingPolicyService(
            db,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IApplicationTransactionExecutor>(),
            currentUser,
            TimeProvider.System,
            Substitute.For<IApplicationLockManager>());

        var result = await service.GetCyclesForCaseAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(SupportCaseProblemCodes.NotFound);
    }

    [Fact]
    public async Task GetCyclesForCase_WhenAuthorizedOperator_ReturnsCycleHistory()
    {
        await using var db = CreateDb();
        var adminUserId = Guid.NewGuid();
        await SeedOperatorAsync(db, adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportView);

        var sc = SupportCase.Create(
            channel: SupportCaseChannel.Customer,
            kind: SupportCaseKind.Complaint,
            ownerTeam: SupportCaseOwnerTeam.PlatformSupport,
            category: "General",
            subject: "Cycle test case",
            body: "Cycle test body",
            requesterUserId: Guid.NewGuid(),
            createdAtUtc: DateTime.UtcNow);

        db.SupportCases.Add(sc);

        var cycle1 = SupportCaseTimingCycle.StartUnconfigured(sc.Id, 1, DateTime.UtcNow.AddHours(-3), null, null, "Cycle 1");
        cycle1.RecordResolution(DateTime.UtcNow.AddHours(-2));
        var cycle2 = SupportCaseTimingCycle.StartUnconfigured(sc.Id, 2, DateTime.UtcNow.AddHours(-1), null, null, "Cycle 2");

        db.SupportCaseTimingCycles.AddRange(cycle1, cycle2);
        await db.SaveChangesAsync();

        var currentUser = CreateCurrentUserMock(adminUserId, AppRoles.SuperAdmin, PermissionCodes.SupportView);

        var service = new SupportTimingPolicyService(
            db,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IApplicationTransactionExecutor>(),
            currentUser,
            TimeProvider.System,
            Substitute.For<IApplicationLockManager>());

        var result = await service.GetCyclesForCaseAsync(sc.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].CycleNumber.Should().Be(1);
        result.Value[1].CycleNumber.Should().Be(2);
    }

    [Fact]
    public async Task SupportTimingProcessor_ScopesNotifications_ToEligibleRegionalOperatorsOnly()
    {
        await using var db = CreateDb();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger<SupportTimingProcessor>.Instance;

        var countryKuwait = Guid.NewGuid();
        var countryQatar = Guid.NewGuid();

        // Seed Country and Tenant entities with all required columns
        var countryKuwaitEntity = new Country
        {
            Id = countryKuwait,
            Code = "KWT",
            NameAr = "الكويت",
            NameEn = "Kuwait",
            DialCode = "+965",
            FlagEmoji = "🇰🇼",
            CurrencyCode = "KWD",
            IsActive = true,
        };
        var tenantKuwait = new Tenant
        {
            Id = Guid.NewGuid(),
            Code = "kwt",
            NameAr = "الكويت",
            NameEn = "Kuwait",
            Domain = "kw.mealmate.local",
            CurrencyCode = "KWD",
            TimeZoneId = "Asia/Kuwait",
            CountryId = countryKuwait,
            Status = TenantStatus.Active,
            IsActive = true,
        };

        var countryQatarEntity = new Country
        {
            Id = countryQatar,
            Code = "QAT",
            NameAr = "قطر",
            NameEn = "Qatar",
            DialCode = "+974",
            FlagEmoji = "🇶🇦",
            CurrencyCode = "QAR",
            IsActive = true,
        };
        var tenantQatar = new Tenant
        {
            Id = Guid.NewGuid(),
            Code = "qat",
            NameAr = "قطر",
            NameEn = "Qatar",
            Domain = "qa.mealmate.local",
            CurrencyCode = "QAR",
            TimeZoneId = "Asia/Qatar",
            CountryId = countryQatar,
            Status = TenantStatus.Active,
            IsActive = true,
        };

        db.Countries.AddRange(countryKuwaitEntity, countryQatarEntity);
        db.Tenants.AddRange(tenantKuwait, tenantQatar);

        var kuwaitOperatorId = Guid.NewGuid();
        var qatarOperatorId = Guid.NewGuid();

        await SeedOperatorAsync(db, kuwaitOperatorId, "KuwaitSupportOperator", PermissionCodes.SupportView, AccessScopeType.Country, countryId: countryKuwait);
        var kuwaitRole = await db.UserRoles.FirstAsync(ur => ur.UserId == kuwaitOperatorId);
        var replyPerm = new Permission
        {
            Id = Guid.NewGuid(),
            Code = PermissionCodes.SupportReply,
            NameAr = PermissionCodes.SupportReply,
            NameEn = PermissionCodes.SupportReply,
            Module = "Support",
            IsActive = true,
        };
        db.Permissions.Add(replyPerm);
        db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = kuwaitRole.RoleId,
            PermissionId = replyPerm.Id,
        });

        await SeedOperatorAsync(db, qatarOperatorId, "QatarSupportOperator", PermissionCodes.SupportView, AccessScopeType.Country, countryId: countryQatar);
        var qatarRole = await db.UserRoles.FirstAsync(ur => ur.UserId == qatarOperatorId);
        db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = qatarRole.RoleId,
            PermissionId = replyPerm.Id,
        });
        await db.SaveChangesAsync();

        // Case is scoped to Kuwait
        var sc = SupportCase.Create(
            channel: SupportCaseChannel.Customer,
            kind: SupportCaseKind.Complaint,
            ownerTeam: SupportCaseOwnerTeam.PlatformSupport,
            category: "Billing",
            subject: "Regional test case",
            body: "Case body",
            requesterUserId: Guid.NewGuid(),
            createdAtUtc: DateTime.UtcNow,
            triageCountryId: countryKuwait);

        var cycle = SupportCaseTimingCycle.StartConfigured(
            sc.Id,
            1,
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            DateTime.UtcNow.AddHours(-2),
            firstResponseDeadlineUtc: DateTime.UtcNow.AddHours(-1),
            resolutionDeadlineUtc: DateTime.UtcNow.AddHours(2),
            escalationThreshold: TimeSpan.FromMinutes(30));

        sc.SetActiveTimingCycle(cycle.Id);

        db.SupportCases.Add(sc);
        db.SupportCaseTimingCycles.Add(cycle);
        await db.SaveChangesAsync();

        var currentUser = CreateCurrentUserMock(null);
        await using var processorServices = CreateProcessorServices(db, currentUser);
        var processor = new SupportTimingProcessor(db, processorServices.GetRequiredService<IServiceScopeFactory>(), timeProvider, logger);

        var result = await processor.ProcessTimingTickAsync(CancellationToken.None);

        result.FirstResponseBreachesCount.Should().Be(1);
        result.EscalatedCount.Should().Be(1);

        // Verification: Kuwait operator received notification, Qatar operator did NOT
        var notifications = await db.AdminNotifications.ToListAsync();
        notifications.Should().ContainSingle(n => n.UserId == kuwaitOperatorId);
        notifications.Should().NotContain(n => n.UserId == qatarOperatorId);
    }

    [Fact]
    public async Task SupportTimingProcessor_HandlesConcurrencyExceptionGracefully_AndDetachesStaleEntities()
    {
        await using var db = CreateDb();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var timeProvider = TimeProvider.System;
        var logger = NullLogger<SupportTimingProcessor>.Instance;

        var sc = SupportCase.Create(
            channel: SupportCaseChannel.Customer,
            kind: SupportCaseKind.Complaint,
            ownerTeam: SupportCaseOwnerTeam.PlatformSupport,
            category: "Delivery",
            subject: "Concurrency test case",
            body: "Body",
            requesterUserId: Guid.NewGuid(),
            createdAtUtc: DateTime.UtcNow);

        var cycle = SupportCaseTimingCycle.StartConfigured(
            sc.Id,
            1,
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            DateTime.UtcNow.AddHours(-2),
            firstResponseDeadlineUtc: DateTime.UtcNow.AddHours(-1),
            resolutionDeadlineUtc: DateTime.UtcNow.AddHours(2));

        sc.SetActiveTimingCycle(cycle.Id);

        db.SupportCases.Add(sc);
        db.SupportCaseTimingCycles.Add(cycle);
        await db.SaveChangesAsync();

        // Simulate concurrency conflict when saving
        unitOfWork.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new DbUpdateConcurrencyException("Optimistic concurrency failure"));

        var currentUser = CreateCurrentUserMock(null);
        await using var processorServices = CreateProcessorServices(db, currentUser, unitOfWork);
        var processor = new SupportTimingProcessor(db, processorServices.GetRequiredService<IServiceScopeFactory>(), timeProvider, logger);

        // ProcessTimingTickAsync must catch concurrency exception and not throw
        var act = async () => await processor.ProcessTimingTickAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
