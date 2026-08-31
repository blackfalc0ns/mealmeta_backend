using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.MenuCycles;

public sealed partial class MenuCycleHandlers
{
    public async Task<Result<MenuCycleDetailsDto>> Handle(AddMenuCycleSequenceDayCommand request, CancellationToken ct)
    {
        var nameAr = request.NameAr?.Trim() ?? string.Empty;
        var nameEn = request.NameEn?.Trim() ?? string.Empty;
        if (nameAr.Length < 1 || nameEn.Length < 1)
            return Error.Validation("menu_cycle.sequence_name_required", "Arabic and English names are required.");

        var plan = await LoadOwnedCycleForSequence(request.PlanId, request.RestaurantId, ct);
        var version = plan?.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (plan is null || version is null) return NotFound(request.PlanId);
        if (plan.Mode != ScheduleMode.Cycle)
            return Error.Conflict("menu_plan.mode_mismatch", "Sequence order is only available for Cycle plans.");

        var activeDays = version.Days
            .Where(x => !x.IsDeleted && x.IsActiveInCycle)
            .OrderBy(x => x.DayNumber)
            .ToList();
        var pending = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
        if (pending.Count == 0)
            pending = MenuCyclePendingSequence.SeedFromActive(activeDays.Select(x => x.Id).ToList());

        if (request.TargetDayNumber < 1 || request.TargetDayNumber > pending.Count + 1)
            return Error.Validation("menu_cycle.sequence_target_invalid", "Target day number is out of range.");

        var provisionalNumber = version.Days.Where(x => !x.IsDeleted).Select(x => x.DayNumber).DefaultIfEmpty(0).Max() + 1;
        var day = new MenuDay
        {
            MenuPlanVersionId = version.Id,
            DayNumber = provisionalNumber,
            NameAr = nameAr,
            NameEn = nameEn,
            IsActiveInCycle = false,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.MenuDays.Add(day);

        pending = MenuCyclePendingSequence.Insert(pending, day.Id, request.TargetDayNumber);
        version.PendingSequenceJson = MenuCyclePendingSequence.Serialize(pending);
        version.ModifiedAtUtc = DateTime.UtcNow;
        AddAudit(version, "sequence_day_added", $"{day.Id}:{request.TargetDayNumber}");
        await _db.SaveChangesAsync(ct);
        return BuildSequenceMutationDetails(plan, version);
    }

    public async Task<Result<MenuCycleDetailsDto>> Handle(SwapMenuCycleSequenceDayCommand request, CancellationToken ct)
    {
        var plan = await LoadOwnedCycleForSequence(request.PlanId, request.RestaurantId, ct);
        var version = plan?.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (plan is null || version is null) return NotFound(request.PlanId);
        if (plan.Mode != ScheduleMode.Cycle)
            return Error.Conflict("menu_plan.mode_mismatch", "Sequence order is only available for Cycle plans.");

        var day = version.Days.FirstOrDefault(x => x.Id == request.DayId && !x.IsDeleted);
        if (day is null)
            return Error.NotFound("menu_cycle.day_not_found", "Menu day was not found on this plan.");

        var activeDays = version.Days
            .Where(x => !x.IsDeleted && x.IsActiveInCycle)
            .OrderBy(x => x.DayNumber)
            .ToList();
        var pending = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
        if (pending.Count == 0)
            pending = MenuCyclePendingSequence.SeedFromActive(activeDays.Select(x => x.Id).ToList());

        // Allow swapping a newly added inactive day that is already in pending.
        if (!pending.Contains(request.DayId))
            return Error.Validation("menu_cycle.sequence_day_not_pending", "Menu day is not part of the sequence.");

        if (request.TargetDayNumber < 1 || request.TargetDayNumber > pending.Count)
            return Error.Validation("menu_cycle.sequence_target_invalid", "Target day number is out of range.");

        try
        {
            pending = MenuCyclePendingSequence.Swap(pending, request.DayId, request.TargetDayNumber);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error.Validation("menu_cycle.sequence_target_invalid", "Target day number is out of range.");
        }
        catch (InvalidOperationException)
        {
            return Error.Validation("menu_cycle.sequence_day_not_pending", "Menu day is not part of the sequence.");
        }

        version.PendingSequenceJson = MenuCyclePendingSequence.Serialize(pending);
        version.ModifiedAtUtc = DateTime.UtcNow;
        AddAudit(version, "sequence_day_swapped", $"{request.DayId}:{request.TargetDayNumber}");
        await _db.SaveChangesAsync(ct);
        return BuildSequenceMutationDetails(plan, version);
    }

    public async Task<Result<MenuCycleDetailsDto>> Handle(CancelMenuCyclePendingSequenceCommand request, CancellationToken ct)
    {
        var plan = await LoadOwnedCycleForSequence(request.PlanId, request.RestaurantId, ct);
        var version = plan?.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (plan is null || version is null) return NotFound(request.PlanId);
        if (plan.Mode != ScheduleMode.Cycle)
            return Error.Conflict("menu_plan.mode_mismatch", "Sequence order is only available for Cycle plans.");

        version.PendingSequenceJson = null;
        version.ModifiedAtUtc = DateTime.UtcNow;

        var inactiveDays = version.Days.Where(x => !x.IsDeleted && !x.IsActiveInCycle).ToList();
        if (inactiveDays.Count > 0)
        {
            var inactiveIds = inactiveDays.Select(x => x.Id).ToList();
            var withMeals = await _db.MenuDayMeals.AsNoTracking()
                .Where(x => inactiveIds.Contains(x.MenuDayId) && !x.IsDeleted)
                .Select(x => x.MenuDayId)
                .Distinct()
                .ToListAsync(ct);
            var withMealsSet = withMeals.ToHashSet();
            foreach (var inactive in inactiveDays)
            {
                if (withMealsSet.Contains(inactive.Id)) continue;
                inactive.IsDeleted = true;
                inactive.DeletedAtUtc = DateTime.UtcNow;
            }
        }

        AddAudit(version, "sequence_pending_cancelled");
        await _db.SaveChangesAsync(ct);
        return BuildSequenceMutationDetails(plan, version);
    }

    public async Task<Result<MenuDayNameDto>> Handle(RenameMenuCycleDayCommand request, CancellationToken ct)
    {
        var nameAr = request.NameAr?.Trim() ?? string.Empty;
        var nameEn = request.NameEn?.Trim() ?? string.Empty;
        if (nameAr.Length < 1 || nameEn.Length < 1)
            return Error.Validation("menu_cycle.day_name_required", "Arabic and English names are required.");
        if (nameAr.Length > 200 || nameEn.Length > 200)
            return Error.Validation("menu_cycle.day_name_too_long", "Menu names must be at most 200 characters.");

        var day = await _db.MenuDays
            .Include(x => x.MenuPlanVersion)
            .ThenInclude(x => x.MenuPlan)
            .FirstOrDefaultAsync(x => x.Id == request.MenuDayId && !x.IsDeleted, ct);
        var plan = day?.MenuPlanVersion?.MenuPlan;
        if (day is null || plan is null || plan.IsDeleted || plan.IsTemplate || plan.RestaurantId != request.RestaurantId)
            return Error.NotFound("menu_cycle.day_not_found", "Menu day was not found on this plan.");

        day.NameAr = nameAr;
        day.NameEn = nameEn;
        day.ModifiedAtUtc = DateTime.UtcNow;
        AddAudit(day.MenuPlanVersion, "menu_day_renamed", $"{day.Id}");
        await _db.SaveChangesAsync(ct);
        return new MenuDayNameDto(day.Id, day.DayNumber, day.NameAr, day.NameEn);
    }

    /// <summary>
    /// Sequence mutations only need plan + days (no meal graph). Full BuildDetails is reserved for GET detail.
    /// </summary>
    private async Task<MenuPlan?> LoadOwnedCycleForSequence(Guid id, Guid restaurantId, CancellationToken ct) =>
        await _db.MenuPlans
            .Include(x => x.Programs)
            .Include(x => x.Versions).ThenInclude(x => x.Days)
            .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId && !x.IsTemplate && !x.IsDeleted, ct);

