using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Rbac;

public sealed record RoleListItemDto(Guid Id, string Name, string? Description, IReadOnlyList<string> Permissions);
public sealed record PermissionListItemDto(Guid Id, string Code, string NameEn, string NameAr, string Module);

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
        var created = await _users.CreateRoleAsync(request.Name, request.Description, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var codes = request.PermissionCodes ?? Array.Empty<string>();
        if (codes.Count > 0)
        {
            var permissions = await _db.Permissions.Where(p => codes.Contains(p.Code)).ToListAsync(cancellationToken);
            foreach (var permission in permissions)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    RoleId = created.Value.Id,
                    PermissionId = permission.Id,
                    CreatedAtUtc = _clock.UtcNow,
                });
            }
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

        if (role.Name == AppRoles.SuperAdmin)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot modify SuperAdmin role permissions.");
        }

        var before = await (
            from rp in _db.RolePermissions
            join p in _db.Permissions on rp.PermissionId equals p.Id
            where rp.RoleId == role.Id
            select p.Code).ToListAsync(cancellationToken);

        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(cancellationToken);
        foreach (var item in existing)
        {
            item.IsDeleted = true;
            item.DeletedAtUtc = _clock.UtcNow;
        }

        var permissions = await _db.Permissions
            .Where(p => request.PermissionCodes.Contains(p.Code))
            .ToListAsync(cancellationToken);

        foreach (var permission in permissions)
        {
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
            After: request.PermissionCodes,
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

public sealed class AssignUserScopeCommandHandler : ICommandHandler<AssignUserScopeCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public AssignUserScopeCommandHandler(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, IDateTimeProvider clock)
    {
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

        var scope = await _db.AccessScopes.FirstOrDefaultAsync(s => s.Id == request.AccessScopeId, cancellationToken);
        if (scope is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Access scope not found.");
        }

        var exists = await _db.UserScopes.AnyAsync(
            us => us.UserId == request.UserId && us.AccessScopeId == request.AccessScopeId,
            cancellationToken);
        if (!exists)
        {
            _db.UserScopes.Add(new UserScope
            {
                UserId = request.UserId,
                AccessScopeId = request.AccessScopeId,
                CreatedAtUtc = _clock.UtcNow,
            });
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
