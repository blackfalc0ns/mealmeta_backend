# Geo Region Matching Implementation Plan

> **For agentic workers:** Implement task-by-task. Steps use checkbox syntax.

**Goal:** Real Area centroids + stronger lat/lng matching so customer map pins resolve the correct catalog Area for delivery coverage.

**Architecture:** Update Kuwait region seeder centroids; tighten `MapPlaceRegionSuggestion` and `KuwaitGeoBounds`; cover with unit tests.

**Tech Stack:** .NET / EF Core / existing Maps enrichment

**Spec:** `docs/superpowers/specs/2026-09-01-geo-region-matching-design.md`

## Files

- `src/MealMate.Persistence/Seeding/KuwaitRegionCatalogSeeder.cs` — real Area centroids
- `src/MealMate.Application/Common/Geography/KuwaitGeoBounds.cs` — distance constants
- `src/MealMate.Application/Features/Maps/MapPlaceRegionSuggestion.cs` — matching algorithm
- `tests/MealMate.UnitTests/Maps/MapPlaceRegionSuggestionTests.cs` — tests
- `docs/mobile/address-and-restaurant-browse.md` — brief note on geo matching

## Tasks

### Task 1: Centroids + matching + tests
- [ ] Add `AreaCentroids` dictionary and apply in seeder (upsert on seed)
- [ ] Add `MaxNameMismatchDistanceKm = 10`, set `MaxSuggestionDistanceKm = 12`
- [ ] Update EnrichAsync per design
- [ ] Extend unit tests (Salmiya pin, far wrong-name discard, nearest fallback)
- [ ] Run MapPlaceRegionSuggestionTests
