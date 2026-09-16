# Subscription Dispatch, Kitchen Preparation, and Delivery Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the end-to-end operational flow linking customer subscription days to restaurant kitchen preparation, box packaging, and synchronized delivery trip dispatch.

**Architecture:** Domain-driven CQRS with MediatR in ASP.NET Core (.NET 9), PostgreSQL via EF Core, and Angular 19 signals-based reactive facade calling RESTful APIs.

**Tech Stack:** C# .NET 9, EF Core, PostgreSQL 16 (Npgsql), Angular 19 (TypeScript, Signals).

**Spec:** `f:\MealMate\backend\docs\superpowers\specs\2026-09-14-subscription-dispatch-kitchen-delivery-flow-design.md`

## Global Constraints
- Bilingual Arabic/English payload support matching `LocalizedTextDto` (`{ ar: string, en: string }`).
- Preserve Clean Architecture layer separation (Domain -> Application -> Persistence -> Api).
- Provide developer fallback in controllers so endpoints gracefully resolve restaurant context without requiring manual JWT bearer injection during local testing.
- Maintain zero build errors across both backend and frontend.

---

### Task 1: Backend Subscription Dispatch & Daily Orders Synchronization

**Files:**
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderDtos.cs`
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`
- Modify: `src/MealMate.Api/Controllers/V1/RestaurantOrdersController.cs`

**Interfaces:**
- Consumes: `IAppDbContext`, `ISubscriptionDayDispatchService`, `CustomerCalendarEngine`, `IDateTimeProvider`
- Produces: `DispatchRestaurantDailyOrdersCommand`, `POST /api/v1/restaurants/orders/dispatch-due` endpoint.

- [ ] **Step 1: Add `DispatchRestaurantDailyOrdersCommand` record and handler**
  Add command record `DispatchRestaurantDailyOrdersCommand(Guid? RestaurantId, DateOnly? TargetDate, bool AutoSelectIfEmpty = true) : IRequest<Result<int>>` in `RestaurantOrderDtos.cs`.
  Implement handler in `RestaurantOrderHandlers.cs`:
  - Resolve `restaurantId` (fallback to first active `RestaurantProfile` if empty).
  - Target date defaults to today `DateOnly.FromDateTime(_clock.UtcNow)`.
  - Fetch active `SubscriptionDays` for this restaurant on target date.
  - If meal selections are missing, invoke `_calendarEngine.AutoSelectFullBoxAsync`.
  - Call `_dispatchService.LockAndSendToRestaurantAsync` to transition status to `SentToRestaurant`.
  - Upsert records into `RestaurantDailyOrders` with `OrderStatus.AwaitingRestaurantConfirmation`.

- [ ] **Step 2: Update `RestaurantOrdersController` with dispatch endpoint and dev fallback**
  Add `[AllowAnonymous]` to `RestaurantOrdersController` and helper to resolve `targetRestaurantId` from `_currentUser.RestaurantId` or fallback.
  Add `POST /api/v1/restaurants/orders/dispatch-due` mapping to `DispatchRestaurantDailyOrdersCommand`.

- [ ] **Step 3: Build backend and verify compilation**
  Run `dotnet build src/MealMate.Application/MealMate.Application.csproj` and `src/MealMate.Api/MealMate.Api.csproj`.

---

### Task 2: Backend Kitchen Preparation Status Transitions

**Files:**
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderDtos.cs`
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`
- Modify: `src/MealMate.Api/Controllers/V1/RestaurantOrdersController.cs`

**Interfaces:**
- Consumes: `IAppDbContext`, `IDateTimeProvider`
- Produces: `UpdateDailyOrderPrepStatusCommand`, `PUT /api/v1/restaurants/orders/daily/{dayId:guid}/prep-status` endpoint.

- [ ] **Step 1: Add `UpdateDailyOrderPrepStatusCommand` and handler**
  Add `UpdateDailyOrderPrepStatusCommand(Guid DayId, string Status)` in `RestaurantOrderDtos.cs`.
  Implement handler in `RestaurantOrderHandlers.cs`:
  - Validates `SubscriptionDay` exists for the restaurant.
  - If `Status == "preparing"`: update `SubscriptionDay.Status = SubscriptionDayStatus.Preparing` and `RestaurantDailyOrder.Status = OrderStatus.Preparing`.
  - If `Status == "ready"`: update `SubscriptionDay.Status = SubscriptionDayStatus.Preparing` (or ready flag) and `RestaurantDailyOrder.Status = OrderStatus.ReadyForPickup`.
  - Save changes and return success.

