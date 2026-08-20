# Restaurant Order Confirmation & Detail Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the backend APIs for restaurant order confirmation (under the -72h locked window) and order details, and integrate them into the `restaurant-dashboard` Angular application to replace mock data with live endpoints.

**Architecture:** 
- Backend: Clean Architecture in ASP.NET Core with CQRS pattern (MediatR-style handlers), EF Core, and restaurant identity isolation using `ICurrentUser.RestaurantId`.
- Frontend: Angular 19 Standalone with Signals, reactive Facades, and dedicated API services communicating through the dev proxy (`/api/v1/restaurants/orders/...`).

**Tech Stack:** C# .NET 9, EF Core, Angular 19, TypeScript, RxJS, xUnit, NSubstitute.

---

## Global Constraints

- Backend models and responses must support bilingual Arabic/English fields matching `LocalizedText` (`{ ar: string, en: string }`).
- Restaurant queries must strictly isolate data by `ICurrentUser.RestaurantId`.
- The 24h confirmation deadline runs from the lock timestamp (`LockAtUtc`), categorized into 4 urgency tiers: `critical` (<4h), `warning` (<8h), `normal` (>=8h), `overdue` (<=0h).
- UI designs and layout structure in `pending-confirmation.page.html` and `order-detail.page.html` must be strictly preserved.

---

### Task 1: Backend DTOs and Contracts

**Files:**
- Create: `f:/MealMate/backend/src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderDtos.cs`

**Interfaces:**
- Produces: `PendingConfirmationDataDto`, `PendingConfirmationItemDto`, `PendingConfirmationSummaryDto`, `RestaurantOrderDetailDto`, `ConfirmRestaurantOrderRequest`, `RejectRestaurantOrderRequest`.

- [ ] **Step 1: Create the DTO definitions**

```csharp
namespace MealMate.Application.Features.RestaurantOrders;

public sealed record LocalizedTextDto(string Ar, string En);

public sealed record PendingConfirmationSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value,
    LocalizedTextDto Hint,
    string Tone,
    string Icon);

public sealed record PendingConfirmationItemDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    string CustomerMaskedId,
    LocalizedTextDto MealSummary,
    LocalizedTextDto ProgramLabel,
    LocalizedTextDto BundleLabel,
    int BoxCount,
    LocalizedTextDto DeliveryDateLabel,
    LocalizedTextDto DeliverySlotLabel,
    LocalizedTextDto ReceivedAtLabel,
    LocalizedTextDto DeadlineLabel,
    int HoursLeft,
    string Urgency,
    bool Confirmed,
    bool IsReassigned,
    string Route);

public sealed record PendingConfirmationDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto WindowHint,
    IReadOnlyList<PendingConfirmationSummaryDto> Summaries,
    IReadOnlyList<PendingConfirmationItemDto> Orders);

public sealed record BoxMealNutritionDto(
    decimal Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal FiberGrams,
    decimal SugarGrams,
    decimal SodiumMg,
    int ProteinDailyPercent,
    int SodiumDailyPercent,
    decimal ServingWeightGrams);

public sealed record BoxMealIngredientDto(
    Guid Id,
    LocalizedTextDto Name,
    LocalizedTextDto Amount,
    decimal WeightGrams,
    decimal Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal FiberGrams,
    decimal SugarGrams,
    decimal SodiumMg,
    int ProteinDailyPercent,
    int SodiumDailyPercent,
    LocalizedTextDto PrepStyle,
    LocalizedTextDto? Note,
    bool Allergen);

public sealed record BoxMealItemDto(
    Guid Id,
    string Slot,
    LocalizedTextDto SlotLabel,
    int SlotIndex,
    LocalizedTextDto Name,
    LocalizedTextDto Description,
    string ImageUrl,
    BoxMealNutritionDto Nutrition,
    IReadOnlyList<BoxMealIngredientDto> Ingredients,
    IReadOnlyList<LocalizedTextDto> AllergyFlags,
    LocalizedTextDto PrepNote);

public sealed record BoxSlotSummaryDto(
    string Slot,
    LocalizedTextDto Label,
    int Expected,
    int Filled);

public sealed record OrderDetailFactDto(
    string Id,
    LocalizedTextDto Label,
    LocalizedTextDto Value,
    string Icon);

public sealed record OrderDetailChecklistItemDto(
    string Id,
    LocalizedTextDto Label,
    bool Done,
    LocalizedTextDto Hint);

public sealed record OrderDetailTimelineItemDto(
    string Id,
    LocalizedTextDto Title,
    LocalizedTextDto Detail,
    LocalizedTextDto Time,
    string Tone);

public sealed record OrderDetailActionDto(
    string Id,
    LocalizedTextDto Label,
    string Route,
    bool Primary,
    string Icon);

public sealed record RestaurantOrderDetailDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    string Status,
    string Shift,
    LocalizedTextDto ShiftLabel,
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    string CustomerMaskedId,
    LocalizedTextDto DeliveryDateLabel,
    LocalizedTextDto DeliverySlotLabel,
    LocalizedTextDto ProgramLabel,
    LocalizedTextDto TierLabel,
    string BundleKind,
    LocalizedTextDto BundleLabel,
    LocalizedTextDto BoxCompositionLabel,
    int BoxCount,
    string? DriverCode,
    string? BarcodeCode,
    LocalizedTextDto WindowLabel,
    LocalizedTextDto PrivacyNote,
    IReadOnlyList<OrderDetailFactDto> Facts,
    IReadOnlyList<BoxSlotSummaryDto> SlotSummary,
    IReadOnlyList<BoxMealItemDto> Meals,
    IReadOnlyList<OrderDetailChecklistItemDto> Checklist,
    IReadOnlyList<OrderDetailTimelineItemDto> Timeline,
    IReadOnlyList<OrderDetailActionDto> Actions);

public sealed record ConfirmRestaurantOrderRequest(string? Note = null);
public sealed record RejectRestaurantOrderRequest(string Reason);
```

