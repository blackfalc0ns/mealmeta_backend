using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;

public sealed record RestaurantDeliveryManagerDetailDto(
    Guid Id,
    Guid? RegistrationId,
    Guid? UserId,
    Guid RestaurantId,
    string FullName,
    string Phone,
    string Email,
    string? NationalId,
    DateTime? NationalIdExpiryUtc,
    DateTime? ContractExpiryUtc,
    DeliveryManagerCoverageType CoverageType,
    string? CoverageScopeId,
    string? CoverageScopeName,
    VerificationStatus VerificationStatus,
    AccountStatus AccountStatus,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    string? RejectionReason,
    string? ChangeRequestNotes,
    IReadOnlyList<DocumentDto> Documents);

public sealed record GetRestaurantDeliveryManagerDetailQuery(Guid Id) : IQuery<RestaurantDeliveryManagerDetailDto>;

public sealed class GetRestaurantDeliveryManagerDetailQueryHandler
    : IQueryHandler<GetRestaurantDeliveryManagerDetailQuery, RestaurantDeliveryManagerDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantDeliveryManagerDetailQueryHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantDeliveryManagerDetailDto>> Handle(
        GetRestaurantDeliveryManagerDetailQuery request,
        CancellationToken cancellationToken)
    {
        var targetRestaurantId = _currentUser.RestaurantId;

        // 1. Try finding in DeliveryManagerRegistrations first (matches either Registration.Id or ResultingUserId)
        var regQuery = _db.DeliveryManagerRegistrations.AsNoTracking()
            .Where(x => x.Id == request.Id || x.ResultingUserId == request.Id);

        if (targetRestaurantId.HasValue && !_currentUser.IsInRole(AppRoles.SuperAdmin) && !_currentUser.IsInRole(AppRoles.OperationsAdmin))
        {
            regQuery = regQuery.Where(x => x.RestaurantId == targetRestaurantId.Value);
        }

        var reg = await regQuery.FirstOrDefaultAsync(cancellationToken);

        if (reg != null)
        {
            var docs = await _db.Documents
                .Include(d => d.Verifications)
                .AsNoTracking()
                .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == reg.Id)
                .OrderBy(d => d.DocumentType)
                .ToListAsync(cancellationToken);

            var docDtos = docs.Select(document =>
            {
                var latest = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                return new DocumentDto(
                    document.Id,
                    document.OwnerType,
                    document.OwnerId,
                    document.DocumentType,
                    document.FileName,
                    document.ContentType,
                    document.SizeBytes,
                    latest?.Status,
                    document.ExpiresAtUtc,
                    latest?.Reason);
            }).ToList();

            var accountStatus = AccountStatus.Registered;
            DateTime? lastLogin = null;
            if (reg.ResultingUserId.HasValue)
            {
                var user = await _users.FindByIdAsync(reg.ResultingUserId.Value, cancellationToken);
                if (user != null)
                {
                    accountStatus = user.AccountStatus;
                    lastLogin = user.LastLoginAtUtc;
                }
            }

            return new RestaurantDeliveryManagerDetailDto(
                reg.Id,
                reg.Id,
                reg.ResultingUserId,
                reg.RestaurantId,
                reg.FullName,
                reg.Phone,
                reg.Email,
                reg.NationalId,
                reg.NationalIdExpiryUtc,
                reg.ContractExpiryUtc,
                reg.CoverageType,
                reg.CoverageScopeId,
                reg.CoverageScopeName,
                reg.Status,
                accountStatus,
                reg.CreatedAtUtc,
                lastLogin,
                reg.RejectionReason,
                reg.ChangeRequestNotes,
                docDtos);
        }

        // 2. Fallback: Check _users directly by request.Id (e.g. legacy/direct delivery manager)
        var directUser = await _users.FindByIdAsync(request.Id, cancellationToken);
        if (directUser != null && directUser.UserType == UserType.DeliveryManager)
        {
            if (targetRestaurantId.HasValue && directUser.RestaurantId != targetRestaurantId.Value &&
                !_currentUser.IsInRole(AppRoles.SuperAdmin) && !_currentUser.IsInRole(AppRoles.OperationsAdmin))
            {
                return Error.NotFound("DeliveryManager.NotFound", "Delivery manager not found.");
            }

            return new RestaurantDeliveryManagerDetailDto(
                directUser.Id,
                null,
                directUser.Id,
                directUser.RestaurantId ?? targetRestaurantId ?? Guid.Empty,
                directUser.FullName ?? "—",
                directUser.PhoneNumber ?? "—",
                directUser.Email ?? "—",
                null,
                null,
                null,
                DeliveryManagerCoverageType.AllRestaurantCoverage,
                null,
                null,
                VerificationStatus.Approved,
                directUser.AccountStatus,
                directUser.CreatedAtUtc,
                directUser.LastLoginAtUtc,
                null,
                null,
                Array.Empty<DocumentDto>());
        }

        return Error.NotFound("DeliveryManager.NotFound", "Delivery manager not found.");
    }
}
