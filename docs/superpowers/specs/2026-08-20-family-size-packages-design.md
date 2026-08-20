# Family size packages — Design

- **Date:** 2026-08-20  
- **Status:** implemented (backend + admin UI; apply DB migration `AddSelectedFamilyPackageIdToFamilyGroup`)  
- **Related:** `2026-08-20-family-catalog-eligibility-design.md`, family checkout contract  
- **Decision locked:** Approach **1** — named size packages stored in pricing settings JSON (not a new domain catalog table).

---

## 1) Problem

Admin needs multiple **named family size packages** (e.g. “Small 2–4”, “Large 4–6”) each with its own member range and discount percent. Customers pick a package by name, then configure each member and checkout.

Today there is a single `FamilyPricingSettings { DiscountPercent, MaxMembers, MinMembersForDiscount }`.

---

## 2) Product rules (v1)

| Rule | Behavior |
| --- | --- |
| Named packages | Admin defines 1..N active packages with `nameAr` / `nameEn` |
| Range + discount | Each package has `minMembers`, `maxMembers` (inclusive, 2–12, min≤max) and `discountPercent` |
| Customer picks first | Mobile lists packages; customer selects one before/with draft group |
| Per-member plans unchanged | After package selection, existing family catalog + plan flow continues |
| Discount when in range | If member count ∈ [min, max], apply that package’s discount on subtotal |
| Hard max | Never allow members above selected package `maxMembers` |
| Soft min during draft | Draft may start with manager only (1). Checkout blocked until count ≥ `minMembers` |
| No overlapping active ranges | Active packages must not have overlapping [min, max] intervals |
| Snapshot on pay | Keep `FamilyGroup.DiscountPercentSnapshot` from selected package at checkout |
| Catalog eligibility | Unchanged (`AvailableForFamily` on program/bundle) |

**Migration of old settings:** On normalize/read, if `packages` empty and legacy fields present, synthesize one active package from legacy (`minMembersForDiscount`→min, `maxMembers`→max, `discountPercent`) named “عائلية / Family”. Persist packages on next admin save. Legacy scalar fields remain for backward compat until removed later (optional mirror: max of package maxes).

---

## 3) Data model

### 3.1 Pricing document

```csharp
public sealed class FamilySizePackageSettings
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public int MinMembers { get; set; } = 2;
    public int MaxMembers { get; set; } = 4;
    public decimal DiscountPercent { get; set; } = 0.10m;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? ImageStorageKey { get; set; } // uploads/admin/family/...
}
```

**Admin UI:** packages are managed under **Subscriptions & Programs → Family packages** (cards + modal with image upload). Pricing workspace only links there.

```csharp
public sealed class FamilyPricingSettings
{
    // Legacy (kept for migration / old clients)
    public decimal DiscountPercent { get; set; } = 0.10m;
    public int MaxMembers { get; set; } = 4;
    public int MinMembersForDiscount { get; set; } = 2;

    public List<FamilySizePackageSettings> Packages { get; set; } = new();
}
```

Normalize:

- Each package: clamp members 2–12, min≤max, discount 0–1, non-empty names, assign Id if empty Guid  
- Active packages: reject overlapping ranges (validation error on admin save)  
- If Packages empty: seed one package from legacy scalars  
- Legacy `MaxMembers` / `DiscountPercent` / `MinMembersForDiscount` updated from selected “primary” or max package for old readers

### 3.2 FamilyGroup

```csharp
public Guid? SelectedFamilyPackageId { get; set; }
// DiscountPercentSnapshot already exists — filled from package at checkout/quote lock
```

EF migration: nullable Guid column + index optional.

---

## 4) Customer APIs

Base: `/api/v1/customer/family-subscription-flow`

| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/packages` | Active packages ordered by `sortOrder` (id, names, min, max, discountPercent) |
| PUT | `/group` | Body adds optional `familyPackageId` (required when creating new draft; required to change package on draft if members would exceed new max — reject) |
| GET | `/current` / screen DTOs | Include selected package summary + effective `maxMembers` / `minMembers` / `discountPercent` from package |
| POST | `/members` | Fail `family.max_members` if count would exceed package max |
| checkout / summary | | Fail `family.min_members` if count < package min; discount from package when in range |

Errors:

- `family.package_required` — no package selected  
- `family.package_not_found` — id inactive/missing  
- `family.max_members` — above package max  
- `family.min_members` — below package min at checkout  
- `family.package_overlap` — admin only  

Metadata: replace single max/discount/min with packages list **or** keep scalars as effective values from selected package when group exists; always expose `GET /packages` for picker.

---

## 5) Admin

Pricing workspace «اشتراك العائلة»:

- List/add/edit/remove packages (name AR/EN, min, max, discount %, active, order)  
- Save validates non-overlap among active packages  
- Remove legacy three-field-only UI (or show as read-only migration hint once packages exist)

---

## 6) Quote / checkout

- Resolve package by `SelectedFamilyPackageId`  
- `discountApplied = memberCount >= package.MinMembers && memberCount <= package.MaxMembers` (with Ready members for pricing lines as today)  
- Checkout gate: Draft, all Ready, `min ≤ count ≤ max`, finalPrice > 0  

---

## 7) Out of scope (v1)

- Separate DB entity / admin CRUD microservice  
- Fixed family price (non per-member)  
- Overlapping ranges  
- Changing package after Active/CheckoutPending  

---

## 8) Testing

- Normalize seeds legacy → one package  
- Overlap rejected on save  
- Add member above max rejected  
- Quote applies discount at min and max inclusive; not below min  
- Checkout blocked below min  

---

## 9) Docs

Update `docs/mobile/family-subscription-api.md` and flutter changes guide for `/packages` + `familyPackageId`.
