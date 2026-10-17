using System.IO;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
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
        await ProgramsSubscriptionsSeeder.SeedProgramsAndBundlesAsync(db, logger, cancellationToken);

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
            PermissionCodes.AdminCoverageOperationsView,
            PermissionCodes.DriverRegView, PermissionCodes.DriverRegApprove, PermissionCodes.DriverRegReject,
            PermissionCodes.InfluencerView, PermissionCodes.CustomersView, PermissionCodes.CustomersSuspend,
            PermissionCodes.ProgramsView, PermissionCodes.ProgramsManage, PermissionCodes.ProgramsApprove,
            PermissionCodes.BundlesView, PermissionCodes.BundlesManage,
            PermissionCodes.SubscriptionsView, PermissionCodes.SubscriptionsManage, PermissionCodes.SubscriptionsPricing,
            PermissionCodes.CampaignsView, PermissionCodes.CampaignsManage,
            PermissionCodes.OffersView, PermissionCodes.OffersManage,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.OperationsAdmin,
        [
            PermissionCodes.AuditView, PermissionCodes.DocumentsReview, PermissionCodes.DocumentsView,
            PermissionCodes.RestaurantRegView, PermissionCodes.RestaurantRegApprove, PermissionCodes.RestaurantRegReject,
            PermissionCodes.AdminCoverageOperationsView,
            PermissionCodes.DriverRegView, PermissionCodes.DriverRegApprove, PermissionCodes.DriverRegReject,
            PermissionCodes.CustomersView, PermissionCodes.RestaurantMealsView, PermissionCodes.RestaurantMealsApprove, PermissionCodes.RestaurantMealsReject,
            PermissionCodes.ProgramsView, PermissionCodes.ProgramsManage,
            PermissionCodes.BundlesView, PermissionCodes.BundlesManage,
            PermissionCodes.SubscriptionsView, PermissionCodes.SubscriptionsManage,
            PermissionCodes.CampaignsView, PermissionCodes.OffersView,
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
            PermissionCodes.AuthSelfManage, PermissionCodes.DocumentsUpload, PermissionCodes.RestaurantRegCreate, PermissionCodes.DeliveryManagersManage,
            PermissionCodes.RestaurantMealsView, PermissionCodes.RestaurantMealsManage, PermissionCodes.RestaurantIngredientsView, PermissionCodes.RestaurantIngredientsManage,
            PermissionCodes.CampaignsView, PermissionCodes.CampaignsManage,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.RestaurantStaff,
        [
            PermissionCodes.AuthSelfManage,
            PermissionCodes.RestaurantMealsView, PermissionCodes.RestaurantMealsManage, PermissionCodes.RestaurantIngredientsView, PermissionCodes.RestaurantIngredientsManage,
            PermissionCodes.CampaignsView, PermissionCodes.CampaignsManage,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.Driver,
        [
            PermissionCodes.AuthSelfManage, PermissionCodes.DocumentsUpload, PermissionCodes.DriverRegCreate,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.Customer,
        [
            PermissionCodes.AuthSelfManage,
        ], cancellationToken);
        await AssignPermissionsToRoleAsync(db, roleManager, AppRoles.DeliveryManager,
        [
            PermissionCodes.AuthSelfManage, PermissionCodes.DeliveryDispatchManage, PermissionCodes.DeliveryTripsManage, PermissionCodes.DeliveryDriversManage,
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

        const string newAdminEmail = "abanoub@mealmate.com";
        var newAdmin = await userManager.FindByEmailAsync(newAdminEmail);
        if (newAdmin is null)
        {
            newAdmin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = newAdminEmail,
                Email = newAdminEmail,
                EmailConfirmed = true,
                FullName = "Abanoub Admin",
                UserType = UserType.Admin,
                AccountStatus = AccountStatus.Active,
                CreatedAtUtc = clock.UtcNow,
            };
            var createNew = await userManager.CreateAsync(newAdmin, "Admin!Password123");
            if (createNew.Succeeded)
            {
                await userManager.AddToRoleAsync(newAdmin, AppRoles.SuperAdmin);
                logger.LogInformation("Seeded SuperAdmin user {Email}", newAdminEmail);
            }
            else
            {
                logger.LogError("Failed to seed admin user {Email}: {Errors}", newAdminEmail, string.Join(", ", createNew.Errors.Select(e => e.Description)));
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
                DialCode = "+965",
                FlagEmoji = "🇰🇼",
                IsActive = true,
                CreatedAtUtc = clock.UtcNow,
            };
            db.Countries.Add(kuwait);
        }
        else
        {
            kuwait.DialCode = "+965";
            kuwait.FlagEmoji = "🇰🇼";
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

        await KuwaitRegionCatalogSeeder.SeedAsync(db, clock.UtcNow, cancellationToken);

        var fileStorage = sp.GetRequiredService<IFileStorage>();
        await SeedDriverWithDocumentsAsync(db, userManager, fileStorage, clock, cancellationToken);
        await SeedDeliveryManagerAsync(db, userManager, clock, cancellationToken);
        await SeedRestaurantsWithDocumentsAsync(db, fileStorage, clock, cancellationToken);
    }

    private static async Task SeedDriverWithDocumentsAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IFileStorage fileStorage,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        var restaurant = await db.RestaurantRegistrations.FirstOrDefaultAsync(cancellationToken);
        var restaurantId = restaurant?.Id;

        var email = "driver1@mealmate.local";
        var driver = await userManager.FindByEmailAsync(email);
        if (driver is null)
        {
            driver = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = "Ahmed Driver (Seed)",
                UserType = UserType.Driver,
                AccountStatus = AccountStatus.Active,
                CreatedAtUtc = clock.UtcNow,
            };
            await userManager.CreateAsync(driver, "ChangeMe!Driver123");
            await userManager.AddToRoleAsync(driver, AppRoles.Driver);
        }

        var profile = await db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == driver.Id, cancellationToken);
        if (profile is null)
        {
            profile = new DriverProfile
            {
                UserId = driver.Id,
                RestaurantId = restaurantId,
                FullNameAr = "أحمد السائق (مذاق البيت)",
                FullNameEn = "Ahmed Driver (Mazaq)",
                NationalId = "290101012345",
                LicenseNumber = "L-98765",
                VehiclePlate = "KW-1001",
                VehicleType = "Car",
                VerificationStatus = VerificationStatus.Approved,
                IsAvailable = true,
                ActivatedAtUtc = clock.UtcNow,
                CreatedAtUtc = clock.UtcNow,
            };
            db.DriverProfiles.Add(profile);
            await db.SaveChangesAsync(cancellationToken);
        }

        var reg = await db.DriverRegistrations.FirstOrDefaultAsync(r => r.SubmittedByUserId == driver.Id, cancellationToken);
        if (reg is null)
        {
            reg = new DriverRegistration
            {
                SubmittedByUserId = driver.Id,
                RestaurantId = restaurantId,
                FullNameAr = "أحمد السائق (مذاق البيت)",
                FullNameEn = "Ahmed Driver (Mazaq)",
                Phone = "+96512345678",
                NationalId = "290101012345",
                LicenseNumber = "L-98765",
                VehiclePlate = "KW-1001",
                VehicleType = "Car",
                Status = VerificationStatus.Approved,
                CreatedAtUtc = clock.UtcNow,
            };
            db.DriverRegistrations.Add(reg);
            await db.SaveChangesAsync(cancellationToken);

            var docTypes = new[] { DocumentType.NationalIdFront, DocumentType.NationalIdBack, DocumentType.DrivingLicense, DocumentType.ProfileImage };
            foreach (var dt in docTypes)
            {
                var pdfBytes = GenerateSamplePdfBytes("Ahmed Driver (Seed)", dt.ToString());
                using var ms = new MemoryStream(pdfBytes);
                var stored = await fileStorage.SaveAsync(ms, $"{dt}.pdf", "application/pdf", "uploads/driver-reg-seed", cancellationToken);
                
                var doc = new Document
                {
                    OwnerType = DocumentOwnerType.Registration,
                    OwnerId = reg.Id,
                    DocumentType = dt,
                    FileName = $"{dt}.pdf",
                    ContentType = "application/pdf",
                    StoragePath = stored.StorageKey,
                    SizeBytes = stored.SizeBytes,
                    Checksum = stored.Checksum,
                    CreatedAtUtc = clock.UtcNow,
                };
                db.Documents.Add(doc);
                
                doc.Verifications.Add(new DocumentVerification
                {
                    Status = VerificationStatus.Approved,
                    CreatedAtUtc = clock.UtcNow,
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedDeliveryManagerAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        var restaurant = await db.RestaurantRegistrations.FirstOrDefaultAsync(cancellationToken);
        var restaurantId = restaurant?.ResultingRestaurantId ?? restaurant?.Id;
        if (!restaurantId.HasValue) return;

        const string email = "dispatcher@mealmate.local";
        const string phone = "+965949499494";

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
        }

        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                PhoneNumber = phone,
                PhoneNumberConfirmed = true,
                EmailConfirmed = true,
                FullName = "خالد المرسل (مسؤول التوصيل)",
                UserType = UserType.DeliveryManager,
                AccountStatus = AccountStatus.Registered,
                HasSetPassword = false,
                RestaurantId = restaurantId.Value,
                CreatedAtUtc = clock.UtcNow,
            };
            var result = await userManager.CreateAsync(user, "Password123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, AppRoles.DeliveryManager);
            }
        }
        else
        {
            user.HasSetPassword = false;
            user.AccountStatus = AccountStatus.Registered;
            await userManager.UpdateAsync(user);
        }

        var reg = await db.DeliveryManagerRegistrations.FirstOrDefaultAsync(
            r => r.Phone == phone || r.ResultingUserId == user.Id, cancellationToken);
        if (reg is null)
        {
            reg = new DeliveryManagerRegistration
            {
                SubmittedByUserId = user.Id,
                RestaurantId = restaurantId.Value,
                FullName = "خالد المرسل (مسؤول التوصيل)",
                Phone = phone,
                Email = email,
                NationalId = "295050512345",
                NationalIdExpiryUtc = clock.UtcNow.AddYears(2),
                ContractExpiryUtc = clock.UtcNow.AddYears(1),
                CoverageType = DeliveryManagerCoverageType.AllRestaurantCoverage,
                Status = VerificationStatus.Approved,
                CreatedAtUtc = clock.UtcNow,
                ResultingUserId = user.Id
            };
            db.DeliveryManagerRegistrations.Add(reg);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedRestaurantsWithDocumentsAsync(
        AppDbContext db,
        IFileStorage fileStorage,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        var regs = await db.RestaurantRegistrations.ToListAsync(cancellationToken);
        var docTypes = new[] { DocumentType.NationalId, DocumentType.CommercialLicense, DocumentType.Contract };

        if (!regs.Any())
        {
            var sampleReg = new RestaurantRegistration
            {
                LegalNameAr = "شركة مذاق البيت التجارية",
                LegalNameEn = "Mazaq Al-Bait Trading Co.",
                TradeNameAr = "مطعم مذاق البيت",
                TradeNameEn = "Mazaq Al-Bait Restaurant",
                ContactPhone = "+96599887766",
                ContactEmail = "info@mazaq.local",
                CommercialRegistrationNumber = "CR-998877",
                OwnerName = "عبد الله العتيبي",
                Address = "مدينة الكويت، الشارع التجاري، برج السلام",
                CountryId = Guid.NewGuid(),
                RegionId = Guid.NewGuid(),
                Status = VerificationStatus.Submitted,
                CreatedAtUtc = clock.UtcNow,
            };
            db.RestaurantRegistrations.Add(sampleReg);
            await db.SaveChangesAsync(cancellationToken);
            regs.Add(sampleReg);
        }

        foreach (var reg in regs)
        {
            var hasDocs = await db.Documents.AnyAsync(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == reg.Id, cancellationToken);
            if (hasDocs) continue;

            foreach (var dt in docTypes)
            {
                var pdfBytes = GenerateSamplePdfBytes(reg.TradeNameEn ?? reg.LegalNameEn ?? "Restaurant", dt.ToString());
                using var ms = new MemoryStream(pdfBytes);
                var stored = await fileStorage.SaveAsync(ms, $"{dt}.pdf", "application/pdf", "uploads/restaurant-reg-seed", cancellationToken);

                var doc = new Document
                {
                    OwnerType = DocumentOwnerType.Registration,
                    OwnerId = reg.Id,
                    DocumentType = dt,
                    FileName = $"{dt}.pdf",
                    ContentType = "application/pdf",
                    StoragePath = stored.StorageKey,
                    SizeBytes = stored.SizeBytes,
                    Checksum = stored.Checksum,
                    CreatedAtUtc = clock.UtcNow,
                };
                db.Documents.Add(doc);

                doc.Verifications.Add(new DocumentVerification
                {
                    Status = VerificationStatus.Submitted,
                    CreatedAtUtc = clock.UtcNow,
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static byte[] GenerateSamplePdfBytes(string title, string docName)
    {
        var pdfContent = $@"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>
endobj
4 0 obj
<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>
endobj
5 0 obj
<< /Length 200 >>
stream
BT
/F1 20 Tf
50 720 Td
(MealMate Official Document Verification) Tj
0 -35 Td
/F1 14 Tf
(Account: {title}) Tj
0 -25 Td
(Document Type: {docName}) Tj
0 -25 Td
(Status: Verified PDF Sample Document) Tj
ET
endstream
endobj
xref
0 6
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000249 00000 n 
0000000323 00000 n 
trailer
<< /Size 6 /Root 1 0 R >>
startxref
570
%%EOF";
        return System.Text.Encoding.UTF8.GetBytes(pdfContent);
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

    public static async Task CreateSuperAdminAsync(
        IServiceProvider services,
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityDataSeeder");
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = sp.GetRequiredService<IDateTimeProvider>();

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            logger.LogWarning("Admin {Email} already exists; no user created", normalizedEmail);
            return;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = true,
            FullName = string.IsNullOrWhiteSpace(fullName) ? "MealMate Admin" : fullName.Trim(),
            UserType = UserType.Admin,
            AccountStatus = AccountStatus.Active,
            CreatedAtUtc = clock.UtcNow,
        };

        var create = await userManager.CreateAsync(admin, password);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create admin {normalizedEmail}: {string.Join(", ", create.Errors.Select(e => e.Description))}");
        }

        var role = await userManager.AddToRoleAsync(admin, AppRoles.SuperAdmin);
        if (!role.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign SuperAdmin to {normalizedEmail}: {string.Join(", ", role.Errors.Select(e => e.Description))}");
        }

        logger.LogInformation("Created SuperAdmin {Email}", normalizedEmail);
    }
}
