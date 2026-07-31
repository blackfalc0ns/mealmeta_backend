using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class IdentityDataSeeder
{
    public const string DefaultAdminEmail = "admin@mealmate.local";
    public const string DefaultAdminPassword = "ChangeMe!Admin123";

    public static async Task SeedAsync(
        IServiceProvider services,
        string adminPassword,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityDataSeeder");
        var db = sp.GetRequiredService<AppDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = sp.GetRequiredService<IDateTimeProvider>();

        await db.Database.MigrateAsync(cancellationToken);

        foreach (var roleName in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            try
            {
                var createRole = await roleManager.CreateAsync(new ApplicationRole(roleName)
                {
                    Id = Guid.NewGuid(),
                    Description = $"{roleName} system role",
                });
                if (createRole.Succeeded)
                {
                    logger.LogInformation("Created role {Role}", roleName);
                }
                else
                {
                    logger.LogWarning(
                        "Skipped creating role {Role}: {Errors}",
                        roleName,
                        string.Join(", ", createRole.Errors.Select(e => e.Description)));
                }
            }
            catch (DbUpdateException ex)
            {
                // Concurrent seed / already exists
                logger.LogDebug(ex, "Role {Role} likely already exists", roleName);
            }
        }

        foreach (var (code, nameEn, nameAr, module) in PermissionCodes.Catalog)
        {
            var exists = await db.Permissions.AnyAsync(p => p.Code == code, cancellationToken);
            if (!exists)
            {
                db.Permissions.Add(new Permission
                {
                    Code = code,
                    NameEn = nameEn,
                    NameAr = nameAr,
                    Module = module,
                    IsActive = true,
                    CreatedAtUtc = clock.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        await AssignAllPermissionsToRoleAsync(db, roleManager, AppRoles.SuperAdmin, cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.CountryAdmin,
        [
            PermissionCodes.RolesView, PermissionCodes.AuditView, PermissionCodes.AdminUsersView,
            PermissionCodes.AdminUsersManage, PermissionCodes.AdminUsersInvite,
            PermissionCodes.DocumentsReview, PermissionCodes.DocumentsView,
            PermissionCodes.RestaurantRegView, PermissionCodes.RestaurantRegApprove, PermissionCodes.RestaurantRegReject,
            PermissionCodes.DriverRegView, PermissionCodes.DriverRegApprove, PermissionCodes.DriverRegReject,
            PermissionCodes.InfluencerView, PermissionCodes.CustomersView, PermissionCodes.CustomersSuspend,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.OperationsAdmin,
        [
            PermissionCodes.AuditView, PermissionCodes.DocumentsReview, PermissionCodes.DocumentsView,
            PermissionCodes.RestaurantRegView, PermissionCodes.RestaurantRegApprove, PermissionCodes.RestaurantRegReject,
            PermissionCodes.DriverRegView, PermissionCodes.DriverRegApprove, PermissionCodes.DriverRegReject,
            PermissionCodes.CustomersView,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.FinanceAdmin,
        [
            PermissionCodes.AuditView, PermissionCodes.AuditExport, PermissionCodes.CustomersView,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.SupportAdmin,
        [
            PermissionCodes.AuditView, PermissionCodes.CustomersView, PermissionCodes.DocumentsView,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.RestaurantOwner,
        [
            PermissionCodes.AuthSelfManage, PermissionCodes.DocumentsUpload, PermissionCodes.RestaurantRegCreate,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.Driver,
        [
            PermissionCodes.AuthSelfManage, PermissionCodes.DocumentsUpload, PermissionCodes.DriverRegCreate,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.Customer,
        [
            PermissionCodes.AuthSelfManage,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.Influencer,
        [
            PermissionCodes.AuthSelfManage, PermissionCodes.InfluencerRegCreate, PermissionCodes.InfluencerView,
        ], cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var admin = await userManager.FindByEmailAsync(DefaultAdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = DefaultAdminEmail,
                Email = DefaultAdminEmail,
                EmailConfirmed = true,
                FullName = "MealMate Super Admin",
                UserType = UserType.Admin,
                AccountStatus = AccountStatus.Active,
                CreatedAtUtc = clock.UtcNow,
            };
            var create = await userManager.CreateAsync(admin, adminPassword);
            if (create.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, AppRoles.SuperAdmin);
                logger.LogWarning(
                    "Seeded default SuperAdmin {Email} — change password immediately",
                    DefaultAdminEmail);
            }
            else
            {
                logger.LogError("Failed to seed admin: {Errors}", string.Join(", ", create.Errors.Select(e => e.Description)));
            }
        }

        // Bootstrap the existing installation as the Kuwait operating tenant.
        // This is idempotent and gives all future tenant-scoped data a stable root.
        var kuwait = await db.Countries.FirstOrDefaultAsync(
            country => country.Code == "KW",
            cancellationToken);
        if (kuwait is null)
        {
            kuwait = new Country
            {
                Code = "KW",
                NameAr = "الكويت",
                NameEn = "Kuwait",
                CurrencyCode = "KWD",
                IsActive = true,
                CreatedAtUtc = clock.UtcNow,
            };
            db.Countries.Add(kuwait);
        }

        var kuwaitTenant = await db.Tenants.FirstOrDefaultAsync(
            tenant => tenant.Code == "TEN-KW",
            cancellationToken);
        if (kuwaitTenant is null)
        {
            kuwaitTenant = new Tenant
            {
                Code = "TEN-KW",
                NameAr = "ميل ميت الكويت",
                NameEn = "MealMate Kuwait",
                CountryId = kuwait.Id,
                Domain = "kw.mealmate.app",
                CurrencyCode = "KWD",
                TimeZoneId = "Asia/Kuwait",
                DefaultLanguage = "ar",
                Status = TenantStatus.Active,
                IsActive = true,
                CreatedAtUtc = clock.UtcNow,
            };
            db.Tenants.Add(kuwaitTenant);
        }

        if (!await db.AccessScopes.AnyAsync(
                scope => scope.TenantId == kuwaitTenant.Id
                    && scope.ScopeType == AccessScopeType.Tenant,
                cancellationToken))
        {
            db.AccessScopes.Add(new AccessScope
            {
                Name = "TEN-KW — MealMate Kuwait",
                ScopeType = AccessScopeType.Tenant,
                CountryId = kuwait.Id,
                TenantId = kuwaitTenant.Id,
                EntityId = kuwaitTenant.Id,
                EntityType = nameof(Tenant),
                IsActive = true,
                CreatedAtUtc = clock.UtcNow,
            });
        }

        // Global access scope
        if (!await db.AccessScopes.AnyAsync(s => s.ScopeType == AccessScopeType.Global, cancellationToken))
        {
            db.AccessScopes.Add(new AccessScope
            {
                Name = "Global",
                ScopeType = AccessScopeType.Global,
                IsActive = true,
                CreatedAtUtc = clock.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task AssignAllPermissionsToRoleAsync(
        AppDbContext db,
        RoleManager<ApplicationRole> roleManager,
        string roleName,
        CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            return;
        }

        var permissionIds = await db.Permissions.Select(p => p.Id).ToListAsync(cancellationToken);
        foreach (var permissionId in permissionIds)
        {
            var exists = await db.RolePermissions.AnyAsync(
                rp => rp.RoleId == role.Id && rp.PermissionId == permissionId,
                cancellationToken);
            if (!exists)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permissionId,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
        }
    }

    private static async Task AssignPermissionsToRoleAsync(
        AppDbContext db,
        RoleManager<ApplicationRole> roleManager,
        string roleName,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            return;
        }

        var codes = permissionCodes.ToHashSet();
        var permissions = await db.Permissions.Where(p => codes.Contains(p.Code)).ToListAsync(cancellationToken);
        foreach (var permission in permissions)
        {
            var exists = await db.RolePermissions.AnyAsync(
                rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id,
                cancellationToken);
            if (!exists)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
        }
    }
}
