# Active Delivery Address Delete Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Block deleting the currently active delivery address until another address activates after 72 hours, and keep a clear bilingual 72h activation notice on address change.

**Architecture:** Extend `DeleteCustomerAddressCommand` to load the customer profile, refuse deletion when `address.Id == profile.ActiveDeliveryAddressId`, and clear pending schedule fields when the deleted address is the pending target. Tighten `BuildDeliveryChangeNotice` copy so create/update responses already returning the notice match product wording.

**Tech Stack:** .NET, MediatR-style handlers, EF Core InMemory unit tests (xUnit), existing `Error.Conflict` / `LocalizedTextDto` patterns.

## Global Constraints

- Guard key is `ActiveDeliveryAddressId`, never `IsDefault` alone.
- Pending address delete is allowed and must clear pending profile fields.
- Do not commit unless the user asks.
- Keep changes limited to address handlers, notice text, and unit tests.

## File map

- Modify: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerAddresses.cs` — delete guard + pending clear
- Modify: `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerDeliveryAddressService.cs` — notice copy
- Modify: `tests/MealMate.UnitTests/IdentityAccess/CustomerAddressesTests.cs` — coverage

---

### Task 1: Failing tests for delete guard and notice

**Files:**
- Modify: `tests/MealMate.UnitTests/IdentityAccess/CustomerAddressesTests.cs`

**Interfaces:**
- Consumes: `DeleteCustomerAddressCommand`, `UpdateCustomerAddressCommand`, `CustomerDeliveryAddressService.ChangeDelay`, existing `Fixture`/`SeedAsync`/`CreateAsync`
- Produces: failing tests that encode conflict code `address.active_delivery_cannot_delete`

- [ ] **Step 1: Add tests**

Add:

1. `Delete_rejects_active_delivery_address`
2. `Delete_allows_non_active_address`
3. `Delete_pending_address_clears_pending_schedule`
4. `Delete_allows_former_active_after_pending_applies`
5. Assert notice AR/EN mention that the new address will not activate / receive deliveries until after 72 hours (extend existing default-switch test or add a focused assert)

- [ ] **Step 2: Run tests and confirm RED**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~CustomerAddressesTests" --no-restore
```

Expected: new delete-guard tests fail (delete currently succeeds for active).

- [ ] **Step 3: Implement delete guard + pending clear + notice copy**

In `DeleteCustomerAddressCommand` handler:

1. Load profile via `_deliveryAddresses.GetOrCreateProfileAsync`
2. If `profile.ActiveDeliveryAddressId == address.Id` → `Error.Conflict("address.active_delivery_cannot_delete", bilingual message)`
3. If `profile.PendingDeliveryAddressId == address.Id` → clear pending id + effectiveFrom
4. Then remove + audit as today

Update `BuildDeliveryChangeNotice`:

- AR: clarify العنوان الجديد لن يُفعَّل ولن تُسلَّم عليه الطلبات إلا بعد 72 ساعة
- EN: clarify the new address will not become active / receive deliveries until after 72 hours

- [ ] **Step 4: Run tests GREEN**

Same `dotnet test` filter; all `CustomerAddressesTests` pass.

---

### Task 2: Spec self-check

- [ ] Confirm conflict code, pending clear, and notice wording match `docs/superpowers/specs/2026-08-27-active-delivery-address-delete-guard-design.md`
