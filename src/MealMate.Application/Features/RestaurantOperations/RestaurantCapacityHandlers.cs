using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOperations;

public sealed class GetRestaurantCapacityOverviewQueryHandler : IQueryHandler<GetRestaurantCapacityOverviewQuery, RestaurantCapacityOverviewDto>
{
    private readonly RestaurantCapacityService _service;

    public GetRestaurantCapacityOverviewQueryHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<RestaurantCapacityOverviewDto>> Handle(GetRestaurantCapacityOverviewQuery request, CancellationToken cancellationToken)
        => _service.GetCapacityOverviewAsync(request, cancellationToken);
}

public sealed class UpdateBaseCapacityCommandHandler : ICommandHandler<UpdateBaseCapacityCommand, RestaurantCapacityOverviewDto>
{
    private readonly RestaurantCapacityService _service;

    public UpdateBaseCapacityCommandHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<RestaurantCapacityOverviewDto>> Handle(UpdateBaseCapacityCommand request, CancellationToken cancellationToken)
        => _service.UpdateBaseCapacityAsync(request, cancellationToken);
}

public sealed class UpdateDateCapacityCommandHandler : ICommandHandler<UpdateDateCapacityCommand, CapacityDayDto>
{
    private readonly RestaurantCapacityService _service;

    public UpdateDateCapacityCommandHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<CapacityDayDto>> Handle(UpdateDateCapacityCommand request, CancellationToken cancellationToken)
        => _service.UpdateDateCapacityAsync(request, cancellationToken);
}

public sealed class SetDateManualBusyCommandHandler : ICommandHandler<SetDateManualBusyCommand, CapacityDayDto>
{
    private readonly RestaurantCapacityService _service;

    public SetDateManualBusyCommandHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<CapacityDayDto>> Handle(SetDateManualBusyCommand request, CancellationToken cancellationToken)
        => _service.SetDateManualBusyAsync(request, cancellationToken);
}

public sealed class GetWorkingDaysScheduleQueryHandler : IQueryHandler<GetWorkingDaysScheduleQuery, WorkingDaysScheduleDto>
{
    private readonly RestaurantCapacityService _service;

    public GetWorkingDaysScheduleQueryHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<WorkingDaysScheduleDto>> Handle(GetWorkingDaysScheduleQuery request, CancellationToken cancellationToken)
        => _service.GetWorkingDaysScheduleAsync(request, cancellationToken);
}

public sealed class UpdateWorkingDaysScheduleCommandHandler : ICommandHandler<UpdateWorkingDaysScheduleCommand, WorkingDaysScheduleDto>
{
    private readonly RestaurantCapacityService _service;

    public UpdateWorkingDaysScheduleCommandHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<WorkingDaysScheduleDto>> Handle(UpdateWorkingDaysScheduleCommand request, CancellationToken cancellationToken)
        => _service.UpdateWorkingDaysScheduleAsync(request, cancellationToken);
}

public sealed class AddWorkingDayExceptionCommandHandler : ICommandHandler<AddWorkingDayExceptionCommand, WorkingDayExceptionDto>
{
    private readonly RestaurantCapacityService _service;

    public AddWorkingDayExceptionCommandHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<WorkingDayExceptionDto>> Handle(AddWorkingDayExceptionCommand request, CancellationToken cancellationToken)
        => _service.AddWorkingDayExceptionAsync(request, cancellationToken);
}

public sealed class DeleteWorkingDayExceptionCommandHandler : ICommandHandler<DeleteWorkingDayExceptionCommand, bool>
{
    private readonly RestaurantCapacityService _service;

    public DeleteWorkingDayExceptionCommandHandler(RestaurantCapacityService service)
        => _service = service;

    public Task<Result<bool>> Handle(DeleteWorkingDayExceptionCommand request, CancellationToken cancellationToken)
        => _service.DeleteWorkingDayExceptionAsync(request, cancellationToken);
}

