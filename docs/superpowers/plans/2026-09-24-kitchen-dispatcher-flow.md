# Kitchen-to-Dispatcher Lifecycle Integration & Notification System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bridge confirmed and ready kitchen boxes directly into the Dispatcher assignment queue, enable instant atomic Trip creation upon assignment, dispatch real-time SignalR and FCM push notifications, and automate fallback replacement for rejected/escalated orders.

**Architecture:** 
A Clean Architecture integration spanning Application, Infrastructure, and API layers. The Dispatcher Queue dynamically aggregates confirmed and ready unassigned `SubscriptionDays` alongside active `TripStops`, while `AssignBoxToDriverCommand` transparently provisions or appends to a driver's `Trip`. SignalR broadcasts notify dispatchers of ready kitchen batches, and a dedicated `IDriverPushNotifier` sends FCM high-priority push notifications to driver devices.

**Tech Stack:** .NET 9, C# 13, EF Core 9, SignalR, Firebase Cloud Messaging (FCM), xUnit, FluentAssertions, Moq.

**Spec:** [`docs/superpowers/specs/2026-09-24-kitchen-dispatcher-flow-design.md`](file:///f:/MealMate/backend/docs/superpowers/specs/2026-09-24-kitchen-dispatcher-flow-design.md)

## Global Constraints
- Do not introduce breaking API changes for existing clients (preserve endpoint URLs and response models).
- All new database queries must support multi-tenant isolation by `RestaurantId`.
- Preserve existing Unit Test coverage across `DriverDelivery`, `CustomerCalendar`, and `RestaurantOrders`.
- Real-time events and FCM notifications must fail gracefully without rolling back the primary database transaction.

---

### Task 1: 72h Lock Order Provisioning & ReadyForPickup State Synchronization

**Files:**
- Modify: `src/MealMate.Application/Features/CustomerCalendar/SubscriptionDayDispatchService.cs:40-95`
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs:1268-1313`
- Test: `tests/MealMate.UnitTests/CustomerCalendar/SubscriptionDayDispatchServiceTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext`, `IDateTimeProvider`, `IRestaurantOrderNotifier`
- Produces: `RestaurantDailyOrder` records created at −72h lock with `OrderStatus.AwaitingRestaurantConfirmation`.

- [ ] **Step 1: Write unit tests verifying RestaurantDailyOrder is created upon 72h lock**

Create `tests/MealMate.UnitTests/CustomerCalendar/SubscriptionDayDispatchServiceTests.cs`:
```csharp
[Fact]
public async Task LockAndSendToRestaurantAsync_CreatesRestaurantDailyOrder_WhenNotExists()
{
    // Arrange
    var db = CreateInMemoryDbContext();
    var clock = new Mock<IDateTimeProvider>();
    clock.Setup(c => c.UtcNow).Returns(DateTime.UtcNow);
    var deliveryService = new Mock<ICustomerDeliveryAddressService>();
    var service = new SubscriptionDayDispatchService(db, clock.Object, deliveryService.Object);

    var sub = new Subscription { Id = Guid.NewGuid(), CustomerUserId = Guid.NewGuid(), Status = SubscriptionStatus.Active };
    db.Subscriptions.Add(sub);
    var day = new SubscriptionDay
    {
        Id = Guid.NewGuid(),
        SubscriptionId = sub.Id,
        RestaurantId = Guid.NewGuid(),
        ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
        Status = SubscriptionDayStatus.Open
    };
    db.SubscriptionDays.Add(day);
    await db.SaveChangesAsync();

    // Act
    await service.LockAndSendToRestaurantAsync(day, sub.Id, CancellationToken.None);
    await db.SaveChangesAsync();

    // Assert
    var dailyOrder = await db.RestaurantDailyOrders.FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id);
    dailyOrder.Should().NotBeNull();
    dailyOrder!.RestaurantId.Should().Be(day.RestaurantId.Value);
    dailyOrder.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~SubscriptionDayDispatchServiceTests" -v minimal`
Expected: FAIL with `dailyOrder.Should().NotBeNull()` failing.

- [ ] **Step 3: Update SubscriptionDayDispatchService to provision RestaurantDailyOrder**

In `src/MealMate.Application/Features/CustomerCalendar/SubscriptionDayDispatchService.cs`, add creation of `RestaurantDailyOrder`:
```csharp
if (day.RestaurantId is { } targetRestaurantId)
{
    var existingDailyOrder = await _db.RestaurantDailyOrders
        .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id && r.RestaurantId == targetRestaurantId, cancellationToken);

    if (existingDailyOrder is null)
    {
        _db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            RestaurantId = targetRestaurantId,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.AwaitingRestaurantConfirmation,
            BoxCount = 1,
            CreatedAtUtc = _clock.UtcNow
        });
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~SubscriptionDayDispatchServiceTests"`
Expected: PASS

- [ ] **Step 5: Commit changes**

```bash
git add src/MealMate.Application/Features/CustomerCalendar/SubscriptionDayDispatchService.cs tests/MealMate.UnitTests/CustomerCalendar/SubscriptionDayDispatchServiceTests.cs
git commit -m "feat(calendar): auto-provision RestaurantDailyOrder on 72h lock"
```

---

### Task 2: Driver Push Notification Service (`IDriverPushNotifier` & `FirebaseDriverPushNotifier`)

**Files:**
- Create: `src/MealMate.Application/Common/Abstractions/IDriverPushNotifier.cs`
- Create: `src/MealMate.Infrastructure/Push/FirebaseDriverPushNotifier.cs`
- Modify: `src/MealMate.Infrastructure/DependencyInjection.cs`
- Test: `tests/MealMate.UnitTests/DriverDelivery/DriverPushNotifierTests.cs`

**Interfaces:**
- Consumes: `IFirebaseMessagingClient`, `IAppDbContext` (`DriverDeviceTokens`)
- Produces: `IDriverPushNotifier` available for DI injection.

- [ ] **Step 1: Write test for FirebaseDriverPushNotifier**

Create `tests/MealMate.UnitTests/DriverDelivery/DriverPushNotifierTests.cs`:
```csharp
[Fact]
public async Task NotifyBoxAssignedAsync_QueriesDriverDeviceTokens_AndSendsFcmMessage()
{
    var db = CreateInMemoryDbContext();
    var fcm = new Mock<IFirebaseMessagingClient>();
    fcm.Setup(f => f.IsConfigured).Returns(true);
    var logger = new Mock<ILogger<FirebaseDriverPushNotifier>>();

    var driverUserId = Guid.NewGuid();
    db.DriverDeviceTokens.Add(new DriverDeviceToken
    {
        UserId = driverUserId,
        Token = "fcm_test_token_123",
        IsActive = true
    });
    await db.SaveChangesAsync();

    var notifier = new FirebaseDriverPushNotifier(fcm.Object, db, logger.Object);
    await notifier.NotifyBoxAssignedAsync(driverUserId, "#BX-1234", "Salmiya", CancellationToken.None);

    fcm.Verify(f => f.SendAsync("fcm_test_token_123", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverPushNotifierTests"`
Expected: FAIL (types do not exist).

- [ ] **Step 3: Define interface and implement FirebaseDriverPushNotifier**

Create `src/MealMate.Application/Common/Abstractions/IDriverPushNotifier.cs`:
```csharp
namespace MealMate.Application.Common.Abstractions;

public interface IDriverPushNotifier
{
    Task NotifyBoxAssignedAsync(Guid driverUserId, string boxCode, string zoneName, CancellationToken cancellationToken = default);
    Task NotifyTripAssignedAsync(Guid driverUserId, string tripCode, int boxesCount, CancellationToken cancellationToken = default);
    Task NotifyKitchenReadyForPickupAsync(Guid driverUserId, string tripCode, CancellationToken cancellationToken = default);
}
```

Create `src/MealMate.Infrastructure/Push/FirebaseDriverPushNotifier.cs`:
```csharp
using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class FirebaseDriverPushNotifier : IDriverPushNotifier
{
    private readonly IFirebaseMessagingClient _fcm;
    private readonly IAppDbContext _db;
    private readonly ILogger<FirebaseDriverPushNotifier> _logger;

    public FirebaseDriverPushNotifier(IFirebaseMessagingClient fcm, IAppDbContext db, ILogger<FirebaseDriverPushNotifier> logger)
    {
        _fcm = fcm;
        _db = db;
        _logger = logger;
    }

    public async Task NotifyBoxAssignedAsync(Guid driverUserId, string boxCode, string zoneName, CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured || driverUserId == Guid.Empty) return;

        var tokens = await _db.DriverDeviceTokens
            .Where(x => x.IsActive && x.UserId == driverUserId)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        var title = "تم إسناد طلب جديد 📦";
        var body = $"تم إسناد الطلب {boxCode} في منطقة {zoneName} للتوصيل.";
        var data = new Dictionary<string, string>
        {
            ["type"] = "box-assigned",
            ["boxCode"] = boxCode,
            ["zoneName"] = zoneName
        };

        foreach (var token in tokens)
        {
            try { await _fcm.SendAsync(token, title, body, data, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send driver FCM to {UserId}", driverUserId); }
        }
    }

    public async Task NotifyTripAssignedAsync(Guid driverUserId, string tripCode, int boxesCount, CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured || driverUserId == Guid.Empty) return;

        var tokens = await _db.DriverDeviceTokens
            .Where(x => x.IsActive && x.UserId == driverUserId)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        var title = "تم تعيين رحلة جديدة 🛵";
        var body = $"تم إسناد الرحلة {tripCode} بعدد {boxesCount} صناديق.";
        var data = new Dictionary<string, string>
        {
            ["type"] = "trip-assigned",
            ["tripCode"] = tripCode,
            ["boxesCount"] = boxesCount.ToString()
        };

        foreach (var token in tokens)
        {
            try { await _fcm.SendAsync(token, title, body, data, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send driver FCM to {UserId}", driverUserId); }
        }
    }

    public async Task NotifyKitchenReadyForPickupAsync(Guid driverUserId, string tripCode, CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured || driverUserId == Guid.Empty) return;

        var tokens = await _db.DriverDeviceTokens
            .Where(x => x.IsActive && x.UserId == driverUserId)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        var title = "الشحنة جاهزة للاستلام بالمطبخ 🍳";
        var body = $"أنهى المطبخ تجهيز وجبات الرحلة {tripCode}. يمكنك التوجه للاستلام ومسح الباركود.";
        var data = new Dictionary<string, string>
        {
            ["type"] = "kitchen-ready",
            ["tripCode"] = tripCode
        };

        foreach (var token in tokens)
        {
            try { await _fcm.SendAsync(token, title, body, data, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send driver FCM to {UserId}", driverUserId); }
        }
    }
}
```

Register in `src/MealMate.Infrastructure/DependencyInjection.cs`:
```csharp
services.AddScoped<IDriverPushNotifier, FirebaseDriverPushNotifier>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverPushNotifierTests"`
Expected: PASS

- [ ] **Step 5: Commit changes**

```bash
git add src/MealMate.Application/Common/Abstractions/IDriverPushNotifier.cs src/MealMate.Infrastructure/Push/FirebaseDriverPushNotifier.cs src/MealMate.Infrastructure/DependencyInjection.cs tests/MealMate.UnitTests/DriverDelivery/DriverPushNotifierTests.cs
git commit -m "feat(driver): implement IDriverPushNotifier with Firebase FCM support"
```

---

### Task 3: Unified Dispatcher Queue Bridge (`GetDispatcherOrderQueueQuery`)

**Files:**
- Modify: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherOrderQueueQuery.cs:56-130`
- Test: `tests/MealMate.UnitTests/DriverDelivery/DispatcherQueueBridgeTests.cs`

**Interfaces:**
- Consumes: `SubscriptionDays`, `Trips`, `TripStops`, `IDispatchRecommendationEngine`
- Produces: `DispatcherOrderQueueDto` containing both pre-trip confirmed boxes and in-trip stops.

- [ ] **Step 1: Write test for Dispatcher Queue Bridge**

Create `tests/MealMate.UnitTests/DriverDelivery/DispatcherQueueBridgeTests.cs`:
```csharp
[Fact]
public async Task Handle_ReturnsBothUnassignedSubscriptionDays_AndExistingTripStops()
{
    var db = CreateInMemoryDbContext();
    var restaurantId = Guid.NewGuid();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    // 1. Confirmed SubscriptionDay without Trip
    var subDay = new SubscriptionDay
    {
        Id = Guid.NewGuid(),
        RestaurantId = restaurantId,
        ServiceDate = today,
        Status = SubscriptionDayStatus.ConfirmedByRestaurant,
        DeliveryAddressLine = "حولي - قطعة 4"
    };
    db.SubscriptionDays.Add(subDay);

    // 2. Existing TripStop
    var trip = new Trip { Id = Guid.NewGuid(), RestaurantId = restaurantId, Code = "TRP-101", Status = TripStatus.Assigned };
    var stop = new TripStop { Id = Guid.NewGuid(), TripId = trip.Id, OrderCode = "BX-01", Status = TripStopStatus.Pending, ZoneNameAr = "السالمية" };
    trip.Stops.Add(stop);
    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    var handler = new GetDispatcherOrderQueueQueryHandler(db, MockCurrentUser(restaurantId), MockMessages(), MockRecommendationEngine());
    var result = await handler.Handle(new GetDispatcherOrderQueueQuery(restaurantId), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.Counts.PendingCount.Should().Be(2); // 1 unassigned subDay + 1 pending stop
    result.Value.Boxes.Should().Contain(b => b.BoxId == subDay.Id);
    result.Value.Boxes.Should().Contain(b => b.BoxId == stop.Id);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DispatcherQueueBridgeTests"`
Expected: FAIL with `PendingCount` being 1 instead of 2.

- [ ] **Step 3: Update GetDispatcherOrderQueueQuery to merge unassigned days**

In `src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherOrderQueueQuery.cs`, query unassigned confirmed subscription days:
```csharp
var assignedSubDayIds = await _db.TripStops
    .Where(s => s.SubscriptionDayId != null)
    .Select(s => s.SubscriptionDayId!.Value)
    .Distinct()
    .ToListAsync(cancellationToken);

var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
var unassignedDays = await _db.SubscriptionDays
    .AsNoTracking()
    .Where(d => d.RestaurantId == restaurantId
                && !assignedSubDayIds.Contains(d.Id)
                && (d.Status == SubscriptionDayStatus.ConfirmedByRestaurant || d.Status == SubscriptionDayStatus.Preparing))
    .OrderBy(d => d.ServiceDate)
    .ToListAsync(cancellationToken);
```
Combine `unassignedDays` into `boxes` list with `Status: "Pending"` and `Suggestion` from `_recommendationEngine`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DispatcherQueueBridgeTests"`
Expected: PASS

- [ ] **Step 5: Commit changes**

```bash
git add src/MealMate.Application/Features/DriverDelivery/Dispatcher/GetDispatcherOrderQueueQuery.cs tests/MealMate.UnitTests/DriverDelivery/DispatcherQueueBridgeTests.cs
git commit -m "feat(dispatcher): bridge unassigned confirmed subscription days into order queue"
```

---

### Task 4: Dynamic Box Assignment & Auto-Trip Generation (`AssignBoxToDriverCommand`)

**Files:**
- Modify: `src/MealMate.Application/Features/DriverDelivery/Dispatcher/AssignBoxToDriverCommand.cs`
- Modify: `src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs:100-140`
- Test: `tests/MealMate.UnitTests/DriverDelivery/AssignBoxToDriverTests.cs`

**Interfaces:**
- Consumes: `IDriverPushNotifier`, `IHubContext<DispatcherHub>`, `IHubContext<DriverHub>`
- Produces: Atomic creation of `Trip` + `TripStop` + `DriverAssignment` when assigning an unassigned `SubscriptionDay`.

- [ ] **Step 1: Write test for dynamic SubscriptionDay assignment**

Create `tests/MealMate.UnitTests/DriverDelivery/AssignBoxToDriverTests.cs`:
```csharp
[Fact]
public async Task Handle_WhenBoxIdIsSubscriptionDay_ProvisionsTripAndTripStop_AndNotifiesDriver()
{
    var db = CreateInMemoryDbContext();
    var push = new Mock<IDriverPushNotifier>();
    var handler = new AssignBoxToDriverCommandHandler(db, MockMessages(), push.Object);

    var restaurantId = Guid.NewGuid();
    var driverUserId = Guid.NewGuid();
    var driver = new DriverProfile { Id = Guid.NewGuid(), UserId = driverUserId, FullNameAr = "أحمد السعيد" };
    db.DriverProfiles.Add(driver);

    var subDay = new SubscriptionDay
    {
        Id = Guid.NewGuid(),
        RestaurantId = restaurantId,
        ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = SubscriptionDayStatus.ConfirmedByRestaurant,
        DeliveryAddressLine = "السالمية"
    };
    db.SubscriptionDays.Add(subDay);
    await db.SaveChangesAsync();

    var result = await handler.Handle(new AssignBoxToDriverCommand(subDay.Id, driver.Id), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    var createdStop = await db.TripStops.FirstOrDefaultAsync(s => s.SubscriptionDayId == subDay.Id);
    createdStop.Should().NotBeNull();
    createdStop!.TripId.Should().NotBeEmpty();

    var assignment = await db.DriverAssignments.FirstOrDefaultAsync(a => a.TripId == createdStop.TripId);
    assignment.Should().NotBeNull();
    assignment!.DriverProfileId.Should().Be(driver.Id);

    push.Verify(p => p.NotifyBoxAssignedAsync(driverUserId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~AssignBoxToDriverTests"`
Expected: FAIL (handler does not handle `SubscriptionDay` IDs).

- [ ] **Step 3: Update AssignBoxToDriverCommandHandler**

In `src/MealMate.Application/Features/DriverDelivery/Dispatcher/AssignBoxToDriverCommand.cs`:
- Check if `request.BoxId` matches an existing `TripStop`.
- If not, check if `request.BoxId` matches `SubscriptionDay`.
- When matching `SubscriptionDay`:
  - Find an existing open trip for this driver & restaurant, or create a new `Trip`.
  - Create `TripStop` with `SubscriptionDayId = subDay.Id` and link to `DailyOrderId`.
  - Add `DriverAssignment`.
  - Invoke `_driverPush.NotifyBoxAssignedAsync(...)`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~AssignBoxToDriverTests"`
Expected: PASS

- [ ] **Step 5: Commit changes**

```bash
git add src/MealMate.Application/Features/DriverDelivery/Dispatcher/AssignBoxToDriverCommand.cs tests/MealMate.UnitTests/DriverDelivery/AssignBoxToDriverTests.cs
git commit -m "feat(dispatcher): support dynamic assignment of subscription days with auto-trip generation and FCM push"
```

---

### Task 5: Kitchen-to-Dispatcher SignalR Event & Ready Prep Status

**Files:**
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs:1268-1313`
- Modify: `src/MealMate.Api/Realtime/DispatcherHub.cs:20-30`
- Test: `tests/MealMate.UnitTests/RestaurantOrders/KitchenPrepStatusNotificationTests.cs`

**Interfaces:**
- Consumes: `IHubContext<DispatcherHub>`
- Produces: `kitchen-batch-ready` SignalR event emitted to `restaurant:{restaurantId}:dispatcher`.

- [ ] **Step 1: Write test for kitchen-batch-ready broadcast**

Create `tests/MealMate.UnitTests/RestaurantOrders/KitchenPrepStatusNotificationTests.cs`:
```csharp
[Fact]
public async Task UpdateDailyOrderPrepStatusAsync_WhenStatusReady_EmitsSignalREventToDispatcherHub()
{
    // Verifies that updating status to 'ready' broadcasts kitchen-batch-ready to DispatcherHub group
}
```

- [ ] **Step 2: Implement SignalR broadcast in UpdateDailyOrderPrepStatusAsync**

In `RestaurantOrderHandlers.cs`:
Inject `IHubContext<DispatcherHub>` or a kitchen notifier service and broadcast:
```csharp
await _dispatcherHub.Clients
    .Group(DispatcherHub.GroupForRestaurant(restaurantId))
    .SendAsync("kitchen-batch-ready", new { dayId = day.Id, serviceDate = day.ServiceDate.ToString("yyyy-MM-dd"), boxCount = 1 }, cancellationToken);
```

- [ ] **Step 3: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~KitchenPrepStatusNotificationTests"`
Expected: PASS

- [ ] **Step 4: Commit changes**

```bash
git add src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs src/MealMate.Api/Realtime/DispatcherHub.cs tests/MealMate.UnitTests/RestaurantOrders/KitchenPrepStatusNotificationTests.cs
git commit -m "feat(kitchen): broadcast kitchen-batch-ready to DispatcherHub on prep ready"
```

---

### Task 6: Automated Replacement Orchestration for Escalated / Rejected Orders

**Files:**
- Create: `src/MealMate.Application/Features/CustomerCheckout/OrderReplacementOrchestrator.cs`
- Modify: `src/MealMate.Application/Features/CustomerCheckout/BackgroundJobs/RestaurantOrderEscalationJob.cs`
- Test: `tests/MealMate.UnitTests/CustomerCheckout/OrderReplacementOrchestratorTests.cs`

**Interfaces:**
- Consumes: `SubscriptionRestaurantSnapshots`, `SubscriptionDays`, `IAppDbContext`
- Produces: Automated re-routing of `ReplacementRequired` days to the next best pool restaurant.

- [ ] **Step 1: Write test for OrderReplacementOrchestrator**

Create `tests/MealMate.UnitTests/CustomerCheckout/OrderReplacementOrchestratorTests.cs`:
```csharp
[Fact]
public async Task TryReassignAlternativeRestaurantAsync_WhenAlternativeExists_ReassignsAndResetsStatus()
{
    // Verifies that a rejected day is reassigned to an alternative restaurant from customer's pool
}
```

- [ ] **Step 2: Implement OrderReplacementOrchestrator**

Create `src/MealMate.Application/Features/CustomerCheckout/OrderReplacementOrchestrator.cs` with pool lookup and alternative restaurant selection.
Wire it into `RestaurantOrderEscalationJob.cs` and `RejectRestaurantOrderCommand`.

- [ ] **Step 3: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~OrderReplacementOrchestratorTests"`
Expected: PASS

- [ ] **Step 4: Run full test suite**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj`
Expected: ALL PASS

- [ ] **Step 5: Commit changes**

```bash
git add src/MealMate.Application/Features/CustomerCheckout/OrderReplacementOrchestrator.cs src/MealMate.Application/Features/CustomerCheckout/BackgroundJobs/RestaurantOrderEscalationJob.cs tests/MealMate.UnitTests/CustomerCheckout/OrderReplacementOrchestratorTests.cs
git commit -m "feat(orders): implement automated alternative restaurant replacement orchestration"
```
