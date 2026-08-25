# Family Manager Wave 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship post-payment family Hub APIs (home, members, access, permissions, invite accept) and open the existing customer calendar to family member subscriptions via an access gate — matching spec `docs/superpowers/specs/2026-08-25-family-manager-wave1-design.md`.

**Architecture:** New Family Hub feature under Application + API. Reuse `CustomerCalendarEngine`; replace blind `IndividualOnly` reject with `IFamilyCalendarAccessGate`. Persist `FamilyMemberAccess` + `FamilyMemberPermissions`.

**Tech Stack:** .NET 9, EF Core + PostgreSQL, MediatR, xUnit, FluentAssertions, NSubstitute, InMemory for unit tests.

## Global Constraints

- Calendar product rules identical to individual (draft → Done → restaurant at −72h only).
- Hub home/members/access/permissions are **manager-only**.
- Linked members edit meals only via calendar routes when `EditDailyMeals` and not paused.
- Do not implement addresses/payments/reports pages (shortcut stubs only).
- Do not commit unless the user explicitly asks.
- Prefer OpenCode `go-implementer` for edits when available; one editor at a time.

## File map

| File | Responsibility |
|------|----------------|
| `src/MealMate.Domain/ProgramsSubscriptions/FamilyMemberAccess.cs` | Access entity |
| `src/MealMate.Domain/ProgramsSubscriptions/FamilyMemberPermissions.cs` | Permissions entity |
| `src/MealMate.Application/Common/Localization/MessageCodes.cs` | `MessageCodes.Family.*` |
| `src/MealMate.Application/Features/FamilyManager/*` | Hub contracts, gate, handlers |
| `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarEngine.cs` | Use gate instead of IndividualOnly |
| `src/MealMate.Application/DependencyInjection.cs` | Register gate + services |
| `src/MealMate.Application/Common/Abstractions/IAppDbContext.cs` | DbSets |
| `src/MealMate.Persistence/...` | Config + migration |
| `src/MealMate.Api/Controllers/V1/CustomerFamilyManagerController.cs` | Hub HTTP |
| `src/MealMate.Api/Resources/Resources.Messages*.resx` | Error strings |
| `tests/MealMate.UnitTests/FamilyManager/*` | Gate + Hub tests |
| `tests/.../CustomerCalendar/*` | Update family calendar path tests |
| `docs/mobile/family-manager/00-family-manager-wave1.md` | Mobile contract |

---

### Task 1: Domain entities + MessageCodes + DbSets

**Files:**
- Create: `src/MealMate.Domain/ProgramsSubscriptions/FamilyMemberAccess.cs`
- Create: `src/MealMate.Domain/ProgramsSubscriptions/FamilyMemberPermissions.cs`
- Modify: `src/MealMate.Application/Common/Localization/MessageCodes.cs`
- Modify: `src/MealMate.Application/Common/Abstractions/IAppDbContext.cs`
- Modify: `src/MealMate.Persistence/AppDbContext.cs`

**Produces:**
- `FamilyMemberAccess`, `FamilyMemberPermissions` types
- `MessageCodes.Family` constants listed below

- [ ] **Step 1: Add entities**

```csharp
// FamilyMemberAccess.cs
public sealed class FamilyMemberAccess : AuditableEntity
{
    public Guid FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = default!;
    public bool HasSeparateAccount { get; set; }
    public Guid? LinkedCustomerUserId { get; set; }
    public string? InviteTokenHash { get; set; }
    public DateTime? InviteExpiresAtUtc { get; set; }
    public bool IsPaused { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
    public uint xmin { get; set; } // mapped as concurrency via EF xmin OR skip if project uses other pattern — prefer no xmin; use ModifiedAtUtc checks in handlers for Wave 1
}
```

Prefer **no PostgreSQL xmin** unless already used; Wave 1 concurrency for access/permissions: reload + overwrite under manager-only (single writer). Calendar: last write wins (spec concurrency.conflict deferred to optional `If-Match` later — document in mobile doc).

```csharp
public sealed class FamilyMemberPermissions : AuditableEntity
{
    public Guid FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = default!;
    public bool EditDailyMeals { get; set; }
    public bool ChangeDeliveryTime { get; set; }
    public bool ViewProgram { get; set; }
    public bool ChangeProgram { get; set; }
    public bool EditMealsAndDays { get; set; }
    public bool EditAddress { get; set; }
    public bool UnitExchange { get; set; }
    public bool ChangePassword { get; set; }
}
```

- [ ] **Step 2: MessageCodes.Family**

```csharp
public static class Family
{
    public const string GroupNotActive = "family.group_not_active";
    public const string MemberNotFound = "family.member_not_found";
    public const string MemberPaused = "family.member_paused";
    public const string PermissionDenied = "family.permission_denied";
    public const string CannotRemoveManager = "family.cannot_remove_manager";
    public const string ManagerPermissionsImmutable = "family.manager_permissions_immutable";
    public const string MemberRequiresPayment = "family.member_requires_payment";
    public const string InviteConflict = "family.invite_conflict";
    public const string InviteInvalid = "family.invite_invalid";
    public const string NotManager = "family.not_manager";
}
```

