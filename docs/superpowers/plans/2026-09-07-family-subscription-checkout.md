# Family Subscription Checkout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align existing `/api/v1/customer/family-subscription-flow` with the Figma family wizard: phone lookup roster, unpaid members, per-member checkout, required allergies, and family discount on payable/active+new count.

**Architecture:** Keep `FamilyGroup` + `FamilyMember` + `IFamilyCheckoutService`. Add phone/occupation/startDate on members, lookup/add that attach or create a customer without a manager-set password, allow roster growth after `Active`, change `FamilyQuoteService` discount count, and gate checkout on member allergies. Flutter is out of repo; update customer workflow/mobile contract.

**Tech Stack:** .NET 9, EF Core/PostgreSQL, MediatR, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-07-family-subscription-checkout-design.md`

## Global Constraints

- Manager JWT only on family-subscription-flow; never trust client `finalPrice`.
- One family per customer; `409 family.already_in_family` if the phone’s user is already in another group.
- Do not collect a password on add; `CreateAsync` may use a random unusable password never returned to the client.
- Unpaid members (`MemberSubscriptionId == null`) must not be treated as subscribers in admin KPIs/subscription rows.
- Allergies required for **selected/payable** members only; disliked ingredients are not a pay gate.
- No separate dietary-preferences step in family checkout.
- Price changes/discounts on later add-ons apply to **new** subscriptions only.
- Do not commit unless the user asks. Do not run `git reset` / `git checkout` / `git clean`.

## File map

| File | Responsibility |
|------|----------------|
| `src/MealMate.Domain/ProgramsSubscriptions/FamilyMember.cs` | `PhoneNumber`, `Occupation`, `StartDate` |
| `src/MealMate.Persistence/Configurations/ProgramsSubscriptions/ProgramsSubscriptionsConfigurations.cs` | column lengths + index on phone |
| `src/MealMate.Persistence/Migrations/*AddFamilyMemberPhoneOccupationStartDate*` | schema |
| `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs` | lookup DTOs, write DTO phone/occupation, plan startDate |
| `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftHandlers.cs` | lookup/add/active roster, startDate patch |
| `src/MealMate.Application/Features/FamilyCheckout/FamilyQuoteService.cs` | payable + active+new discount count |
| `src/MealMate.Application/Features/FamilyCheckout/FamilyCheckoutService.cs` | allergies gate |
| `src/MealMate.Api/Controllers/V1/CustomerFamilySubscriptionFlowController.cs` | lookup, member allergies/addresses |
| `tests/MealMate.ApiTests/CustomerCheckout/CustomerFamilySubscriptionFlowControllerTests.cs` | API tests |
| `tests/MealMate.UnitTests/ProgramsSubscriptions/FamilyQuoteServiceTests.cs` | quote count tests |
| `../workflows/customer/F19_family_subscription.md` | product text |
| `../workflows/customer/00_mobile_subscription_app.md` | Flutter contract |

---

### Task 1: Member schema — phone, occupation, start date

**Files:**
- Modify: `src/MealMate.Domain/ProgramsSubscriptions/FamilyMember.cs`
- Modify: `src/MealMate.Persistence/Configurations/ProgramsSubscriptions/ProgramsSubscriptionsConfigurations.cs` (`FamilyMemberConfiguration`)
- Create: EF migration `AddFamilyMemberPhoneOccupationStartDate`
- Modify: `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs` (`FamilyMemberCardDto`, `FamilyMemberWriteDto`, `SetFamilyMemberPlanRequest`)

**Interfaces:**
- Consumes: existing `FamilyMember` + `FamilyMemberConfiguration`
- Produces: `FamilyMember.PhoneNumber` (`string?`, max 32), `Occupation` (`string?`, max 80), `StartDate` (`DateOnly?`); card DTO includes the same; write DTO includes `Phone` + `Occupation`; plan request includes `Optional<DateOnly> StartDate`

- [ ] **Step 1: Write a failing compile-level unit assertion by adding properties to a throwaway test if needed — prefer mapping test in Task 2. For this task, add properties and configuration first, then migration.**

Add to `FamilyMember`:

```csharp
public string? PhoneNumber { get; set; }
public string? Occupation { get; set; }
public DateOnly? StartDate { get; set; }
```

In `FamilyMemberConfiguration`:

```csharp
builder.Property(x => x.PhoneNumber).HasMaxLength(32);
builder.Property(x => x.Occupation).HasMaxLength(80);
builder.HasIndex(x => x.PhoneNumber);
```

Extend records:

```csharp
public sealed record FamilyMemberCardDto(
    Guid MemberId,
    FamilyMemberRole Role,
    string DisplayName,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    Guid? DeliveryAddressId,
    FamilyMemberSetupStatus SetupStatus,
    Guid? ProgramId,
    Guid? BundleId,
    RestaurantTier? SelectedTier,
    int? DurationDays,
    bool IncludeFriday,
    bool IsCurrentUser,
    string? PhoneNumber = null,
    string? Occupation = null,
    DateOnly? StartDate = null);

public sealed record FamilyMemberWriteDto(
    string DisplayName,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    [property: JsonConverter(typeof(EmptyStringToNullableGuidConverter))]
    Guid? DeliveryAddressId,
    string? Phone = null,
    string? Occupation = null);
```

Add to `SetFamilyMemberPlanRequest`:

```csharp
[JsonConverter(typeof(OptionalJsonConverter<DateOnly>))]
public Optional<DateOnly> StartDate { get; init; }
```

Include `StartDate.IsSpecified` in `HasAnyPatch`.

- [ ] **Step 2: Add migration**

From `src/MealMate.Persistence` (or repo EF convention):

```powershell
dotnet ef migrations add AddFamilyMemberPhoneOccupationStartDate --project src/MealMate.Persistence --startup-project src/MealMate.Api
```

- [ ] **Step 3: Build**

```powershell
dotnet build src/MealMate.Api/MealMate.Api.csproj
```

Expected: PASS. Fix `MapScreen` in `FamilyDraftHandlers` to pass the new card fields so the solution compiles.

- [ ] **Step 4: Run existing family API tests**

```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter FullyQualifiedName~CustomerFamilySubscriptionFlowControllerTests
```

Expected: PASS (new fields optional).

---

### Task 2: Phone lookup

**Files:**
- Modify: `FamilyDraftCommands.cs` — add `FamilyMemberLookupRequest`, `FamilyMemberLookupDto`, `LookupFamilyMemberByPhoneQuery`
- Modify: `FamilyDraftHandlers.cs` — handle lookup
- Modify: `CustomerFamilySubscriptionFlowController.cs` — `POST members/lookup`
- Test: `CustomerFamilySubscriptionFlowControllerTests.cs`

**Interfaces:**
- Consumes: `IUserAccountService.FindByPhoneAsync(string phone, CancellationToken)`
- Produces: `POST /api/v1/customer/family-subscription-flow/members/lookup` → `FamilyMemberLookupDto(bool Found, string? DisplayName, int? AgeYears, FamilyMemberGender? Gender)`
- Errors: `400 family.phone_required`; `409 family.already_in_family` when the user is linked to another `FamilyGroup` (any status except cancelled) via `FamilyMemberAccess.LinkedCustomerUserId` or manager id

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Lookup_WhenPhoneHasCustomer_ReturnsDisplayName()
{
    await using var db = CreateDb();
    var managerId = Guid.NewGuid();
    var otherId = Guid.NewGuid();
    var users = Substitute.For<IUserAccountService>();
    users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
        .Returns(new UserAccountDto(managerId, null, "+96550000001", "مدير", UserType.Customer, AccountStatus.Active, null, false, false, DateTime.UtcNow, null));
    users.FindByPhoneAsync("+96550000002", Arg.Any<CancellationToken>())
        .Returns(new UserAccountDto(otherId, null, "+96550000002", "سارة", UserType.Customer, AccountStatus.Active, null, false, false, DateTime.UtcNow, null));

    var controller = CreateController(db, managerId, users);
    await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);

    var result = await controller.LookupMember(new FamilyMemberLookupRequest("+96550000002"), CancellationToken.None);
    var ok = result.Should().BeOfType<OkObjectResult>().Subject;
    var dto = ok.Value.Should().BeOfType<FamilyMemberLookupDto>().Subject;
    dto.Found.Should().BeTrue();
    dto.DisplayName.Should().Be("سارة");
}
```

Add `LookupMember` on the controller in the same task after the test fails to compile.

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter FullyQualifiedName~Lookup_WhenPhoneHasCustomer_ReturnsDisplayName
```

Expected: FAIL (method/type missing).

- [ ] **Step 3: Implement lookup**

```csharp
public sealed record FamilyMemberLookupRequest(string Phone);
public sealed record FamilyMemberLookupDto(bool Found, string? DisplayName, int? AgeYears, FamilyMemberGender? Gender);
public sealed record LookupFamilyMemberByPhoneQuery(string Phone) : IQuery<FamilyMemberLookupDto>;
```

Controller:

```csharp
[HttpPost("members/lookup")]
public async Task<IActionResult> LookupMember([FromBody] FamilyMemberLookupRequest request, CancellationToken cancellationToken)
{
    if (!IsCustomer()) return Forbid();
    return HandleResult(await Sender.Send(new LookupFamilyMemberByPhoneQuery(request.Phone), cancellationToken));
}
```

Handler: trim phone; empty → `Error.Validation("family.phone_required", ...)`. `FindByPhoneAsync`; null → `new FamilyMemberLookupDto(false, null, null, null)`. If found, query whether that user id is `ManagerCustomerUserId` or `FamilyMemberAccess.LinkedCustomerUserId` on another group → `Error.Conflict("family.already_in_family", ...)`. Else return found + `FullName`. Age/gender only if already stored on a member row for that user; otherwise null.

Do not return email, full address, or other PII.

- [ ] **Step 4: Re-run test**

```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter FullyQualifiedName~Lookup_WhenPhoneHasCustomer
```

Expected: PASS. Add a second test `Lookup_WhenUnknown_ReturnsFoundFalse` in the same file.

---

### Task 3: Add member by phone (attach or create, no manager password)

**Files:**
- Modify: `FamilyDraftHandlers.cs` (`AddFamilyDraftMemberCommand`, `UpdateFamilyMemberCommand`, `ApplyWrite`)
- Modify: `IUserAccountService` / `CreateUserRequest` only if password must be optional; otherwise `CreateAsync(new CreateUserRequest(null, phone, randomPassword, displayName, UserType.Customer, AccountStatus.Active))`
- Test: `CustomerFamilySubscriptionFlowControllerTests.cs`

**Interfaces:**
- Consumes: lookup rules; `CreateAsync`; `FamilyMemberAccess.LinkedCustomerUserId`
- Produces: add member with `Phone` required; miss → new user; hit → attach; `MemberSubscriptionId` remains null; works when group `Status` is `Draft` **or** `Active` for unpaid add

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task AddMember_UnknownPhone_CreatesUserWithoutReturningPassword()
{
    // Upsert group, AddMember with phone + name, assert 200, MemberSubscriptionId null,
    // users.CreateAsync received PhoneNumber and a non-empty Password that is not in the HTTP response.
}

[Fact]
public async Task AddMember_WhenGroupActive_AddsUnpaidMember()
{
    // Seed Active family group for manager, POST members, 200, new member SetupStatus not subscribed.
}
```

- [ ] **Step 2: Run tests — expect FAIL** (`family.not_draft` on active group; phone ignored).

```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter FullyQualifiedName~AddMember_
```

- [ ] **Step 3: Implement**

- Require non-empty `Phone` on add (`family.phone_required`).
- Reuse lookup conflict `family.already_in_family`.
- `FindByPhoneAsync`: if user exists, set `FamilyMemberAccess.HasSeparateAccount = true` and `LinkedCustomerUserId`; fill `DisplayName` from account if client sent blank.
- Else `CreateAsync` with random 32-byte password (`Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))`); never put it on any DTO.
- Remove `if (group.Status != FamilyGroupStatus.Draft) return family.not_draft` for **add/update of members with `MemberSubscriptionId == null`**. Keep draft-only for deleting paid members and for changing plans of already-paid members.
- `ApplyWrite` sets `PhoneNumber` and `Occupation`.
- Still enforce `maxMembers`.

- [ ] **Step 4: Re-run Task 3 tests — PASS.**

---

### Task 4: Quote discount count = payable / active+new

**Files:**
- Modify: `src/MealMate.Application/Features/FamilyCheckout/FamilyQuoteService.cs`
- Modify: `FamilyPackageResolver` if it only uses `SelectedFamilyPackageId` — resolve by count when snapshot id missing or when recounting
- Test: `tests/MealMate.UnitTests/ProgramsSubscriptions/FamilyQuoteServiceTests.cs`

**Interfaces:**
- Consumes: `FamilyQuoteLine.MemberPrice`, `FamilyMember.MemberSubscriptionId`, packages in `FamilyPricingSettings`
- Produces: `discountCount` = payable lines (price > 0) for first checkout; for add-on, `activeFamilySubscriberCount + newPayableCount` where active = members with non-null `MemberSubscriptionId` and subscription still active. Package chosen by `MinMembers`/`MaxMembers`. Existing paid lines stay in `Lines` but do not change historical subscription prices (checkout already only creates new subs).

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public async Task Quote_UnpaidExtraRosterMember_DoesNotInflateDiscountCount()
{
    // 2 pricable members + 1 member with no program → discount as 2, not 3
}

[Fact]
public async Task Quote_LaterAddon_UsesActivePlusNewCountForPackage()
{
    // 3 members with MemberSubscriptionId set + 1 new pricable unpaid → package for 4 applies to payable (new) subtotal only
}
```

- [ ] **Step 2: Run**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter FullyQualifiedName~FamilyQuoteServiceTests.Quote_UnpaidExtraRosterMember
```

Expected: FAIL (current code uses `group.Members.Count`).

- [ ] **Step 3: Replace**

```csharp
var memberCount = group.Members.Count;
```

with discount-count as spec. Use `FamilyPackageResolver.ResolveForCount(settings, discountCount)` if that helper exists; otherwise pick the active package where `discountCount` is in `[MinMembers, MaxMembers]`. Still skip discount when payable subtotal is 0.

- [ ] **Step 4: Run all `FamilyQuoteServiceTests` — PASS**, including existing converted-manager prepaid test.

---

### Task 5: Member allergies + checkout gate + start date

**Files:**
- Modify: `CustomerFamilySubscriptionFlowController.cs`
- Modify: `FamilyCheckoutService.cs` (before payment)
- Modify: `FamilyDraftHandlers.cs` (`SetFamilyMemberPlanCommand` startDate)
- Reuse: `SaveCustomerAllergiesCommand` — add overload `SaveCustomerAllergiesCommand(Guid targetUserId, IReadOnlyList<FoodPreferenceItemDto> allergies)` **or** new `SaveFamilyMemberAllergiesCommand(Guid memberId, ...)` that verifies manager owns the group and writes allergies for `LinkedCustomerUserId`
- Test: API tests + checkout conflict `family.allergies_required`

**Interfaces:**
- Consumes: existing `SaveCustomerAllergiesCommand` / `GetCustomerAllergiesQuery` internals
- Produces: `PUT .../members/{memberId}/allergies` body `{ "allergies": [ { "code": "NONE" } ] }`; checkout `409 family.allergies_required` if any payable member’s linked user has not saved allergies; `SetFamilyMemberPlan` persists `StartDate`

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public async Task SaveMemberAllergies_None_AllowsCheckoutReadiness()
{
    // add linked member, PUT allergies NONE, GET summary CanCheckout not blocked by allergies
}

[Fact]
public async Task Checkout_WhenSelectedMemberMissingAllergies_ReturnsAllergiesRequired()
{
    // plan-complete member without allergies save → checkout 409 family.allergies_required
}
```

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Implement commands + controller routes. For addresses, manager `POST .../members/{memberId}/addresses` should create `CustomerAddress` for `LinkedCustomerUserId` and set `FamilyMember.DeliveryAddressId` (reuse individual address DTO). If a small address helper already exists for the manager, parameterize by target user id with ownership checks.**

Checkout payable members = pricable + (manager not prepaid-keep). Each must have allergies confirmed (same rule as individual: saved including NONE) and `regionId` on address.

- [ ] **Step 4: Run Task 5 tests — PASS.**

---

### Task 6: Admin unpaid members + workflow/mobile contract

**Files:**
- Modify: `src/MealMate.Application/Features/FamilyCheckout/AdminFamilyGroupHandlers.cs` — subscriber counts use members with `MemberSubscriptionId != null` only
- Test: existing admin family group tests if present; else add one in ApiTests
- Modify: `workflows/customer/F19_family_subscription.md`
- Modify: `workflows/admin/F19_family_subscription.md`
- Modify: `workflows/customer/00_mobile_subscription_app.md` §3.1 and add family-subscription-flow table matching spec screens 1–6
- Modify: `workflows/customer/00_screens_flowcharts.md` F19 section

**Interfaces:**
- Consumes: spec §§4–5, 8
- Produces: docs that Flutter can implement without guessing; admin stats not counting unpaid roster as subscribers

- [ ] **Step 1: Write failing admin stats test if `GetAdminFamilyGroupStatsQuery` counts all members — expect familyMembers subscriber count to ignore unpaid.**
- [ ] **Step 2: Run — FAIL if current code counts roster.**
- [ ] **Step 3: Filter `MemberSubscriptionId != null`. Update the four workflow files: roster first, phone lookup, no password, select whom to pay, per-member plan, required allergies, no dietary step, discount rules.**
- [ ] **Step 4: `dotnet test` filter `Family` — PASS.**

---

## Spec coverage

| Spec section | Task |
|--------------|------|
| Phone lookup / no existing-user CTA | 2 |
| Add without password / create or attach | 3 |
| Unpaid members after Active | 3, 6 |
| Per-member plan / duration / friday / tier | existing + 1 startDate |
| Quote payable vs roster; later active+new | 4 |
| Allergies required / no skip | 5 |
| Per-member address | 5 |
| Admin not subscriber | 6 |
| Mobile contract / F19 | 6 |
| Dietary screen omitted | 6 (docs) + checkout readiness must not require dietary |

## Execution

Plan saved to `docs/superpowers/plans/2026-09-07-family-subscription-checkout.md`.
