using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed class LedgerAccess(IAppDbContext db, ICurrentUser currentUser)
{
    public Error? Require(string permission)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            return Error.Unauthorized("ledger.authentication_required");
        if (currentUser.UserType != UserType.Admin)
            return Error.Forbidden("ledger.admin_only");
        if (!IsSuperAdmin && !currentUser.HasPermission(permission))
            return Error.Forbidden("ledger.permission_required");
        return null;
    }

    public bool IsSuperAdmin => currentUser.IsInRole(AppRoles.SuperAdmin);

    public Task<bool> CanAccess(Guid legalEntityId, CancellationToken ct = default) =>
        Task.FromResult(IsSuperAdmin || currentUser.TenantIds.Contains(legalEntityId));

    public async Task<Error?> ValidateEntityAsync(Guid legalEntityId, bool requireActiveKwd, CancellationToken ct = default)
    {
        if (legalEntityId == Guid.Empty) return Error.Validation("ledger.entity_required");
        if (!await CanAccess(legalEntityId, ct)) return Error.Forbidden("ledger.entity_access_denied");
        var entity = await db.LegalEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == legalEntityId, ct);
        if (entity is null) return Error.NotFound("ledger.entity_not_found");
        if (requireActiveKwd && (entity.Status != LegalEntityStatus.Active || entity.Currency != "KWD"))
            return Error.Validation("ledger.entity_inactive_or_currency_unsupported");
        return null;
    }

    public async Task<Error?> ValidateExistingScopeAsync(Guid legalEntityId, CancellationToken ct = default)
    {
        if (!await CanAccess(legalEntityId, ct)) return Error.Forbidden("ledger.entity_access_denied");
        return await db.LegalEntities.AsNoTracking().AnyAsync(x => x.Id == legalEntityId, ct)
            ? null
            : Error.NotFound("ledger.entity_not_found");
    }

    public async Task<Error?> ValidateActiveKwdEntityAsync(Guid legalEntityId, CancellationToken ct = default)
    {
        var entity = await db.LegalEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == legalEntityId, ct);
        if (entity is null) return Error.NotFound("ledger.entity_not_found");
        if (entity.Status != LegalEntityStatus.Active || entity.Currency != "KWD")
            return Error.Validation("ledger.entity_inactive_or_currency_unsupported");
        return null;
    }
}
