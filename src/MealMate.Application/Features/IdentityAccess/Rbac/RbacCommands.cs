using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Rbac;

public sealed record RoleListItemDto(Guid Id, string Name, string? Description, IReadOnlyList<string> Permissions);
public sealed record PermissionListItemDto(Guid Id, string Code, string NameEn, string NameAr, string Module);
public sealed record AccessScopeListItemDto(
    Guid Id,
    string Name,
    AccessScopeType ScopeType,
    Guid? CountryId,
    Guid? RegionId,
    Guid? RestaurantId,
    Guid? EntityId,
    string? EntityType);

public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleListItemDto>>;

public sealed class ListRolesQueryHandler : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleListItemDto>>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;

    public ListRolesQueryHandler(IUserAccountService users, IAppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public async Task<Result<IReadOnlyList<RoleListItemDto>>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _users.ListRolesAsync(cancellationToken);
        var result = new List<RoleListItemDto>();
        foreach (var role in roles)
        {
            var codes = await (
                from rp in _db.RolePermissions
                join p in _db.Permissions on rp.PermissionId equals p.Id
                where rp.RoleId == role.Id && p.IsActive
                select p.Code).ToListAsync(cancellationToken);
            result.Add(new RoleListItemDto(role.Id, role.Name, role.Description, codes));
        }

        return result;
    }
}

public sealed record ListPermissionsQuery : IQuery<IReadOnlyList<PermissionListItemDto>>;

public sealed class ListPermissionsQueryHandler : IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionListItemDto>>
{
    private readonly IAppDbContext _db;

    public ListPermissionsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<PermissionListItemDto>>> Handle(ListPermissionsQuery request, CancellationToken cancellationToken)
    {
        var items = await _db.Permissions
            .Where(p => p.IsActive)
            .OrderBy(p => p.Module).ThenBy(p => p.Code)
            .Select(p => new PermissionListItemDto(p.Id, p.Code, p.NameEn, p.NameAr, p.Module))
            .ToListAsync(cancellationToken);
        return items;
    }
}

public sealed record ListAccessScopesQuery : IQuery<IReadOnlyList<AccessScopeListItemDto>>;

public sealed class ListAccessScopesQueryHandler : IQueryHandler<ListAccessScopesQuery, IReadOnlyList<AccessScopeListItemDto>>
{
    private readonly IAppDbContext _db;

    public ListAccessScopesQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AccessScopeListItemDto>>> Handle(
        ListAccessScopesQuery request,
        CancellationToken cancellationToken)
    {
        var scopes = await _db.AccessScopes
            .Where(scope => scope.IsActive)
            .OrderBy(scope => scope.ScopeType)
            .ThenBy(scope => scope.Name)
            .Select(scope => new AccessScopeListItemDto(
                scope.Id,
                scope.Name,
                scope.ScopeType,
                scope.CountryId,
                scope.RegionId,
                scope.RestaurantId,
                scope.EntityId,
                scope.EntityType))
            .ToListAsync(cancellationToken);

        return scopes;
    }
}

