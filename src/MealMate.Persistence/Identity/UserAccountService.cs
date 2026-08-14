using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Identity;

public sealed class UserAccountService : IUserAccountService
{
    private const string DummyPassword = "DummyPassword123!";

    private static readonly Lazy<string> DummyPasswordHash = new(() =>
    {
        var hasher = new PasswordHasher<ApplicationUser>();
        return hasher.HashPassword(new ApplicationUser { UserName = "dummy", LockoutEnabled = true }, DummyPassword);
    });

    private static readonly ApplicationUser DummyUser = new()
    {
        UserName = "dummy",
        LockoutEnabled = true,
    };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public UserAccountService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        AppDbContext db,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _clock = clock;
    }

    public async Task<UserAccountDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : Map(user);
    }

    public async Task<UserAccountDto?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(email);
        return user is null ? null : Map(user);
    }

    public async Task<UserAccountDto?> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
        return user is null ? null : Map(user);
    }

    public async Task<IReadOnlyList<UserIdentityDto>> GetUserIdentitiesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return Array.Empty<UserIdentityDto>();
        }

        var distinctIds = userIds.Distinct().ToArray();
        return await _db.Users
            .AsNoTracking()
            .Where(user => distinctIds.Contains(user.Id))
            .Select(user => new UserIdentityDto(user.Id, user.FullName, user.Email, user.PhoneNumber))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<UserAccountDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existingEmail = await _userManager.FindByEmailAsync(request.Email);
            if (existingEmail is not null)
            {
                return Error.Conflict(MessageCodes.Auth.DuplicateEmail, "Email already registered.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var existingPhone = await _db.Users.AnyAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken);
            if (existingPhone)
            {
                return Error.Conflict(MessageCodes.Auth.DuplicatePhone, "Phone already registered.");
            }
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = !string.IsNullOrWhiteSpace(request.Email)
                ? request.Email
                : request.PhoneNumber,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            FullName = request.FullName,
            UserType = request.UserType,
            AccountStatus = request.AccountStatus,
            RestaurantId = request.RestaurantId,
            CreatedAtUtc = _clock.UtcNow,
            EmailConfirmed = false,
            PhoneNumberConfirmed = false,
            LockoutEnabled = true,
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return MapIdentityCreateError(result);
        }

        return Map(user);
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is not null && await _userManager.CheckPasswordAsync(user, password);
    }

    public Task<bool> VerifyPasswordOrDummyAsync(Guid? userId, string password, CancellationToken cancellationToken = default)
    {
        if (userId is null)
        {
            // Compare against a stable precomputed hash so unknown-user logins
            // stay timing-comparable without hashing twice per request (DoS risk).
            _userManager.PasswordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash.Value, password);
            return Task.FromResult(false);
        }

        return CheckPasswordAsync(userId.Value, password, cancellationToken);
    }

    public async Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is not null && user.LockoutEnabled && await _userManager.IsLockedOutAsync(user);
    }

    public async Task RegisterFailedLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        await _userManager.AccessFailedAsync(user);
    }

    public async Task ResetAccessFailedCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        await _userManager.ResetAccessFailedCountAsync(user);
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        return result.Succeeded
            ? Result.Success()
            : Error.Validation(MessageCodes.Auth.PasswordTooWeak, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, string resetToken, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        var result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);
        return result.Succeeded
            ? Result.Success()
            : Error.Validation(MessageCodes.Auth.PasswordTooWeak, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User not found.");
        return await _userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task UpdateAccountStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User not found.");
        user.AccountStatus = status;
        await _userManager.UpdateAsync(user);
    }

    public async Task SetEmailConfirmedAsync(Guid userId, bool confirmed, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User not found.");
        user.EmailConfirmed = confirmed;
        await _userManager.UpdateAsync(user);
    }

    public async Task SetPhoneConfirmedAsync(Guid userId, bool confirmed, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User not found.");
        user.PhoneNumberConfirmed = confirmed;
        if (confirmed && user.AccountStatus == AccountStatus.PhonePending)
        {
            user.AccountStatus = AccountStatus.Active;
        }

        await _userManager.UpdateAsync(user);
    }

    public async Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        user.LastLoginAtUtc = _clock.UtcNow;
        await _userManager.UpdateAsync(user);
    }

    public async Task SetRestaurantIdAsync(Guid userId, Guid? restaurantId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User not found.");
        user.RestaurantId = restaurantId;
        await _userManager.UpdateAsync(user);
    }

    public async Task SetUserTypeAsync(Guid userId, UserType userType, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User not found.");
        user.UserType = userType;
        await _userManager.UpdateAsync(user);
    }

    public async Task UpdateFullNameAsync(Guid userId, string fullName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User not found.");
        user.FullName = fullName;
        await _userManager.UpdateAsync(user);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Array.Empty<string>();
        }

        var roles = await _userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var roles = await GetRolesAsync(userId, cancellationToken);
        if (roles.Count == 0)
        {
            return Array.Empty<string>();
        }

        var roleIds = await _roleManager.Roles
            .Where(r => roles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var permissions = await (
            from rp in _db.RolePermissions
            join p in _db.Permissions on rp.PermissionId equals p.Id
            where roleIds.Contains(rp.RoleId) && p.IsActive
            select p.Code).Distinct().ToListAsync(cancellationToken);

        return permissions;
    }

    public async Task<Result> AddToRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            return Error.NotFound(MessageCodes.Auth.RoleNotFound, "Role not found.");
        }

        var result = await _userManager.AddToRoleAsync(user, roleName);
        return result.Succeeded
            ? Result.Success()
            : Error.Failure(MessageCodes.Domain.Generic, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Result> RemoveFromRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        return result.Succeeded
            ? Result.Success()
            : Error.Failure(MessageCodes.Domain.Generic, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Result> EnsureRoleExistsAsync(string roleName, string? description = null, CancellationToken cancellationToken = default)
    {
        if (await _roleManager.RoleExistsAsync(roleName))
        {
            return Result.Success();
        }

        var role = new ApplicationRole(roleName) { Id = Guid.NewGuid(), Description = description };
        var result = await _roleManager.CreateAsync(role);
        return result.Succeeded
            ? Result.Success()
            : Error.Failure(MessageCodes.Domain.Generic, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        return await _roleManager.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description))
            .ToListAsync(cancellationToken);
    }

    public async Task<RoleDto?> FindRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        return role is null ? null : new RoleDto(role.Id, role.Name!, role.Description);
    }

    public async Task<RoleDto?> FindRoleByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByNameAsync(roleName);
        return role is null ? null : new RoleDto(role.Id, role.Name!, role.Description);
    }

    public async Task<Result<RoleDto>> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        if (await _roleManager.RoleExistsAsync(name))
        {
            return Error.Conflict(MessageCodes.Domain.Generic, "Role already exists.");
        }

        var role = new ApplicationRole(name) { Id = Guid.NewGuid(), Description = description };
        var result = await _roleManager.CreateAsync(role);
        return result.Succeeded
            ? new RoleDto(role.Id, role.Name!, role.Description)
            : Error.Failure(MessageCodes.Domain.Generic, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Result> UpdateRoleAsync(Guid roleId, string? description, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return Error.NotFound(MessageCodes.Auth.RoleNotFound, "Role not found.");
        }

        role.Description = description;
        var result = await _roleManager.UpdateAsync(role);
        return result.Succeeded
            ? Result.Success()
            : Error.Failure(MessageCodes.Domain.Generic, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<IReadOnlyList<UserAccountDto>> ListUsersByTypeAsync(UserType userType, CancellationToken cancellationToken = default)
    {
        var users = await _db.Users.Where(u => u.UserType == userType).ToListAsync(cancellationToken);
        return users.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<UserAccountDto>> ListAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users.OrderByDescending(u => u.CreatedAtUtc).ToListAsync(cancellationToken);
        return users.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<UserAccountDto>> SearchUsersAsync(
        string? search = null,
        UserType? userType = null,
        AccountStatus? status = null,
        string? role = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                (u.FullName != null && EF.Functions.ILike(u.FullName, $"%{term}%")) ||
                (u.Email != null && EF.Functions.ILike(u.Email, $"%{term}%")) ||
                (u.PhoneNumber != null && EF.Functions.ILike(u.PhoneNumber, $"%{term}%")) ||
                (u.UserName != null && EF.Functions.ILike(u.UserName, $"%{term}%")));
        }

        if (userType.HasValue)
        {
            query = query.Where(u => u.UserType == userType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(u => u.AccountStatus == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleNameUpper = role.Trim().ToUpper();
            var userIdsWithRole = _db.UserRoles
                .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.NormalizedName })
                .Where(x => x.NormalizedName == roleNameUpper)
                .Select(x => x.UserId);

            query = query.Where(u => userIdsWithRole.Contains(u.Id));
        }

        var users = await query.OrderByDescending(u => u.CreatedAtUtc).ToListAsync(cancellationToken);
        return users.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<UserAccountDto>> ListUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var users = await _userManager.GetUsersInRoleAsync(roleName);
        return users.Select(Map).ToList();
    }

    private static UserAccountDto Map(ApplicationUser user) => new(
        user.Id,
        user.Email,
        user.PhoneNumber,
        user.FullName,
        user.UserType,
        user.AccountStatus,
        user.RestaurantId,
        user.EmailConfirmed,
        user.PhoneNumberConfirmed,
        user.CreatedAtUtc,
        user.LastLoginAtUtc);

    private static Error MapIdentityCreateError(IdentityResult result)
    {
        var errors = result.Errors.ToArray();
        var description = string.Join("; ", errors.Select(e => e.Description));

        if (errors.Any(e => e.Code.StartsWith("Password", StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Validation(MessageCodes.Auth.PasswordTooWeak, description);
        }

        if (errors.Any(e => e.Code.Equals("DuplicateEmail", StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateEmail, description);
        }

        if (errors.Any(e => e.Code.Equals("DuplicateUserName", StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicatePhone, description);
        }

        return Error.Validation(MessageCodes.Auth.InvalidState, description);
    }
}

public sealed class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationIdAccessor _correlationId;
    private readonly IDateTimeProvider _clock;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public AuditLogger(
        AppDbContext db,
        ICurrentUser currentUser,
        ICorrelationIdAccessor correlationId,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _correlationId = correlationId;
        _clock = clock;
    }

    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = _currentUser.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            BeforeJson = entry.Before is null ? null : JsonSerializer.Serialize(entry.Before, JsonOptions),
            AfterJson = entry.After is null ? null : JsonSerializer.Serialize(entry.After, JsonOptions),
            Reason = entry.Reason,
            CorrelationId = _correlationId.CorrelationId,
            SourceChannel = entry.SourceChannel,
            OccurredAtUtc = _clock.UtcNow,
        });

        return Task.CompletedTask;
    }
}