public sealed class RestaurantCapacityService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RestaurantCapacityService(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<RestaurantCapacityOverviewDto>> GetCapacityOverviewAsync(
        GetRestaurantCapacityOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<RestaurantCapacityOverviewDto>(restaurantIdResult.Error);

        var restaurantId = restaurantIdResult.Value;
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var fromDate = request.FromDate ?? today;
        var toDate = request.ToDate ?? today.AddDays(13);

        var overview = await BuildCapacityOverviewAsync(restaurantId, fromDate, toDate, cancellationToken);
        return Result.Success(overview);
    }

    public async Task<Result<RestaurantCapacityOverviewDto>> UpdateBaseCapacityAsync(
        UpdateBaseCapacityCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<RestaurantCapacityOverviewDto>(restaurantIdResult.Error);

        var restaurantId = restaurantIdResult.Value;
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var currentBaseCapacity = await GetCurrentBaseCapacityAsync(restaurantId, today, cancellationToken);

        if (request.NewCapacity < currentBaseCapacity)
        {
            return Result.Failure<RestaurantCapacityOverviewDto>(Error.ValidationWithArguments(
                MessageCodes.RestaurantOperations.CapacityCannotReduce,
                "Capacity cannot be reduced below the current limit.",
                currentBaseCapacity));
        }

        var existingCaps = await _db.DailyCapacities
            .Where(x => x.RestaurantId == restaurantId && x.CapacityType == CapacityType.Base && x.ServiceDate >= today)
            .ToListAsync(cancellationToken);

        if (existingCaps.Count == 0)
        {
            _db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = restaurantId,
                ServiceDate = today,
                CapacityType = CapacityType.Base,
                MaxBoxes = request.NewCapacity,
                ReservedBoxes = 0
            });
        }
        else
        {
            foreach (var cap in existingCaps)
            {
                cap.MaxBoxes = request.NewCapacity;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var overview = await BuildCapacityOverviewAsync(restaurantId, today, today.AddDays(13), cancellationToken);
        return Result.Success(overview);
    }

    public async Task<Result<CapacityDayDto>> UpdateDateCapacityAsync(
        UpdateDateCapacityCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<CapacityDayDto>(restaurantIdResult.Error);

        var restaurantId = restaurantIdResult.Value;
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        if (request.Date < today)
            return Result.Failure<CapacityDayDto>(Error.Validation("capacity.date_in_past", "Capacity cannot be changed for a past date."));

        var baseCapacity = await GetCurrentBaseCapacityAsync(restaurantId, request.Date, cancellationToken);
        var confirmedMeals = await CountConfirmedMealsForDateAsync(restaurantId, request.Date, cancellationToken);
        var minimumCapacity = Math.Max(baseCapacity, confirmedMeals);
        if (request.NewCapacity < minimumCapacity)
        {
            return Result.Failure<CapacityDayDto>(Error.ValidationWithArguments(
                MessageCodes.RestaurantOperations.CapacityCannotReduce,
                "Capacity cannot be reduced below the current limit.",
                minimumCapacity));
        }

        var dateCapacity = await _db.DailyCapacities
            .FirstOrDefaultAsync(x =>
                x.RestaurantId == restaurantId &&
                x.ServiceDate == request.Date &&
                x.CapacityType == CapacityType.DateOverride &&
                x.CampaignId == null,
                cancellationToken);

        if (dateCapacity == null)
        {
            _db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = restaurantId,
                ServiceDate = request.Date,
                CapacityType = CapacityType.DateOverride,
                MaxBoxes = request.NewCapacity,
                ReservedBoxes = confirmedMeals
            });
        }
        else
        {
            dateCapacity.MaxBoxes = request.NewCapacity;
            dateCapacity.ReservedBoxes = confirmedMeals;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var overview = await BuildCapacityOverviewAsync(restaurantId, request.Date, request.Date, cancellationToken);
        var day = overview.Days.SingleOrDefault();
        return day == null
            ? Result.Failure<CapacityDayDto>(Error.NotFound("capacity.day_not_found", "Failed to resolve updated day status."))
            : Result.Success(day);
    }

    public async Task<Result<CapacityDayDto>> SetDateManualBusyAsync(
        SetDateManualBusyCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<CapacityDayDto>(restaurantIdResult.Error);

        var restaurantId = restaurantIdResult.Value;

        var existingBusy = await _db.BusyStatuses
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.StartsAtUtc.Date == request.Date.ToDateTime(TimeOnly.MinValue).Date, cancellationToken);

        if (request.IsManualBusy)
        {
            if (existingBusy == null)
            {
                _db.BusyStatuses.Add(new BusyStatus
                {
                    RestaurantId = restaurantId,
                    IsBusy = true,
                    Reason = request.Reason ?? "Manual Busy toggle",
                    StartsAtUtc = request.Date.ToDateTime(TimeOnly.MinValue),
                    EndsAtUtc = request.Date.ToDateTime(TimeOnly.MaxValue)
                });
            }
            else
            {
                existingBusy.IsBusy = true;
                existingBusy.Reason = request.Reason ?? existingBusy.Reason;
            }
        }
        else
        {
            if (existingBusy != null)
            {
                existingBusy.IsBusy = false;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var overview = await BuildCapacityOverviewAsync(restaurantId, request.Date, request.Date, cancellationToken);
        var dayDto = overview.Days.FirstOrDefault(x => x.DateIso == request.Date.ToString("yyyy-MM-dd"));
        if (dayDto == null)
            return Result.Failure<CapacityDayDto>(Error.NotFound("capacity.day_not_found", "Failed to resolve updated day status."));

        return Result.Success(dayDto);
    }

    public async Task<Result<WorkingDaysScheduleDto>> GetWorkingDaysScheduleAsync(
        GetWorkingDaysScheduleQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<WorkingDaysScheduleDto>(restaurantIdResult.Error);

        var schedule = await GetWorkingDaysScheduleDtoAsync(restaurantIdResult.Value, cancellationToken);
        return Result.Success(schedule);
    }

    public async Task<Result<WorkingDaysScheduleDto>> UpdateWorkingDaysScheduleAsync(
        UpdateWorkingDaysScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<WorkingDaysScheduleDto>(restaurantIdResult.Error);

        var restaurantId = restaurantIdResult.Value;

        var existingSchedules = await _db.RestaurantOperatingSchedules
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(cancellationToken);

        foreach (var item in request.Schedule)
        {
            var dayOfWeek = (DayOfWeek)item.DayOfWeek;
            var existing = existingSchedules.FirstOrDefault(x => x.DayOfWeek == dayOfWeek);
            if (existing != null)
            {
                existing.IsWorkingDay = item.IsWorkingDay;
            }
            else
            {
                _db.RestaurantOperatingSchedules.Add(new RestaurantOperatingSchedule
                {
                    RestaurantId = restaurantId,
                    DayOfWeek = dayOfWeek,
                    IsWorkingDay = item.IsWorkingDay
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var resultSchedule = await GetWorkingDaysScheduleDtoAsync(restaurantId, cancellationToken);
        return Result.Success(resultSchedule);
    }

    public async Task<Result<WorkingDayExceptionDto>> AddWorkingDayExceptionAsync(
        AddWorkingDayExceptionCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<WorkingDayExceptionDto>(restaurantIdResult.Error);

        var restaurantId = restaurantIdResult.Value;

        if (!request.IsWorkingDay)
        {
            var confirmedMealsCount = await CountConfirmedMealsForDateAsync(restaurantId, request.Date, cancellationToken);
            if (confirmedMealsCount > 0)
            {
                return Result.Failure<WorkingDayExceptionDto>(Error.ValidationWithArguments(
                    MessageCodes.RestaurantOperations.ConfirmedMealsExist,
                    "Cannot set this date as a holiday because confirmed meals must be reassigned or processed first.",
                    request.Date.ToString("yyyy-MM-dd"), confirmedMealsCount));
            }
        }

        var existingException = await _db.RestaurantOperatingDayExceptions
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.Date == request.Date, cancellationToken);

        if (existingException != null)
        {
            existingException.IsWorkingDay = request.IsWorkingDay;
            existingException.Reason = request.Reason;
        }
        else
        {
            existingException = new RestaurantOperatingDayException
            {
                RestaurantId = restaurantId,
                Date = request.Date,
                IsWorkingDay = request.IsWorkingDay,
                Reason = request.Reason
            };
            _db.RestaurantOperatingDayExceptions.Add(existingException);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var dto = new WorkingDayExceptionDto(
            existingException.Id,
            existingException.Date.ToString("yyyy-MM-dd"),
            existingException.IsWorkingDay,
            existingException.Reason);

        return Result.Success(dto);
    }

    public async Task<Result<bool>> DeleteWorkingDayExceptionAsync(
        DeleteWorkingDayExceptionCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = ResolveRestaurantId(request.RestaurantId);
        if (restaurantIdResult.IsFailure)
            return Result.Failure<bool>(restaurantIdResult.Error);

        var restaurantId = restaurantIdResult.Value;

        var existingException = await _db.RestaurantOperatingDayExceptions
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.Date == request.Date, cancellationToken);

        if (existingException != null)
        {
            _db.RestaurantOperatingDayExceptions.Remove(existingException);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(true);
    }

    private Result<Guid> ResolveRestaurantId(Guid? overrideRestaurantId = null)
        => RestaurantOperationsScope.ResolveRestaurantId(_currentUser, overrideRestaurantId);

    private async Task<int> GetCurrentBaseCapacityAsync(Guid restaurantId, DateOnly date, CancellationToken cancellationToken)
    {
        var cap = await _db.DailyCapacities
            .Where(x => x.RestaurantId == restaurantId && x.CapacityType == CapacityType.Base && x.ServiceDate <= date)
            .OrderByDescending(x => x.ServiceDate)
            .FirstOrDefaultAsync(cancellationToken);

        return cap?.MaxBoxes ?? 40;
    }

    private async Task<int> CountConfirmedMealsForDateAsync(Guid restaurantId, DateOnly date, CancellationToken cancellationToken)
    {
        var countFromSubDays = await _db.SubscriptionDays
            .CountAsync(x => x.RestaurantId == restaurantId && x.ServiceDate == date && x.Status != SubscriptionDayStatus.Cancelled, cancellationToken);

        var countFromSelections = await _db.DailyRestaurantSelections
            .CountAsync(x => x.RestaurantId == restaurantId && x.SubscriptionDay != null && x.SubscriptionDay.ServiceDate == date, cancellationToken);

        return Math.Max(countFromSubDays, countFromSelections);
    }

    private async Task<Dictionary<DateOnly, int>> GetConfirmedMealsByDateAsync(
        Guid restaurantId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var subscriptionDayCounts = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId &&
                        x.ServiceDate >= fromDate &&
                        x.ServiceDate <= toDate &&
                        x.Status != SubscriptionDayStatus.Cancelled)
            .GroupBy(x => x.ServiceDate)
            .Select(group => new { Date = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count, cancellationToken);

        var selectionCounts = await _db.DailyRestaurantSelections
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId &&
                        x.SubscriptionDay.ServiceDate >= fromDate &&
                        x.SubscriptionDay.ServiceDate <= toDate)
            .GroupBy(x => x.SubscriptionDay.ServiceDate)
            .Select(group => new { Date = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count, cancellationToken);

        foreach (var (date, count) in selectionCounts)
        {
            subscriptionDayCounts[date] = Math.Max(
                subscriptionDayCounts.GetValueOrDefault(date),
                count);
        }

        return subscriptionDayCounts;
    }

    private async Task<WorkingDaysScheduleDto> GetWorkingDaysScheduleDtoAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var schedules = await _db.RestaurantOperatingSchedules
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(cancellationToken);

        var exceptions = await _db.RestaurantOperatingDayExceptions
            .Where(x => x.RestaurantId == restaurantId)
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        var weeklyDays = new List<WeeklyDayScheduleDto>();
        var dayNames = new (string Ar, string En)[]
        {
            ("الأحد", "Sunday"),
            ("الإثنين", "Monday"),
            ("الثلاثاء", "Tuesday"),
            ("الأربعاء", "Wednesday"),
            ("الخميس", "Thursday"),
            ("الجمعة", "Friday"),
            ("السبت", "Saturday")
        };

        for (int i = 0; i <= 6; i++)
        {
            var dayOfWeek = (DayOfWeek)i;
            var match = schedules.FirstOrDefault(x => x.DayOfWeek == dayOfWeek);
            var defaultWorking = dayOfWeek != DayOfWeek.Friday;
            var isWorking = match?.IsWorkingDay ?? defaultWorking;

            weeklyDays.Add(new WeeklyDayScheduleDto(
                i,
                dayNames[i].Ar,
                dayNames[i].En,
                isWorking));
        }

        var exceptionDtos = exceptions.Select(x => new WorkingDayExceptionDto(
            x.Id,
            x.Date.ToString("yyyy-MM-dd"),
            x.IsWorkingDay,
            x.Reason)).ToList();

        return new WorkingDaysScheduleDto(weeklyDays, exceptionDtos);
    }

    private async Task<RestaurantCapacityOverviewDto> BuildCapacityOverviewAsync(
        Guid restaurantId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var baseCapacity = await GetCurrentBaseCapacityAsync(restaurantId, today, cancellationToken);
        var schedule = await GetWorkingDaysScheduleDtoAsync(restaurantId, cancellationToken);

        var busyStatuses = await _db.BusyStatuses
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsBusy)
            .ToListAsync(cancellationToken);

        var confirmedMealsByDate = await GetConfirmedMealsByDateAsync(
            restaurantId,
            fromDate,
            toDate,
            cancellationToken);

        var dateCapacityOverrides = await _db.DailyCapacities
            .AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId &&
                x.CapacityType == CapacityType.DateOverride &&
                x.CampaignId == null &&
                x.ServiceDate >= fromDate &&
                x.ServiceDate <= toDate)
            .ToDictionaryAsync(x => x.ServiceDate, x => x.MaxBoxes, cancellationToken);

        var daysList = new List<CapacityDayDto>();
        var upcomingWorkingCount = 0;

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var dateIso = date.ToString("yyyy-MM-dd");
            var weekdayIndex = (int)date.DayOfWeek;
            var weeklyConfig = schedule.WeeklyDays.FirstOrDefault(x => x.DayOfWeek == weekdayIndex);
            var dateException = schedule.Exceptions.FirstOrDefault(x => x.DateIso == dateIso);

            bool isWorkingDay;
            string? holidayReason = null;

            if (dateException != null)
            {
                isWorkingDay = dateException.IsWorkingDay;
                holidayReason = dateException.Reason;
            }
            else
            {
                isWorkingDay = weeklyConfig?.IsWorkingDay ?? (date.DayOfWeek != DayOfWeek.Friday);
            }

            var isPast = date < today;
            var isToday = date == today;
            if (!isPast && isWorkingDay) upcomingWorkingCount++;

            var confirmed = confirmedMealsByDate.GetValueOrDefault(date);
            var isManualBusy = busyStatuses.Any(x => x.StartsAtUtc.Date <= date.ToDateTime(TimeOnly.MinValue).Date &&
                (x.EndsAtUtc == null || x.EndsAtUtc.Value.Date >= date.ToDateTime(TimeOnly.MinValue).Date));

            var configuredLimit = dateCapacityOverrides.GetValueOrDefault(date, baseCapacity);
            var effectiveLimit = isWorkingDay ? configuredLimit : 0;
            var isAutoBusy = isWorkingDay && effectiveLimit > 0 && confirmed >= effectiveLimit;
            var isHoliday = !isWorkingDay;

            string busyKind;
            if (isHoliday) busyKind = "holiday";
            else if (isManualBusy) busyKind = "manual";
            else if (isAutoBusy) busyKind = "capacity";
            else busyKind = "none";

            var dayLabel = date.ToString("d MMM");
            var weekdayNameAr = weeklyConfig?.DayNameAr ?? "";
            var weekdayNameEn = weeklyConfig?.DayNameEn ?? "";

            daysList.Add(new CapacityDayDto(
                Id: dateIso,
                DateIso: dateIso,
                WeekdayIndex: weekdayIndex,
                WeekdayLabelAr: weekdayNameAr,
                WeekdayLabelEn: weekdayNameEn,
                DateLabel: dayLabel,
                Confirmed: confirmed,
                Limit: effectiveLimit,
                IsWorkingDay: isWorkingDay,
                IsHoliday: isHoliday,
                HolidayReason: holidayReason,
                IsManualBusy: isManualBusy,
                IsAutoBusy: isAutoBusy,
                BusyKind: busyKind,
                IsToday: isToday,
                IsPast: isPast,
                CanToggleManual: isWorkingDay && !isPast
            ));
        }

        var confirmedToday = daysList.FirstOrDefault(x => x.IsToday)?.Confirmed ?? 0;
        var status = new OperationalStatusDto(
            IsActive: upcomingWorkingCount > 0,
            UpcomingWorkingDaysCount: upcomingWorkingCount,
            StatusLabelAr: upcomingWorkingCount > 0 ? "نشط - يعمل وفق الجدول" : "متوقف مؤقتًا",
            StatusLabelEn: upcomingWorkingCount > 0 ? "Active - Operating schedule" : "Paused",
            WorkingDaysSummaryAr: $"أيام العمل القادمة: {upcomingWorkingCount} يومًا من الـ {daysList.Count} القادمة",
            WorkingDaysSummaryEn: $"Upcoming working days: {upcomingWorkingCount} of next {daysList.Count} days");

        return new RestaurantCapacityOverviewDto(
            Status: status,
            DailyLimit: baseCapacity,
            ConfirmedToday: confirmedToday,
            NearLimitThreshold: 0.8m,
            UpdatedAtIso: DateTime.UtcNow.ToString("O"),
            Days: daysList,
            WeeklyDays: schedule.WeeklyDays,
            Exceptions: schedule.Exceptions);
    }
}
