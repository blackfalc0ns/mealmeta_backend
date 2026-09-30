using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record RestaurantBranchDto(
    Guid Id,
    Guid RestaurantRegistrationId,
    string BranchCode,
    string NameAr,
    string NameEn,
    string AddressAr,
    string AddressEn,
    string GovernorateAr,
    string GovernorateEn,
    string ContactPhone,
    bool IsActive,
    bool IsPrimary,
    Guid? ManagerStaffId,
    int StaffCount,
    DateTime CreatedAtUtc,
    string? PhotoStorageKey = null);

public sealed record ListRestaurantBranchesQuery(Guid RestaurantRegistrationId) : IQuery<IReadOnlyList<RestaurantBranchDto>>;

public sealed class ListRestaurantBranchesQueryHandler : IQueryHandler<ListRestaurantBranchesQuery, IReadOnlyList<RestaurantBranchDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListRestaurantBranchesQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<RestaurantBranchDto>>> Handle(
        ListRestaurantBranchesQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(
                _currentUser,
                request.RestaurantRegistrationId == Guid.Empty ? null : request.RestaurantRegistrationId);

            if (scopeResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<RestaurantBranchDto>>(scopeResult.Error);
            }

            var targetRestaurantId = scopeResult.Value;
            var registrationIds = await ResolveRegistrationIds(targetRestaurantId, cancellationToken);
            if (registrationIds == null || registrationIds.Count == 0)
            {
                return Result.Success<IReadOnlyList<RestaurantBranchDto>>(Array.Empty<RestaurantBranchDto>());
            }

            var dbBranches = await _db.RestaurantBranches
                .AsNoTracking()
                .Where(b => registrationIds.Contains(b.RestaurantRegistrationId))
                .OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.NameAr)
                .ToListAsync(cancellationToken);

            var staffAssignments = await _db.BranchStaffAssignments
                .AsNoTracking()
                .Where(s => s.IsActive)
                .ToListAsync(cancellationToken);

            var pendingInvites = await _db.StaffInvitations
                .AsNoTracking()
                .Where(i => i.Status == "Invited")
                .Where(i => registrationIds.Contains(i.RestaurantRegistrationId))
                .ToListAsync(cancellationToken);

            var dtos = dbBranches.Select(b => new RestaurantBranchDto(
                b.Id,
                b.RestaurantRegistrationId,
                b.BranchCode,
                b.NameAr,
                b.NameEn,
                b.AddressAr,
                b.AddressEn,
                b.GovernorateAr,
                b.GovernorateEn,
                b.ContactPhone,
                b.IsActive,
                b.IsPrimary,
                b.ManagerStaffId,
                CountLinkedStaff(b.Id, staffAssignments, pendingInvites),
                b.CreatedAtUtc,
                b.PhotoStorageKey)).ToList();

            // Always expose the registration's primary branch unless a persisted primary branch exists.
            if (dtos.All(b => !b.IsPrimary))
            {
                var reg = await _db.RestaurantRegistrations
                    .AsNoTracking()
                    .Where(r => registrationIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.TradeNameAr, r.TradeNameEn, r.ContactPhone, r.Address, r.CreatedAtUtc })
                    .FirstOrDefaultAsync(cancellationToken);

                if (reg != null)
                {
                    dtos.Insert(0, new RestaurantBranchDto(
                        Id: reg.Id,
                        RestaurantRegistrationId: reg.Id,
                        BranchCode: "MAIN-01",
                        NameAr: $"الفرع الرئيسي — {reg.TradeNameAr}",
                        NameEn: $"Main branch — {reg.TradeNameEn ?? reg.TradeNameAr}",
                        AddressAr: reg.Address ?? "-",
                        AddressEn: reg.Address ?? "-",
                        GovernorateAr: "-",
                        GovernorateEn: "-",
                        ContactPhone: reg.ContactPhone,
                        IsActive: true,
                        IsPrimary: true,
                        ManagerStaffId: null,
                        StaffCount: CountLinkedStaff(reg.Id, staffAssignments, pendingInvites),
                        CreatedAtUtc: reg.CreatedAtUtc,
                        PhotoStorageKey: null));
                }
            }

            return Result.Success<IReadOnlyList<RestaurantBranchDto>>(dtos);
        }
        catch (Exception ex)
        {
            return Error.Failure("branches.list_failed", ex.Message);
        }
    }

    private async Task<IReadOnlyList<Guid>?> ResolveRegistrationIds(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var ids = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(r => r.Id == id || r.ResultingRestaurantId == id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        return ids.Count > 0 ? ids : [id];
    }

    private static int CountLinkedStaff(
        Guid branchId,
        IReadOnlyCollection<BranchStaffAssignment> assignments,
        IReadOnlyCollection<StaffInvitation> pendingInvites)
    {
        return assignments.Count(s => s.RestaurantBranchId == branchId)
            + pendingInvites.Count(i => IsLinkedToBranch(i, branchId));
    }

    private static bool IsLinkedToBranch(StaffInvitation invitation, Guid branchId)
    {
        if (invitation.AllBranches)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(invitation.BranchIdsJson))
        {
            return false;
        }

        return invitation.BranchIdsJson
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => Guid.TryParse(value, out var id) && id == branchId);
    }
}

public sealed record CreateRestaurantBranchCommand(
    Guid RestaurantRegistrationId,
    string BranchCode,
    string NameAr,
    string NameEn,
    string AddressAr,
    string AddressEn,
    string GovernorateAr,
    string GovernorateEn,
    string ContactPhone,
    bool IsPrimary = false,
    Guid? ManagerStaffId = null,
    string? PhotoStorageKey = null) : ICommand<RestaurantBranchDto>;

