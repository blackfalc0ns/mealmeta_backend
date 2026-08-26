using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record RolePermissionEntryDto(
    string RoleId,
    string ModuleKey,
    string PermissionLevel);

public sealed record RoleDefinitionDto(
    string Id,
    LocalizedTextDto Name,
    LocalizedTextDto Description,
    bool IsSystem,
    IReadOnlyDictionary<string, string> Permissions);

public sealed record ModuleDefinitionDto(
    string Key,
    LocalizedTextDto Label,
    LocalizedTextDto Detail);

public sealed record RestaurantPermissionsDto(
    IReadOnlyList<RoleDefinitionDto> Roles,
    IReadOnlyList<ModuleDefinitionDto> Modules);

// ── Static role & module definitions (authoritative source) ───────────────────
internal static class TeamDefinitions
{
    internal static readonly IReadOnlyList<(string Id, string NameAr, string NameEn, string DescAr, string DescEn)> Roles =
    [
        ("owner",          "مالك",        "Owner",          "صلاحيات كاملة على المطعم والفروع والموظفين",       "Full access to restaurant, branches, and staff"),
        ("branch_manager", "مدير فرع",    "Branch manager", "تشغيل وطلبات وقائمة للفروع المعيّنة",             "Ops, orders, and menu for assigned branches"),
        ("kitchen",        "مطبخ",        "Kitchen",        "طلبات وملصقات وسعة يومية",                         "Orders, labels, and daily capacity"),
        ("finance",        "مالية",       "Finance",        "تقارير مالية ومدفوعات",                            "Financial reports and payments"),
        ("viewer",         "مشاهد",       "Viewer",         "عرض فقط بدون تعديل",                               "View-only access across all modules"),
    ];

    internal static readonly IReadOnlyList<(string Key, string LabelAr, string LabelEn, string DetailAr, string DetailEn)> Modules =
    [
        ("overview",   "إدارة الرئيسية",  "Overview dept",   "نظرة عامة وتحليلات",          "Overview and analytics"),
        ("orders",     "إدارة الطلبات",   "Orders dept",     "اليوم والتأكيد والملصقات",      "Daily, confirmation, labels"),
        ("menu",       "إدارة القوائم",   "Menus dept",      "الوجبات والتسعير",              "Meals and pricing"),
        ("operations", "إدارة التشغيل",  "Operations dept", "المناطق والسعة",                "Regions and capacity"),
        ("delivery",   "إدارة التوصيل",  "Delivery dept",   "السائقون والرحلات",             "Drivers and trips"),
        ("finance",    "إدارة المالية",   "Finance dept",    "مستحقات وفواتير",               "Dues and invoices"),
        ("quality",    "إدارة الجودة",   "Quality dept",    "التقييمات والشكاوى",            "Ratings and complaints"),
        ("team",       "إدارة الفريق",   "Team dept",       "فروع وموظفون وصلاحيات",         "Branches, staff, permissions"),
        ("settings",   "إدارة الإعدادات","Settings dept",   "ملف المطعم والأمان",            "Restaurant profile and security"),
    ];
}

// ── Default role-permission matrix ────────────────────────────────────────────
internal static class DefaultRolePermissions
{
    internal static readonly IReadOnlyList<(string RoleId, string Module, string Level)> Defaults =
    [
        // owner — full manage
        ("owner", "overview","manage"), ("owner","orders","manage"), ("owner","menu","manage"),
        ("owner","operations","manage"), ("owner","delivery","manage"), ("owner","finance","manage"),
        ("owner","quality","manage"), ("owner","team","manage"), ("owner","settings","manage"),
        // branch_manager
        ("branch_manager","overview","manage"), ("branch_manager","orders","manage"),
        ("branch_manager","menu","manage"), ("branch_manager","operations","manage"),
        ("branch_manager","delivery","view"), ("branch_manager","finance","view"),
        ("branch_manager","quality","view"), ("branch_manager","team","view"),
        ("branch_manager","settings","none"),
        // kitchen
        ("kitchen","overview","view"), ("kitchen","orders","manage"), ("kitchen","menu","view"),
        ("kitchen","operations","manage"), ("kitchen","delivery","none"), ("kitchen","finance","none"),
        ("kitchen","quality","view"), ("kitchen","team","none"), ("kitchen","settings","none"),
        // finance
        ("finance","overview","view"), ("finance","orders","view"), ("finance","menu","view"),
        ("finance","operations","view"), ("finance","delivery","none"), ("finance","finance","manage"),
        ("finance","quality","view"), ("finance","team","none"), ("finance","settings","none"),
        // viewer
        ("viewer","overview","view"), ("viewer","orders","view"), ("viewer","menu","view"),
        ("viewer","operations","view"), ("viewer","delivery","view"), ("viewer","finance","view"),
        ("viewer","quality","view"), ("viewer","team","none"), ("viewer","settings","none"),
    ];