public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string>? PermissionCodes) : ICommand<RoleListItemDto>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class CreateRoleCommandHandler : ICommandHandler<CreateRoleCommand, RoleListItemDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public CreateRoleCommandHandler(IUserAccountService users, IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<RoleListItemDto>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var codes = (request.PermissionCodes ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = new List<Permission>();
        if (codes.Length > 0)
        {
            permissions = await _db.Permissions
                .Where(p => codes.Contains(p.Code))
                .ToListAsync(cancellationToken);
            if (permissions.Count != codes.Length)
            {
                return Error.Validation(MessageCodes.Validation.Unknown, "One or more permission codes are invalid.");
            }
        }

        var created = await _users.CreateRoleAsync(request.Name, request.Description, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        foreach (var permission in permissions)
        {
            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = created.Value.Id,
                PermissionId = permission.Id,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        await _audit.LogAsync(new AuditEntry("RoleCreated", "Role", created.Value.Id, After: request, SourceChannel: "admin"), cancellationToken);
        return new RoleListItemDto(created.Value.Id, created.Value.Name, created.Value.Description, codes.ToList());
    }
}

public sealed record UpdateRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<string> PermissionCodes,
    string Reason) : ICommand;

public sealed class UpdateRolePermissionsCommandValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class UpdateRolePermissionsCommandHandler : ICommandHandler<UpdateRolePermissionsCommand>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public UpdateRolePermissionsCommandHandler(IUserAccountService users, IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await _users.FindRoleByIdAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return Error.NotFound(MessageCodes.Auth.RoleNotFound, "Role not found.");
        }

        if (AppRoles.IsSuperAdmin(role.Name))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot modify SuperAdmin role permissions.");
        }

        var before = await (
            from rp in _db.RolePermissions
            join p in _db.Permissions on rp.PermissionId equals p.Id
            where rp.RoleId == role.Id
            select p.Code).ToListAsync(cancellationToken);

        var requestedCodes = request.PermissionCodes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = await _db.Permissions
            .Where(p => requestedCodes.Contains(p.Code))
            .ToListAsync(cancellationToken);

        if (permissions.Count != requestedCodes.Length)
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "One or more permission codes are invalid.");
        }

        var permissionIds = permissions.Select(permission => permission.Id).ToHashSet();
        var existing = await _db.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync(cancellationToken);

        foreach (var item in existing)
        {
            var shouldBeActive = permissionIds.Contains(item.PermissionId);
            item.IsDeleted = !shouldBeActive;
            item.DeletedAtUtc = shouldBeActive ? null : _clock.UtcNow;
        }

        var existingPermissionIds = existing.Select(item => item.PermissionId).ToHashSet();
        foreach (var permission in permissions)
        {
            if (existingPermissionIds.Contains(permission.Id))
            {
                continue;
            }

            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        await _audit.LogAsync(new AuditEntry(
            "RolePermissionsUpdated",
            "Role",
            role.Id,
            Before: before,
            After: requestedCodes,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}

public sealed record AssignUserRoleCommand(Guid UserId, string RoleName, string Reason) : ICommand;
public sealed record RemoveUserRoleCommand(Guid UserId, string RoleName, string Reason) : ICommand;

public sealed class AssignUserRoleCommandValidator : AbstractValidator<AssignUserRoleCommand>
{
    public AssignUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleName).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class RemoveUserRoleCommandValidator : AbstractValidator<RemoveUserRoleCommand>
{
    public RemoveUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleName).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class AssignUserRoleCommandHandler : ICommandHandler<AssignUserRoleCommand>
{
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public AssignUserRoleCommandHandler(IUserAccountService users, ICurrentUser currentUser, IAuditLogger audit)
    {
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result> Handle(AssignUserRoleCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == request.UserId)
        {
            return Error.Forbidden(MessageCodes.Auth.SelfPrivilegeEscalation, "Cannot assign roles to yourself.");
        }

        if (AppRoles.IsSuperAdmin(request.RoleName) && !_currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Only SuperAdmin can assign SuperAdmin role.");
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Admin user not found.");
        }

        var before = await _users.GetRolesAsync(request.UserId, cancellationToken);
        var result = await _users.AddToRoleAsync(request.UserId, request.RoleName, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        var after = await _users.GetRolesAsync(request.UserId, cancellationToken);
        await _audit.LogAsync(new AuditEntry(
            "UserRoleAssigned",
            "User",
            request.UserId,
            Before: before,
            After: after,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);
        return Result.Success();
    }
}

public sealed class RemoveUserRoleCommandHandler : ICommandHandler<RemoveUserRoleCommand>
{
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public RemoveUserRoleCommandHandler(IUserAccountService users, ICurrentUser currentUser, IAuditLogger audit)
    {
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result> Handle(RemoveUserRoleCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == request.UserId)
        {
            return Error.Forbidden(MessageCodes.Auth.SelfPrivilegeEscalation, "Cannot remove your own roles.");
        }

        if (AppRoles.IsSuperAdmin(request.RoleName) && !_currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Only SuperAdmin can remove SuperAdmin role.");
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Admin user not found.");
        }

        var before = await _users.GetRolesAsync(request.UserId, cancellationToken);
        var result = await _users.RemoveFromRoleAsync(request.UserId, request.RoleName, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        var after = await _users.GetRolesAsync(request.UserId, cancellationToken);
        await _audit.LogAsync(new AuditEntry(
            "UserRoleRemoved",
            "User",
            request.UserId,
            Before: before,
            After: after,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);
        return Result.Success();
    }
}

public sealed record AssignUserScopeCommand(Guid UserId, Guid AccessScopeId, string Reason) : ICommand;
public sealed record RemoveUserScopeCommand(Guid UserId, Guid AccessScopeId, string Reason) : ICommand;

public sealed class AssignUserScopeCommandValidator : AbstractValidator<AssignUserScopeCommand>
{
    public AssignUserScopeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.AccessScopeId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RemoveUserScopeCommandValidator : AbstractValidator<RemoveUserScopeCommand>
{
    public RemoveUserScopeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.AccessScopeId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class AssignUserScopeCommandHandler : ICommandHandler<AssignUserScopeCommand>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public AssignUserScopeCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result> Handle(AssignUserScopeCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == request.UserId)
        {
            return Error.Forbidden(MessageCodes.Auth.SelfPrivilegeEscalation, "Cannot escalate your own scope.");
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Admin user not found.");
        }

        var scope = await _db.AccessScopes.FirstOrDefaultAsync(s => s.Id == request.AccessScopeId, cancellationToken);
        if (scope is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Access scope not found.");
        }

        var existing = await _db.UserScopes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
            us => us.UserId == request.UserId && us.AccessScopeId == request.AccessScopeId,
            cancellationToken);
        if (existing is null)
        {
            _db.UserScopes.Add(new UserScope
            {
                UserId = request.UserId,
                AccessScopeId = request.AccessScopeId,
                CreatedAtUtc = _clock.UtcNow,
            });
        }
        else if (existing.IsDeleted)
        {
            existing.IsDeleted = false;
            existing.DeletedAtUtc = null;
        }

        await _audit.LogAsync(new AuditEntry(
            "UserScopeAssigned",
            "UserScope",
            request.UserId,
            After: new { request.AccessScopeId },
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}

public sealed class RemoveUserScopeCommandHandler : ICommandHandler<RemoveUserScopeCommand>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public RemoveUserScopeCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result> Handle(RemoveUserScopeCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == request.UserId)
        {
            return Error.Forbidden(MessageCodes.Auth.SelfPrivilegeEscalation, "Cannot remove your own scope.");
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Admin user not found.");
        }

        var existing = await _db.UserScopes.FirstOrDefaultAsync(
            scope => scope.UserId == request.UserId && scope.AccessScopeId == request.AccessScopeId,
            cancellationToken);

        if (existing is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "User scope assignment not found.");
        }

        existing.IsDeleted = true;
        existing.DeletedAtUtc = _clock.UtcNow;

        await _audit.LogAsync(new AuditEntry(
            "UserScopeRemoved",
            "UserScope",
            request.UserId,
            Before: new { request.AccessScopeId },
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}
