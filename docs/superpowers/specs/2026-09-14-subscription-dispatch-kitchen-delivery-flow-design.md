# Design Specification: Subscription Dispatch to Kitchen Preparation and Delivery Flow

**Date:** 2026-09-14  
**Status:** Approved  
**Author:** Antigravity Team  
**Scope:** Backend (`f:\MealMate\backend`) & Frontend Restaurant Dashboard (`f:\MealMate\frontend\restaurant-dashboard`)

---

## 1. Overview & Objective

Connect the entire MealMate operational cycle seamlessly from active customer subscriptions to kitchen preparation and final customer delivery:
1. **Subscription Dispatch**: Allow automated and on-demand dispatch of subscription days and meals for a given restaurant and date (`SentToRestaurant`).
2. **Kitchen Confirmation & Preparation**: Restaurant receives incoming orders in `/orders/pending-confirmation`, confirms them, and moves them to `/orders/daily` where kitchen staff can update preparation status (`Preparing` -> `Ready`).
3. **Handover & Delivery Synchronization**: Link `/orders/handover` and `/delivery/trips` so that starting a trip (`in_progress`) automatically sets all assigned orders to `OutForDelivery`, and delivering each stop sets the subscription day to `Delivered`.

---

## 2. Status Mapping Across Systems

| Stage | SubscriptionDayStatus | OrderStatus (DailyOrder) | TripStopStatus | Client UI Status |
|---|---|---|---|---|
| **1. Dispatched** | `SentToRestaurant` (5) | `AwaitingRestaurantConfirmation` (2) | — | `pending` (in confirmation) |
| **2. Confirmed** | `ConfirmedByRestaurant` (6) | `RestaurantConfirmed` (3) | — | `confirmed` / `preparing` |
| **3. Cooking / Packing** | `Preparing` (9) | `Preparing` (6) | — | `preparing` |
| **4. Ready for Driver** | `Preparing` (9) (or Ready flag) | `ReadyForPickup` (7) | — | `ready` / `waiting-driver` |
| **5. Out for Delivery** | `OutForDelivery` (10) | `OnTheWay` (10) | `PickedUp` / `Pending` | `waiting-driver` / `in_progress` |
| **6. Delivered** | `Delivered` (11) | `Delivered` (13) | `Delivered` | `completed` |

---

## 3. Backend Architecture Changes

### 3.1 New & Enhanced Commands & Handlers

1. **`DispatchRestaurantDailyOrdersCommand`**
   - **File:** `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`
   - **Parameters:** `Guid? RestaurantId`, `DateOnly? TargetDate`, `bool AutoSelectIfEmpty = true`
   - **Behavior:**
     - Identifies active `SubscriptionDays` for the target restaurant and date (defaulting to today).
     - If meal selections are missing, invokes `CustomerCalendarEngine.AutoSelectFullBoxAsync` to populate standard meal items based on customer's program and bundle.
     - Transitions status to `SubscriptionDayStatus.SentToRestaurant` via `ISubscriptionDayDispatchService`.
     - Ensures corresponding `RestaurantDailyOrder` records exist with `OrderStatus.AwaitingRestaurantConfirmation` or `RestaurantConfirmed`.

2. **`UpdateDailyOrderPrepStatusCommand`**
   - **File:** `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`
   - **Parameters:** `Guid DayId`, `string Status` (`"preparing"`, `"ready"`, `"waiting-driver"`)
   - **Behavior:**
     - Validates ownership for the target restaurant.
     - Transitions `SubscriptionDay.Status` to `Preparing` and updates `RestaurantDailyOrder.Status` to `Preparing` (6) or `ReadyForPickup` (7).
     - Records modification timestamp and audit.

