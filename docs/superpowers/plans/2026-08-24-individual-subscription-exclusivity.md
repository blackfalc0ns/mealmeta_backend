# Individual Subscription Exclusivity Gate — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent a customer with a current individual subscription from starting or creating another individual subscription.

**Architecture:** Central `ICustomerIndividualSubscriptionGate` queries blocking individual rows (`FamilyGroupId == null` + current statuses). Wire into readiness, individual checkout, and `CreateSubscriptionCommand`. Error code: `checkout.existing_subscription` (HTTP 409).

**Tech Stack:** .NET 9, EF Core InMemory tests, NSubstitute, xUnit, MediatR handlers

**Spec:** `docs/superpowers/specs/2026-08-24-individual-subscription-exclusivity-design.md`

## Global Constraints

- Blocking statuses (exact): `PendingPayment`, `Paid`, `Scheduled`, `Active`, `Frozen`, `SuspendedForReview`
- Blocking row filter: `FamilyGroupId == null` (individual only)
- Error code (exact): `checkout.existing_subscription`
- Do **not** break family multi-create for managers without an individual current subscription
- Do **not** implement family conversion / upgrade chooser
- Do **not** commit unless the user explicitly asks
- Prefer existing patterns in `SubscriptionCheckoutReadinessServiceTests` and `CustomerCheckoutGateControllerTests`

## File map

| File | Responsibility |
|------|----------------|
| `src/MealMate.Application/Features/CustomerCheckout/CustomerIndividualSubscriptionGate.cs` | Gate interface + implementation + shared status helper |
| `src/MealMate.Application/Features/CustomerCheckout/SubscriptionCheckoutReadinessService.cs` | Early UX block + DTO optional blocking fields |
| `src/MealMate.Application/Features/ProgramsSubscriptions/SubscriptionCommandHandlers.cs` | Hard stop on create |
| `src/MealMate.Application/DependencyInjection.cs` | DI registration |
| `src/MealMate.Api/Controllers/V1/CustomerCheckoutController.cs` | 409 on checkout when blocked |
| `src/MealMate.Api/Controllers/V1/CustomerSubscriptionFlowController.cs` | Surface block on readiness/summary entry if needed |
| `src/MealMate.Api/Controllers/V1/CustomerSubscriptionManagementController.cs` | Reuse shared status helper for `IsCurrentStatus` |
| `tests/MealMate.UnitTests/CustomerCheckout/CustomerIndividualSubscriptionGateTests.cs` | Gate unit tests |
| `tests/MealMate.UnitTests/CustomerCheckout/SubscriptionCheckoutReadinessServiceTests.cs` | Readiness block test |
| `tests/MealMate.ApiTests/CustomerCheckout/CustomerCheckoutGateControllerTests.cs` | Checkout 409 test |
| `docs/mobile-customer-subscription-flow.md` | One-paragraph note on exclusivity |

---

### Task 1: Gate service + unit tests

**Files:**
- Create: `src/MealMate.Application/Features/CustomerCheckout/CustomerIndividualSubscriptionGate.cs`
- Create: `tests/MealMate.UnitTests/CustomerCheckout/CustomerIndividualSubscriptionGateTests.cs`
- Modify: `src/MealMate.Application/DependencyInjection.cs`

**Interfaces:**
- Produces:
  - `ICustomerIndividualSubscriptionGate.FindBlockingAsync(Guid customerUserId, CancellationToken ct = default) -> Task<BlockingIndividualSubscription?>`
  - `ICustomerIndividualSubscriptionGate.HasBlockingAsync(Guid customerUserId, CancellationToken ct = default) -> Task<bool>`
  - `record BlockingIndividualSubscription(Guid SubscriptionId, SubscriptionStatus Status, DateOnly? StartDate, DateOnly? EndDate)`
  - `static class CurrentIndividualSubscriptionStatuses` with `Blocking` array and `IsBlocking(SubscriptionStatus)` / `IsCurrentStatusName(string)`

- [ ] **Step 1: Write failing unit tests** in `CustomerIndividualSubscriptionGateTests.cs`:

