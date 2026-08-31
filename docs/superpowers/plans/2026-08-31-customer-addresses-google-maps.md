# Customer Addresses Google Maps Implementation Plan

> **For agentic workers:** Implement task-by-task. Steps use checkbox syntax.

**Goal:** Enrich maps geocode responses for form autofill + require Kuwait lat/lng on customer addresses.

**Architecture:** Extend `GoogleGeocodeResult` and Google component parsing; enrich with nearest Area centroid suggestion via Application service; tighten FluentValidation on address create/update.

**Tech Stack:** .NET 9, EF Core, FluentValidation, existing Google Geocoding HTTP client

## Global Constraints

- Kuwait bounds: lat 28.5–30.2, lng 46.5–48.5
- Maps endpoints stay anonymous
- Mobile resolves before save; server validates only
- Do not change 72h delivery activation behavior

---

### Task 1: Shared Kuwait bounds + expanded geocode result

**Files:**
- Create `src/MealMate.Application/Common/Geography/KuwaitGeoBounds.cs`
- Modify `IGoogleGeocodingService.cs` (`GoogleGeocodeResult`)
- Modify `GoogleGeocodingService.cs` (parse `address_components`)
- Modify restaurant rules to call shared bounds (optional thin wrapper)

- [ ] Add `KuwaitGeoBounds.IsWithin(...)`
- [ ] Expand result with governorate/area/block/street/building + suggested region fields
- [ ] Parse Google address_components
- [ ] Unit-test component mapping with a fake payload if practical; otherwise cover via address validation tests

### Task 2: Region suggestion + Maps controller

**Files:**
- Create `src/MealMate.Application/Features/Maps/MapPlaceEnrichment.cs` (or similar)
- Modify `MapsController.cs`
- Register DI if needed

- [ ] After geocode/reverse, suggest nearest active Area with centroid ≤ 20 km
- [ ] Prefer name match on Area NameAr/NameEn when available
- [ ] Return enriched DTO from controller

### Task 3: Customer address validation

**Files:**
- Modify `CustomerAddresses.cs` validators (+ handler guard)
- Modify `CustomerAddressesTests.cs`
- Add test rejecting missing/out-of-bounds coords

- [ ] Require Latitude/Longitude
- [ ] Must pass `KuwaitGeoBounds`
- [ ] Run unit tests for CustomerAddresses

### Task 4: Mobile contract note + frontend type update

**Files:**
- Spec already documents mobile needs
- Update restaurant/admin `maps-geocoding-api.service.ts` interfaces for new optional fields (compat)

- [ ] Extend TS interfaces
- [ ] Commit backend changes
