# Daily Orders & Upcoming 24h Prep Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement backend APIs for Restaurant Daily Orders (`/restaurant/orders/daily`) and Upcoming 24h Preparation (`/restaurant/orders/upcoming-24h`), integrate them with the operational Calendar (`ServiceDate`), and connect the Angular `restaurant-dashboard` frontend to live backend endpoints.

**Architecture:** 
- Backend: Clean Architecture in ASP.NET Core with CQRS pattern (MediatR-style handlers), EF Core, and restaurant identity isolation using `ICurrentUser.RestaurantId`.
- Calendar Integration: Querying `SubscriptionDays` by `ServiceDate` (with default today and custom date support `?date=YYYY-MM-DD`).
- Frontend: Angular 19 Standalone with Signals, reactive Facades, and dedicated API services communicating through the dev proxy (`/api/v1/restaurants/orders/...`).

**Tech Stack:** C# .NET 9, EF Core, Angular 19, TypeScript, RxJS, xUnit, NSubstitute.

---

## Global Constraints

- Bilingual Arabic/English support matching `LocalizedText` (`{ ar: string, en: string }`).
- Restaurant queries must strictly isolate data by `ICurrentUser.RestaurantId`.
- Support calendar date querying `?date=YYYY-MM-DD` and shift filtering (`all`, `morning`, `noon`, `evening`).
- Full preservation of existing UI designs in `daily-orders.page.html` and `upcoming-24h.page.html`.

---

### Task 1: Backend DTOs and CQRS Contracts

**Files:**
- Modify: `f:/MealMate/backend/src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderDtos.cs`

**Interfaces:**
- Produces: `DailyOrderItemDto`, `DailyOrderSummaryDto`, `DailyOrdersDataDto`, `Upcoming24hItemDto`, `Upcoming24hSummaryDto`, `Upcoming24hDataDto`, `GetRestaurantDailyOrdersQuery`, `GetRestaurantUpcoming24hQuery`, `ArchiveDailyOrderCommand`.

- [ ] **Step 1: Add DTO definitions for Daily Orders & Upcoming 24h**

```csharp
public sealed record DailyOrderSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value,
    LocalizedTextDto Hint,
    string Tone,
    string Icon);

public sealed record DailyOrderItemDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    LocalizedTextDto MealName,
    LocalizedTextDto ProgramLabel,
    int BoxCount,
    string Shift,
    LocalizedTextDto ShiftLabel,
    string Status,
    LocalizedTextDto? DriverLabel,
    LocalizedTextDto WindowLabel,
    bool BarcodeReady,
    LocalizedTextDto ActionLabel,
    string Route);

public sealed record DailyOrdersDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto PrintLabel,
    IReadOnlyList<DailyOrderSummaryDto> Summaries,
    IReadOnlyList<DailyOrderItemDto> Orders);

public sealed record Upcoming24hSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value,
    LocalizedTextDto Hint,
    string Tone,
    string Icon);

public sealed record Upcoming24hItemDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    string CustomerMaskedId,
    LocalizedTextDto MealSummary,
    LocalizedTextDto ProgramLabel,
    LocalizedTextDto BundleLabel,
    int BoxCount,
    string Shift,
    LocalizedTextDto ShiftLabel,
    LocalizedTextDto DeliveryDateLabel,
    LocalizedTextDto DeliverySlotLabel,
    int HoursToDelivery,
    bool InvoiceGenerated,
    bool BarcodeGenerated,
    bool LabelsGenerated,
    string PrepStatus,
    string Route,
    string LabelsRoute);

public sealed record Upcoming24hDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto WindowHint,
    LocalizedTextDto PrintLabel,
    IReadOnlyList<Upcoming24hSummaryDto> Summaries,
    IReadOnlyList<Upcoming24hItemDto> Orders);

public sealed record GetRestaurantDailyOrdersQuery(DateOnly? Date = null, string? Shift = null, string? Status = null, string? Search = null) : IRequest<Result<DailyOrdersDataDto>>;
public sealed record GetRestaurantUpcoming24hQuery(DateOnly? TargetDate = null, string? Shift = null, string? PrepStatus = null, string? Search = null) : IRequest<Result<Upcoming24hDataDto>>;
public sealed record ArchiveDailyOrderCommand(Guid DayId) : IRequest<Result<bool>>;
```

