using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    public async Task<Result<IReadOnlyList<SupportCaseTaskRecipientDto>>> GetTaskRecipientsAsync(Guid caseId, CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        if (!_access.CanManage) return Forbidden();
        var supportCase = await _db.SupportCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        if (supportCase.RestaurantId is not { } restaurantId) return Result.Success<IReadOnlyList<SupportCaseTaskRecipientDto>>([]);
        var managerIds = await _db.GetDeliveryManagerUserIdsAsync(restaurantId, ct);
        var recipients = new List<SupportCaseTaskRecipientDto>(managerIds.Count);
        foreach (var userId in managerIds.Order())
        {
            var name = await _db.GetUserDisplayNameAsync(userId, ct);
            recipients.Add(new SupportCaseTaskRecipientDto(userId, string.IsNullOrWhiteSpace(name) ? null : name));
        }
        return Result.Success<IReadOnlyList<SupportCaseTaskRecipientDto>>(recipients);
    }

    public async Task<Result<SupportCasePageDto>> ListAsync(SupportCaseListQuery query, CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        if (_currentUser.UserType == MealMate.Domain.Enums.UserType.Admin && !_access.IsSupportAdmin && !_access.IsSuperAdmin) return Forbidden();
        if (_currentUser.UserType == MealMate.Domain.Enums.UserType.Driver && !await _access.HasActiveDriverIdentityAsync(ct)) return Forbidden();
        var search = query.Search?.Trim();
        // Searching full case content is restricted to platform operators. Task-only recipients
        // must not infer fields that ProjectAsync deliberately conceals from them.
        if (!string.IsNullOrEmpty(search) && !_access.IsSupportAdmin && !_access.IsSuperAdmin) return Forbidden();
        if (query.PageNumber < 1 || query.PageSize is < 1 or > 100
            || search?.Length > 200
            || (long)query.PageNumber * query.PageSize > int.MaxValue
            || (query.Status.HasValue && !Enum.IsDefined(query.Status.Value))
            || (query.Kind.HasValue && !Enum.IsDefined(query.Kind.Value))) return Invalid();
        var cases = await _access.VisibleCasesAsync(ct);
        if (!string.IsNullOrEmpty(search))
        {
            var normalized = search.ToLowerInvariant();
            var hasId = Guid.TryParse(search, out var caseId);
            cases = cases.Where(c => (hasId && c.Id == caseId)
                || c.Subject.ToLower().Contains(normalized)
                || c.Body.ToLower().Contains(normalized)
                || c.Category.ToLower().Contains(normalized));
        }
        if (query.Status.HasValue) cases = cases.Where(c => c.Status == query.Status.Value);
        if (query.Kind.HasValue) cases = cases.Where(c => c.Kind == query.Kind.Value);
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            // A private category must not become discoverable through a task-only list filter.
            if (!_access.IsSupportAdmin && !_access.IsSuperAdmin)
                cases = cases.Where(c => c.RequesterUserId == ActorId
                    || (_currentUser.UserType == MealMate.Domain.Enums.UserType.Restaurant
                        && c.Channel == SupportCaseChannel.Restaurant && c.RestaurantId == _currentUser.RestaurantId));
            cases = cases.Where(c => c.Category == query.Category.Trim());
        }
        var total = await cases.CountAsync(ct);
        var selected = await cases.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(c => new { Case = c, Version = EF.Property<uint>(c, "xmin") }).ToListAsync(ct);
        var items = new List<SupportCaseDto>(selected.Count);
        foreach (var row in selected) items.Add(await ProjectAsync(row.Case, ct, row.Version));
        return Result.Success(new SupportCasePageDto(items, query.PageNumber, query.PageSize, total));
    }

    public async Task<Result<SupportCaseDto>> GetAsync(Guid caseId, CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        var row = await _db.SupportCases.Where(x => x.Id == caseId)
            .Select(x => new { Case = x, Version = EF.Property<uint>(x, "xmin") }).SingleOrDefaultAsync(ct);
        if (row is null || !await _access.CanReadAsync(row.Case, ct)) return NotFound();
        return Result.Success(await ProjectAsync(row.Case, ct, row.Version));
    }
}
