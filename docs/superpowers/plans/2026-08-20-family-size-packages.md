# Family Size Packages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or implement inline with checkpoints. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace single family max/discount settings with named size packages (min–max + discount); customer selects a package then continues the existing family draft/checkout flow.

**Architecture:** Store packages in `FamilyPricingSettings.Packages` JSON. Add `FamilyGroup.SelectedFamilyPackageId`. Enforce package max on add-member; enforce min + package discount on quote/checkout. Admin pricing card edits the package list.

**Tech Stack:** .NET 9, EF Core, Angular 19 admin dashboard, existing FamilyCheckout feature.

## Global Constraints

- Active package ranges must not overlap.
- Members clamp 2–12; discount 0–1.
- Soft min during draft (manager alone OK); hard max always.
- No git commits unless user asks.
- Keep `AvailableForFamily` catalog behavior unchanged.

## File map

| Area | Files |
| --- | --- |
| Settings model | `SubscriptionPricingSettings.cs` |
| Domain | `FamilyGroup.cs` + EF config + migration |
| Quote / gate / draft | `FamilyQuoteService.cs`, `FamilyCheckoutGate.cs`, `FamilyDraftHandlers.cs`, `FamilyDraftCommands.cs`, controller |
| Admin API already persists pricing JSON | pricing save path (no new controller if document already PUT) |
| Admin UI | `pricing-workspace-page`, `subscriptions-state.service.ts`, `pricing-api.service.ts`, i18n |
| Tests | `CustomerFamilySubscriptionFlowControllerTests.cs` (+ normalize unit if exists) |
| Docs | `docs/mobile/family-subscription-*.md` |

---

### Task 1: Settings model + normalize + FamilyGroup column

**Files:**
- Modify: `src/MealMate.Application/Features/ProgramsSubscriptions/SubscriptionPricingSettings.cs`
- Modify: `src/MealMate.Domain/ProgramsSubscriptions/FamilyGroup.cs`
- Modify: EF FamilyGroup configuration
- Create: migration `AddSelectedFamilyPackageIdToFamilyGroup`

- [ ] Add `FamilySizePackageSettings` + `Packages` list on `FamilyPricingSettings`
- [ ] Normalize: seed from legacy if empty; clamp; validate no active overlaps (throw or clear invalid on normalize — prefer validation on admin write; normalize only clamps + seeds)
- [ ] Add `SelectedFamilyPackageId` Guid? on `FamilyGroup` + migration
- [ ] Build Persistence/Application

---

### Task 2: Quote, gate, draft handlers, APIs

**Files:**
- `FamilyQuoteService.cs` — discount from selected package when count in [min,max]
- `FamilyCheckoutGate.cs` — min/max from package
- `FamilyDraftHandlers.cs` / commands / DTOs — package on screen + metadata; require package; max on add
- `CustomerFamilySubscriptionFlowController.cs` — `GET packages`; `PUT group` accepts `familyPackageId`
- Fix typo `ذusing` in `FamilyDraftCommands.cs` if still present

- [ ] `GET .../packages`
- [ ] Upsert group sets/validates package
- [ ] MapScreen exposes package id + effective min/max/discount
- [ ] Tests: max reject, min checkout block, discount at 2 and 4 for package 2–4

---

### Task 3: Admin pricing UI

**Files:**
- `pricing-api.service.ts` — packages on family settings type
- `subscriptions-state.service.ts` — load/save packages
- `pricing-workspace-page` HTML/TS — package editor list
- `subscriptions.i18n.ts`

- [ ] Replace single three-field card with package list editor
- [ ] Client-side overlap warning; server remains source of truth

---

### Task 4: Mobile docs

- Update API + flutter guides for packages + `familyPackageId`
- Mark design status implemented after ship

---

## Execution

User requested immediate implementation → **inline execution** in this session (explicit Codex fallback; no wait for execution-choice UI).