public sealed class CreateRestaurantBranchCommandHandler : ICommandHandler<CreateRestaurantBranchCommand, RestaurantBranchDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CreateRestaurantBranchCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantBranchDto>> Handle(
        CreateRestaurantBranchCommand request,
        CancellationToken cancellationToken)
    {
        if (!BranchSecurityHelper.IsAuthorizedToManageTeam(_currentUser))
        {
            return Error.Forbidden("restaurant.forbidden", "Only restaurant owners, admins, or authorized staff may manage branches.");
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(
            _currentUser,
            request.RestaurantRegistrationId == Guid.Empty ? null : request.RestaurantRegistrationId);

        if (scopeResult.IsFailure)
        {
            return Result.Failure<RestaurantBranchDto>(scopeResult.Error);
        }

        var targetRestaurantId = scopeResult.Value;

        if (!_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
        {
            var isApproved = await _db.RestaurantProfiles.AnyAsync(p => p.Id == targetRestaurantId, cancellationToken)
                || await _db.RestaurantRegistrations.AnyAsync(r => (r.Id == targetRestaurantId || r.ResultingRestaurantId == targetRestaurantId) && r.Status == VerificationStatus.Approved, cancellationToken);

            if (isApproved)
            {
                return Error.Conflict("branch.review_required", "Direct branch creation is not allowed for approved restaurants. Please submit a branch change proposal.");
            }
        }

        var branch = new RestaurantBranch
        {
            RestaurantRegistrationId = targetRestaurantId,
            BranchCode = string.IsNullOrWhiteSpace(request.BranchCode) ? "BR-" + Random.Shared.Next(100, 999) : request.BranchCode,
            NameAr = request.NameAr,
            NameEn = string.IsNullOrWhiteSpace(request.NameEn) ? request.NameAr : request.NameEn,
            AddressAr = request.AddressAr,
            AddressEn = string.IsNullOrWhiteSpace(request.AddressEn) ? request.AddressAr : request.AddressEn,
            GovernorateAr = request.GovernorateAr,
            GovernorateEn = string.IsNullOrWhiteSpace(request.GovernorateEn) ? request.GovernorateAr : request.GovernorateEn,
            ContactPhone = request.ContactPhone,
            IsActive = true,
            IsPrimary = request.IsPrimary,
            ManagerStaffId = request.ManagerStaffId,
            PhotoStorageKey = string.IsNullOrWhiteSpace(request.PhotoStorageKey) ? null : request.PhotoStorageKey.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.RestaurantBranches.Add(branch);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new RestaurantBranchDto(
            branch.Id,
            branch.RestaurantRegistrationId,
            branch.BranchCode,
            branch.NameAr,
            branch.NameEn,
            branch.AddressAr,
            branch.AddressEn,
            branch.GovernorateAr,
            branch.GovernorateEn,
            branch.ContactPhone,
            branch.IsActive,
            branch.IsPrimary,
            branch.ManagerStaffId,
            0,
            branch.CreatedAtUtc,
            branch.PhotoStorageKey);

        return Result.Success(dto);
    }
}

public sealed record ToggleBranchStatusCommand(Guid BranchId) : ICommand<bool>;

public sealed class ToggleBranchStatusCommandHandler : ICommandHandler<ToggleBranchStatusCommand, bool>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ToggleBranchStatusCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<bool>> Handle(
        ToggleBranchStatusCommand request,
        CancellationToken cancellationToken)
    {
        if (!BranchSecurityHelper.IsAuthorizedToManageTeam(_currentUser))
        {
            return Error.Forbidden("restaurant.forbidden", "Only restaurant owners, admins, or authorized staff may toggle branch status.");
        }

        var branch = await _db.RestaurantBranches.FirstOrDefaultAsync(b => b.Id == request.BranchId, cancellationToken);
        if (branch is null)
        {
            return Error.NotFound("Branch.NotFound", "Branch not found.");
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(_currentUser, branch.RestaurantRegistrationId);
        if (scopeResult.IsFailure)
        {
            return Result.Failure<bool>(scopeResult.Error);
        }

        if (!_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
        {
            var targetRestaurantId = branch.RestaurantRegistrationId;
            var isApproved = await _db.RestaurantProfiles.AnyAsync(p => p.Id == targetRestaurantId, cancellationToken)
                || await _db.RestaurantRegistrations.AnyAsync(r => (r.Id == targetRestaurantId || r.ResultingRestaurantId == targetRestaurantId) && r.Status == VerificationStatus.Approved, cancellationToken);

            if (isApproved)
            {
                return Error.Conflict("branch.review_required", "Direct branch toggling is not allowed for approved restaurants. Please submit a branch change proposal.");
            }
        }

        branch.IsActive = !branch.IsActive;
        branch.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(branch.IsActive);
    }
}

internal static class BranchSecurityHelper
{
    internal static bool IsAuthorizedToManageTeam(ICurrentUser user)
    {
        if (!user.IsAuthenticated) return false;
        if (user.UserType == UserType.Admin) return true;
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.OperationsAdmin)) return true;
        if (user.HasPermission(PermissionCodes.RestaurantRegApprove) || user.HasPermission(PermissionCodes.AdminUsersManage)) return true;
        if (user.IsInRole(AppRoles.RestaurantOwner) || user.IsInRole("owner") || user.IsInRole("Owner") ||
            user.Roles.Any(r => string.Equals(r, "owner", StringComparison.OrdinalIgnoreCase) || string.Equals(r, AppRoles.RestaurantOwner, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        if (user.HasPermission("team.manage") || user.HasPermission("team:manage")) return true;
        return false;
    }
}
