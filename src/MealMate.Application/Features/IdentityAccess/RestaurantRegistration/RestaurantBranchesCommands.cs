using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
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

    public ListRestaurantBranchesQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<RestaurantBranchDto>>> Handle(
        ListRestaurantBranchesQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var registrationIds = await ResolveRegistrationIds(request.RestaurantRegistrationId, cancellationToken);
            var dbBranches = await _db.RestaurantBranches
                .AsNoTracking()
                .Where(b => registrationIds == null || registrationIds.Contains(b.RestaurantRegistrationId))
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
                .Where(i => registrationIds == null || registrationIds.Contains(i.RestaurantRegistrationId))
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
                    .Where(r => registrationIds == null || registrationIds.Contains(r.Id))
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
        catch
        {
            return Result.Success<IReadOnlyList<RestaurantBranchDto>>(Array.Empty<RestaurantBranchDto>());
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

    public CreateRestaurantBranchCommandHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<RestaurantBranchDto>> Handle(
        CreateRestaurantBranchCommand request,
        CancellationToken cancellationToken)
    {
        var branch = new RestaurantBranch
        {
            RestaurantRegistrationId = request.RestaurantRegistrationId,
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

    public ToggleBranchStatusCommandHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<bool>> Handle(
        ToggleBranchStatusCommand request,
        CancellationToken cancellationToken)
    {
        var branch = await _db.RestaurantBranches.FirstOrDefaultAsync(b => b.Id == request.BranchId, cancellationToken);
        if (branch is null)
        {
            return Error.NotFound("Branch.NotFound", "Branch not found.");
        }

        branch.IsActive = !branch.IsActive;
        branch.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(branch.IsActive);
    }
}
