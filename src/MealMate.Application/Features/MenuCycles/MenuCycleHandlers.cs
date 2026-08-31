using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.MenuCycles;

internal static class MenuCycleRules
{
    public static readonly string[] Slots = ["breakfast", "lunch", "dinner", "snack"];
    public static bool IsSlot(string value) => Slots.Contains(value.Trim().ToLowerInvariant());
    public static bool Editable(VerificationStatus status) => status is VerificationStatus.Draft
        or VerificationStatus.Rejected or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired;
}

public sealed class CreateRestaurantMenuCycleValidator : AbstractValidator<CreateRestaurantMenuCycleCommand>
{
    public CreateRestaurantMenuCycleValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        When(x => x.Mode != ScheduleMode.Weekday, () =>
        {
            RuleFor(x => x.Days).Must(x => x is not null && x.Count is >= 1 and <= 60)
                .WithMessage("A cycle must contain between 1 and 60 menu days.");
            RuleForEach(x => x.Days).SetValidator(new MenuCycleDayInputValidator());
        });
        RuleFor(x => x.ProgramIds)
            .Must(programIds => programIds is not null
                && programIds.Where(id => id != Guid.Empty).Distinct().Count() >= 1)
            .WithMessage("A menu plan must be linked to at least one program.");
    }
}

