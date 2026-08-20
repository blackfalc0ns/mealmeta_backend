# Order Lifecycle Gap Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close all operational and architectural gaps in the subscription and restaurant confirmation flow: automated overdue escalation, fair reassignment window, structured rejection reasons, and freeze safeguards on locked days.

**Architecture:** 
- Background Services: Scheduled job `RestaurantOrderEscalationJob` periodically auto-escalating overdue restaurant confirmations (> 24h) to `ReplacementRequired`.
- Reassignment: Resetting `LockAtUtc` upon reassignment to give the substitute restaurant a full 24h window.
- Rejection Tracking: Standardized reason codes logged into `SubscriptionOperations`.
- Freeze Guard: Protecting locked/confirmed kitchen prep days (-72h) from customer freezes.

**Tech Stack:** C# .NET 9, EF Core, BackgroundService, xUnit, NSubstitute.

---

## Global Constraints

- Preserve all existing API responses and DTO compatibility.
- Fully isolated tenant queries by `RestaurantId`.
- Transparent audit logging in `SubscriptionOperations`.

---

### Task 1: Automated Overdue Escalation Job

**Files:**
- Create: `f:/MealMate/backend/src/MealMate.Application/Features/CustomerCheckout/BackgroundJobs/RestaurantOrderEscalationJob.cs`
- Modify: `f:/MealMate/backend/src/MealMate.Application/Features/CustomerCheckout/BackgroundJobs/SubscriptionJobs.cs`

- [ ] **Step 1: Create `RestaurantOrderEscalationJob.cs`**

```csharp
namespace MealMate.Application.Features.CustomerCheckout.BackgroundJobs;

public sealed class RestaurantOrderEscalationJob(IServiceScopeFactory scopes, ILogger<RestaurantOrderEscalationJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    protected override string JobName => nameof(RestaurantOrderEscalationJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var now = clock.UtcNow;

        var overdueDays = await db.SubscriptionDays
            .Where(x => (x.Status == SubscriptionDayStatus.Locked72h || x.Status == SubscriptionDayStatus.SentToRestaurant)
                        && x.LockAtUtc != null
                        && x.LockAtUtc.Value.AddHours(24) <= now)
            .ToListAsync(cancellationToken);

        foreach (var day in overdueDays)
        {
            day.Status = SubscriptionDayStatus.ReplacementRequired;
            db.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = day.SubscriptionId,
                OperationType = "ORDER_CONFIRMATION_OVERDUE_ESCALATED",
                PreviousStatus = SubscriptionStatus.Active,
                NewStatus = SubscriptionStatus.Active,
                Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; RestaurantId={day.RestaurantId}; Exceeded 24h confirmation window.",
                PerformedByUserId = Guid.Empty,
                PerformedByUserType = UserType.Admin,
                TimestampUtc = now
            });
        }

        if (overdueDays.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Escalated {Count} overdue restaurant confirmation days to ReplacementRequired.", overdueDays.Count);
        }
    }
}
```

---

### Task 2: Reassignment Window Reset & Structured Rejection Reasons

**Files:**
- Modify: `f:/MealMate/backend/src/MealMate.Api/Controllers/V1/AdminCheckoutOperationsController.cs`
- Modify: `f:/MealMate/backend/src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderDtos.cs`
- Modify: `f:/MealMate/backend/src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs`

- [ ] **Step 1: Update `AdminCheckoutOperationsController.cs` to reset `LockAtUtc` and clear `ConfirmedAtUtc` on reassignment**

- [ ] **Step 2: Update `RejectRestaurantOrderRequest` and `RejectOrderAsync` to log structured reason code in `SubscriptionOperations`**

---

### Task 3: Subscription Freeze Safeguard on Locked/Confirmed Days

**Files:**
- Modify: `f:/MealMate/backend/src/MealMate.Application/Features/ProgramsSubscriptions/SubscriptionOperationsHandlers.cs`

- [ ] **Step 1: Add validation in freeze request handler to protect days within -72h locked/confirmed window**

---

### Task 4: Unit Testing & Verification

**Files:**
- Create: `f:/MealMate/backend/tests/MealMate.UnitTests/RestaurantOperations/OrderLifecycleGapClosureTests.cs`

- [ ] **Step 1: Write unit tests covering overdue escalation, reassignment reset, rejection logging, and freeze protection**
- [ ] **Step 2: Run test suite to verify 100% pass**
- [ ] **Step 3: Build backend and frontend**
