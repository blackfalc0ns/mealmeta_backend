# MealMate Order Lifecycle & Custody Handover — TDD Implementation Plan

**Plan Identifier:** `PLAN-2026-09-28-ORDER-LIFECYCLE-HANDOVER`  
**Document Version:** `1.0.0-PROD-PLAN`  
**Date:** `2026-09-28`  
**Associated Spec:** [2026-09-28-order-lifecycle-and-handover-design.md](file:///F:/MealMate/backend/docs/superpowers/specs/2026-09-28-order-lifecycle-and-handover-design.md)  
**Execution Strategy:** Strict Test-Driven Development (Red $\rightarrow$ Green $\rightarrow$ Refactor)  
**Strict Policy:** Zero Mock / Fake / Placeholder Data in Production Code

---

## Plan Structure Overview

This implementation plan is organized into 13 autonomous, test-driven phases:
1. **Phase 1: Security & Multi-Tenant Authorization Foundation**
2. **Phase 2: Universal Application Transaction Abstraction**
3. **Phase 3: Barcode Domain Model & Canonical Source of Truth**
4. **Phase 4: Explicit State Machines & Ownership Guards**
5. **Phase 5: Concurrency-Safe Capacity Reservation & Atomic Transfer**
6. **Phase 6: Kitchen Preparation, Prep Documents & Barcode Generation**
7. **Phase 7: Dispatcher Trip Assembly & Vehicle Capacity Allocation**
8. **Phase 8: Driver Pickup Pre-Validation & Condition Photo Verification**
9. **Phase 9: Atomic Handover Persistence & Custody Transfer**
10. **Phase 10: Enterprise Outbox Event Pipeline & Live SignalR Broadcast**
11. **Phase 11: Admin Dashboard Overdue Orders Contract Implementation**
12. **Phase 12: Restaurant Dashboard & Driver Client Contract Alignment**
13. **Phase 13: End-to-End Pipeline & Concurrency Failure-Injection Tests**

---

## Phase 1: Security & Multi-Tenant Authorization Foundation

### Task 1.1: Enforce Restaurant Ownership and Eliminate Public Fallback

**Interfaces**
- **Consumes:** `ClaimsPrincipal`, `ICurrentUser`, `IAppDbContext`
- **Produces:** Authenticated and strictly tenant-scoped restaurant command execution
- **Preconditions:** Caller possesses valid JWT containing `restaurant_id` claim matching the target entity.
- **Invariants:** No unauthenticated request can reach restaurant handlers; no caller can inspect or mutate another restaurant's data via `overrideId`.

**Files**
- **Modify:** `src/MealMate.Api/Controllers/V1/RestaurantOrdersController.cs`
- **Modify:** `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`
- **Test:** `tests/MealMate.UnitTests/RestaurantOrders/RestaurantOrdersAuthorizationTests.cs`

**Execution Steps (2–5 minutes per step)**
- [ ] Write failing test `UnauthenticatedUser_CannotAccess_PendingConfirmation`:
  ```csharp
  [Fact]
  public async Task UnauthenticatedUser_CannotAccess_PendingConfirmation()
  {
      var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
      var response = await client.GetAsync("/api/v1/restaurants/orders/pending-confirmation");
      Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~RestaurantOrdersAuthorizationTests"
  ```
- [ ] Expected failure: Returns `200 OK` due to `[AllowAnonymous]` on line 12 of `RestaurantOrdersController.cs`.
- [ ] Minimal implementation:
  - In `RestaurantOrdersController.cs`: Remove `[AllowAnonymous]`, apply `[Authorize(Policy = "RestaurantStaffPolicy")]`.
  - In `RestaurantOrderHandlers.cs`: Remove fallback lines 63–71 (`_db.RestaurantProfiles.Select(r => r.Id).FirstOrDefaultAsync()`). Throw `UnauthorizedAccessException` if `_currentUser.RestaurantId` is null.
- [ ] Run test command and verify **PASS**.
- [ ] Run regression suite: `dotnet test --filter "Category=Auth"`.
- [ ] Self-review: Confirm zero mock data or placeholder fallbacks introduced.
- [ ] Git commit: `fix(auth): secure restaurant orders controller and eliminate unauthenticated fallback`

---

### Task 1.2: Enforce Dispatcher Policy on Fleet Management Endpoints

**Interfaces**
- **Consumes:** JWT with role `Dispatcher`, `DeliveryManager`, or `Admin`
- **Produces:** Authorized fleet assignments
- **Preconditions:** Caller authenticated with authorized fleet claims.
- **Invariants:** Zero anonymous access to driver tracking, manifests, or trip assignments.

**Files**
- **Modify:** `src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs`
- **Test:** `tests/MealMate.UnitTests/DriverDelivery/DispatcherAuthorizationTests.cs`

**Execution Steps**
- [ ] Write failing test `AnonymousCaller_AccessingAssignBox_ReturnsUnauthorized`:
  ```csharp
  [Fact]
  public async Task AnonymousCaller_AccessingAssignBox_ReturnsUnauthorized()
  {
      var client = _factory.CreateClient();
      var response = await client.PostAsJsonAsync("/api/v1/dispatcher/assign-box", new { });
      Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~DispatcherAuthorizationTests"
  ```
- [ ] Expected failure: Returns `200 OK` or `400 Bad Request` due to `[AllowAnonymous]` on line 14 of `DispatcherDashboardController.cs`.
- [ ] Minimal implementation: Remove `[AllowAnonymous]` on line 14, apply `[Authorize(Policy = "DispatcherPolicy")]`.
- [ ] Run test command and verify **PASS**.
- [ ] Run regression suite: `dotnet test --filter "Category=Dispatcher"`.
- [ ] Self-review: Clean architecture compliance.
- [ ] Git commit: `fix(auth): secure dispatcher endpoints with DispatcherPolicy`

---

## Phase 2: Universal Application Transaction Abstraction

### Task 2.1: Define and Register `IApplicationTransactionExecutor`

**Interfaces**
- **Consumes:** `Func<CancellationToken, Task<T>>`
- **Produces:** Execution of operations within an atomic PostgreSQL transaction utilizing EF Core execution strategies.
- **Preconditions:** PostgreSQL connection healthy.
- **Invariants:** All mutations in the delegate commit atomically; any uncaught exception triggers complete rollback.

**Files**
- **Create:** `src/MealMate.Application/Common/Abstractions/IApplicationTransactionExecutor.cs`
- **Create:** `src/MealMate.Persistence/Common/PostgresApplicationTransactionExecutor.cs`
- **Modify:** `src/MealMate.Persistence/DependencyInjection.cs`
- **Test:** `tests/MealMate.UnitTests/Infrastructure/TransactionExecutorTests.cs`

**Execution Steps**
- [ ] Write failing test `ExecuteAsync_WhenExceptionThrown_RollsBackAllMutations`:
  ```csharp
  [Fact]
  public async Task ExecuteAsync_WhenExceptionThrown_RollsBackAllMutations()
  {
      var executor = _services.GetRequiredService<IApplicationTransactionExecutor>();
      var db = _services.GetRequiredService<IAppDbContext>();
      var testId = Guid.NewGuid();

      await Assert.ThrowsAsync<InvalidOperationException>(async () =>
      {
          await executor.ExecuteAsync(async ct =>
          {
              db.MealBarcodes.Add(new MealBarcode { Id = testId, BarcodeValue = "TEST-ROLLBACK" });
              await db.SaveChangesAsync(ct);
              throw new InvalidOperationException("Simulated Failure");
          });
      });

      var exists = await db.MealBarcodes.AnyAsync(b => b.Id == testId);
      Assert.False(exists);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~TransactionExecutorTests"
  ```
- [ ] Expected failure: `IApplicationTransactionExecutor` interface not found in DI.
- [ ] Minimal implementation:
  - Create interface in `MealMate.Application.Common.Abstractions`:
    ```csharp
    public interface IApplicationTransactionExecutor
    {
        Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
        Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
    }
    ```
  - Create implementation in `MealMate.Persistence.Common`:
    ```csharp
    public sealed class PostgresApplicationTransactionExecutor : IApplicationTransactionExecutor
    {
        private readonly AppDbContext _db;
        public PostgresApplicationTransactionExecutor(AppDbContext db) => _db = db;

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                var result = await operation(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return result;
            });
        }

        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => ExecuteAsync(async ct => { await operation(ct); return true; }, cancellationToken);
    }
    ```
  - Register as scoped in `Persistence/DependencyInjection.cs`.
- [ ] Run test command and verify **PASS**.
- [ ] Self-review: Confirm no hardcoded connections.
- [ ] Git commit: `feat(persistence): introduce IApplicationTransactionExecutor for universal atomic execution`

---

## Phase 3: Barcode Domain Model & Canonical Source of Truth

### Task 3.1: Expand `MealBarcode` and Associate Foreign Key with `TripStop`

**Interfaces**
- **Consumes:** `SubscriptionDayId`, `ServiceDate`, `OrderId`
- **Produces:** Canonical `MealBarcode` record linked to `TripStop`
- **Preconditions:** Subscription day exists and is locked.
- **Invariants:** `MealBarcode.BarcodeValue` is unique system-wide; `TripStop.MealBarcodeId` references valid `MealBarcode`.

**Files**
- **Modify:** `src/MealMate.Domain/RestaurantOperations/MealBarcode.cs`
- **Modify:** `src/MealMate.Domain/DriverDelivery/TripStop.cs`
- **Modify:** `src/MealMate.Persistence/Configurations/RestaurantOperations/MealBarcodeConfiguration.cs`
- **Modify:** `src/MealMate.Persistence/Configurations/DriverDelivery/DriverDeliveryConfigurations.cs`
- **Test:** `tests/MealMate.UnitTests/Domain/MealBarcodeCanonicalTests.cs`

**Execution Steps**
- [ ] Write failing test `TripStop_CanLinkTo_CanonicalMealBarcode`:
  ```csharp
  [Fact]
  public void TripStop_CanLinkTo_CanonicalMealBarcode()
  {
      var barcode = new MealBarcode
      {
          Id = Guid.NewGuid(),
          BarcodeValue = "MM-20260928-8F12C9B4",
          Status = "Generated"
      };
      var stop = new TripStop
      {
          Id = Guid.NewGuid(),
          MealBarcodeId = barcode.Id,
          OrderCode = "#BX-1001"
      };

      Assert.Equal(barcode.Id, stop.MealBarcodeId);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~MealBarcodeCanonicalTests"
  ```
- [ ] Expected failure: Compilation error — `MealBarcodeId` property does not exist on `TripStop`.
- [ ] Minimal implementation:
  - Add `MealBarcodeId` and navigation property `MealBarcode? MealBarcode` to `TripStop.cs`.
  - Add lifecycle properties (`Status`, `PrintedAtUtc`, `ScannedAtUtc`, `IsRevoked`, `RevokedAtUtc`) to `MealBarcode.cs`.
  - Configure EF Core relationships in `DriverDeliveryConfigurations.cs`.
- [ ] Run test command and verify **PASS**.
- [ ] Self-review: Verify zero mock data.
- [ ] Git commit: `feat(domain): link TripStop to canonical MealBarcode entity`

---

## Phase 4: Explicit State Machines & Ownership Guards

### Task 4.1: Enforce Strict Domain State Transition Validation on `SubscriptionDay`

**Interfaces**
- **Consumes:** Current `SubscriptionDayStatus`, Target `SubscriptionDayStatus`
- **Produces:** Success `Result` or domain error `INVALID_STATE_TRANSITION`
- **Preconditions:** Day is in an authorized prior status.
- **Invariants:** `SubscriptionDay` never jumps illegal steps (e.g. `Open` directly to `Delivered`).

**Files**
- **Modify:** `src/MealMate.Domain/CustomerSubscriptions/SubscriptionDay.cs`
- **Test:** `tests/MealMate.UnitTests/Domain/SubscriptionDayStateMachineTests.cs`

**Execution Steps**
- [ ] Write failing test `TransitionTo_IllegalStatus_ThrowsDomainException`:
  ```csharp
  [Theory]
  [InlineData(SubscriptionDayStatus.Open, SubscriptionDayStatus.Delivered)]
  [InlineData(SubscriptionDayStatus.Locked72h, SubscriptionDayStatus.OutForDelivery)]
  public void TransitionTo_IllegalStatus_ReturnsFailure(SubscriptionDayStatus from, SubscriptionDayStatus to)
  {
      var day = new SubscriptionDay { Status = from };
      var result = day.CanTransitionTo(to);
      Assert.False(result.IsSuccess);
      Assert.Equal("INVALID_STATE_TRANSITION", result.Error.Code);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~SubscriptionDayStateMachineTests"
  ```
- [ ] Expected failure: Method `CanTransitionTo` does not exist on `SubscriptionDay`.
- [ ] Minimal implementation: Implement `CanTransitionTo(SubscriptionDayStatus target)` on `SubscriptionDay` matching the state machine matrix defined in Section 6.1 of the design spec.
- [ ] Run test command and verify **PASS**.
- [ ] Run regression suite: `dotnet test --filter "Category=SubscriptionLifecycle"`.
- [ ] Git commit: `feat(domain): implement SubscriptionDay strict state machine guard`

---

## Phase 5: Concurrency-Safe Capacity Reservation & Atomic Transfer

### Task 5.1: Atomic Capacity Transfer on Customer Restaurant Selection

**Interfaces**
- **Consumes:** `SubscriptionDayId`, `NewRestaurantId`, `ServiceDate`
- **Produces:** Atomic transfer of capacity hold from old restaurant to new restaurant
- **Preconditions:** New restaurant has available capacity on `ServiceDate`.
- **Invariants:** If new restaurant is full, old restaurant capacity is untouched and transaction aborts with `RESTAURANT_CAPACITY_FULL`.

**Files**
- **Modify:** `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarEngine.cs`
- **Test:** `tests/MealMate.UnitTests/CustomerCalendar/CapacityTransferTests.cs`

**Execution Steps**
- [ ] Write failing test `SelectRestaurant_WhenNewRestaurantFull_AbortsWithoutWipingSelections`:
  ```csharp
  [Fact]
  public async Task SelectRestaurant_WhenNewRestaurantFull_AbortsWithoutWipingSelections()
  {
      var oldRestId = Guid.NewGuid();
      var newRestId = Guid.NewGuid();
      var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
      var day = await CreateTestDayAsync(oldRestId, date);

      // Seed New Restaurant at maximum capacity
      await FillRestaurantCapacityAsync(newRestId, date, maxBoxes: 50);

      var result = await _engine.SelectRestaurantAsync(day.SubscriptionId, date, newRestId, CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal("RESTAURANT_CAPACITY_FULL", result.Error.Code);

      // Verify day.RestaurantId remains oldRestId
      var reloadedDay = await _db.SubscriptionDays.FindAsync(day.Id);
      Assert.Equal(oldRestId, reloadedDay.RestaurantId);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~CapacityTransferTests"
  ```
- [ ] Expected failure: `SelectRestaurantAsync` currently overwrites `day.RestaurantId` unconditionally on line 265 without checking capacity.
- [ ] Minimal implementation:
  - Add capacity check with PostgreSQL advisory lock `SELECT pg_advisory_xact_lock(...)` ordered by restaurant ID to prevent deadlocks.
  - If capacity exceeded, return `Result.Failure(Error.Conflict("RESTAURANT_CAPACITY_FULL", "..."))`.
- [ ] Run test command and verify **PASS**.
- [ ] Run regression suite: `dotnet test --filter "Category=Capacity"`.
- [ ] Git commit: `fix(calendar): enforce deadlock-free atomic capacity check during restaurant change`

---

### Task 5.2: Eliminate Random Meal Fallback in Order Dispatch

**Interfaces**
- **Consumes:** `SubscriptionDay` without meal selections
- **Produces:** Safe auto-selection or transition to `ReplacementRequired`
- **Preconditions:** Customer calendar deadline expired.
- **Invariants:** Under zero circumstances may an arbitrary meal from `_db.Meals.FirstOrDefaultAsync()` be selected.

**Files**
- **Modify:** `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`
- **Test:** `tests/MealMate.UnitTests/RestaurantOrders/DispatchDailyOrdersTests.cs`

**Execution Steps**
- [ ] Write failing test `DispatchOrders_WhenAutoSelectionFails_DoesNotAssignRandomMeal`:
  ```csharp
  [Fact]
  public async Task DispatchOrders_WhenAutoSelectionFails_DoesNotAssignRandomMeal()
  {
      var day = await CreateDayWithAllergyConflictAsync();
      var handler = _services.GetRequiredService<DispatchDailyOrdersHandler>();

      var result = await handler.Handle(new DispatchDailyOrdersCommand(day.ServiceDate), CancellationToken.None);

      var selections = await _db.DailyMealSelections.Where(s => s.SubscriptionDayId == day.Id).ToListAsync();
      Assert.Empty(selections);

      var reloadedDay = await _db.SubscriptionDays.FindAsync(day.Id);
      Assert.Equal(SubscriptionDayStatus.ReplacementRequired, reloadedDay.Status);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~DispatchDailyOrdersTests"
  ```
- [ ] Expected failure: Currently inserts `_db.Meals.FirstOrDefaultAsync()` on lines 1300–1311 of `RestaurantOrderHandlers.cs`.
- [ ] Minimal implementation:
  - Delete `catch` block on lines 1298–1311.
  - If `AutoSelectFullBoxAsync` fails, set `day.Status = SubscriptionDayStatus.ReplacementRequired` and emit `CustomerReplacementRequiredDomainEvent`.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `fix(fulfillment): eradicate random meal fallback in compliance with zero mock data policy`

---

## Phase 6: Kitchen Preparation, Prep Documents & Barcode Generation

### Task 6.1: Canonical Barcode Generation during Prep Document Rendering

**Interfaces**
- **Consumes:** `DailyOrderId`, `SubscriptionDayId`, `ServiceDate`
- **Produces:** `MealBarcode` with `BarcodeValue = $"MM-{ServiceDate:yyyyMMdd}-{SubscriptionDayId:N}"`
- **Preconditions:** Order confirmed by restaurant.
- **Invariants:** Exactly one active barcode per subscription day; barcode value strictly formatted.

**Files**
- **Modify:** `src/MealMate.Application/Features/PrepDocuments/PrepDocumentsGenerator.cs`
- **Test:** `tests/MealMate.UnitTests/PrepDocuments/PrepDocumentsGeneratorTests.cs`

**Execution Steps**
- [ ] Write failing test `GeneratePrepDocument_CreatesAndPersists_CanonicalMealBarcode`:
  ```csharp
  [Fact]
  public async Task GeneratePrepDocument_CreatesAndPersists_CanonicalMealBarcode()
  {
      var order = await CreateConfirmedRestaurantDailyOrderAsync();
      var generator = _services.GetRequiredService<PrepDocumentsGenerator>();

      var doc = await generator.GenerateForOrderAsync(order.Id, CancellationToken.None);

      var barcode = await _db.MealBarcodes.FirstOrDefaultAsync(b => b.SubscriptionDayId == order.SubscriptionDayId);
      Assert.NotNull(barcode);
      Assert.Equal($"MM-{order.ServiceDate:yyyyMMdd}-{order.SubscriptionDayId:N}", barcode.BarcodeValue);
      Assert.Equal("Generated", barcode.Status);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~PrepDocumentsGeneratorTests"
  ```
- [ ] Expected failure: Barcode entity generated with missing lifecycle status or unlinked order reference.
- [ ] Minimal implementation: In `PrepDocumentsGenerator.cs`, ensure `MealBarcode` is saved with `Status = "Generated"`, `DailyOrderId = order.Id`, and `GeneratedAtUtc = _clock.UtcNow`.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `feat(prep): persist canonical MealBarcode during prep document creation`

---

## Phase 7: Dispatcher Trip Assembly & Vehicle Capacity Allocation

### Task 7.1: Link `TripStop` to `MealBarcode` and Calculate Vehicle Load by Total BoxCount

**Interfaces**
- **Consumes:** `TripId`, `SubscriptionDayId`, `DriverProfileId`
- **Produces:** `TripStop` with valid `MealBarcodeId` and validated vehicle `BoxCount` capacity
- **Preconditions:** Box is `ReadyForPickup`; driver has sufficient vehicle capacity.
- **Invariants:** Total `BoxCount` across trip stops does not exceed `DriverVehicle.MaxBoxCapacity`.

**Files**
- **Modify:** `src/MealMate.Application/Features/DriverDelivery/Dispatcher/AssignBoxToDriverCommand.cs`
- **Test:** `tests/MealMate.UnitTests/DriverDelivery/AssignBoxToDriverTests.cs`

**Execution Steps**
- [ ] Write failing test `AssignBox_WhenVehicleBoxCapacityExceeded_ReturnsError`:
  ```csharp
  [Fact]
  public async Task AssignBox_WhenVehicleBoxCapacityExceeded_ReturnsError()
  {
      var driver = await CreateDriverWithVehicleCapacityAsync(maxBoxes: 10);
      var trip = await CreateTripForDriverAsync(driver.Id);
      await SeedStopsWithBoxesAsync(trip.Id, currentBoxCount: 9);

      var multiBoxDay = await CreateSubscriptionDayAsync(boxCount: 2); // 9 + 2 = 11 > 10

      var handler = _services.GetRequiredService<AssignBoxToDriverCommandHandler>();
      var result = await handler.Handle(new AssignBoxToDriverCommand(trip.Id, multiBoxDay.Id), CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal("VEHICLE_CAPACITY_EXCEEDED", result.Error.Code);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~AssignBoxToDriverTests"
  ```
- [ ] Expected failure: `AssignBoxToDriverCommand` calculates capacity using `stops.Count` instead of `stops.Sum(s => s.BoxCount)`.
- [ ] Minimal implementation:
  - In `AssignBoxToDriverCommand.cs`: Calculate `currentLoad = trip.Stops.Sum(s => s.BoxCount)`. If `currentLoad + newBoxCount > vehicle.MaxBoxes`, return error.
  - Find `MealBarcode` by `SubscriptionDayId` and set `stop.MealBarcodeId = barcode.Id`.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `fix(dispatcher): enforce vehicle BoxCount capacity and bind MealBarcodeId`

---

## Phase 8: Driver Pickup Pre-Validation & Condition Photo Verification

### Task 8.1: Resolve Scanned Barcode via Canonical `MealBarcode`

**Interfaces**
- **Consumes:** Scanned raw string (`BarcodeValue`) from mobile camera
- **Produces:** `PickupValidation` token valid for 5 minutes
- **Preconditions:** Barcode exists, is not revoked, and is assigned to the requesting driver's trip.
- **Invariants:** Returns `BARCODE_NOT_FOUND` if barcode does not exist; returns `BOX_NOT_ASSIGNED_TO_YOUR_TRIP` if assigned to another driver.

**Files**
- **Modify:** `src/MealMate.Application/Features/DriverDelivery/DriverApp/ValidatePickupBarcodeCommand.cs`
- **Test:** `tests/MealMate.UnitTests/DriverDelivery/ValidatePickupBarcodeTests.cs`

**Execution Steps**
- [ ] Write failing test `ValidateBarcode_WithPrintedCanonicalFormat_MatchesCorrectly`:
  ```csharp
  [Fact]
  public async Task ValidateBarcode_WithPrintedCanonicalFormat_MatchesCorrectly()
  {
      var (driver, trip, stop, barcode) = await SeedActiveTripWithCanonicalBarcodeAsync();
      var handler = _services.GetRequiredService<ValidatePickupBarcodeCommandHandler>();

      var result = await handler.Handle(
          new ValidatePickupBarcodeCommand(barcode.BarcodeValue, IsManualEntry: false), 
          CancellationToken.None);

      Assert.True(result.IsSuccess);
      Assert.Equal(stop.SubscriptionDayId, result.Value.BoxId);
      Assert.False(string.IsNullOrWhiteSpace(result.Value.ValidationToken));
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~ValidatePickupBarcodeTests"
  ```
- [ ] Expected failure: Fails with `BARCODE_NOT_FOUND` because handler searches `TripStop.OrderCode` instead of `MealBarcode.BarcodeValue`.
- [ ] Minimal implementation:
  - Query `MealBarcode` by `BarcodeValue`.
  - Validate that `MealBarcode.Status != "Revoked"`.
  - Join with `TripStop` on `stop.MealBarcodeId == barcode.Id` and `stop.TripId == currentDriverTrip.Id`.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `fix(driver): resolve scanner barcode against canonical MealBarcode`

---

## Phase 9: Atomic Handover Persistence & Custody Transfer

### Task 9.1: Implement Complete Atomic Pickup Confirmation Transaction

**Interfaces**
- **Consumes:** `boxId`, `ValidationToken`, `ConditionPhotoStorageKey`, `IdempotencyKey`
- **Produces:** Atomic creation of `OrderHandover`, state update across `TripStop`, `SubscriptionDay`, `RestaurantDailyOrder`, and `PickupScan`.
- **Preconditions:** Valid unconsumed token; approved photo storage key; stop is `Pending`.
- **Invariants:** 100% all-or-nothing database commit; single execution per `Idempotency-Key`.

**Files**
- **Modify:** `src/MealMate.Application/Features/DriverDelivery/DriverApp/ConfirmPickupBoxCommand.cs`
- **Test:** `tests/MealMate.UnitTests/DriverDelivery/ConfirmPickupBoxAtomicTests.cs`

**Execution Steps**
- [ ] Write failing test `ConfirmPickup_AtomicallyUpdates_AllAggregatesAndInsertsHandover`:
  ```csharp
  [Fact]
  public async Task ConfirmPickup_AtomicallyUpdates_AllAggregatesAndInsertsHandover()
  {
      var context = await SeedValidatedBoxReadyForPickupAsync();
      var handler = _services.GetRequiredService<ConfirmPickupBoxCommandHandler>();

      var command = new ConfirmPickupBoxCommand(
          BoxId: context.BoxId,
          ValidationToken: context.ValidationToken,
          ConditionPhotoStorageKey: "photos/valid.jpg",
          Latitude: 24.77,
          Longitude: 46.73,
          IdempotencyKey: Guid.NewGuid().ToString());

      var result = await handler.Handle(command, CancellationToken.None);

      Assert.True(result.IsSuccess);

      // Assert TripStop is PickedUp
      var stop = await _db.TripStops.FindAsync(context.TripStopId);
      Assert.Equal(TripStopStatus.PickedUp, stop.Status);

      // Assert SubscriptionDay is OutForDelivery
      var day = await _db.SubscriptionDays.FindAsync(context.BoxId);
      Assert.Equal(SubscriptionDayStatus.OutForDelivery, day.Status);

      // Assert RestaurantDailyOrder is OnTheWay
      var order = await _db.RestaurantDailyOrders.FindAsync(context.DailyOrderId);
      Assert.Equal(OrderStatus.OnTheWay, order.Status);

      // Assert OrderHandover is Persisted
      var handover = await _db.OrderHandovers.FirstOrDefaultAsync(h => h.DailyOrderId == order.Id);
      Assert.NotNull(handover);
      Assert.Equal("Completed", handover.Status);
      Assert.Equal(1, handover.AttemptNumber);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~ConfirmPickupBoxAtomicTests"
  ```
- [ ] Expected failure: `SubscriptionDay.Status` remains `Preparing`; `OrderHandover` is not inserted.
- [ ] Minimal implementation: In `ConfirmPickupBoxCommand.cs`, wrap operations inside `_transactionExecutor.ExecuteAsync(...)`, mutating `SubscriptionDay`, `RestaurantDailyOrder`, and inserting `OrderHandover` as specified in Design Spec Section 7.1.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `feat(handover): execute atomic pickup confirmation across all aggregates`

---

## Phase 10: Enterprise Outbox Event Pipeline & Live SignalR Broadcast

### Task 10.1: Standardize `OutboxMessage` and Implement Background Polling Worker

**Interfaces**
- **Consumes:** Domain events serialized to JSON
- **Produces:** Guaranteed at-least-once dispatch to MediatR notification handlers and SignalR hubs.
- **Preconditions:** Transaction committed to PostgreSQL.
- **Invariants:** Poison messages quarantined after 5 failed retry attempts.

**Files**
- **Modify:** `src/MealMate.Domain/Common/OutboxMessage.cs`
- **Create:** `src/MealMate.Application/Common/Outbox/IOutboxService.cs`
- **Create:** `src/MealMate.Persistence/Outbox/OutboxBackgroundJob.cs`
- **Test:** `tests/MealMate.UnitTests/Outbox/OutboxPipelineTests.cs`

**Execution Steps**
- [ ] Write failing test `OutboxJob_DispatchesPendingEvents_AndMarksProcessed`:
  ```csharp
  [Fact]
  public async Task OutboxJob_DispatchesPendingEvents_AndMarksProcessed()
  {
      var message = new OutboxMessage
      {
          Id = Guid.NewGuid(),
          EventType = "TestHandoverEvent",
          AggregateType = "OrderHandover",
          AggregateId = Guid.NewGuid().ToString(),
          CorrelationId = Guid.NewGuid().ToString(),
          DeduplicationKey = Guid.NewGuid().ToString(),
          PayloadJson = "{}",
          CreatedAtUtc = DateTime.UtcNow
      };
      _db.OutboxMessages.Add(message);
      await _db.SaveChangesAsync();

      var worker = _services.GetRequiredService<OutboxBackgroundJob>();
      await worker.ProcessPendingMessagesAsync(CancellationToken.None);

      var reloaded = await _db.OutboxMessages.FindAsync(message.Id);
      Assert.NotNull(reloaded.ProcessedAtUtc);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~OutboxPipelineTests"
  ```
- [ ] Expected failure: `OutboxBackgroundJob` missing or fields not implemented on `OutboxMessage`.
- [ ] Minimal implementation: Expand `OutboxMessage` entity with `AggregateType`, `AggregateId`, `CorrelationId`, and `DeduplicationKey`. Implement background polling job with exponential backoff.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `feat(outbox): generalize outbox message processing with retry and deduplication`

---

## Phase 11: Admin Dashboard Overdue Orders Contract Implementation

### Task 11.1: Implement `AdminOrdersController` for Overdue Order Escalation

**Interfaces**
- **Consumes:** `GET /api/v1/admin/orders/confirmation-overdue`
- **Produces:** Paginated list of restaurant orders exceeding 30-minute confirmation SLA
- **Preconditions:** Caller is authenticated with `AdminPolicy`.
- **Invariants:** Returns zero mock rows; strictly reflects database orders in `AwaitingRestaurantConfirmation`.

**Files**
- **Create:** `src/MealMate.Api/Controllers/V1/AdminOrdersController.cs`
- **Create:** `src/MealMate.Application/Features/AdminOrders/GetConfirmationOverdueOrdersQuery.cs`
- **Test:** `tests/MealMate.UnitTests/AdminOrders/AdminOrdersControllerTests.cs`

**Execution Steps**
- [ ] Write failing test `GetConfirmationOverdue_ReturnsOrders_ExceedingSla`:
  ```csharp
  [Fact]
  public async Task GetConfirmationOverdue_ReturnsOrders_ExceedingSla()
  {
      var overdueOrder = await CreateOrderAwaitingConfirmationAsync(dispatchedMinutesAgo: 40);
      var freshOrder = await CreateOrderAwaitingConfirmationAsync(dispatchedMinutesAgo: 10);

      var client = CreateAdminAuthenticatedClient();
      var response = await client.GetAsync("/api/v1/admin/orders/confirmation-overdue");

      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      var payload = await response.Content.ReadFromJsonAsync<PagedResult<OverdueOrderDto>>();
      Assert.Contains(payload.Items, x => x.OrderId == overdueOrder.Id);
      Assert.DoesNotContain(payload.Items, x => x.OrderId == freshOrder.Id);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~AdminOrdersControllerTests"
  ```
- [ ] Expected failure: `404 Not Found` — route does not exist.
- [ ] Minimal implementation:
  - Create `AdminOrdersController` with `[Authorize(Policy = "AdminPolicy")]`.
  - Implement `GetConfirmationOverdueOrdersQueryHandler` querying `RestaurantDailyOrders` where `Status == OrderStatus.AwaitingRestaurantConfirmation` and `DispatchedAtUtc <= UtcNow.AddMinutes(-30)`.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `feat(admin): implement confirmation-overdue monitoring endpoint`

---

## Phase 12: Restaurant Dashboard & Driver Client Contract Alignment

### Task 12.1: Align Scanner Request/Response Contracts with Driver Mobile App

**Interfaces**
- **Consumes:** Viewfinder camera text payload
- **Produces:** Cryptographic pre-validation token and box metadata
- **Preconditions:** Mobile app transmits unmodified barcode standard 128 / QR payload.
- **Invariants:** 100% backward-compatible with mobile driver app schema.

**Files**
- **Modify:** `src/MealMate.Api/Controllers/V1/DriverPickupController.cs`
- **Test:** `tests/MealMate.UnitTests/DriverDelivery/DriverPickupControllerContractTests.cs`

**Execution Steps**
- [ ] Write failing contract test `ValidateBarcode_ReturnsExpectedContractProperties`:
  ```csharp
  [Fact]
  public async Task ValidateBarcode_ReturnsExpectedContractProperties()
  {
      var context = await SeedValidatedBoxReadyForPickupAsync();
      var client = CreateDriverAuthenticatedClient(context.DriverId);

      var response = await client.PostAsJsonAsync("/api/v1/driver/pickup/validate-barcode", new
      {
          barcodeValue = context.BarcodeValue,
          isManualEntry = false
      });

      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      var json = await response.Content.ReadFromJsonAsync<JsonElement>();
      Assert.True(json.TryGetProperty("boxId", out _));
      Assert.True(json.TryGetProperty("orderCode", out _));
      Assert.True(json.TryGetProperty("validationToken", out _));
      Assert.True(json.TryGetProperty("expiresAtUtc", out _));
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.UnitTests\MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverPickupControllerContractTests"
  ```
- [ ] Expected failure: Property casing or response contract mismatch.
- [ ] Minimal implementation: Verify `ValidatePickupBarcodeResponseDto` strictly maps `boxId`, `orderCode`, `validationToken`, and `expiresAtUtc`.
- [ ] Run test command and verify **PASS**.
- [ ] Git commit: `test(contract): verify driver pickup barcode validation response contract`

---

## Phase 13: End-to-End Pipeline & Concurrency Failure-Injection Tests

### Task 13.1: True End-to-End Order Lifecycle Integration Test

**Interfaces**
- **Consumes:** Customer checkout webhook through driver handover confirmation
- **Produces:** Fully verified delivery box in `OutForDelivery` with immutable `OrderHandover`
- **Preconditions:** Authentic test PostgreSQL database instance running.
- **Invariants:** Zero mock data; production pipeline executed via real mediator commands and controllers.

**Files**
- **Create:** `tests/MealMate.IntegrationTests/Lifecycle/EndToEndOrderLifecycleTests.cs`

**Execution Steps**
- [ ] Write integration test `CompleteOrderLifecycle_CheckoutToHandover_SucceedsDeterministically`:
  ```csharp
  [Fact]
  public async Task CompleteOrderLifecycle_CheckoutToHandover_SucceedsDeterministically()
  {
      // 1. Payment Webhook Ingestion
      var subId = await SimulateSuccessfulPaymentWebhookAsync();

      // 2. Calendar Selection
      var targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
      await SelectMealsForDayAsync(subId, targetDate);

      // 3. 72h Cutoff Execution
      await Trigger72hLockJobAsync();

      // 4. Kitchen Confirmation & Cooking
      var dailyOrderId = await ConfirmKitchenOrderAsync(targetDate);
      await AdvanceKitchenPrepStatusAsync(dailyOrderId, OrderStatus.ReadyForPickup);

      // 5. Trip Assignment
      var (tripId, stopId, barcodeValue) = await DispatcherAssignBoxAsync(dailyOrderId);

      // 6. Driver Viewfinder Scan
      var validationToken = await DriverScanBarcodeAsync(barcodeValue);

      // 7. Photo Upload & Handover Confirmation
      var handoverResponse = await DriverConfirmPickupAsync(stopId, validationToken);

      Assert.NotNull(handoverResponse.HandoverId);
      Assert.Equal("PickedUp", handoverResponse.Status);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.IntegrationTests\MealMate.IntegrationTests.csproj --filter "FullyQualifiedName~EndToEndOrderLifecycleTests"
  ```
- [ ] Verify **PASS** across the entire pipeline.
- [ ] Git commit: `test(e2e): verify complete checkout-to-handover order lifecycle`

---

### Task 13.2: Concurrent Driver Scan Race-Condition Verification

**Interfaces**
- **Consumes:** Two concurrent pickup confirmation requests for the same box from different drivers
- **Produces:** Exactly one successful handover; exactly one `409 Conflict` error
- **Preconditions:** Box is ready for pickup.
- **Invariants:** Database never creates duplicate `OrderHandover` records or double-allocates boxes.

**Files**
- **Create:** `tests/MealMate.IntegrationTests/Concurrency/ConcurrentPickupRaceTests.cs`

**Execution Steps**
- [ ] Write concurrency race test `ConcurrentDriverPickup_ExactlyOneSucceeds`:
  ```csharp
  [Fact]
  public async Task ConcurrentDriverPickup_ExactlyOneSucceeds()
  {
      var context = await SeedValidatedBoxReadyForPickupAsync();
      var barrier = new Barrier(2);

      var task1 = Task.Run(() => AttemptPickupAsync(context, driverId1, barrier));
      var task2 = Task.Run(() => AttemptPickupAsync(context, driverId2, barrier));

      var responses = await Task.WhenAll(task1, task2);

      var successCount = responses.Count(r => r.IsSuccess);
      var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

      Assert.Equal(1, successCount);
      Assert.Equal(1, conflictCount);

      // Verify DB constraint
      var handovers = await _db.OrderHandovers.Where(h => h.DailyOrderId == context.DailyOrderId).ToListAsync();
      Assert.Single(handovers);
  }
  ```
- [ ] Run test command:
  ```powershell
  dotnet test F:\MealMate\backend\tests\MealMate.IntegrationTests\MealMate.IntegrationTests.csproj --filter "FullyQualifiedName~ConcurrentPickupRaceTests"
  ```
- [ ] Verify **PASS**.
- [ ] Git commit: `test(concurrency): verify atomic lock prevents duplicate driver pickup`

---

## Pre-Execution Verification Checklist

Before commencing implementation of any task, ensure:
1. `git status` in `F:\MealMate\backend` remains completely clean.
2. Unstaged changes in `frontend/admin-dashboard` and `frontend/restaurant-dashboard` remain untouched.
3. No migration files or production source code files have been written.
4. All four `BLOCKING BUSINESS DECISIONS` in Section 22 of the Design Spec are submitted to stakeholders for binding decisions.
