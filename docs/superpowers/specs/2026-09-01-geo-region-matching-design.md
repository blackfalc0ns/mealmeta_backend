# Design: Geo → catalog region matching for delivery coverage

## Goal

When a customer confirms a map pin (lat/lng), reverse-geocode must resolve the correct MealMate **Area** `suggestedRegionId` so restaurant service-region coverage works. Restaurant area selection stays list-based.

## Current problem

Area centroids are fake offsets from governorate centers. Matching leans on Google area names; nearest-centroid fallback can pick the wrong Area (e.g. Salmiya vs Salmi).

## Approach

1. Seed **real** `CentroidLatitude` / `CentroidLongitude` per Area code.
2. Strengthen matching:
   - Name candidates (AR/EN) + governorate boost → pick **closest** among name matches.
   - If best name match is farther than `MaxNameMismatchDistanceKm` (10 km), discard name and use nearest centroid.
   - No name → nearest Area within `MaxSuggestionDistanceKm` (12 km, down from 20).
3. Coverage unchanged: exact `CustomerAddress.RegionId` ↔ active `RestaurantServiceRegions.RegionId`.

## Out of scope

- Polygons / geofences
- Changing restaurant service-region UI
- Changing `addressId` / token delivery resolution

## Success

- Pin in Salmiya → `suggestedRegionId` = Salmiya (`KW-SL`)
- Restaurant covering that Area appears in tier browse; others do not