```csharp
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class CustomerIndividualSubscriptionGateTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Frozen)]
    [InlineData(SubscriptionStatus.PendingPayment)]
    [InlineData(SubscriptionStatus.Paid)]
    [InlineData(SubscriptionStatus.Scheduled)]
    [InlineData(SubscriptionStatus.SuspendedForReview)]
    public async Task FindBlocking_returns_individual_current(SubscriptionStatus status)
    {
        var (db, customerId, subId) = await SeedAsync(status, familyGroupId: null);
        var gate = new CustomerIndividualSubscriptionGate(db);
        var blocking = await gate.FindBlockingAsync(customerId);
        Assert.NotNull(blocking);
        Assert.Equal(subId, blocking!.SubscriptionId);
        Assert.Equal(status, blocking.Status);
        Assert.True(await gate.HasBlockingAsync(customerId));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Completed)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    [InlineData(SubscriptionStatus.PaymentFailed)]
    [InlineData(SubscriptionStatus.Upgraded)]
    public async Task FindBlocking_ignores_non_current(SubscriptionStatus status)
    {
        var (db, customerId, _) = await SeedAsync(status, familyGroupId: null);
        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.Null(await gate.FindBlockingAsync(customerId));
        Assert.False(await gate.HasBlockingAsync(customerId));
    }

    [Fact]
    public async Task FindBlocking_ignores_family_group_subscription()
    {
        var (db, customerId, _) = await SeedAsync(SubscriptionStatus.Active, familyGroupId: Guid.NewGuid());
        var gate = new CustomerIndividualSubscriptionGate(db);
        Assert.Null(await gate.FindBlockingAsync(customerId));
    }

    private static async Task<(AppDbContext Db, Guid CustomerId, Guid SubscriptionId)> SeedAsync(
        SubscriptionStatus status, Guid? familyGroupId)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = status,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            FamilyGroupId = familyGroupId,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100m, "KWD"),
        };
        db.AddRange(program, bundle, sub);
        await db.SaveChangesAsync();
        return (db, customerId, sub.Id);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL** (type missing)

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~CustomerIndividualSubscriptionGateTests" --no-restore 2>&1
```

(If restore needed, run `dotnet test` with restore.)

- [ ] **Step 3: Implement gate + register DI**

`CustomerIndividualSubscriptionGate.cs`:

```csharp
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record BlockingIndividualSubscription(
    Guid SubscriptionId,
    SubscriptionStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate);

public interface ICustomerIndividualSubscriptionGate
{
    Task<BlockingIndividualSubscription?> FindBlockingAsync(Guid customerUserId, CancellationToken ct = default);
    Task<bool> HasBlockingAsync(Guid customerUserId, CancellationToken ct = default);
}

public static class CurrentIndividualSubscriptionStatuses
{
    public static readonly SubscriptionStatus[] Blocking =
    [
        SubscriptionStatus.PendingPayment,
        SubscriptionStatus.Paid,
        SubscriptionStatus.Scheduled,
        SubscriptionStatus.Active,
        SubscriptionStatus.Frozen,
        SubscriptionStatus.SuspendedForReview,
    ];

    public static bool IsBlocking(SubscriptionStatus status) => Blocking.Contains(status);

    public static bool IsCurrentStatusName(string status) =>
        status is nameof(SubscriptionStatus.PendingPayment)
            or nameof(SubscriptionStatus.Paid)
            or nameof(SubscriptionStatus.Scheduled)
            or nameof(SubscriptionStatus.Active)
            or nameof(SubscriptionStatus.Frozen)
            or nameof(SubscriptionStatus.SuspendedForReview);
}

public sealed class CustomerIndividualSubscriptionGate(IAppDbContext db) : ICustomerIndividualSubscriptionGate
{
    public const string ExistingSubscriptionError = "checkout.existing_subscription";

    public async Task<BlockingIndividualSubscription?> FindBlockingAsync(Guid customerUserId, CancellationToken ct = default)
    {
        if (customerUserId == Guid.Empty) return null;
        return await db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId
                        && x.FamilyGroupId == null
                        && CurrentIndividualSubscriptionStatuses.Blocking.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new BlockingIndividualSubscription(x.Id, x.Status, x.StartDate, x.EndDate))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> HasBlockingAsync(Guid customerUserId, CancellationToken ct = default)
        => await FindBlockingAsync(customerUserId, ct) is not null;
}
```

