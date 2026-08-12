using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.MenuCycles;

public sealed partial class MenuCycleHandlers :
    ICommandHandler<SetWeekdayMealCommand, MenuCycleDto>,
    ICommandHandler<RemoveWeekdayMealCommand, MenuCycleDto>,
    IQueryHandler<ResolveMenuPlanQuery, ResolvedMenuPlanDto>
{
    public async Task<Result<MenuCycleDto>> Handle(SetWeekdayMealCommand request, CancellationToken ct)
    {
        var plan = await LoadOwnedCycle(request.PlanId, request.RestaurantId, ct);
        var version = plan?.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (plan is null || version is null) return NotFound(request.PlanId);
        if (plan.Mode != ScheduleMode.Weekday) return Error.Conflict("menu_plan.mode_mismatch", "Plan is not in Weekday mode.");
        if (!MenuCycleRules.Editable(version.Status)) return NotEditable();

        var meal = await _db.Meals.Include(x => x.ProgramAssignments)
            .FirstOrDefaultAsync(x => x.Id == request.MealId && x.RestaurantId == request.RestaurantId && !x.IsDeleted, ct);
        if (meal is null) return Error.NotFound("meal.not_found", "Meal was not found.");
        var programId = meal.ProgramAssignments.FirstOrDefault(x => !x.IsDeleted)?.ProgramId ?? meal.ProgramId;
        if (programId is null) return Error.Validation("meal.program_required", "The meal must have a program before it can be scheduled.");
        if (!MenuCycleRules.IsSlot(meal.Slot ?? string.Empty)) return Error.Validation("meal.slot_invalid", "Meal slot must be breakfast, lunch, dinner, or snack.");

        var existing = version.WeekdayAssignments.FirstOrDefault(x => x.MealId == meal.Id && x.DayOfWeek == request.DayOfWeek && !x.IsDeleted);
        if (existing is null)
            version.WeekdayAssignments.Add(new WeekdayMealAssignment
            {
                DayOfWeek = request.DayOfWeek,
                ProgramId = programId.Value,
                MealId = meal.Id,
                Slot = meal.Slot!,
                SortOrder = request.SortOrder,
                IsPrimary = request.IsPrimary,
                CreatedAtUtc = DateTime.UtcNow,
            });
        else
        {
            existing.SortOrder = request.SortOrder;
            existing.IsPrimary = request.IsPrimary;
            existing.ModifiedAtUtc = DateTime.UtcNow;
        }
        if (request.IsPrimary)
            foreach (var row in version.WeekdayAssignments.Where(x => !x.IsDeleted && x.DayOfWeek == request.DayOfWeek
                         && x.ProgramId == programId && x.Slot == meal.Slot && x.MealId != meal.Id))
                row.IsPrimary = false;

        AddAudit(version, "weekday_meal_linked", $"{meal.Id}:{request.DayOfWeek}");
        await _db.SaveChangesAsync(ct);
        return await MapCycle(plan.Id, version.Id, ct);
    }

    public async Task<Result<MenuCycleDto>> Handle(RemoveWeekdayMealCommand request, CancellationToken ct)
    {
        var plan = await LoadOwnedCycle(request.PlanId, request.RestaurantId, ct);
        var version = plan?.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (plan is null || version is null) return NotFound(request.PlanId);
        if (!MenuCycleRules.Editable(version.Status)) return NotEditable();
        var link = version.WeekdayAssignments.FirstOrDefault(x => x.MealId == request.MealId && x.DayOfWeek == request.DayOfWeek && !x.IsDeleted);
        if (link is null) return Error.NotFound("menu_plan.weekday_link_not_found", "Meal is not linked to this weekday.");
        link.IsDeleted = true;
        link.DeletedAtUtc = DateTime.UtcNow;
        AddAudit(version, "weekday_meal_unlinked", $"{request.MealId}:{request.DayOfWeek}");
        await _db.SaveChangesAsync(ct);
        return await MapCycle(plan.Id, version.Id, ct);
    }

    public async Task<Result<ResolvedMenuPlanDto>> Handle(ResolveMenuPlanQuery request, CancellationToken ct)
    {
        var link = await _db.MenuPlanPrograms.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ProgramId == request.ProgramId
                && x.EffectiveFrom <= request.ServiceDate
                && (x.EffectiveTo == null || x.EffectiveTo >= request.ServiceDate))
            .Join(_db.MenuPlans.AsNoTracking().Where(p => p.RestaurantId == request.RestaurantId && !p.IsTemplate && !p.IsDeleted),
                l => l.MenuPlanId, p => p.Id, (l, p) => new { Link = l, Plan = p })
            .OrderByDescending(x => x.Link.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        if (link is null) return Error.NotFound("menu_plan.not_found_for_date", "No active menu plan for this restaurant/program/date.");

        var plan = await _db.MenuPlans.AsNoTracking()
            .Include(x => x.Versions).ThenInclude(x => x.Days).ThenInclude(x => x.Meals)
            .Include(x => x.Versions).ThenInclude(x => x.WeekdayAssignments)
            .FirstAsync(x => x.Id == link.Plan.Id, ct);
        var version = plan.Versions
            .Where(x => x.Status == VerificationStatus.Approved && !x.IsPaused && x.EffectiveFrom <= request.ServiceDate)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.VersionNumber)
            .FirstOrDefault();
        if (version is null) return Error.NotFound("menu_plan.version_not_found", "No approved menu plan version is active for this date.");

        if (plan.Mode == ScheduleMode.Weekday)
        {
            var meals = version.WeekdayAssignments.Where(x => !x.IsDeleted && x.DayOfWeek == request.ServiceDate.DayOfWeek && x.ProgramId == request.ProgramId)
                .OrderBy(x => x.Slot).ThenBy(x => x.SortOrder).ToList();
            var mealEntities = await _db.Meals.AsNoTracking().Where(x => meals.Select(m => m.MealId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var programs = await _db.Programs.AsNoTracking().Where(x => x.Id == request.ProgramId).ToDictionaryAsync(x => x.Id, ct);
            var dtos = meals.Select(x =>
            {
                mealEntities.TryGetValue(x.MealId, out var meal);
                programs.TryGetValue(x.ProgramId, out var program);
                return new MenuCycleMealDto(x.MealId, x.ProgramId, program?.NameAr ?? "", program?.NameEn ?? "",
                    meal?.NameAr ?? "", meal?.NameEn ?? "", x.Slot, x.SortOrder, x.IsPrimary, meal?.ApprovalStatus ?? VerificationStatus.Draft);
            }).ToList();
            return new ResolvedMenuPlanDto(plan.Id, version.Id, request.ProgramId, ScheduleMode.Weekday, null, null, null, null, dtos);
        }

        var preview = await BuildPreview(plan, request.RestaurantId, request.ServiceDate, 1, true, ct);
        var day = preview.First();
        var dayMeals = day.MenuDayId is null
            ? []
            : await _db.MenuDayMeals.AsNoTracking().Where(x => x.MenuDayId == day.MenuDayId && x.ProgramId == request.ProgramId && !x.IsDeleted)
                .Join(_db.Meals.AsNoTracking(), l => l.MealId, m => m.Id, (l, m) => new { l, m })
                .Join(_db.Programs.AsNoTracking(), x => x.l.ProgramId, p => p.Id, (x, p) => new { x.l, x.m, p })
                .OrderBy(x => x.l.Slot).ThenBy(x => x.l.SortOrder)
                .Select(x => new MenuCycleMealDto(x.m.Id, x.l.ProgramId, x.p.NameAr, x.p.NameEn, x.m.NameAr, x.m.NameEn, x.l.Slot, x.l.SortOrder, x.l.IsPrimary, x.m.ApprovalStatus))
                .ToListAsync(ct);
        return new ResolvedMenuPlanDto(plan.Id, version.Id, request.ProgramId, ScheduleMode.Cycle, day.MenuDayId, day.DayNumber, day.NameAr, day.NameEn, dayMeals);
    }
}