- [ ] **Step 3: DbSets on IAppDbContext + AppDbContext**

- [ ] **Step 4: Build Domain + Application**

Run: `dotnet build src/MealMate.Application/MealMate.Application.csproj -o .tmp-build-app`
Expected: 0 errors

---

### Task 2: EF configuration + migration

**Files:**
- Modify: `src/MealMate.Persistence/Configurations/ProgramsSubscriptions/ProgramsSubscriptionsConfigurations.cs` (append configs)
- Create migration: `AddFamilyMemberAccessAndPermissions`

- [ ] **Step 1: Configure 1:1 unique on FamilyMemberId, indexes on LinkedCustomerUserId, InviteTokenHash**
- [ ] **Step 2: `dotnet ef migrations add AddFamilyMemberAccessAndPermissions --project src/MealMate.Persistence --startup-project src/MealMate.Api`**
- [ ] **Step 3: Build Persistence**

---

### Task 3: FamilyCalendarAccessGate

**Files:**
- Create: `src/MealMate.Application/Features/FamilyManager/FamilyCalendarAccessGate.cs`
- Create: `tests/MealMate.UnitTests/FamilyManager/FamilyCalendarAccessGateTests.cs`
- Modify: `CustomerCalendarEngine.WithContext` ownership block
- Modify: `DependencyInjection.cs` register `IFamilyCalendarAccessGate`

**Produces:**
```csharp
public interface IFamilyCalendarAccessGate
{
    Task<Result<FamilyCalendarAccess>> AuthorizeAsync(
        Subscription subscription, bool requireWrite, CancellationToken ct);
}
public sealed record FamilyCalendarAccess(
    bool IsManager, Guid? FamilyMemberId, bool EditDailyMeals);
```

- [ ] **Step 1: Write failing tests** (manager write OK; linked+flag write OK; linked without flag write fails; pause write fails; other user fails; individual owner still OK)
- [ ] **Step 2: Implement gate**
- [ ] **Step 3: Wire engine** — if `FamilyGroupId` null → owner check; else gate
- [ ] **Step 4: Run tests** `dotnet test --filter FullyQualifiedName~FamilyCalendarAccessGate|FullyQualifiedName~CustomerCalendar`

---

### Task 4: Family Hub handlers (home, members, access, permissions)

**Files:**
- Create: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`
- Create: `src/MealMate.Application/Features/FamilyManager/FamilyManagerHandlers.cs`
- Create: `tests/MealMate.UnitTests/FamilyManager/FamilyManagerHandlerTests.cs`

**APIs (MediatR):**
- `GetFamilyHomeQuery`
- `GetFamilyMembersQuery` / `GetFamilyMemberQuery`
- `CreateFamilyMemberCommand` — if seat needs payment → `MemberRequiresPayment`
- `Get/UpdateFamilyMemberAccessCommand`
- `Get/UpdateFamilyMemberPermissionsCommand`
- `AcceptFamilyInviteCommand`

Ensure creating member also creates Access+Permissions defaults. Soft-remove sets `RemovedAtUtc` and excludes from lists.

- [ ] **Step 1: Failing handler tests for home (manager), not-manager 403, pause, cannot remove manager, permissions immutable for manager role**
- [ ] **Step 2: Implement handlers**
- [ ] **Step 3: Tests green**

---

### Task 5: API controller + resx

**Files:**
- Create: `src/MealMate.Api/Controllers/V1/CustomerFamilyManagerController.cs`
- Modify: `Resources.Messages.resx` + `.ar.resx`

Routes under `/api/v1/customer/family` per spec section 6.

- [ ] **Step 1: Controller mapping to MediatR**
- [ ] **Step 2: Localization strings for MessageCodes.Family**
- [ ] **Step 3: Build Api to temp dir**

---

### Task 6: Calendar E2E family path + mobile doc

**Files:**
- Create/extend: `tests/.../CustomerCalendar/CustomerCalendarFamilyAccessTests.cs`
- Create: `docs/mobile/family-manager/00-family-manager-wave1.md`

- [ ] **Step 1: Test manager completes member box; linked member with permission completes; without permission fails**
- [ ] **Step 2: Mobile contract doc (Arabic-first like individual calendar)**
- [ ] **Step 3: Full `dotnet test tests/MealMate.UnitTests` green

---

## Spec coverage check

| Spec section | Task |
|--------------|------|
| Access/Permissions entities | 1–2 |
| Calendar gate | 3 |
| Hub APIs | 4–5 |
| Invite accept | 4–5 |
| Error codes | 1 + 5 |
| Tests | 3, 4, 6 |
| Mobile contract | 6 |
| Waves 2–3 out of scope | noted in home shortcuts |

## Placeholder scan

No TBD steps. Calendar HTTP concurrency token deferred explicitly (last write wins) to avoid Schema churn on SubscriptionDay.

---

**Execution:** User said ابدأ — proceed **inline** with Task 1 immediately after this plan is saved (OpenCode go-implementer when available).
