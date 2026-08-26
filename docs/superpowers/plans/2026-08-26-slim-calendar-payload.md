# Slim Calendar Payload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `GET .../calendar` return slim day/meal summaries, and add `GET .../days/{date}` for full day detail.

**Architecture:** Split calendar list DTOs from full day DTOs. `GetCalendarAsync` maps summary meals (no ingredients/nutrition/allergens/description). New `GetDayAsync` reuses existing full `MapDayAsync`. Mutations keep returning full `CustomerCalendarDayDto`.

**Tech Stack:** .NET / MediatR-style handlers, EF Core, xUnit + FluentAssertions, existing `CustomerCalendarEngine`.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-26-slim-calendar-payload-design.md`
- Do not change restaurant/meal selection business rules
- No DB migration
- Mutations continue to return full day DTO
- Update mobile gap docs for Flutter

---

## File map

| File | Responsibility |
|------|----------------|
| `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarContracts.cs` | Summary DTOs + `GetCustomerCalendarDayQuery`; change screen days type |
| `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarEngine.cs` | Slim map for calendar; `GetDayAsync` full map |
| `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarHandlers.cs` | Wire new query |
| `src/MealMate.Api/Controllers/V1/CustomerCalendarController.cs` | `GET days/{date}` |
| `tests/MealMate.UnitTests/CustomerCalendar/CustomerCalendarHandlerTests.cs` | Slim vs full assertions |
| `docs/mobile/individual-mobile-gaps/calendar/02-day-selection-api.md` | Document endpoints |
| `docs/mobile/individual-calendar/01-individual-calendar-api.md` | Align if calendar payload documented |

---

### Task 1: Contracts + engine mapping + API + tests + docs

**Files:**
- Modify: `CustomerCalendarContracts.cs`
- Modify: `CustomerCalendarEngine.cs`
- Modify: `CustomerCalendarHandlers.cs`
- Modify: `CustomerCalendarController.cs`
- Modify: `CustomerCalendarHandlerTests.cs`
- Modify: `docs/mobile/individual-mobile-gaps/calendar/02-day-selection-api.md`
- Modify: `docs/mobile/individual-calendar/01-individual-calendar-api.md` (only if it documents selectedMeals payload)

**Interfaces:**
- Produces: `CustomerCalendarMealSummaryDto`, `CustomerCalendarDaySummaryDto`, `GetCustomerCalendarDayQuery`, `GetDayAsync`, `GET days/{date}`
- Consumes: existing `MapDayAsync` / `ToMealDto` for full day

- [ ] **Step 1: Add summary DTOs and day query**

In `CustomerCalendarContracts.cs`:

```csharp
public sealed record CustomerCalendarMealSummaryDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string Slot,
    string SlotNameAr,
    string SlotNameEn,
    int SortOrder,
    string? ImageUrl,
    bool IsSelected = false);

public sealed record CustomerCalendarDaySummaryDto(
    Guid Id,
    DateOnly ServiceDate,
    Guid? RestaurantId,
    string? RestaurantNameAr,
    string? RestaurantNameEn,
    string? RestaurantLogoUrl,
    SubscriptionDayStatus Status,
    string UiStatus,
    bool IsAutoSelected,
    bool CanEdit,
    DateTime? SelectionDeadlineUtc,
    DateTime? LockAtUtc,
    int? LockRemainingSeconds,
    int FilledCount,
    int RequiredCount,
    IReadOnlyList<CustomerCalendarSlotDto> SlotRequirements,
    string? FailureReasonCode,
    IReadOnlyList<CustomerCalendarMealSummaryDto> SelectedMeals);

// Change CustomerCalendarScreenDto.Days to IReadOnlyList<CustomerCalendarDaySummaryDto>

public sealed record GetCustomerCalendarDayQuery(Guid SubscriptionId, DateOnly ServiceDate)
    : IQuery<CustomerCalendarDayDto>;
```

Keep `CustomerCalendarDayDto` / `CustomerCalendarMealDto` unchanged for full payloads.

- [ ] **Step 2: Engine — slim calendar + GetDayAsync**

```csharp
public Task<Result<CustomerCalendarScreenDto>> GetCalendarAsync(...)
    => WithContext(..., async ctx =>
    {
        var days = await MapDaySummariesAsync(ctx, ct);
        return Result.Success(new CustomerCalendarScreenDto(..., days));
    });

public Task<Result<CustomerCalendarDayDto>> GetDayAsync(Guid subscriptionId, DateOnly date, CancellationToken ct)
    => WithContext(..., async ctx =>
    {
        var day = ctx.Day(date);
        if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
        return Result.Success(await MapDayAsync(ctx, day, ct));
    });
```

Add `ToMealSummaryDto` / `MapDaySummaryAsync` that copies only summary fields from `MealSnapshotItem` (or empty names if missing). Do **not** call `ToMealDto` for calendar list.

- [ ] **Step 3: Handler + controller**

```csharp
// Handlers: implement IQueryHandler<GetCustomerCalendarDayQuery, CustomerCalendarDayDto>
public Task<Result<CustomerCalendarDayDto>> Handle(GetCustomerCalendarDayQuery query, CancellationToken ct)
    => _engine.GetDayAsync(query.SubscriptionId, query.ServiceDate, ct);

// Controller
[HttpGet("days/{date}")]
[ProducesResponseType(typeof(CustomerCalendarDayDto), StatusCodes.Status200OK)]
public async Task<IActionResult> GetDay(Guid subscriptionId, DateOnly date, CancellationToken cancellationToken)
{
    if (!IsCustomer()) return Forbid();
    return HandleResult(await Sender.Send(new GetCustomerCalendarDayQuery(subscriptionId, date), cancellationToken));
}
```

Place `GetDay` near other day GETs.

- [ ] **Step 4: Tests**

Extend `CustomerCalendarHandlerTests`:

1. After selecting a meal with non-empty allergens in seed JSON, `GetCalendar` selected meal must not expose ingredients/allergens/macros/description — assert summary type fields exist (`MealId`, `NameAr`, `Slot`, `ImageUrl`, `IsSelected`) and that the runtime type is `CustomerCalendarMealSummaryDto` (no `Ingredients` property).
2. `GetCustomerCalendarDayQuery` for same day returns full `CustomerCalendarMealDto` with calories/description present when in snapshot.
3. Unknown date → failure with `MessageCodes.Calendar.DayUnavailable`.

Also fix compile breaks in any test that assumed `Days` elements were `CustomerCalendarDayDto` if they access full-only fields.

- [ ] **Step 5: Docs**

Update `02-day-selection-api.md` table + flow for slim calendar and `GET /days/{date}`. Align `01-individual-calendar-api.md` if it lists selectedMeals fields.

- [ ] **Step 6: Verify**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~CustomerCalendarHandlerTests" --no-restore
```

Expected: all CustomerCalendarHandlerTests pass.

- [ ] **Step 7: Commit only when user asks** (`ارفع` / commit request)

---

## Self-review

1. Spec coverage: slim calendar, full day GET, mutations unchanged, docs, tests — all in Task 1.
2. No placeholders.
3. Types: Screen uses `CustomerCalendarDaySummaryDto`; day GET / mutations use `CustomerCalendarDayDto`.
