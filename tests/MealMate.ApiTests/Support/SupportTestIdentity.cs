using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MealMate.ApiTests.Support;

internal static class SupportTestIdentity
{
    public static async Task SeedAsync(AppDbContext db, TokenUser tokenUser, CancellationToken ct = default)
    {
        if (tokenUser.UserType != UserType.Admin) throw new ArgumentException("Only support operator test identities are seeded here.", nameof(tokenUser));
        if (!await db.Users.AnyAsync(x => x.Id == tokenUser.UserId, ct))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = tokenUser.UserId,
                UserType = UserType.Admin,
                AccountStatus = AccountStatus.Active,
                UserName = $"support-test-{tokenUser.UserId:N}",
                NormalizedUserName = $"SUPPORT-TEST-{tokenUser.UserId:N}",
                Email = $"support-test-{tokenUser.UserId:N}@example.test",
                NormalizedEmail = $"SUPPORT-TEST-{tokenUser.UserId:N}@EXAMPLE.TEST",
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        foreach (var roleName in tokenUser.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var role = await db.Roles.SingleOrDefaultAsync(x => x.NormalizedName == roleName.ToUpper(), ct);
            if (role is null)
            {
                role = new ApplicationRole(roleName)
                {
                    Id = Guid.NewGuid(),
                    NormalizedName = roleName.ToUpper(),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                };
                db.Roles.Add(role);
            }
            if (!await db.UserRoles.AnyAsync(x => x.UserId == tokenUser.UserId && x.RoleId == role.Id, ct))
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = tokenUser.UserId, RoleId = role.Id });

            foreach (var code in tokenUser.Permissions.Distinct(StringComparer.Ordinal))
            {
                var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code, ct);
                if (permission is null)
                {
                    permission = new Permission
                    {
                        Code = code,
                        NameAr = code,
                        NameEn = code,
                        Module = "Support",
                        IsActive = true,
                    };
                    db.Permissions.Add(permission);
                }
                if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id, ct))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
        }

        foreach (var scopeId in tokenUser.ScopeIds?.Distinct() ?? [])
        {
            if (await db.AccessScopes.AnyAsync(x => x.Id == scopeId, ct)
                && !await db.UserScopes.AnyAsync(x => x.UserId == tokenUser.UserId && x.AccessScopeId == scopeId, ct))
                db.UserScopes.Add(new UserScope { UserId = tokenUser.UserId, AccessScopeId = scopeId });
        }
        await db.SaveChangesAsync(ct);
    }
}
