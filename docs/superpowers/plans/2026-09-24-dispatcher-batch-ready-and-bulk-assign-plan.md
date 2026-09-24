# Dispatcher Batch Ready Notification & Bulk Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement intelligent batch notification throttling (alerting the Delivery Manager via FCM push and SignalR when 5 ready orders accumulate, preventing notification spam) and provide a one-click Bulk Assignment API to assign up to 25 boxes to a driver in a single trip.

**Architecture:** A domain-driven application service (`IBatchDispatchNotifier` implemented in Infrastructure with Firebase FCM and SignalR) tracks unassigned ready orders per restaurant and applies threshold (5 orders) and cooldown/debounce rules. A MediatR command (`BulkAssignBoxesCommand`) performs atomic multi-box assignment to a unified Trip with sequential stops and triggers driver notifications.

**Tech Stack:** .NET 9, C# 13, EF Core 9, SignalR, Firebase Cloud Messaging (FCM), xUnit, FluentAssertions, NSubstitute.

**Spec:** [`docs/superpowers/specs/2026-09-24-dispatcher-batch-ready-and-bulk-assign-design.md`](file:///f:/MealMate/backend/docs/superpowers/specs/2026-09-24-dispatcher-batch-ready-and-bulk-assign-design.md)

## Global Constraints

- Never break existing 1,152 unit tests.
- Always use `IAppDbContext` and follow Clean Architecture layering.
- Push notifications must fail gracefully without aborting database transactions.
- All public endpoints in Dispatcher controller must support both authorized dispatcher sessions and development fallback.

---

### Task 1: Batch Dispatch Notification Throttler (`IBatchDispatchNotifier`)

**Files:**
- Create: `src/MealMate.Application/Common/Abstractions/IBatchDispatchNotifier.cs`
- Create: `src/MealMate.Infrastructure/Push/BatchDispatchNotifier.cs`
- Modify: `src/MealMate.Infrastructure/DependencyInjection.cs`
- Test: `tests/MealMate.UnitTests/DriverDelivery/BatchDispatchNotifierTests.cs`

**Interfaces:**
- `IBatchDispatchNotifier`:
  `Task NotifyIfBatchThresholdReachedAsync(Guid restaurantId, Guid subscriptionDayId, CancellationToken cancellationToken = default);`
  `Task CheckAndNotifyHangingBatchesAsync(Guid restaurantId, CancellationToken cancellationToken = default);`

- [x] **Step 1: Write failing unit tests for `BatchDispatchNotifier`**

Create `tests/MealMate.UnitTests/DriverDelivery/BatchDispatchNotifierTests.cs`:
1. When unassigned ready count is < 5 (e.g. 1 to 4): does NOT send push notification.
2. When unassigned ready count reaches 5: sends unified FCM push to `RestaurantDeviceTokens` with count = 5.
3. When 6th order arrives immediately after 5th: suppressed by cooldown / non-multiple threshold.
4. When unassigned ready count reaches 10: sends notification for batch 2.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~BatchDispatchNotifierTests"`
Expected: FAIL.

- [x] **Step 3: Implement `IBatchDispatchNotifier` & `BatchDispatchNotifier`**

Create interface and implementation in Application and Infrastructure layers. Register in `DependencyInjection.cs`.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~BatchDispatchNotifierTests"`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/MealMate.Application/Common/Abstractions/ src/MealMate.Infrastructure/ tests/MealMate.UnitTests/DriverDelivery/BatchDispatchNotifierTests.cs
git commit -m "feat(dispatcher): implement smart batch ready notification throttling service with FCM push"
```

---

### Task 2: Kitchen Order Handlers Integration with Batch Throttler

**Files:**
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs:1390-1420`
- Test: `tests/MealMate.UnitTests/RestaurantOrders/KitchenBatchNotificationIntegrationTests.cs`

**Interfaces:**
- Injects `IBatchDispatchNotifier` into `RestaurantOrderService` / `RestaurantOrderHandlers`.
- Calls `_batchDispatchNotifier.NotifyIfBatchThresholdReachedAsync(restaurantId, day.Id, cancellationToken)` when daily order transitions to `ReadyForPickup`.

- [x] **Step 1: Write integration unit test**

Create `tests/MealMate.UnitTests/RestaurantOrders/KitchenBatchNotificationIntegrationTests.cs`:
Verifies that updating order status to "ready" invokes `IBatchDispatchNotifier.NotifyIfBatchThresholdReachedAsync`.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~KitchenBatchNotificationIntegrationTests"`
Expected: FAIL.

- [x] **Step 3: Update `RestaurantOrderHandlers.cs`**

Inject and invoke `IBatchDispatchNotifier`.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~KitchenBatchNotificationIntegrationTests"`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/RestaurantOrders/ tests/MealMate.UnitTests/RestaurantOrders/KitchenBatchNotificationIntegrationTests.cs
git commit -m "feat(kitchen): wire kitchen ready status updates to batch dispatch notifier"
```

---

### Task 3: Bulk Box Assignment Command (`BulkAssignBoxesCommand`)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/BulkAssignBoxesCommand.cs`
- Test: `tests/MealMate.UnitTests/DriverDelivery/BulkAssignBoxesCommandTests.cs`

**Interfaces:**
- `BulkAssignBoxesCommand(IReadOnlyList<Guid> BoxIds, Guid DriverId, string? Notes = null) : ICommand<BulkAssignBoxesResponseDto>;`
- `BulkAssignBoxesResponseDto(Guid TripId, string TripCode, int AssignedCount, Guid DriverId, string DriverName, IReadOnlyList<Guid> AssignedBoxIds, DateTime AssignedAtUtc);`

- [x] **Step 1: Write failing unit tests for `BulkAssignBoxesCommand`**

Create `tests/MealMate.UnitTests/DriverDelivery/BulkAssignBoxesCommandTests.cs`:
1. Fails when `BoxIds` is empty or exceeds 25.
2. Fails when `DriverId` does not exist or is inactive.
3. Assigns 5 boxes to one trip with sequential stop numbers 1 through 5.
4. Notifies the assigned driver via FCM push and SignalR.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~BulkAssignBoxesCommandTests"`
Expected: FAIL.

- [x] **Step 3: Implement `BulkAssignBoxesCommand` & Handler**

Write validator, command, and handler using existing `AssignBoxToDriverCommand` logic enhanced for bulk provisioning.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~BulkAssignBoxesCommandTests"`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/DriverDelivery/Dispatcher/ tests/MealMate.UnitTests/DriverDelivery/BulkAssignBoxesCommandTests.cs
git commit -m "feat(dispatcher): implement bulk box assignment command with atomic trip provisioning"
```

---

### Task 4: API Endpoint in `DispatcherDashboardController`

**Files:**
- Modify: `src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs`
- Test: `tests/MealMate.ApiTests/Dispatcher/DispatcherBulkAssignEndpointTests.cs` (or unit test in `MealMate.UnitTests`)

**Interfaces:**
- `[HttpPost("orders/bulk-assign")]`
- Accepts `BulkAssignRequest(IReadOnlyList<Guid> BoxIds, Guid DriverId, string? Notes = null)`
- Returns `200 OK` with `BulkAssignBoxesResponseDto` and broadcasts real-time events.

- [x] **Step 1: Write endpoint test**

Add test verifying `POST /api/v1/dispatcher/orders/bulk-assign` handles validation and returns result.

- [x] **Step 2: Implement endpoint in controller**

Add `[HttpPost("orders/bulk-assign")]` in `DispatcherDashboardController.cs`.

- [x] **Step 3: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~BulkAssign"`
Expected: PASS.

- [x] **Step 4: Commit**

```bash
git add src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs
git commit -m "feat(api): expose bulk box assignment endpoint for mobile delivery manager"
```

---

### Task 5: Mobile Documentation & Full Solution Verification

**Files:**
- Modify: `mobile-screens/fcm-token-integration-guide.md`
- Modify: `mobile-screens/screen-04.03-dispatcher-order-queue.md`
- Solution-wide test runner

- [x] **Step 1: Update mobile screens documentation**

Document batch ready notifications and bulk assign in screen 04.03 and FCM guide.

- [x] **Step 2: Run full unit test suite**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj`
Expected: 1,160+ passing tests, 0 failed.

- [x] **Step 3: Verify build**

Run: `dotnet build src/MealMate.Application/MealMate.Application.csproj` and `src/MealMate.Persistence/MealMate.Persistence.csproj`

- [x] **Step 4: Update plan and final commit**

Mark all tasks as completed.