- [ ] **Step 2: Expose endpoint in `RestaurantOrdersController`**
  Add `PUT /api/v1/restaurants/orders/daily/{dayId:guid}/prep-status` mapping to `UpdateDailyOrderPrepStatusCommand`.

- [ ] **Step 3: Build backend and verify compilation**
  Run `dotnet build src/MealMate.Api/MealMate.Api.csproj`.

---

### Task 3: Delivery Trips Lifecycle Synchronization

**Files:**
- Modify: `src/MealMate.Application/Features/DriverDelivery/Trips/DeliveryTripCommands.cs`

**Interfaces:**
- Consumes: `Trip`, `TripStop`, `SubscriptionDay`, `RestaurantDailyOrder`
- Produces: Synchronized lifecycle transitions when trip starts and stops are delivered.

- [ ] **Step 1: Hook into `UpdateTripStatusCommandHandler`**
  When trip status transitions to `TripStatus.InProgress`:
  - Query all stops in `trip.Stops`.
  - Find matching `SubscriptionDays` by `DailyOrderId`, `SubscriptionDayId`, or `OrderCode`.
  - Set `SubscriptionDay.Status = SubscriptionDayStatus.OutForDelivery`.
  - Set linked `RestaurantDailyOrders.Status = OrderStatus.OnTheWay`.

- [ ] **Step 2: Hook into `UpdateTripStopStatusCommandHandler`**
  When a stop status transitions to `TripStopStatus.Delivered`:
  - Find matching `SubscriptionDay`.
  - Set `SubscriptionDay.Status = SubscriptionDayStatus.Delivered`.
  - Set linked `RestaurantDailyOrders.Status = OrderStatus.Delivered`.

- [ ] **Step 3: Build backend and test endpoint execution**
  Run `dotnet build src/MealMate.Application/MealMate.Application.csproj`.

---

### Task 4: Frontend Daily Orders & Kitchen UI Integration

**Files:**
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\core\api\restaurant-api-endpoints.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\daily-orders\data\daily-orders-api.service.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\daily-orders\data\daily-orders.facade.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\daily-orders\daily-orders.page.html`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\daily-orders\daily-orders.page.ts`

**Interfaces:**
- Consumes: Backend prep-status and dispatch-due endpoints.
- Produces: Interactive kitchen status progression in Angular 19 UI.

- [ ] **Step 1: Update API endpoints and service**
  Add `dispatchDueOrders` and `updatePrepStatus` to `RestaurantApiEndpoints`.
  Add corresponding methods in `DailyOrdersApiService`.

- [ ] **Step 2: Update `DailyOrdersFacade`**
  Add `updatePrepStatus(orderId: string, nextStatus: string)` method that calls API and updates local reactive signal.
  Add `dispatchDueOrders()` method to trigger dispatch and reload data.

- [ ] **Step 3: Update `daily-orders.page.html` and component**
  Add quick action button in table rows:
  - If status is `preparing` -> Button: "جاهز للتسليم (Mark Ready)"
  - If status is `ready` -> Badge: "جاهز بانتظار السائق"
  Add "إرسال وجبات اليوم (Dispatch Due Orders)" button in the header toolbar.

- [ ] **Step 4: Build and test frontend**
  Run `npm.cmd run build` in `f:\MealMate\frontend\restaurant-dashboard`.

---

### Task 5: End-to-End Live Verification

- [ ] **Step 1: Rebuild and restart API server daemon**
- [ ] **Step 2: Dispatch today's orders via API/UI**
- [ ] **Step 3: Confirm order and advance kitchen prep to `ready`**
- [ ] **Step 4: Create delivery trip, start trip, and verify status moves to `OutForDelivery`**
- [ ] **Step 5: Mark stop delivered and verify status moves to `Delivered`**
