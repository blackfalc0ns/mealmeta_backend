using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Common.Abstractions;

public sealed record UserAccountDto(
    Guid Id,
    string? Email,
    string? PhoneNumber,
    string? FullName,
    UserType UserType,
    AccountStatus AccountStatus,
    Guid? RestaurantId,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record UserIdentityDto(
    Guid Id,
    string? FullName,
    string? Email);

public sealed record CreateUserRequest(
    string? Email,
    string? PhoneNumber,
    string Password,
    string? FullName,
    UserType UserType,
    AccountStatus AccountStatus,
    Guid? RestaurantId = null);

public interface IUserAccountService
{
    Task<UserAccountDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserAccountDto?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<UserAccountDto?> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserIdentityDto>> GetUserIdentitiesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    Task<Result<UserAccountDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task<bool> VerifyPasswordOrDummyAsync(Guid? userId, string password, CancellationToken cancellationToken = default);

    Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken = default);

    Task RegisterFailedLoginAsync(Guid userId, CancellationToken cancellationToken = default);

    Task ResetAccessFailedCountAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(Guid userId, string resetToken, string newPassword, CancellationToken cancellationToken = default);

    Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    Task UpdateAccountStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken = default);

    Task SetEmailConfirmedAsync(Guid userId, bool confirmed, CancellationToken cancellationToken = default);

    Task SetPhoneConfirmedAsync(Guid userId, bool confirmed, CancellationToken cancellationToken = default);

    Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetRestaurantIdAsync(Guid userId, Guid? restaurantId, CancellationToken cancellationToken = default);

    Task SetUserTypeAsync(Guid userId, UserType userType, CancellationToken cancellationToken = default);

    Task UpdateFullNameAsync(Guid userId, string fullName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> AddToRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default);

    Task<Result> RemoveFromRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default);

    Task<Result> EnsureRoleExistsAsync(string roleName, string? description = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default);

    Task<RoleDto?> FindRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<RoleDto?> FindRoleByNameAsync(string roleName, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default);

    Task<Result> UpdateRoleAsync(Guid roleId, string? description, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountDto>> ListUsersByTypeAsync(UserType userType, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountDto>> ListAllUsersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountDto>> SearchUsersAsync(
        string? search = null,
        UserType? userType = null,
        AccountStatus? status = null,
        string? role = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountDto>> ListUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default);
}

public sealed record RoleDto(Guid Id, string Name, string? Description);