---

### Task 2: Backend Handlers and Unit Tests (TDD)

**Files:**
- Create: `f:/MealMate/backend/tests/MealMate.UnitTests/RestaurantOperations/DailyOrdersAndUpcoming24hTests.cs`
- Modify: `f:/MealMate/backend/src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`

- [ ] **Step 1: Write the failing unit tests for Daily Orders & Upcoming 24h**

Test covers:
1. `GetDailyOrders`: returns orders for specified calendar date, calculates summaries (total, preparing, ready, with driver).
2. `GetUpcoming24h`: returns orders for tomorrow / 24h window with invoice, barcode, and label flags.
3. `ArchiveDailyOrder`: updates completed order status.
4. Restaurant isolation: returns unauthorized when context is missing.

- [ ] **Step 2: Run unit test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DailyOrdersAndUpcoming24hTests"`

- [ ] **Step 3: Implement `GetDailyOrdersAsync`, `GetUpcoming24hAsync`, `ArchiveDailyOrderAsync` in `RestaurantOrderService` and Handlers**

- [ ] **Step 4: Run unit test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DailyOrdersAndUpcoming24hTests"`

---

### Task 3: Backend Controller Layer

**Files:**
- Modify: `f:/MealMate/backend/src/MealMate.Api/Controllers/V1/RestaurantOrdersController.cs`

- [ ] **Step 1: Add endpoints for daily orders, upcoming-24h, and archive in `RestaurantOrdersController`**

```csharp
[HttpGet("daily")]
public async Task<IActionResult> GetDailyOrders(
    [FromQuery] DateOnly? date = null,
    [FromQuery] string? shift = null,
    [FromQuery] string? status = null,
    [FromQuery] string? search = null,
    CancellationToken cancellationToken = default)
    => HandleResult(await Sender.Send(new GetRestaurantDailyOrdersQuery(date, shift, status, search), cancellationToken));

[HttpGet("upcoming-24h")]
public async Task<IActionResult> GetUpcoming24h(
    [FromQuery] DateOnly? targetDate = null,
    [FromQuery] string? shift = null,
    [FromQuery] string? prepStatus = null,
    [FromQuery] string? search = null,
    CancellationToken cancellationToken = default)
    => HandleResult(await Sender.Send(new GetRestaurantUpcoming24hQuery(targetDate, shift, prepStatus, search), cancellationToken));

[HttpPost("{id:guid}/archive")]
public async Task<IActionResult> ArchiveOrder(
    Guid id,
    CancellationToken cancellationToken = default)
    => HandleResult(await Sender.Send(new ArchiveDailyOrderCommand(id), cancellationToken));
```

- [ ] **Step 2: Build backend solution to verify compilation**

Run: `dotnet build src/MealMate.Api/MealMate.Api.csproj`

---

### Task 4: Frontend API Services & Integration in Restaurant Dashboard

**Files:**
- Create: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/daily-orders/data/daily-orders-api.service.ts`
- Create: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/upcoming-24h/data/upcoming-24h-api.service.ts`
- Modify: `f:/MealMate/frontend/restaurant-dashboard/src/app/core/api/restaurant-api-endpoints.ts`
- Modify: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/daily-orders/data/daily-orders.facade.ts`
- Modify: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/upcoming-24h/data/upcoming-24h.facade.ts`

- [ ] **Step 1: Create `DailyOrdersApiService`**

- [ ] **Step 2: Create `Upcoming24hApiService`**

- [ ] **Step 3: Update `DailyOrdersFacade` with API integration and calendar date support**

- [ ] **Step 4: Update `Upcoming24hFacade` with API integration**

---

### Task 5: Full Verification and Build Validation

- [ ] **Step 1: Run all backend tests**

Run: `dotnet test` in `f:/MealMate/backend`

- [ ] **Step 2: Run frontend build check**

Run: `npm.cmd run build` in `f:/MealMate/frontend/restaurant-dashboard`