3. **Trip & Delivery Lifecycle Hook (Synchronous Orchestration)**
   - **File:** `src/MealMate.Application/Features/DriverDelivery/Trips/DeliveryTripCommands.cs`
   - **On `UpdateTripStatusCommand` (status -> `InProgress`):**
     - Queries all stops belonging to the trip that have linked `DailyOrderId` or matching `OrderCode`.
     - Updates linked `SubscriptionDays.Status` to `SubscriptionDayStatus.OutForDelivery`.
     - Updates linked `RestaurantDailyOrders.Status` to `OrderStatus.OnTheWay`.
   - **On `UpdateTripStopStatusCommand` (status -> `Delivered`):**
     - Updates linked `SubscriptionDay.Status` to `SubscriptionDayStatus.Delivered`.
     - Updates linked `RestaurantDailyOrders.Status` to `OrderStatus.Delivered`.
     - Sets `DeliveredAtUtc = DateTime.UtcNow`.

### 3.2 Web API Controller Updates

- **File:** `src/MealMate.Api/Controllers/V1/RestaurantOrdersController.cs`
  - Add `[AllowAnonymous]` with fallback resolution of `restaurantId` from `ICurrentUser` or default active `RestaurantProfile` (matching `RestaurantDeliveryTripsController`).
  - Add endpoints:
    - `POST /api/v1/restaurants/orders/dispatch-due` -> triggers `DispatchRestaurantDailyOrdersCommand`.
    - `PUT /api/v1/restaurants/orders/daily/{dayId:guid}/prep-status` -> triggers `UpdateDailyOrderPrepStatusCommand`.

---

## 4. Frontend Integration (`restaurant-dashboard`)

### 4.1 API Services & Endpoints
- **File:** `src/app/core/api/restaurant-api-endpoints.ts`
  - Add `dispatchDueOrders: '/api/v1/restaurants/orders/dispatch-due'`
  - Add `updatePrepStatus: (dayId: string) => `/api/v1/restaurants/orders/daily/${dayId}/prep-status``
- **File:** `src/app/features/restaurant/daily-orders/data/daily-orders-api.service.ts`
  - Add `updatePrepStatus(dayId: string, status: string): Observable<boolean>`
  - Add `dispatchDueOrders(date?: string): Observable<boolean>`

### 4.2 Facade & UI Updates
- **File:** `src/app/features/restaurant/daily-orders/data/daily-orders.facade.ts`
  - Add `updateOrderStatus(orderId: string, newStatus: string): void`
  - Add `dispatchDue(): void`
- **File:** `src/app/features/restaurant/daily-orders/daily-orders.page.html` & `order-detail.page.html`
  - Add action buttons in the daily orders list and detail view:
    - If status is `confirmed` -> Show "بدء التحضير (Start Prep)"
    - If status is `preparing` -> Show "اكتمل التجهيز (Mark as Ready)"
    - If status is `ready` -> Show "جاهز للتسليم / بانتظار السائق"
- **File:** `src/app/features/restaurant/handover/data/handover.facade.ts`
  - Connect to fetch ready orders and simulate barcode handover scan confirming box transfer to the driver.

---

## 5. Verification Plan

1. **Build Verification**:
   - `dotnet build src/MealMate.Api/MealMate.Api.csproj` (0 errors)
   - `npm.cmd run build` in `frontend/restaurant-dashboard` (0 errors)
2. **End-to-End Live Workflow Verification via API & UI**:
   - Dispatch today's active subscription days via `POST /api/v1/restaurants/orders/dispatch-due`.
   - Verify incoming orders in `/orders/pending-confirmation`.
   - Confirm order via `POST /api/v1/restaurants/orders/{id}/confirm`.
   - Verify order in `/orders/daily` with status `preparing`.
   - Update prep status to `ready` via `PUT /api/v1/restaurants/orders/daily/{id}/prep-status`.
   - Create and start a delivery trip with this order via `POST /api/v1/restaurants/delivery/trips` and `PUT .../status`.
   - Verify the subscription day transitions to `OutForDelivery`.
   - Deliver stop and verify subscription day transitions to `Delivered`.