In `DependencyInjection.cs` add next to other checkout registrations:

```csharp
services.AddScoped<Features.CustomerCheckout.ICustomerIndividualSubscriptionGate, Features.CustomerCheckout.CustomerIndividualSubscriptionGate>();
```

- [ ] **Step 4: Run tests — expect PASS**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~CustomerIndividualSubscriptionGateTests"
```

Expected: all tests passed.

---

### Task 2: Readiness service blocks checkout

**Files:**
- Modify: `src/MealMate.Application/Features/CustomerCheckout/SubscriptionCheckoutReadinessService.cs`
- Modify: `tests/MealMate.UnitTests/CustomerCheckout/SubscriptionCheckoutReadinessServiceTests.cs`

**Interfaces:**
- Consumes: `ICustomerIndividualSubscriptionGate`
- Produces: extend `SubscriptionCheckoutReadinessDto` with optional `Guid? BlockingSubscriptionId = null`, `string? BlockingSubscriptionStatus = null`
- When blocking: `CanCheckout=false`, `CanOpenSummary=false`, `CheckoutBlockReason = CustomerIndividualSubscriptionGate.ExistingSubscriptionError` (or `"checkout.existing_subscription"`), prefer this over profile incompleteness for `CheckoutBlockReason` when a blocking sub exists (still return profile items for UX)

- [ ] **Step 1: Add failing readiness test**

```csharp
[Fact]
public async Task Existing_active_individual_subscription_blocks_checkout()
{
    var fixture = await SeedAsync(address: true, allergies: true, dietary: true, eligibleRestaurant: true);
    fixture.Db.Subscriptions.Add(new Subscription
    {
        CustomerUserId = fixture.CustomerId,
        ProgramId = fixture.ProgramId,
        BundleId = fixture.BundleId,
        SelectedTier = RestaurantTier.Basic,
        DurationDays = 26,
        Status = SubscriptionStatus.Active,
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 26),
        PaidAmount = new Money(100m, "KWD"),
    });
    await fixture.Db.SaveChangesAsync();

    var result = await fixture.Service.EvaluateAsync(
        fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

    Assert.True(result.IsSuccess);
    Assert.False(result.Value.CanCheckout);
    Assert.False(result.Value.CanOpenSummary);
    Assert.Equal("checkout.existing_subscription", result.Value.CheckoutBlockReason);
    Assert.NotNull(result.Value.BlockingSubscriptionId);
}
```

Update `SeedAsync` to construct service with `new CustomerIndividualSubscriptionGate(db)`.

Update DTO construction sites in ApiTests that use `SubscriptionCheckoutReadinessDto` — add trailing optional args only if compile breaks.

- [ ] **Step 2: Run filter — expect FAIL**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~Existing_active_individual"
```

- [ ] **Step 3: Wire gate into readiness**

1. Add fields to `SubscriptionCheckoutReadinessDto` after `Restaurants`:
   `Guid? BlockingSubscriptionId = null, string? BlockingSubscriptionStatus = null`
2. Inject `ICustomerIndividualSubscriptionGate` into `SubscriptionCheckoutReadinessService`
3. After loading profile completeness / restaurants, call `FindBlockingAsync(customerUserId)`
4. If blocking != null:
   - `canCheckout = false`
   - `canOpenSummary = false` (or keep CanOpenSummary true only if product wants — **spec says CanOpenSummary = false**)
   - `checkoutBlock = CustomerIndividualSubscriptionGate.ExistingSubscriptionError`
5. Pass blocking id/status into DTO

Also add constant on `SubscriptionCheckoutGateCodes`:
`public const string ExistingSubscription = "checkout.existing_subscription";`