---

### Task 2: Backend Handlers and Unit Tests

**Files:**
- Create: `f:/MealMate/backend/src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`
- Create: `f:/MealMate/backend/tests/MealMate.UnitTests/RestaurantOperations/RestaurantOrdersConfirmationTests.cs`

- [ ] **Step 1: Write the failing unit tests for Restaurant Order Confirmation & Details**

Test covers:
1. `GetPendingConfirmation`: calculates 24h countdown, assigns urgency (`critical`, `warning`, `normal`, `overdue`), aggregates summaries.
2. `ConfirmOrder`: updates `SubscriptionDay.Status = ConfirmedByRestaurant`, sets `ConfirmedAtUtc`.
3. `RejectOrder`: updates `SubscriptionDay.Status = ReplacementRequired`, records `RejectionReason`.
4. `GetOrderDetail`: builds complete rich details (nutrition, ingredients, timeline, checklist).
5. Unauthorized access check when restaurant context is missing.

- [ ] **Step 2: Run unit test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~RestaurantOrdersConfirmationTests"`

- [ ] **Step 3: Implement `RestaurantOrderService` and Handlers**

Implement the business logic in `RestaurantOrderHandlers.cs`.

- [ ] **Step 4: Run unit test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~RestaurantOrdersConfirmationTests"`

---

### Task 3: Backend Controller Layer

**Files:**
- Create: `f:/MealMate/backend/src/MealMate.Api/Controllers/V1/RestaurantOrdersController.cs`

- [ ] **Step 1: Implement `RestaurantOrdersController`**

```csharp
using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Features.RestaurantOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/orders")]
[Route("api/restaurant/orders")]
[Authorize]
public sealed class RestaurantOrdersController : ApiControllerBase
{
    [HttpGet("pending-confirmation")]
    public async Task<IActionResult> GetPendingConfirmation(
        [FromQuery] string? filter = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantPendingConfirmationQuery(filter, search), cancellationToken));

    [HttpGet("detail/{orderCodeOrId}")]
    [HttpGet("{orderCodeOrId}")]
    public async Task<IActionResult> GetOrderDetail(
        string orderCodeOrId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantOrderDetailQuery(orderCodeOrId), cancellationToken));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> ConfirmOrder(
        Guid id,
        [FromBody] ConfirmRestaurantOrderRequest? request = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ConfirmRestaurantOrderCommand(id, request?.Note), cancellationToken));

    [HttpPost("{id:guid}/reject-confirmation")]
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> RejectOrder(
        Guid id,
        [FromBody] RejectRestaurantOrderRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new RejectRestaurantOrderCommand(id, request.Reason), cancellationToken));
}
```

- [ ] **Step 2: Build backend solution to verify compilation**

Run: `dotnet build src/MealMate.Api/MealMate.Api.csproj`

---

### Task 4: Frontend API Services & Integration in Restaurant Dashboard

**Files:**
- Create: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/pending-confirmation/data/pending-confirmation-api.service.ts`
- Create: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/daily-orders/detail/data/order-detail-api.service.ts`
- Modify: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/pending-confirmation/data/pending-confirmation.facade.ts`
- Modify: `f:/MealMate/frontend/restaurant-dashboard/src/app/features/restaurant/daily-orders/detail/data/order-detail.facade.ts`

- [ ] **Step 1: Create `PendingConfirmationApiService`**

```typescript
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import { PendingConfirmationData } from '../models/pending-confirmation.model';

@Injectable({ providedIn: 'root' })
export class PendingConfirmationApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/restaurants/orders`;

  getPendingConfirmation(filter?: string, search?: string): Observable<PendingConfirmationData> {
    let params = new HttpParams();
    if (filter && filter !== 'all') params = params.set('filter', filter);
    if (search) params = params.set('search', search);
    return this.http.get<PendingConfirmationData>(`${this.baseUrl}/pending-confirmation`, { params });
  }

  confirmOrder(orderId: string, note?: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${orderId}/confirm`, { note });
  }

  rejectOrder(orderId: string, reason: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${orderId}/reject`, { reason });
  }
}
```

- [ ] **Step 2: Create `OrderDetailApiService`**

```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../../../environments/environment';
import { OrderDetailData } from '../models/order-detail.model';

@Injectable({ providedIn: 'root' })
export class OrderDetailApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/restaurants/orders`;

  getOrderDetail(orderCodeOrId: string): Observable<OrderDetailData> {
    return this.http.get<OrderDetailData>(`${this.baseUrl}/detail/${encodeURIComponent(orderCodeOrId)}`);
  }
}
```

- [ ] **Step 3: Update `PendingConfirmationFacade` to use live API service**

Wire `PendingConfirmationApiService` in `PendingConfirmationFacade`, replace mock timeouts, handle `load()`, `confirmOrder()`, filtering, pagination, and error states.

- [ ] **Step 4: Update `OrderDetailFacade` to use live API service**

Wire `OrderDetailApiService` in `OrderDetailFacade`, replace mock timeouts, handle `load(orderCode)`, loading states, and error handling.

---

### Task 5: Full Verification and Build Validation

- [ ] **Step 1: Run all backend tests**

Run: `dotnet test` in `f:/MealMate/backend`

- [ ] **Step 2: Run frontend build check**

Run: `npm run build` in `f:/MealMate/frontend/restaurant-dashboard`