    private static MenuCycleDetailsDto BuildSequenceMutationDetails(MenuPlan plan, MenuPlanVersion version)
    {
        var days = version.Days.Where(x => !x.IsDeleted).OrderBy(x => x.DayNumber).ToList();
        var dayById = days.ToDictionary(x => x.Id);
        var activeSequence = days
            .Where(x => x.IsActiveInCycle)
            .OrderBy(x => x.DayNumber)
            .Select(x => new MenuSequenceItemDto(x.Id, x.DayNumber, x.NameAr, x.NameEn, x.IsActiveInCycle))
            .ToList();

        IReadOnlyList<MenuSequenceItemDto>? pendingSequence = null;
        var pendingApplies = false;
        var pendingIds = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
        if (pendingIds.Count > 0)
        {
            var items = new List<MenuSequenceItemDto>();
            var pendingNumber = 1;
            foreach (var id in pendingIds)
            {
                if (!dayById.TryGetValue(id, out var day)) continue;
                items.Add(new MenuSequenceItemDto(day.Id, pendingNumber++, day.NameAr, day.NameEn, day.IsActiveInCycle));
            }
            pendingSequence = items;
            var activeIds = activeSequence.Select(x => x.DayId).ToList();
            pendingApplies = pendingIds.Count != activeIds.Count || !pendingIds.SequenceEqual(activeIds);
        }

        // Shell days only — client keeps existing meal payloads for unchanged days.
        var dayDtos = days
            .Select(day => new MenuDayDto(
                day.Id,
                day.DayNumber,
                day.NameAr,
                day.NameEn,
                false,
                Array.Empty<MenuProgramReadinessDto>(),
                Array.Empty<MenuCycleMealDto>()))
            .ToList();
        var versionDto = new MenuCycleVersionDto(
            version.Id,
            version.VersionNumber,
            version.EffectiveFrom,
            version.Status,
            version.IsPaused,
            version.ReviewNotes,
            version.ReviewedAtUtc,
            0,
            dayDtos.Count,
            false,
            dayDtos);
        var programIds = plan.Programs.Where(x => !x.IsDeleted).Select(x => x.ProgramId).Distinct().ToList();
        var cycleDto = new MenuCycleDto(
            plan.Id,
            plan.RestaurantId,
            plan.NameAr,
            plan.NameEn,
            plan.Source,
            plan.IsTemplate,
            plan.Mode,
            plan.CreatedAtUtc,
            versionDto,
            programIds,
            programIds.Count > 1);
        return new MenuCycleDetailsDto(cycleDto, Array.Empty<MenuCycleAuditDto>(), activeSequence, pendingSequence, pendingApplies);
    }
}