- [ ] **Step 4: Run readiness tests — expect PASS**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~SubscriptionCheckoutReadinessServiceTests"
```

---

### Task 3: CreateSubscriptionCommand + Checkout controller 409

**Files:**
- Modify: `src/MealMate.Application/Features/ProgramsSubscriptions/SubscriptionCommandHandlers.cs`
- Modify: `src/MealMate.Api/Controllers/V1/CustomerCheckoutController.cs`
- Modify: `src/MealMate.Api/Controllers/V1/CustomerSubscriptionManagementController.cs` (`IsCurrentStatus` → shared helper)
- Modify: `tests/MealMate.ApiTests/CustomerCheckout/CustomerCheckoutGateControllerTests.cs`
- Optionally touch: `CustomerSubscriptionFlowController.cs` if summary/checkout entry ignores readiness block reason

**Interfaces:**
- Consumes: `ICustomerIndividualSubscriptionGate`
- Create handler: at top of `Handle(CreateSubscriptionCommand)` after basic validation entry, if `FindBlockingAsync(request.CustomerUserId)` not null → `return Error.Conflict(CustomerIndividualSubscriptionGate.ExistingSubscriptionError, "Customer already has a current individual subscription.");`
- Checkout: after readiness profile block check, if `gate.Value.CheckoutBlockReason == "checkout.existing_subscription"` OR `FindBlockingAsync` → `return Conflict(new { error = "checkout.existing_subscription", subscriptionId = ..., status = ... });`

- [ ] **Step 1: Add API test**

```csharp
[Fact]
public async Task Checkout_WithExistingSubscriptionBlock_ReturnsConflict()
{
    var userId = Guid.NewGuid();
    var programId = Guid.NewGuid();
    var bundleId = Guid.NewGuid();
    var blockingId = Guid.NewGuid();
    // readiness returns complete profile BUT CheckoutBlockReason = checkout.existing_subscription
    // assert Conflict with error checkout.existing_subscription
}
```

Follow existing `CheckoutWithItemsAsync` pattern; set `CanCheckout=false`, `CheckoutBlockReason="checkout.existing_subscription"`, `BlockingSubscriptionId=blockingId`.

Update `CustomerCheckoutController.Checkout` to treat `CheckoutBlockReason == checkout.existing_subscription` as Conflict (same as profile blocks), including `subscriptionId` / `status` when present on DTO.

Inject gate into controller **only if** re-check needed; preferred: trust readiness `CheckoutBlockReason` first, and also call `FindBlockingAsync` inside transaction before create for race safety.

Minimal for this task:
1. Readiness already sets block reason
2. Checkout: if `gate.Value.CheckoutBlockReason == "checkout.existing_subscription"` → Conflict with payload
3. CreateSubscription: inject optional `ICustomerIndividualSubscriptionGate?` (default null for tests) and conflict when blocking

Update `SubscriptionCommandHandlers` ctor to accept `ICustomerIndividualSubscriptionGate? individualGate = null`.

- [ ] **Step 2: Implement + run**

```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter "FullyQualifiedName~CustomerCheckoutGateControllerTests"
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~CustomerCheckout"
```

Expected: PASS

- [ ] **Step 3: Align management `IsCurrentStatus`**

```csharp
private static bool IsCurrentStatus(string status) => CurrentIndividualSubscriptionStatuses.IsCurrentStatusName(status);
```

- [ ] **Step 4: Doc note** — add short bullet under checkout prerequisites in `docs/mobile-customer-subscription-flow.md`:

```markdown
- إذا كان للعميل اشتراك فردي حالي (PendingPayment/Paid/Scheduled/Active/Frozen/SuspendedForReview) يُرفض checkout فردي جديد بـ `409 checkout.existing_subscription`.
```

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| Blocking statuses A | Task 1 |
| FamilyGroupId null filter | Task 1 |
| Readiness early block | Task 2 |
| Checkout 409 | Task 3 |
| CreateSubscription conflict | Task 3 |
| Shared status helper | Task 1 + 3 |
| Family multi-create not broken | Task 1 (family ignored) |
| No conversion UI | — out of scope |
| Error code exact | All tasks |

## Self-review

- No TBD placeholders
- DTO optional trailing params keep ApiTests compiling
- Gate registered in DI
- Commit steps omitted from execution unless user asks