    internal static IReadOnlyDictionary<string, string> ForRole(string roleId,
        IEnumerable<(string RoleId, string Module, string Level)> overrides)
    {
        var dict = Defaults
            .Where(d => d.RoleId == roleId)
            .ToDictionary(d => d.Module, d => d.Level);
        foreach (var ov in overrides.Where(o => o.RoleId == roleId))
            dict[ov.Module] = ov.Level;
        return dict;
    }
}

// ── Query: Get full permissions config for a restaurant ───────────────────────

public sealed record GetRolePermissionsQuery(Guid RestaurantRegistrationId) : IQuery<RestaurantPermissionsDto>;

public sealed class GetRolePermissionsQueryHandler : IQueryHandler<GetRolePermissionsQuery, RestaurantPermissionsDto>
{
    private readonly IAppDbContext _db;

    public GetRolePermissionsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<RestaurantPermissionsDto>> Handle(
        GetRolePermissionsQuery request,
        CancellationToken cancellationToken)
    {
        // Load saved overrides from DB (safe — falls back to empty if table missing)
        IReadOnlyList<(string RoleId, string Module, string Level)> saved = [];
        try
        {
            var rows = await _db.RestaurantRolePermissions
                .AsNoTracking()
                .Where(p => request.RestaurantRegistrationId == Guid.Empty
                         || p.RestaurantRegistrationId == request.RestaurantRegistrationId)
                .Select(p => new { p.RoleId, p.ModuleKey, p.PermissionLevel })
                .ToListAsync(cancellationToken);
            saved = rows.Select(r => (r.RoleId, r.ModuleKey, r.PermissionLevel)).ToList();
        }
        catch { /* table may not exist yet — use defaults */ }

        // Load custom roles from DB
        IReadOnlyList<RestaurantCustomRole> customRoles = [];
        try
        {
            customRoles = await _db.RestaurantCustomRoles
                .AsNoTracking()
                .Where(r => request.RestaurantRegistrationId == Guid.Empty
                         || r.RestaurantRegistrationId == request.RestaurantRegistrationId)
                .ToListAsync(cancellationToken);
        }
        catch { }

        // Build system role definitions with merged permissions
        var roles = TeamDefinitions.Roles.Select(r => new RoleDefinitionDto(
            Id: r.Id,
            Name: new LocalizedTextDto(r.NameAr, r.NameEn),
            Description: new LocalizedTextDto(r.DescAr, r.DescEn),
            IsSystem: true,
            Permissions: DefaultRolePermissions.ForRole(r.Id, saved)
        )).ToList();

        // Append custom roles
        foreach (var cr in customRoles)
        {
            roles.Add(new RoleDefinitionDto(
                Id: cr.RoleId,
                Name: new LocalizedTextDto(cr.NameAr, cr.NameEn),
                Description: new LocalizedTextDto(cr.DescriptionAr, cr.DescriptionEn),
                IsSystem: false,
                Permissions: DefaultRolePermissions.ForRole(cr.RoleId, saved)
            ));
        }

        // Build module definitions
        var modules = TeamDefinitions.Modules.Select(m => new ModuleDefinitionDto(
            Key: m.Key,
            Label: new LocalizedTextDto(m.LabelAr, m.LabelEn),
            Detail: new LocalizedTextDto(m.DetailAr, m.DetailEn)
        )).ToList();

        return Result.Success(new RestaurantPermissionsDto(roles, modules));
    }
}

// ── Command: Create custom role ───────────────────────────────────────────────

public sealed record CreateCustomRoleCommand(
    Guid RestaurantRegistrationId,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn) : ICommand<RoleDefinitionDto>;

public sealed class CreateCustomRoleCommandHandler : ICommandHandler<CreateCustomRoleCommand, RoleDefinitionDto>
{
    private readonly IAppDbContext _db;

    public CreateCustomRoleCommandHandler(IAppDbContext db) => _db = db;