public sealed class MenuCycleDayInputValidator : AbstractValidator<MenuCycleDayInput>
{
    public MenuCycleDayInputValidator()
    {
        RuleFor(x => x.DayNumber).GreaterThan(0);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

public sealed partial class MenuCycleHandlers :
    ICommandHandler<CreateRestaurantMenuCycleCommand, MenuCycleDto>,
    ICommandHandler<UpdateMenuCycleDraftCommand, MenuCycleDto>,
    ICommandHandler<CreateFutureMenuCycleVersionCommand, MenuCycleDto>,
    ICommandHandler<SubmitMenuCycleCommand, MenuCycleDto>,
    ICommandHandler<SetMenuDayMealCommand, MenuDayDto>,
    ICommandHandler<RemoveMenuDayMealCommand, MenuDayDto>,
    ICommandHandler<ReviewMenuCycleCommand, MenuCycleDto>,
    ICommandHandler<SetMenuCyclePausedCommand, MenuCycleDto>,
    ICommandHandler<CreateAdminFutureMenuCycleVersionCommand, MenuCycleDto>,
    ICommandHandler<SetProgramSlotRequirementsCommand, ProgramSlotRequirementsDto>,
    IQueryHandler<GetRestaurantMenuCyclesQuery, IReadOnlyList<MenuCycleListItemDto>>,
    IQueryHandler<GetRestaurantMenuCycleQuery, MenuCycleDetailsDto>,
    IQueryHandler<PreviewRestaurantMenuCycleQuery, IReadOnlyList<MenuCyclePreviewDayDto>>,
    IQueryHandler<GetRestaurantMenuOfDateQuery, MenuCyclePreviewDayDto>,
    IQueryHandler<GetAdminMenuCyclesQuery, IReadOnlyList<MenuCycleListItemDto>>,
    IQueryHandler<GetAdminMenuCycleQuery, MenuCycleDetailsDto>,
    IQueryHandler<PreviewAdminMenuCycleQuery, IReadOnlyList<MenuCyclePreviewDayDto>>,
    IQueryHandler<GetProgramSlotRequirementsQuery, IReadOnlyList<ProgramSlotRequirementsDto>>,
    ICommandHandler<AddMenuCycleSequenceDayCommand, MenuCycleDetailsDto>,
    ICommandHandler<SwapMenuCycleSequenceDayCommand, MenuCycleDetailsDto>,
    ICommandHandler<CancelMenuCyclePendingSequenceCommand, MenuCycleDetailsDto>,
    ICommandHandler<RenameMenuCycleDayCommand, MenuDayNameDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser? _currentUser;
    private readonly IMenuPlanResolutionService? _menuResolver;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public MenuCycleHandlers(IAppDbContext db, ICurrentUser? currentUser = null, IMenuPlanResolutionService? menuResolver = null)
    {
        _db = db;
        _currentUser = currentUser;
        _menuResolver = menuResolver;
    }

    public async Task<Result<MenuCycleDto>> Handle(CreateRestaurantMenuCycleCommand request, CancellationToken ct)
    {
        IReadOnlyList<MenuCycleDayInput> days = request.Days;
        var source = MenuPlanSource.RestaurantCreated;
        var dayError = request.Mode == ScheduleMode.Weekday ? null : ValidateDays(days);
        if (dayError is not null) return dayError;
        if (request.Mode == ScheduleMode.Weekday && days.Count == 0)
            days = DefaultWeekdayDays();

        var programIds = (request.ProgramIds ?? Array.Empty<Guid>()).Where(x => x != Guid.Empty).Distinct().ToList();
        if (programIds.Count < 1)
            return Error.Validation("menu_plan.program_required", "A menu plan must be linked to at least one program.");
        var validPrograms = await _db.Programs.AsNoTracking().CountAsync(
            x => programIds.Contains(x.Id) && x.IsActive && x.Status != ProgramStatus.Archived
                && x.Status != ProgramStatus.Suspended && x.Status != ProgramStatus.Rejected, ct);
        if (validPrograms != programIds.Count)
            return Error.Validation("menu_plan.invalid_program", "Every linked program must exist and be active.");

        var existingOfferProgramIds = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.RestaurantId == request.RestaurantId && x.IsActive && programIds.Contains(x.ProgramId))
            .Select(x => x.ProgramId)
            .Distinct()
            .ToListAsync(ct);
        if (programIds.Except(existingOfferProgramIds).Any())
            return Error.Validation("menu_plan.program_not_selected", "Every linked program must be an active offering of this restaurant.");
        var cycle = NewCycle(request.RestaurantId, request.NameAr, request.NameEn, source, false, request.Mode);
        var version = NewVersion(cycle, 1, request.EffectiveFrom, days);
        _db.MenuPlans.Add(cycle);
        var overlap = await HasActiveProgramOverlap(request.RestaurantId, programIds, request.EffectiveFrom, null, ct);
        if (overlap) return Error.Conflict("menu_plan.program_overlap", "Another active plan already covers one of these programs on this date.");
        for (var i = 0; i < programIds.Count; i++)
        {
            cycle.Programs.Add(new MenuPlanProgram
            {
                ProgramId = programIds[i],
                EffectiveFrom = request.EffectiveFrom,
                IsPrimaryOwner = i == 0,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        AddAudit(version, "created");
        await _db.SaveChangesAsync(ct);
        return await MapCycle(cycle.Id, version.Id, ct);
    }

    public async Task<Result<MenuCycleDto>> Handle(UpdateMenuCycleDraftCommand request, CancellationToken ct)
    {
        var cycle = await _db.MenuPlans
            .Include(x => x.Programs)
            .FirstOrDefaultAsync(x => x.Id == request.CycleId && x.RestaurantId == request.RestaurantId && !x.IsTemplate && !x.IsDeleted, ct);
        if (cycle is null) return NotFound(request.CycleId);

        var version = await _db.MenuPlanVersions
            .Where(x => x.MenuPlanId == cycle.Id && !x.IsDeleted)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (version is null) return NotFound(request.CycleId);
        if (!MenuCycleRules.Editable(version.Status)) return NotEditable();
        var dayError = ValidateDays(request.Days);
        if (dayError is not null) return dayError;

        cycle.NameAr = request.NameAr.Trim(); cycle.NameEn = request.NameEn.Trim();
        cycle.ModifiedAtUtc = DateTime.UtcNow;
        version.EffectiveFrom = request.EffectiveFrom;
        await ReplaceDaysAsync(version, request.Days, ct);
        version.Status = VerificationStatus.Draft;
        version.ReviewNotes = null;
        AddAudit(version, "draft_updated");
        await _db.SaveChangesAsync(ct);
        return await MapCycleLite(cycle.Id, version.Id, ct);
    }

    public async Task<Result<MenuCycleDto>> Handle(CreateFutureMenuCycleVersionCommand request, CancellationToken ct)
    {
        if (request.EffectiveFrom <= Today)
            return Error.Validation("menu_cycle.future_date_required", "A future version must start after today.");

        var cycle = await _db.MenuPlans
            .Include(x => x.Programs)
            .FirstOrDefaultAsync(x => x.Id == request.CycleId && x.RestaurantId == request.RestaurantId && !x.IsTemplate && !x.IsDeleted, ct);
        if (cycle is null) return NotFound(request.CycleId);

        var current = await _db.MenuPlanVersions
            .Where(x => x.MenuPlanId == cycle.Id && !x.IsDeleted)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (current is null) return NotFound(request.CycleId);

        if (MenuCycleRules.Editable(current.Status))
        {
            if (request.Days is { Count: > 0 })
            {
                var editableDayError = ValidateDays(request.Days);
                if (editableDayError is not null) return editableDayError;
                current.EffectiveFrom = request.EffectiveFrom;
                await ReplaceDaysAsync(current, request.Days, ct);
                AddAudit(current, "draft_updated", "Updated days on existing draft version.");
            }
            else if (current.EffectiveFrom != request.EffectiveFrom)
            {
                current.EffectiveFrom = request.EffectiveFrom;
                AddAudit(current, "draft_updated", "Updated effective date on existing draft version.");
            }

            await _db.SaveChangesAsync(ct);
            return await MapCycleLite(cycle.Id, current.Id, ct);
        }

        var dayInputs = request.Days is { Count: > 0 }
            ? request.Days
            : await _db.MenuDays.AsNoTracking()
                .Where(x => x.MenuPlanVersionId == current.Id && !x.IsDeleted)
                .OrderBy(x => x.DayNumber)
                .Select(x => new MenuCycleDayInput(x.DayNumber, x.NameAr, x.NameEn))
                .ToListAsync(ct);
        var dayError = ValidateDays(dayInputs);
        if (dayError is not null) return dayError;

        var next = NewVersion(cycle, current.VersionNumber + 1, request.EffectiveFrom, dayInputs);
        await CloneVersionMealLinksAsync(current.Id, next, ct);
        await CloneWeekdayAssignmentsAsync(current.Id, next, ct);
        AddAudit(next, "future_version_created", $"Cloned from version {current.VersionNumber}");
        await _db.SaveChangesAsync(ct);
        return await MapCycleLite(cycle.Id, next.Id, ct);
    }

    public async Task<Result<MenuCycleDto>> Handle(CreateAdminFutureMenuCycleVersionCommand request, CancellationToken ct)
    {
        var restaurantId = await _db.MenuPlans.AsNoTracking().Where(x => x.Id == request.CycleId && !x.IsTemplate)
            .Select(x => x.RestaurantId).FirstOrDefaultAsync(ct);
        if (!restaurantId.HasValue) return NotFound(request.CycleId);
        return await Handle(new CreateFutureMenuCycleVersionCommand(restaurantId.Value, request.CycleId, request.EffectiveFrom), ct);
    }

    public async Task<Result<MenuCycleDto>> Handle(SubmitMenuCycleCommand request, CancellationToken ct)
    {
        var cycle = await LoadOwnedCycle(request.CycleId, request.RestaurantId, ct);
        var version = cycle?.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (cycle is null || version is null) return NotFound(request.CycleId);
        if (!MenuCycleRules.Editable(version.Status)) return NotEditable();
        var dto = await BuildVersionDto(version.Id, request.RestaurantId, ct);
        if (!dto.IsReady) return Error.Validation("menu_cycle.not_ready", "Every active program must have approved meals in its required slots on every menu day.");
        version.Status = VerificationStatus.Submitted;
        AddAudit(version, "submitted");
        await _db.SaveChangesAsync(ct);
        return await MapCycle(cycle.Id, version.Id, ct);
    }

    public async Task<Result<MenuDayDto>> Handle(SetMenuDayMealCommand request, CancellationToken ct)
    {
        var day = await LoadEditableDay(request.MenuDayId, request.RestaurantId, ct);
        if (day is null) return Error.NotFound("menu_cycle.day_not_found", "Editable menu day was not found.");
        var meal = await _db.Meals.FirstOrDefaultAsync(x => x.Id == request.MealId && x.RestaurantId == request.RestaurantId && !x.IsDeleted, ct);
        if (meal?.ProgramId is null) return Error.Validation("meal.program_required", "The meal must have a program before it can be added to a menu day.");
        if (!MenuCycleRules.IsSlot(meal.Slot ?? string.Empty)) return Error.Validation("meal.slot_invalid", "Meal slot must be breakfast, lunch, dinner, or snack.");
        var existing = day.Meals.FirstOrDefault(x => x.MealId == meal.Id && !x.IsDeleted);
        if (existing is null) day.Meals.Add(NewLink(day, meal, request.SortOrder, request.IsPrimary));
        else { existing.SortOrder = request.SortOrder; existing.IsPrimary = request.IsPrimary; existing.ModifiedAtUtc = DateTime.UtcNow; }
        if (request.IsPrimary)
            foreach (var link in day.Meals.Where(x => !x.IsDeleted && x.MealId != meal.Id && x.ProgramId == meal.ProgramId && x.Slot == meal.Slot)) link.IsPrimary = false;
        AddAudit(day.MenuPlanVersion, "meal_linked", $"{meal.Id}:{day.Id}");
        await _db.SaveChangesAsync(ct);
        return await BuildDayDto(day.Id, request.RestaurantId, ct);
    }

    public async Task<Result<MenuDayDto>> Handle(RemoveMenuDayMealCommand request, CancellationToken ct)
    {
        var day = await LoadEditableDay(request.MenuDayId, request.RestaurantId, ct);
        if (day is null) return Error.NotFound("menu_cycle.day_not_found", "Editable menu day was not found.");
        var link = day.Meals.FirstOrDefault(x => x.MealId == request.MealId && !x.IsDeleted);
        if (link is null) return Error.NotFound("menu_cycle.meal_link_not_found", "Meal is not linked to this menu day.");
        link.IsDeleted = true; link.DeletedAtUtc = DateTime.UtcNow;
        AddAudit(day.MenuPlanVersion, "meal_unlinked", $"{request.MealId}:{day.Id}");
        await _db.SaveChangesAsync(ct);
        return await BuildDayDto(day.Id, request.RestaurantId, ct);
    }

    public async Task<Result<MenuCycleDto>> Handle(ReviewMenuCycleCommand request, CancellationToken ct)
    {
        if (request.Decision is not (VerificationStatus.Approved or VerificationStatus.Rejected or VerificationStatus.NeedsChanges))
            return Error.Validation("menu_cycle.review_decision_invalid", "Decision must be Approved, Rejected, or NeedsChanges.");
        var cycle = await _db.MenuPlans.Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == request.CycleId && !x.IsTemplate && !x.IsDeleted, ct);
        var version = cycle?.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (cycle?.RestaurantId is null || version is null) return NotFound(request.CycleId);
        if (version.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
            return Error.Conflict("menu_cycle.not_under_review", "Only a submitted cycle can be reviewed.");
        if (request.Decision == VerificationStatus.Approved)
        {
            var dto = await BuildVersionDto(version.Id, cycle.RestaurantId.Value, ct);
            if (!dto.IsReady) return Error.Validation("menu_cycle.not_ready", "The cycle is no longer ready for approval.");
            var cyclePrograms = await _db.MenuPlanPrograms.AsNoTracking().Where(x => x.MenuPlanId == cycle.Id && !x.IsDeleted).ToListAsync(ct);
            var conflict = false;
            foreach (var currentProgram in cyclePrograms)
            {
                var currentEnd = currentProgram.EffectiveTo;
                conflict = await (from other in _db.MenuPlanVersions.AsNoTracking()
                                  join otherCycle in _db.MenuPlans.AsNoTracking() on other.MenuPlanId equals otherCycle.Id
                                  join otherProgram in _db.MenuPlanPrograms.AsNoTracking() on otherCycle.Id equals otherProgram.MenuPlanId
                                  where otherCycle.Id != cycle.Id && otherCycle.RestaurantId == cycle.RestaurantId && !otherCycle.IsDeleted
                                      && other.Status == VerificationStatus.Approved && !other.IsPaused && !otherProgram.IsDeleted
                                      && otherProgram.ProgramId == currentProgram.ProgramId
                                      && (currentEnd == null || otherProgram.EffectiveFrom <= currentEnd)
                                      && (otherProgram.EffectiveTo == null || otherProgram.EffectiveTo >= currentProgram.EffectiveFrom)
                                  select other.Id).AnyAsync(ct);
                if (conflict) break;
            }
            if (conflict) return Error.Conflict("menu_cycle.effective_date_conflict", "Another approved cycle starts on the same date.");
        }
        version.Status = request.Decision; version.ReviewNotes = request.Notes?.Trim();
        version.ReviewedAtUtc = DateTime.UtcNow; version.ReviewedByUserId = _currentUser?.UserId;
        AddAudit(version, request.Decision.ToString().ToLowerInvariant(), request.Notes);
        await _db.SaveChangesAsync(ct); return await MapCycle(cycle.Id, version.Id, ct);
    }

    public async Task<Result<MenuCycleDto>> Handle(SetMenuCyclePausedCommand request, CancellationToken ct)
    {
        var cycle = await _db.MenuPlans.Include(x => x.Versions).FirstOrDefaultAsync(x => x.Id == request.CycleId && !x.IsTemplate && !x.IsDeleted, ct);
        var version = cycle?.Versions.Where(x => x.Status == VerificationStatus.Approved).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
        if (cycle is null || version is null) return Error.Conflict("menu_cycle.no_approved_version", "An approved version is required.");
        version.IsPaused = request.IsPaused; version.PausedAtUtc = request.IsPaused ? DateTime.UtcNow : null;
        AddAudit(version, request.IsPaused ? "paused" : "resumed", request.Notes); await _db.SaveChangesAsync(ct);
        return await MapCycle(cycle.Id, version.Id, ct);
    }

    public async Task<Result<ProgramSlotRequirementsDto>> Handle(SetProgramSlotRequirementsCommand request, CancellationToken ct)
    {
        var slots = request.Slots.Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
        if (slots.Count == 0 || slots.Any(x => !MenuCycleRules.IsSlot(x)))
            return Error.Validation("menu_cycle.slots_invalid", "At least one valid slot is required.");
        var program = await _db.Programs.FirstOrDefaultAsync(x => x.Id == request.ProgramId && !x.IsDeleted, ct);
        if (program is null) return Error.NotFound("program.not_found", "Program was not found.");
        var current = await _db.ProgramMealSlotRequirements.IgnoreQueryFilters().Where(x => x.ProgramId == request.ProgramId).ToListAsync(ct);
        foreach (var row in current) { row.IsDeleted = !slots.Contains(row.Slot); row.DeletedAtUtc = row.IsDeleted ? DateTime.UtcNow : null; row.IsRequired = true; }
        foreach (var slot in slots.Where(s => current.All(x => x.Slot != s)))
            _db.ProgramMealSlotRequirements.Add(new ProgramMealSlotRequirement { ProgramId = request.ProgramId, Slot = slot, IsRequired = true, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct);
        return new ProgramSlotRequirementsDto(program.Id, program.NameAr, program.NameEn, slots);
    }

    public async Task<Result<IReadOnlyList<ProgramSlotRequirementsDto>>> Handle(GetProgramSlotRequirementsQuery request, CancellationToken ct)
    {
        var programs = await _db.Programs.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(ct);
        var rows = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => x.IsRequired).ToListAsync(ct);
        IReadOnlyList<ProgramSlotRequirementsDto> result = programs.Select(p =>
        {
            var configured = rows.Where(x => x.ProgramId == p.Id).Select(x => x.Slot).ToList();
            return new ProgramSlotRequirementsDto(p.Id, p.NameAr, p.NameEn, configured.Count > 0 ? configured : MenuCycleRules.Slots);
        }).ToList();
        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<MenuCycleListItemDto>>> Handle(GetRestaurantMenuCyclesQuery request, CancellationToken ct)
        => Result.Success(await BuildList(request.RestaurantId, ct));
    public async Task<Result<IReadOnlyList<MenuCycleListItemDto>>> Handle(GetAdminMenuCyclesQuery request, CancellationToken ct)
        => Result.Success(await BuildList(null, ct));

    public async Task<Result<MenuCycleDetailsDto>> Handle(GetRestaurantMenuCycleQuery request, CancellationToken ct)
    {
        var owned = await _db.MenuPlans.AsNoTracking().AnyAsync(x => x.Id == request.CycleId && x.RestaurantId == request.RestaurantId, ct);
        if (!owned) return NotFound(request.CycleId);
        return await BuildDetails(request.CycleId, ct);
    }
    public Task<Result<MenuCycleDetailsDto>> Handle(GetAdminMenuCycleQuery request, CancellationToken ct) => BuildDetails(request.CycleId, ct);

    public async Task<Result<IReadOnlyList<MenuCyclePreviewDayDto>>> Handle(PreviewRestaurantMenuCycleQuery request, CancellationToken ct)
    {
        if (request.Days is not (14 or 28)) return Error.Validation("menu_cycle.preview_range", "Preview range must be 14 or 28 days.");
        var cycle = await _db.MenuPlans.AsNoTracking().Include(x => x.Versions).ThenInclude(x => x.Days)
            .FirstOrDefaultAsync(x => x.Id == request.CycleId && x.RestaurantId == request.RestaurantId, ct);
        if (cycle is null) return NotFound(request.CycleId);
        return Result.Success(await BuildPreview(cycle, request.RestaurantId, request.From, request.Days, false, ct));
    }

    public async Task<Result<MenuCyclePreviewDayDto>> Handle(GetRestaurantMenuOfDateQuery request, CancellationToken ct)
    {
        if (request.ProgramId is { } programId && _menuResolver is not null)
        {
            var resolvedMenu = await _menuResolver.ResolveAsync(request.RestaurantId, programId, request.Date, ct);
            if (resolvedMenu is null)
                return Error.NotFound("menu_cycle.no_active_cycle", "No approved menu cycle is active on this date.");
            if (resolvedMenu.Assignments.FirstOrDefault(x => x.MenuDayId.HasValue)?.MenuDayId is not { } resolvedDayId)
                return new MenuCyclePreviewDayDto(request.Date, true, null, resolvedMenu.MenuPlanVersionId, null, null, null, null);
            var day = await _db.MenuDays.AsNoTracking().Where(x => x.Id == resolvedDayId)
                .Select(x => new { x.Id, x.DayNumber, x.NameAr, x.NameEn }).FirstAsync(ct);
            return new MenuCyclePreviewDayDto(request.Date, true, null, resolvedMenu.MenuPlanVersionId,
                day.Id, day.DayNumber, day.NameAr, day.NameEn);
        }
        var resolved = await (from version in _db.MenuPlanVersions.AsNoTracking()
                              join cycleRow in _db.MenuPlans.AsNoTracking() on version.MenuPlanId equals cycleRow.Id
                              where cycleRow.RestaurantId == request.RestaurantId && !cycleRow.IsTemplate
                                  && version.Status == VerificationStatus.Approved && version.EffectiveFrom <= request.Date
                              orderby version.EffectiveFrom descending, version.VersionNumber descending
                              select new { cycleRow.Id, Version = version }).FirstOrDefaultAsync(ct);
        if (resolved is null) return Error.NotFound("menu_cycle.no_active_cycle", "No approved menu cycle is active on this date.");
        if (resolved.Version.IsPaused) return Error.Conflict("menu_cycle.paused", "The active menu cycle is paused.");
        var cycle = await _db.MenuPlans.AsNoTracking().Include(x => x.Versions).ThenInclude(x => x.Days).FirstAsync(x => x.Id == resolved.Id, ct);
        var preview = await BuildPreview(cycle, request.RestaurantId, request.Date, 1, true, ct);
        return preview[0];
    }

    public async Task<Result<IReadOnlyList<MenuCyclePreviewDayDto>>> Handle(PreviewAdminMenuCycleQuery request, CancellationToken ct)
    {
        if (request.Days is not (14 or 28)) return Error.Validation("menu_cycle.preview_range", "Preview range must be 14 or 28 days.");
        var cycle = await _db.MenuPlans.AsNoTracking().Include(x => x.Versions).ThenInclude(x => x.Days)
            .FirstOrDefaultAsync(x => x.Id == request.CycleId && !x.IsTemplate, ct);
        if (cycle?.RestaurantId is null) return NotFound(request.CycleId);
        return Result.Success(await BuildPreview(cycle, cycle.RestaurantId.Value, request.From, request.Days, false, ct));
    }

    private async Task<IReadOnlyList<MenuCycleListItemDto>> BuildList(Guid? restaurantId, CancellationToken ct)
    {
        var query = _db.MenuPlans.AsNoTracking()
            .Include(x => x.Programs)
            .Include(x => x.Versions)
            .ThenInclude(x => x.Days)
            .Where(x => !x.IsDeleted);
        if (restaurantId.HasValue) query = query.Where(x => x.RestaurantId == restaurantId && !x.IsTemplate);
        else query = query.Where(x => !x.IsTemplate);

        var cycles = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var restaurantIds = cycles.Where(x => x.RestaurantId.HasValue)
            .Select(x => x.RestaurantId!.Value).Distinct().ToList();

        var latestVersions = cycles.Select(x => x.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault())
            .Where(x => x is not null).Cast<MenuPlanVersion>().ToList();
        var latestDayIds = latestVersions.SelectMany(x => x.Days).Select(x => x.Id).ToList();
        var approvedEffectiveDates = cycles.SelectMany(x => x.Versions)
            .Where(x => x.Status == VerificationStatus.Approved && !x.IsPaused)
            .Select(x => x.EffectiveFrom)
            .ToList();
        var exceptionFrom = approvedEffectiveDates.Count == 0 ? Today : approvedEffectiveDates.Min();
        var previewTo = Today.AddDays(13);

        // Sequential only — EF DbContext is not safe for concurrent queries.
        var names = restaurantIds.Count == 0
            ? []
            : await _db.RestaurantProfiles.AsNoTracking()
                .Where(x => restaurantIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
        var offers = restaurantIds.Count == 0
            ? []
            : await _db.RestaurantProgramBundleOffers.AsNoTracking()
                .Where(x => restaurantIds.Contains(x.RestaurantId) && x.IsActive)
                .Select(x => new MenuListProgramOffer(x.RestaurantId, x.ProgramId))
                .ToListAsync(ct);
        var links = latestDayIds.Count == 0
            ? []
            : await (
                from link in _db.MenuDayMeals.AsNoTracking()
                join meal in _db.Meals.AsNoTracking() on link.MealId equals meal.Id
                where latestDayIds.Contains(link.MenuDayId)
                select new MenuListMealLink(
                    link.MenuDayId,
                    link.ProgramId,
                    link.Slot,
                    meal.ApprovalStatus == VerificationStatus.Approved && meal.IsAvailableForNewOrders))
                .ToListAsync(ct);
        var schedules = restaurantIds.Count == 0
            ? []
            : await _db.RestaurantOperatingSchedules.AsNoTracking()
                .Where(x => restaurantIds.Contains(x.RestaurantId))
                .ToListAsync(ct);
        var exceptions = restaurantIds.Count == 0
            ? []
            : await _db.RestaurantOperatingDayExceptions.AsNoTracking()
                .Where(x => restaurantIds.Contains(x.RestaurantId)
                    && x.Date >= exceptionFrom
                    && x.Date <= previewTo)
                .ToListAsync(ct);

        var programIds = offers.Select(x => x.ProgramId).Concat(links.Select(x => x.ProgramId)).Distinct().ToList();
        var requirements = programIds.Count == 0
            ? []
            : await _db.ProgramMealSlotRequirements.AsNoTracking()
                .Where(x => programIds.Contains(x.ProgramId) && x.IsRequired)
                .Select(x => new MenuListSlotRequirement(x.ProgramId, x.Slot))
                .ToListAsync(ct);

        var offerLookup = offers.GroupBy(x => x.RestaurantId)
            .ToDictionary(x => x.Key, x => x.Select(value => value.ProgramId).ToHashSet());
        var fallbackProgramLookup = latestVersions
            .SelectMany(version => version.Days.Select(day => new { version.MenuPlanId, DayId = day.Id }))
            .Join(links, day => day.DayId, link => link.MenuDayId, (day, link) => new { day.MenuPlanId, link.ProgramId })
            .GroupBy(x => x.MenuPlanId)
            .ToDictionary(x => x.Key, x => x.Select(value => value.ProgramId).ToHashSet());
        var approvedSlotLookup = links.Where(x => x.IsApproved)
            .GroupBy(x => (x.MenuDayId, x.ProgramId))
            .ToDictionary(x => x.Key, x => x.Select(value => value.Slot).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var requirementLookup = requirements.GroupBy(x => x.ProgramId)
            .ToDictionary(x => x.Key, x => x.Select(value => value.Slot).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var list = new List<MenuCycleListItemDto>();
        foreach (var cycle in cycles)
        {
            var version = cycle.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault(); if (version is null) continue;

            var readyDays = 0;
            MenuDay? today = null;
            MenuDay? next = null;
            if (cycle.RestaurantId is { } cycleRestaurantId)
            {
                var activePrograms = offerLookup.GetValueOrDefault(cycleRestaurantId)
                    ?? fallbackProgramLookup.GetValueOrDefault(cycle.Id)
                    ?? [];
                readyDays = version.Days.Count(day =>
                    activePrograms.Count > 0
                    && activePrograms.All(programId =>
                    {
                        var requiredSlots = requirementLookup.GetValueOrDefault(programId)
                            ?? MenuCycleRules.Slots.ToHashSet(StringComparer.OrdinalIgnoreCase);
                        return approvedSlotLookup.TryGetValue((day.Id, programId), out var approvedSlots)
                            && requiredSlots.All(approvedSlots.Contains);
                    }));

                var restaurantSchedules = schedules.Where(x => x.RestaurantId == cycleRestaurantId).ToList();
                var restaurantExceptions = exceptions.Where(x => x.RestaurantId == cycleRestaurantId).ToList();
                (today, next) = BuildListPreview(cycle, restaurantSchedules, restaurantExceptions);
            }

            names.TryGetValue(cycle.RestaurantId ?? Guid.Empty, out var profile);
            var totalDays = version.Days.Count(d => d.IsActiveInCycle);
            if (totalDays == 0) totalDays = version.Days.Count;
            var percent = totalDays == 0 ? 0 : (int)Math.Round(readyDays * 100m / totalDays);
            var programIdsForPlan = cycle.Programs.Where(x => !x.IsDeleted && (x.EffectiveTo is null || x.EffectiveTo >= Today))
                .Select(x => x.ProgramId).Distinct().ToList();
            if (programIdsForPlan.Count == 0)
                programIdsForPlan = fallbackProgramLookup.GetValueOrDefault(cycle.Id)?.ToList() ?? [];
            list.Add(new MenuCycleListItemDto(cycle.Id, cycle.RestaurantId, profile?.TradeNameAr, profile?.TradeNameEn,
                cycle.NameAr, cycle.NameEn, cycle.IsTemplate, cycle.Source, cycle.Mode, version.Status, version.IsPaused,
                readyDays, totalDays, percent, today?.DayNumber, today?.NameAr,
                next?.DayNumber, next?.NameAr, cycle.IsTemplate ? null : version.EffectiveFrom,
                programIdsForPlan, programIdsForPlan.Count > 1));
        }

        return list;
    }

    private static (MenuDay? TodayMenu, MenuDay? NextMenu) BuildListPreview(
        MenuPlan cycle,
        IReadOnlyList<RestaurantOperatingSchedule> schedules,
        IReadOnlyList<RestaurantOperatingDayException> exceptions)
    {
        MenuDay? todayMenu = null;
        MenuDay? nextMenu = null;
        var versions = cycle.Versions
            .Where(x => x.Status == VerificationStatus.Approved && !x.IsPaused)
            .OrderBy(x => x.EffectiveFrom)
            .ThenBy(x => x.VersionNumber)
            .ToList();

        for (var date = Today; date <= Today.AddDays(13); date = date.AddDays(1))
        {
            var version = versions.LastOrDefault(x => x.EffectiveFrom <= date);
            if (version is null || !IsWorkingDate(date, schedules, exceptions)) continue;
            var activeDays = version.Days.Where(x => !x.IsDeleted && x.IsActiveInCycle).OrderBy(x => x.DayNumber).ToList();
            if (activeDays.Count == 0) continue;
            var workingIndex = CountWorkingDays(version.EffectiveFrom, date, schedules, exceptions);
            var pendingIds = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
            var dayId = MenuCyclePendingSequence.PickDayIdForWorkingIndex(
                activeDays.Select(x => x.Id).ToList(),
                pendingIds,
                workingIndex);
            var menu = version.Days.FirstOrDefault(x => x.Id == dayId) ?? activeDays[workingIndex % activeDays.Count];
            if (date == Today) todayMenu = menu;
            else if (nextMenu is null) nextMenu = menu;
            if (todayMenu is not null && nextMenu is not null) break;
        }

        return (todayMenu, nextMenu);
    }

    private static bool IsWorkingDate(
        DateOnly date,
        IReadOnlyList<RestaurantOperatingSchedule> schedules,
        IReadOnlyList<RestaurantOperatingDayException> exceptions)
    {
        var exception = exceptions.FirstOrDefault(x => x.Date == date);
        if (exception is not null) return exception.IsWorkingDay;
        return schedules.Count == 0 || schedules.FirstOrDefault(x => x.DayOfWeek == date.DayOfWeek)?.IsWorkingDay == true;
    }

    private static int CountWorkingDays(
        DateOnly from,
        DateOnly toExclusive,
        IReadOnlyList<RestaurantOperatingSchedule> schedules,
        IReadOnlyList<RestaurantOperatingDayException> exceptions)
    {
        var totalDays = toExclusive.DayNumber - from.DayNumber;
        if (totalDays <= 0) return 0;

        var workingDaysPerWeek = schedules.Count == 0
            ? 7
            : Enum.GetValues<DayOfWeek>().Count(day =>
                schedules.FirstOrDefault(x => x.DayOfWeek == day)?.IsWorkingDay == true);
        var result = totalDays / 7 * workingDaysPerWeek;
        var remainder = totalDays % 7;
        for (var index = 0; index < remainder; index++)
        {
            var date = from.AddDays(totalDays / 7 * 7 + index);
            if (schedules.Count == 0
                || schedules.FirstOrDefault(x => x.DayOfWeek == date.DayOfWeek)?.IsWorkingDay == true)
                result++;
        }

        foreach (var exception in exceptions.Where(x => x.Date >= from && x.Date < toExclusive))
        {
            var scheduledWorking = schedules.Count == 0
                || schedules.FirstOrDefault(x => x.DayOfWeek == exception.Date.DayOfWeek)?.IsWorkingDay == true;
            if (exception.IsWorkingDay && !scheduledWorking) result++;
            else if (!exception.IsWorkingDay && scheduledWorking) result--;
        }

        return result;
    }

    private sealed record MenuListProgramOffer(Guid RestaurantId, Guid ProgramId);
    private sealed record MenuListMealLink(Guid MenuDayId, Guid ProgramId, string Slot, bool IsApproved);
    private sealed record MenuListSlotRequirement(Guid ProgramId, string Slot);

    private async Task<Result<MenuCycleDetailsDto>> BuildDetails(Guid cycleId, CancellationToken ct)
    {
        var exists = await _db.MenuPlans.AsNoTracking().AnyAsync(x => x.Id == cycleId, ct); if (!exists) return NotFound(cycleId);
        // Light readiness for GET/sequence mutations — full scan stays on submit gate only.
        var dto = await MapCycle(cycleId, null, ct, fullReadiness: false);
        var versionId = dto.CurrentVersion?.Id;
        var audits = versionId.HasValue ? await _db.MenuPlanAudits.AsNoTracking().Where(x => x.MenuPlanVersionId == versionId)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new MenuCycleAuditDto(x.Id, x.Action, x.Notes, x.PerformedByUserId, x.CreatedAtUtc)).ToListAsync(ct) : [];

        IReadOnlyList<MenuSequenceItemDto> activeSequence = [];
        IReadOnlyList<MenuSequenceItemDto>? pendingSequence = null;
        var pendingApplies = false;
        if (versionId.HasValue)
        {
            var version = await _db.MenuPlanVersions.AsNoTracking().FirstAsync(x => x.Id == versionId.Value, ct);
            var days = await _db.MenuDays.AsNoTracking()
                .Where(x => x.MenuPlanVersionId == versionId.Value && !x.IsDeleted)
                .ToListAsync(ct);
            var dayById = days.ToDictionary(x => x.Id);
            activeSequence = days
                .Where(x => x.IsActiveInCycle)
                .OrderBy(x => x.DayNumber)
                .Select(x => new MenuSequenceItemDto(x.Id, x.DayNumber, x.NameAr, x.NameEn, x.IsActiveInCycle))
                .ToList();
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
                pendingApplies = pendingIds.Count != activeIds.Count
                    || !pendingIds.SequenceEqual(activeIds);
            }
        }

        return new MenuCycleDetailsDto(dto, audits, activeSequence, pendingSequence, pendingApplies);
    }

    private async Task<MenuCycleDto> MapCycleLite(Guid cycleId, Guid versionId, CancellationToken ct)
    {
        var cycle = await _db.MenuPlans.AsNoTracking()
            .Include(x => x.Programs)
            .FirstAsync(x => x.Id == cycleId, ct);
        var versionDto = cycle.RestaurantId.HasValue
            ? await BuildVersionDto(versionId, cycle.RestaurantId.Value, ct, fullReadiness: false)
            : null;
        var programIds = cycle.Programs.Where(x => !x.IsDeleted).Select(x => x.ProgramId).Distinct().ToList();
        return new MenuCycleDto(cycle.Id, cycle.RestaurantId, cycle.NameAr, cycle.NameEn, cycle.Source, cycle.IsTemplate, cycle.Mode, cycle.CreatedAtUtc, versionDto, programIds, programIds.Count > 1);
    }

    private async Task<MenuCycleDto> MapCycle(Guid cycleId, Guid? versionId, CancellationToken ct, bool fullReadiness = true)
    {
        var cycle = await _db.MenuPlans.AsNoTracking()
            .Include(x => x.Versions)
            .Include(x => x.Programs)
            .FirstAsync(x => x.Id == cycleId, ct);
        var version = versionId.HasValue ? cycle.Versions.Single(x => x.Id == versionId) : cycle.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        var versionDto = version is null
            ? null
            : cycle.RestaurantId.HasValue
                ? await BuildVersionDto(version.Id, cycle.RestaurantId.Value, ct, fullReadiness)
                : EmptyVersion(version) with { EffectiveFrom = null };
        var programIds = cycle.Programs.Where(x => !x.IsDeleted).Select(x => x.ProgramId).Distinct().ToList();
        return new MenuCycleDto(cycle.Id, cycle.RestaurantId, cycle.NameAr, cycle.NameEn, cycle.Source, cycle.IsTemplate, cycle.Mode, cycle.CreatedAtUtc, versionDto, programIds, programIds.Count > 1);
    }

    private async Task<MenuCycleVersionDto> BuildVersionDto(Guid versionId, Guid restaurantId, CancellationToken ct, bool fullReadiness = true)
    {
        var version = await _db.MenuPlanVersions.AsNoTracking().FirstAsync(x => x.Id == versionId, ct);
        var mode = await _db.MenuPlans.AsNoTracking().Where(x => x.Id == version.MenuPlanId).Select(x => x.Mode).FirstAsync(ct);
        if (mode == ScheduleMode.Weekday)
            await EnsureWeekdayMenuDaysAsync(versionId, ct);
        var days = await _db.MenuDays.AsNoTracking()
            .Where(x => x.MenuPlanVersionId == versionId)
            .OrderBy(x => x.DayNumber)
            .ToListAsync(ct);
        var dayIds = days.Select(x => x.Id).ToList();
        var links = dayIds.Count == 0
            ? []
            : await _db.MenuDayMeals.AsNoTracking().Where(x => dayIds.Contains(x.MenuDayId) && !x.IsDeleted)
                .Join(_db.Meals.AsNoTracking(), l => l.MealId, m => m.Id, (l, m) => new { l, m })
                .Join(_db.Programs.AsNoTracking(), x => x.l.ProgramId, p => p.Id, (x, p) => new { x.l, x.m, p })
                .OrderBy(x => x.l.MenuDayId).ThenBy(x => x.l.ProgramId).ThenBy(x => x.l.Slot).ThenBy(x => x.l.SortOrder)
                .ToListAsync(ct);
        var readyMealIds = fullReadiness
            ? await GetOperationallyReadyMealIds(links.Select(x => x.m.Id).Distinct().ToList(), ct)
            : links.Where(x => x.m.ApprovalStatus == VerificationStatus.Approved && x.m.IsAvailableForNewOrders)
                .Select(x => x.m.Id).ToHashSet();
        var activeProgramIds = await _db.MenuPlanPrograms.AsNoTracking()
            .Where(x => x.MenuPlanId == version.MenuPlanId && !x.IsDeleted && (x.EffectiveTo == null || x.EffectiveTo >= Today))
            .Select(x => x.ProgramId).Distinct().ToListAsync(ct);
        if (activeProgramIds.Count == 0)
            activeProgramIds = await _db.RestaurantProgramBundleOffers.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .Select(x => x.ProgramId).Distinct().ToListAsync(ct);
        if (activeProgramIds.Count == 0)
            activeProgramIds = links.Select(x => x.l.ProgramId).Distinct().ToList();
        var requirements = activeProgramIds.Count == 0
            ? []
            : await _db.ProgramMealSlotRequirements.AsNoTracking()
                .Where(x => activeProgramIds.Contains(x.ProgramId) && x.IsRequired)
                .ToListAsync(ct);
        var programs = activeProgramIds.Count == 0
            ? []
            : await _db.Programs.AsNoTracking().Where(x => activeProgramIds.Contains(x.Id)).ToListAsync(ct);
        var programById = programs.ToDictionary(x => x.Id);
        var dayDtos = days.Select(day =>
        {
            var dayLinks = links.Where(x => x.l.MenuDayId == day.Id).ToList();
            var readiness = new List<MenuProgramReadinessDto>();
            foreach (var programId in activeProgramIds)
            {
                if (!programById.TryGetValue(programId, out var program)) continue;
                var required = requirements.Where(x => x.ProgramId == programId).Select(x => x.Slot).ToList();
                if (required.Count == 0) required = MenuCycleRules.Slots.ToList();
                var approved = dayLinks.Where(x => x.l.ProgramId == programId && readyMealIds.Contains(x.m.Id)).ToList();
                var completed = required.Count(slot => approved.Any(x => x.l.Slot == slot));
                readiness.Add(new MenuProgramReadinessDto(programId, program.NameAr, program.NameEn, completed, required.Count,
                    approved.Count, completed == required.Count, required.Where(slot => approved.All(x => x.l.Slot != slot)).ToList()));
            }
            var mealDtos = dayLinks.Select(x => new MenuCycleMealDto(x.m.Id, x.l.ProgramId, x.p.NameAr, x.p.NameEn, x.m.NameAr, x.m.NameEn,
                x.l.Slot, x.l.SortOrder, x.l.IsPrimary, x.m.ApprovalStatus)).ToList();
            return new MenuDayDto(day.Id, day.DayNumber, day.NameAr, day.NameEn, readiness.Count > 0 && readiness.All(x => x.IsReady), readiness, mealDtos);
        }).ToList();
        return new MenuCycleVersionDto(version.Id, version.VersionNumber, version.EffectiveFrom, version.Status, version.IsPaused,
            version.ReviewNotes, version.ReviewedAtUtc, dayDtos.Count(x => x.IsReady), dayDtos.Count,
            dayDtos.Count > 0 && dayDtos.All(x => x.IsReady), dayDtos);
    }

    private async Task<List<MenuDayDto>> BuildWeekdayDtos(MenuPlanVersion version, Guid restaurantId, CancellationToken ct)
    {
        var programIds = await _db.MenuPlanPrograms.AsNoTracking().Where(x => x.MenuPlanId == version.MenuPlanId).Select(x => x.ProgramId).Distinct().ToListAsync(ct);
        var programs = await _db.Programs.AsNoTracking().Where(x => programIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var requirements = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => programIds.Contains(x.ProgramId) && x.IsRequired).ToListAsync(ct);
        var assignments = await _db.WeekdayMealAssignments.AsNoTracking().Where(x => x.MenuPlanVersionId == version.Id)
            .Join(_db.Meals.AsNoTracking(), x => x.MealId, x => x.Id, (link, meal) => new { link, meal }).ToListAsync(ct);
        var readyMealIds = await GetOperationallyReadyMealIds(assignments.Select(x => x.meal.Id).Distinct().ToList(), ct);
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking().Where(x => x.RestaurantId == restaurantId).ToListAsync(ct);
        var workingDays = schedules.Count == 0 ? Enum.GetValues<DayOfWeek>().ToList() : schedules.Where(x => x.IsWorkingDay).Select(x => x.DayOfWeek).Distinct().ToList();
        var result = new List<MenuDayDto>();
        foreach (var weekday in workingDays.OrderBy(x => x))
        {
            var readiness = new List<MenuProgramReadinessDto>();
            foreach (var programId in programIds)
            {
                var required = requirements.Where(x => x.ProgramId == programId).Select(x => x.Slot).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var approved = assignments.Where(x => x.link.DayOfWeek == weekday && x.link.ProgramId == programId && readyMealIds.Contains(x.meal.Id)).ToList();
                var completed = required.Count(slot => approved.Any(x => string.Equals(x.link.Slot, slot, StringComparison.OrdinalIgnoreCase)));
                programs.TryGetValue(programId, out var program);
                readiness.Add(new MenuProgramReadinessDto(programId, program?.NameAr ?? programId.ToString(), program?.NameEn ?? programId.ToString(), completed, required.Count, approved.Count, required.Count > 0 && completed == required.Count, required.Where(slot => approved.All(x => !string.Equals(x.link.Slot, slot, StringComparison.OrdinalIgnoreCase))).ToList()));
            }
            var meals = assignments.Where(x => x.link.DayOfWeek == weekday).Select(x => new MenuCycleMealDto(x.meal.Id, x.link.ProgramId, programs.GetValueOrDefault(x.link.ProgramId)?.NameAr ?? "", programs.GetValueOrDefault(x.link.ProgramId)?.NameEn ?? "", x.meal.NameAr, x.meal.NameEn, x.link.Slot, x.link.SortOrder, x.link.IsPrimary, x.meal.ApprovalStatus)).ToList();
            result.Add(new MenuDayDto(Guid.Empty, (int)weekday + 1, weekday.ToString(), weekday.ToString(), readiness.Count > 0 && readiness.All(x => x.IsReady), readiness, meals));
        }
        return result;
    }

    private async Task<MenuDayDto> BuildDayDto(Guid dayId, Guid restaurantId, CancellationToken ct)
    {
        var day = await _db.MenuDays.AsNoTracking().Include(x => x.MenuPlanVersion).FirstAsync(x => x.Id == dayId, ct);
        var links = await _db.MenuDayMeals.AsNoTracking().Where(x => x.MenuDayId == dayId)
            .Join(_db.Meals.AsNoTracking(), l => l.MealId, m => m.Id, (l, m) => new { l, m })
            .Join(_db.Programs.AsNoTracking(), x => x.l.ProgramId, p => p.Id, (x, p) => new { x.l, x.m, p })
            .OrderBy(x => x.l.ProgramId).ThenBy(x => x.l.Slot).ThenBy(x => x.l.SortOrder).ToListAsync(ct);
        var readyMealIds = await GetOperationallyReadyMealIds(links.Select(x => x.m.Id).Distinct().ToList(), ct);
        var activeProgramIds = await _db.MenuPlanPrograms.AsNoTracking()
            .Where(x => x.MenuPlanId == day.MenuPlanVersion.MenuPlanId && !x.IsDeleted && (x.EffectiveTo == null || x.EffectiveTo >= Today))
            .Select(x => x.ProgramId).Distinct().ToListAsync(ct);
        if (activeProgramIds.Count == 0)
            activeProgramIds = await _db.RestaurantProgramBundleOffers.AsNoTracking().Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => x.ProgramId).Distinct().ToListAsync(ct);
        if (activeProgramIds.Count == 0) activeProgramIds = links.Select(x => x.l.ProgramId).Distinct().ToList();
        var requirements = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => activeProgramIds.Contains(x.ProgramId) && x.IsRequired).ToListAsync(ct);
        var programs = new List<MenuProgramReadinessDto>();
        foreach (var programId in activeProgramIds)
        {
            var program = await _db.Programs.AsNoTracking().FirstAsync(x => x.Id == programId, ct);
            var required = requirements.Where(x => x.ProgramId == programId).Select(x => x.Slot).ToList();
            if (required.Count == 0) required = MenuCycleRules.Slots.ToList();
            var approved = links.Where(x => x.l.ProgramId == programId && readyMealIds.Contains(x.m.Id)).ToList();
            var completed = required.Count(slot => approved.Any(x => x.l.Slot == slot));
            programs.Add(new MenuProgramReadinessDto(programId, program.NameAr, program.NameEn, completed, required.Count,
                approved.Count, completed == required.Count, required.Where(slot => approved.All(x => x.l.Slot != slot)).ToList()));
        }
        var mealDtos = links.Select(x => new MenuCycleMealDto(x.m.Id, x.l.ProgramId, x.p.NameAr, x.p.NameEn, x.m.NameAr, x.m.NameEn,
            x.l.Slot, x.l.SortOrder, x.l.IsPrimary, x.m.ApprovalStatus)).ToList();
        return new MenuDayDto(day.Id, day.DayNumber, day.NameAr, day.NameEn, programs.Count > 0 && programs.All(x => x.IsReady), programs, mealDtos);
    }

    private async Task<HashSet<Guid>> GetOperationallyReadyMealIds(IReadOnlyList<Guid> mealIds, CancellationToken ct)
    {
        if (mealIds.Count == 0) return [];
        var meals = await _db.Meals.AsNoTracking()
            .Include(x => x.Images.Where(image => !image.IsDeleted))
            .Include(x => x.Ingredients.Where(link => !link.IsDeleted)).ThenInclude(x => x.Ingredient)
            .Where(x => mealIds.Contains(x.Id)).ToListAsync(ct);
        var programIds = meals.Where(x => x.ProgramId.HasValue).Select(x => x.ProgramId!.Value).Distinct().ToList();
        var standards = await _db.ProgramNutritionStandards.AsNoTracking().Where(x => programIds.Contains(x.ProgramId) && x.IsActive).ToListAsync(ct);
        var programs = await _db.Programs.AsNoTracking().Where(x => programIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var allergenDefinitions = await _db.AllergenDefinitions.AsNoTracking().Where(x => x.IsActive).Select(x => x.Code).ToListAsync(ct);
        var ready = new HashSet<Guid>();
        foreach (var meal in meals)
        {
            if (meal.ProgramId is not { } programId || meal.ApprovalStatus != VerificationStatus.Approved || !meal.IsAvailableForNewOrders
                || string.IsNullOrWhiteSpace(meal.DescriptionAr) || string.IsNullOrWhiteSpace(meal.DescriptionEn)
                || meal.Images.Count == 0 || meal.Ingredients.Count == 0
                || meal.Ingredients.Any(x => x.Ingredient.RestaurantId != meal.RestaurantId || !x.Ingredient.IsActive || string.IsNullOrWhiteSpace(x.Ingredient.OriginCountryCode))
                || meal.Calories is null || meal.ProteinG is null || meal.CarbsG is null || meal.FatG is null
                || !meal.AllergensReviewed || !meal.DataResponsibilityConfirmed)
                continue;
            var allergenCodes = FoodCodeNormalizer.Split(meal.PresentAllergenCodes).Concat(FoodCodeNormalizer.Split(meal.MayContainAllergenCodes)).Concat(meal.Ingredients.SelectMany(x => FoodCodeNormalizer.Split(x.Ingredient.AllergenCodes))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allergenCodes.Any(code => !allergenDefinitions.Contains(code))) continue;
            var standard = standards.FirstOrDefault(x => x.ProgramId == programId && x.MealCategory == NutritionMealCategories.FromSlot(meal.Slot));
            if (standard is null || MealNutritionStandardValidator.Evaluate(standard, programs.GetValueOrDefault(programId)?.ShortCode ?? programs.GetValueOrDefault(programId)?.Code,
                meal.Calories, meal.ProteinG, meal.CarbsG, meal.NetCarbsG, meal.FatG, meal.FiberG).Count > 0) continue;
            ready.Add(meal.Id);
        }
        return ready;
    }

    private async Task<IReadOnlyList<MenuCyclePreviewDayDto>> BuildPreview(MenuPlan cycle, Guid restaurantId, DateOnly from, int days, bool approvedOnly, CancellationToken ct)
    {
        var to = from.AddDays(days - 1);
        if (from <= Today && Today <= to)
            await TryApplyPendingSequencesForRestaurantAsync(cycle, restaurantId, ct);

        var toFinal = from.AddDays(days - 1);
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking().Where(x => x.RestaurantId == restaurantId).ToListAsync(ct);
        // Reload versions/days after possible apply
        var freshCycle = await _db.MenuPlans.AsNoTracking()
            .Include(x => x.Versions).ThenInclude(x => x.Days)
            .FirstAsync(x => x.Id == cycle.Id, ct);
        var versions = freshCycle.Versions.Where(x => !approvedOnly || x.Status == VerificationStatus.Approved).Where(x => !x.IsPaused).OrderBy(x => x.EffectiveFrom).ToList();
        var exceptionFrom = versions.Count == 0 ? from : versions.Min(x => x.EffectiveFrom);
        var exceptions = await _db.RestaurantOperatingDayExceptions.AsNoTracking().Where(x => x.RestaurantId == restaurantId && x.Date >= exceptionFrom && x.Date <= toFinal).ToListAsync(ct);
        var result = new List<MenuCyclePreviewDayDto>();
        for (var date = from; date <= toFinal; date = date.AddDays(1))
        {
            var version = versions.LastOrDefault(x => x.EffectiveFrom <= date);
            var exception = exceptions.FirstOrDefault(x => x.Date == date);
            var schedule = schedules.FirstOrDefault(x => x.DayOfWeek == date.DayOfWeek);
            var working = exception?.IsWorkingDay ?? (schedules.Count == 0 || schedule?.IsWorkingDay == true);
            if (!working || version is null)
            {
                result.Add(new(date, working, exception?.Reason, version?.Id, null, null, null, null)); continue;
            }
            var activeOrdered = version.Days.Where(x => !x.IsDeleted && x.IsActiveInCycle).OrderBy(x => x.DayNumber).ToList();
            var workingIndex = 0;
            for (var cursor = version.EffectiveFrom; cursor < date; cursor = cursor.AddDays(1))
            {
                var ex = exceptions.FirstOrDefault(x => x.Date == cursor);
                var sch = schedules.FirstOrDefault(x => x.DayOfWeek == cursor.DayOfWeek);
                if (ex?.IsWorkingDay ?? (schedules.Count == 0 || sch?.IsWorkingDay == true)) workingIndex++;
            }
            if (activeOrdered.Count == 0) { result.Add(new(date, true, null, version.Id, null, null, null, null)); continue; }
            var pendingIds = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
            var dayId = MenuCyclePendingSequence.PickDayIdForWorkingIndex(
                activeOrdered.Select(x => x.Id).ToList(),
                pendingIds,
                workingIndex);
            var menuDay = version.Days.FirstOrDefault(x => x.Id == dayId) ?? activeOrdered[workingIndex % activeOrdered.Count];
            var displayNumber = menuDay.DayNumber;
            if (pendingIds.Count > 0 && workingIndex >= activeOrdered.Count)
            {
                var pendingIndex = workingIndex - activeOrdered.Count;
                displayNumber = (pendingIndex % pendingIds.Count) + 1;
            }
            result.Add(new(date, true, null, version.Id, menuDay.Id, displayNumber, menuDay.NameAr, menuDay.NameEn));
        }
        return result;
    }

    private async Task TryApplyPendingSequencesForRestaurantAsync(MenuPlan cycle, Guid restaurantId, CancellationToken ct)
    {
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking().Where(x => x.RestaurantId == restaurantId).ToListAsync(ct);
        var exceptions = await _db.RestaurantOperatingDayExceptions.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.Date >= Today.AddDays(-90) && x.Date <= Today)
            .ToListAsync(ct);

        var versionIds = cycle.Versions
            .Where(x => x.Status == VerificationStatus.Approved && !x.IsPaused && x.EffectiveFrom <= Today)
            .Select(x => x.Id)
            .Distinct()
            .ToList();

        foreach (var versionId in versionIds)
        {
            var version = await _db.MenuPlanVersions
                .Include(x => x.Days)
                .FirstOrDefaultAsync(x => x.Id == versionId, ct);
            if (version is null) continue;
            var pendingIds = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
            if (pendingIds.Count == 0) continue;

            var activeCount = version.Days.Count(x => !x.IsDeleted && x.IsActiveInCycle);
            if (activeCount == 0) continue;
            if (!IsWorkingDate(Today, schedules, exceptions)) continue;

            var workingIndex = 0;
            for (var cursor = version.EffectiveFrom; cursor < Today; cursor = cursor.AddDays(1))
            {
                if (IsWorkingDate(cursor, schedules, exceptions)) workingIndex++;
            }

            if (!MenuCyclePendingSequence.ShouldActivate(workingIndex - 1, activeCount))
                continue;

            var dayById = version.Days.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
            var number = 1;
            foreach (var id in pendingIds)
            {
                if (!dayById.TryGetValue(id, out var day)) continue;
                day.IsActiveInCycle = true;
                day.DayNumber = number++;
                day.ModifiedAtUtc = DateTime.UtcNow;
            }

            // Soft-delete active days that were dropped from pending (should not happen for swap/insert flows)
            foreach (var day in version.Days.Where(x => !x.IsDeleted && !pendingIds.Contains(x.Id)))
            {
                if (!day.IsActiveInCycle) continue;
                // Keep orphan actives out of rotation by deactivating if not in pending — should not occur for seed+edit flows
                day.IsActiveInCycle = false;
                day.ModifiedAtUtc = DateTime.UtcNow;
            }

            version.PendingSequenceJson = null;
            version.ModifiedAtUtc = DateTime.UtcNow;
            AddAudit(version, "sequence_pending_activated");
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<MenuPlan?> LoadOwnedCycle(Guid id, Guid restaurantId, CancellationToken ct) => await _db.MenuPlans
        .Include(x => x.Programs)
        .Include(x => x.Versions).ThenInclude(x => x.Days).ThenInclude(x => x.Meals).ThenInclude(x => x.Meal)
        .Include(x => x.Versions).ThenInclude(x => x.WeekdayAssignments)
        .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId && !x.IsTemplate && !x.IsDeleted, ct);
    private async Task<MenuDay?> LoadEditableDay(Guid id, Guid restaurantId, CancellationToken ct)
    {
        var editableStatuses = new[] { VerificationStatus.Draft, VerificationStatus.Rejected, VerificationStatus.NeedsChanges, VerificationStatus.ResubmissionRequired };
        return await _db.MenuDays.Include(x => x.Meals).Include(x => x.MenuPlanVersion).ThenInclude(x => x.MenuPlan)
            .FirstOrDefaultAsync(x => x.Id == id && x.MenuPlanVersion.MenuPlan.RestaurantId == restaurantId && editableStatuses.Contains(x.MenuPlanVersion.Status), ct);
    }

    private static MenuPlan NewCycle(Guid? restaurantId, string ar, string en, MenuPlanSource source, bool template, ScheduleMode mode = ScheduleMode.Cycle) => new()
    { RestaurantId = restaurantId, NameAr = ar.Trim(), NameEn = en.Trim(), Source = source, IsTemplate = template, Mode = mode, CreatedAtUtc = DateTime.UtcNow };

    private async Task<bool> HasActiveProgramOverlap(Guid restaurantId, IReadOnlyList<Guid> programIds, DateOnly from, Guid? excludePlanId, CancellationToken ct)
    {
        return await _db.MenuPlanPrograms.AsNoTracking()
            .Where(x => !x.IsDeleted && programIds.Contains(x.ProgramId) && x.EffectiveFrom <= from && (x.EffectiveTo == null || x.EffectiveTo >= from))
            .Join(_db.MenuPlans.AsNoTracking().Where(p => p.RestaurantId == restaurantId && !p.IsTemplate && !p.IsDeleted && (excludePlanId == null || p.Id != excludePlanId)), link => link.MenuPlanId, plan => plan.Id, (link, plan) => link)
            .Join(_db.MenuPlanVersions.AsNoTracking().Where(v => v.Status == VerificationStatus.Approved && !v.IsPaused), link => link.MenuPlanId, version => version.MenuPlanId, (link, version) => link)
            .AnyAsync(ct);
    }
    private static MenuPlanVersion NewVersion(MenuPlan cycle, int number, DateOnly effective, IReadOnlyList<MenuCycleDayInput> days)
    {
        var version = new MenuPlanVersion { MenuPlan = cycle, VersionNumber = number, EffectiveFrom = effective, Status = VerificationStatus.Draft, CreatedAtUtc = DateTime.UtcNow };
        foreach (var day in days.OrderBy(x => x.DayNumber)) version.Days.Add(new MenuDay { MenuPlanVersion = version, DayNumber = day.DayNumber, NameAr = day.NameAr.Trim(), NameEn = day.NameEn.Trim(), CreatedAtUtc = DateTime.UtcNow });
        cycle.Versions.Add(version); return version;
    }

    private static IReadOnlyList<MenuCycleDayInput> DefaultWeekdayDays() =>
    [
        new(1, "السبت", "Saturday"),
        new(2, "الأحد", "Sunday"),
        new(3, "الاثنين", "Monday"),
        new(4, "الثلاثاء", "Tuesday"),
        new(5, "الأربعاء", "Wednesday"),
        new(6, "الخميس", "Thursday"),
    ];

    private async Task EnsureWeekdayMenuDaysAsync(Guid versionId, CancellationToken ct)
    {
        var existing = await _db.MenuDays.IgnoreQueryFilters()
            .Where(x => x.MenuPlanVersionId == versionId)
            .ToListAsync(ct);
        var changed = false;
        foreach (var deleted in existing.Where(x => x.IsDeleted))
        {
            deleted.IsDeleted = false;
            deleted.DeletedAtUtc = null;
            deleted.DeletedByUserId = null;
            deleted.ModifiedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        var existingNumbers = existing.Select(x => x.DayNumber).ToHashSet();
        foreach (var day in DefaultWeekdayDays())
        {
            if (!existingNumbers.Add(day.DayNumber)) continue;
            _db.MenuDays.Add(new MenuDay
            {
                MenuPlanVersionId = versionId,
                DayNumber = day.DayNumber,
                NameAr = day.NameAr,
                NameEn = day.NameEn,
                CreatedAtUtc = DateTime.UtcNow,
            });
            changed = true;
        }

        if (changed) await _db.SaveChangesAsync(ct);

        var days = await _db.MenuDays.Where(x => x.MenuPlanVersionId == versionId && !x.IsDeleted).ToListAsync(ct);
        var dayByNumber = days.ToDictionary(x => x.DayNumber);
        var assignments = await _db.WeekdayMealAssignments.AsNoTracking()
            .Where(x => x.MenuPlanVersionId == versionId && !x.IsDeleted)
            .ToListAsync(ct);
        if (assignments.Count == 0) return;

        var dayIds = days.Select(x => x.Id).ToList();
        var existingLinks = await _db.MenuDayMeals
            .Where(x => dayIds.Contains(x.MenuDayId) && !x.IsDeleted)
            .Select(x => new { x.MenuDayId, x.ProgramId, x.MealId, x.Slot, x.IsPrimary })
            .ToListAsync(ct);
        var linked = existingLinks.Select(x => (x.MenuDayId, x.ProgramId, x.MealId)).ToHashSet();
        var primaryUsed = existingLinks.Where(x => x.IsPrimary)
            .Select(x => (x.MenuDayId, x.ProgramId, Slot: x.Slot.ToLowerInvariant()))
            .ToHashSet();
        var added = false;
        foreach (var assignment in assignments)
        {
            if (WeekdayDayNumber(assignment.DayOfWeek) is not { } dayNumber) continue;
            if (!dayByNumber.TryGetValue(dayNumber, out var day)) continue;
            if (!linked.Add((day.Id, assignment.ProgramId, assignment.MealId))) continue;
            var slotKey = assignment.Slot.ToLowerInvariant();
            var isPrimary = assignment.IsPrimary && primaryUsed.Add((day.Id, assignment.ProgramId, slotKey));
            _db.MenuDayMeals.Add(new MenuDayMeal
            {
                MenuDayId = day.Id,
                MealId = assignment.MealId,
                ProgramId = assignment.ProgramId,
                Slot = assignment.Slot,
                SortOrder = assignment.SortOrder,
                IsPrimary = isPrimary,
                CreatedAtUtc = DateTime.UtcNow,
            });
            added = true;
        }

        if (added) await _db.SaveChangesAsync(ct);
    }

    private static int? WeekdayDayNumber(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => 1,
        DayOfWeek.Sunday => 2,
        DayOfWeek.Monday => 3,
        DayOfWeek.Tuesday => 4,
        DayOfWeek.Wednesday => 5,
        DayOfWeek.Thursday => 6,
        _ => null,
    };
    private static MenuDayMeal NewLink(MenuDay day, Meal meal, int sort, bool primary) => new()
    { MenuDay = day, Meal = meal, ProgramId = meal.ProgramId!.Value, Slot = meal.Slot!, SortOrder = sort, IsPrimary = primary, CreatedAtUtc = DateTime.UtcNow };
    private void AddAudit(MenuPlanVersion version, string action, string? notes = null) => _db.MenuPlanAudits.Add(new MenuPlanAudit
    { MenuPlanVersionId = version.Id, Action = action, Notes = notes?.Trim(), PerformedByUserId = _currentUser?.UserId, CreatedAtUtc = DateTime.UtcNow });
    private static Error? ValidateDays(IReadOnlyList<MenuCycleDayInput> days) => days.Count is < 1 or > 60 || days.Any(x => x.DayNumber < 1 || string.IsNullOrWhiteSpace(x.NameAr) || string.IsNullOrWhiteSpace(x.NameEn)) || !days.Select(x => x.DayNumber).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, days.Count))
        ? Error.Validation("menu_cycle.days_invalid", "Cycle days must be unique, named, and between 1 and 60.") : null;
    private async Task CloneVersionMealLinksAsync(Guid sourceVersionId, MenuPlanVersion targetVersion, CancellationToken ct)
    {
        var sourceDays = await _db.MenuDays.AsNoTracking()
            .Where(x => x.MenuPlanVersionId == sourceVersionId && !x.IsDeleted)
            .Select(x => new { x.Id, x.DayNumber })
            .ToListAsync(ct);
        if (sourceDays.Count == 0) return;

        var dayNumberBySourceId = sourceDays.ToDictionary(x => x.Id, x => x.DayNumber);
        var targetDayByNumber = targetVersion.Days.ToDictionary(x => x.DayNumber);
        var sourceDayIds = sourceDays.Select(x => x.Id).ToList();
        var links = await _db.MenuDayMeals.AsNoTracking()
            .Where(x => sourceDayIds.Contains(x.MenuDayId) && !x.IsDeleted)
            .Select(x => new { x.MenuDayId, x.MealId, x.ProgramId, x.Slot, x.SortOrder, x.IsPrimary })
            .ToListAsync(ct);

        foreach (var link in links)
        {
            if (!dayNumberBySourceId.TryGetValue(link.MenuDayId, out var dayNumber)) continue;
            if (!targetDayByNumber.TryGetValue(dayNumber, out var targetDay)) continue;
            _db.MenuDayMeals.Add(new MenuDayMeal
            {
                MenuDay = targetDay,
                MealId = link.MealId,
                ProgramId = link.ProgramId,
                Slot = link.Slot,
                SortOrder = link.SortOrder,
                IsPrimary = link.IsPrimary,
            });
        }
    }

    private async Task CloneWeekdayAssignmentsAsync(Guid sourceVersionId, MenuPlanVersion targetVersion, CancellationToken ct)
    {
        var assignments = await _db.WeekdayMealAssignments.AsNoTracking()
            .Where(x => x.MenuPlanVersionId == sourceVersionId && !x.IsDeleted)
            .Select(x => new { x.DayOfWeek, x.ProgramId, x.MealId, x.Slot, x.SortOrder, x.IsPrimary })
            .ToListAsync(ct);
        foreach (var source in assignments)
            targetVersion.WeekdayAssignments.Add(new WeekdayMealAssignment
            {
                MenuPlanVersion = targetVersion,
                DayOfWeek = source.DayOfWeek,
                ProgramId = source.ProgramId,
                MealId = source.MealId,
                Slot = source.Slot,
                SortOrder = source.SortOrder,
                IsPrimary = source.IsPrimary,
            });
    }

    private async Task ReplaceDaysAsync(MenuPlanVersion version, IReadOnlyList<MenuCycleDayInput> inputs, CancellationToken ct)
    {
        var requestedNumbers = inputs.Select(x => x.DayNumber).ToHashSet();
        var allDays = await _db.MenuDays.IgnoreQueryFilters()
            .Where(x => x.MenuPlanVersionId == version.Id)
            .ToListAsync(ct);

        var removedDayIds = allDays.Where(x => !x.IsDeleted && !requestedNumbers.Contains(x.DayNumber)).Select(x => x.Id).ToList();
        if (removedDayIds.Count > 0)
        {
            var meals = await _db.MenuDayMeals
                .Where(x => removedDayIds.Contains(x.MenuDayId) && !x.IsDeleted)
                .ToListAsync(ct);
            foreach (var meal in meals)
                _db.MenuDayMeals.Remove(meal);
            foreach (var old in allDays.Where(x => removedDayIds.Contains(x.Id)))
                _db.MenuDays.Remove(old);
        }

        foreach (var input in inputs.OrderBy(x => x.DayNumber))
        {
            var nameAr = input.NameAr.Trim();
            var nameEn = input.NameEn.Trim();
            var existing = allDays.FirstOrDefault(x => x.DayNumber == input.DayNumber);
            if (existing is null)
            {
                _db.MenuDays.Add(new MenuDay
                {
                    MenuPlanVersionId = version.Id,
                    DayNumber = input.DayNumber,
                    NameAr = nameAr,
                    NameEn = nameEn,
                });
                continue;
            }

            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                existing.DeletedAtUtc = null;
                existing.DeletedByUserId = null;
            }

            if (existing.NameAr != nameAr || existing.NameEn != nameEn)
            {
                existing.NameAr = nameAr;
                existing.NameEn = nameEn;
                existing.ModifiedAtUtc = DateTime.UtcNow;
            }
        }
    }
    private static MenuCycleVersionDto EmptyVersion(MenuPlanVersion v) => new(v.Id, v.VersionNumber, v.EffectiveFrom, v.Status, v.IsPaused, v.ReviewNotes, v.ReviewedAtUtc, 0, v.Days.Count, false,
        v.Days.OrderBy(x => x.DayNumber).Select(x => new MenuDayDto(x.Id, x.DayNumber, x.NameAr, x.NameEn, false, [], [])).ToList());
    private static Error NotFound(Guid id) => Error.NotFound("menu_cycle.not_found", $"Menu cycle '{id}' was not found.");
    private static Error NotEditable() => Error.Conflict("menu_cycle.not_editable", "This version cannot be edited. Create a future version instead.");
}