    public async Task<Result<RoleDefinitionDto>> Handle(CreateCustomRoleCommand request, CancellationToken cancellationToken)
    {
        var regId = request.RestaurantRegistrationId;
        if (regId == Guid.Empty)
        {
            regId = await _db.RestaurantRegistrations.Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        }

        var roleId = $"custom_{Guid.NewGuid():N}";
        var moduleKeys = TeamDefinitions.Modules.Select(m => m.Key).ToList();

        var customRole = new RestaurantCustomRole
        {
            RestaurantRegistrationId = regId,
            RoleId = roleId,
            NameAr = request.NameAr,
            NameEn = string.IsNullOrWhiteSpace(request.NameEn) ? request.NameAr : request.NameEn,
            DescriptionAr = request.DescriptionAr ?? "",
            DescriptionEn = string.IsNullOrWhiteSpace(request.DescriptionEn) ? (request.DescriptionAr ?? "") : request.DescriptionEn,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.RestaurantCustomRoles.Add(customRole);

        // Seed default 'none' permissions for all modules
        var perms = moduleKeys.Select(m => new RestaurantRolePermission
        {
            RestaurantRegistrationId = regId,
            RoleId = roleId,
            ModuleKey = m,
            PermissionLevel = "none",
            CreatedAtUtc = DateTime.UtcNow,
        }).ToList();
        _db.RestaurantRolePermissions.AddRange(perms);

        await _db.SaveChangesAsync(cancellationToken);

        IReadOnlyDictionary<string, string> permissions = moduleKeys.ToDictionary(m => m, _ => "none");

        return Result.Success(new RoleDefinitionDto(
            roleId,
            new LocalizedTextDto(customRole.NameAr, customRole.NameEn),
            new LocalizedTextDto(customRole.DescriptionAr, customRole.DescriptionEn),
            false,
            permissions));
    }
}

// ── Command: Delete custom role ───────────────────────────────────────────────

public sealed record DeleteCustomRoleCommand(
    Guid RestaurantRegistrationId,
    string RoleId) : ICommand;

public sealed class DeleteCustomRoleCommandHandler : ICommandHandler<DeleteCustomRoleCommand>
{
    private readonly IAppDbContext _db;

    public DeleteCustomRoleCommandHandler(IAppDbContext db) => _db = db;

    public async Task<Result> Handle(DeleteCustomRoleCommand request, CancellationToken cancellationToken)
    {
        if (!request.RoleId.StartsWith("custom_"))
            return Error.Failure("Role.CannotDelete", "System roles cannot be deleted.");

        var role = await _db.RestaurantCustomRoles
            .FirstOrDefaultAsync(r => (request.RestaurantRegistrationId == Guid.Empty || r.RestaurantRegistrationId == request.RestaurantRegistrationId)
                                   && r.RoleId == request.RoleId, cancellationToken);
        if (role is null) return Error.NotFound("Role.NotFound", "Custom role not found.");

        _db.RestaurantCustomRoles.Remove(role);

        var perms = await _db.RestaurantRolePermissions
            .Where(p => (request.RestaurantRegistrationId == Guid.Empty || p.RestaurantRegistrationId == request.RestaurantRegistrationId)
                     && p.RoleId == request.RoleId)
            .ToListAsync(cancellationToken);
        _db.RestaurantRolePermissions.RemoveRange(perms);

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}


// ── Command: Update one role-module permission ────────────────────────────────

public sealed record UpdateRolePermissionCommand(
    Guid RestaurantRegistrationId,
    string RoleId,
    string ModuleKey,
    string PermissionLevel) : ICommand<RolePermissionEntryDto>;

public sealed class UpdateRolePermissionCommandHandler : ICommandHandler<UpdateRolePermissionCommand, RolePermissionEntryDto>
{
    private readonly IAppDbContext _db;

    public UpdateRolePermissionCommandHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<RolePermissionEntryDto>> Handle(
        UpdateRolePermissionCommand request,
        CancellationToken cancellationToken)
    {
        // owner is always manage — ignore any attempt to change
        if (request.RoleId == "owner")
        {
            return Result.Success(new RolePermissionEntryDto("owner", request.ModuleKey, "manage"));
        }

        try
        {
            var regId = request.RestaurantRegistrationId;
            if (regId == Guid.Empty)
            {
                regId = await _db.RestaurantRegistrations.Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            }

            var existing = await _db.RestaurantRolePermissions
                .FirstOrDefaultAsync(
                    p => (regId == Guid.Empty || p.RestaurantRegistrationId == regId)
                      && p.RoleId == request.RoleId
                      && p.ModuleKey == request.ModuleKey,
                    cancellationToken);

            if (existing is null)
            {
                existing = new RestaurantRolePermission
                {
                    RestaurantRegistrationId = regId,
                    RoleId = request.RoleId,
                    ModuleKey = request.ModuleKey,
                    PermissionLevel = request.PermissionLevel,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _db.RestaurantRolePermissions.Add(existing);
            }
            else
            {
                existing.PermissionLevel = request.PermissionLevel;
                existing.ModifiedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(new RolePermissionEntryDto(request.RoleId, request.ModuleKey, request.PermissionLevel));
        }
        catch
        {
            return Result.Success(new RolePermissionEntryDto(request.RoleId, request.ModuleKey, request.PermissionLevel));
        }
    }
}
