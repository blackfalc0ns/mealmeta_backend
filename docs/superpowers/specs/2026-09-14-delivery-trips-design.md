# Delivery Trips Subsystem Design Document

- **Date:** 2026-09-14
- **Author:** Antigravity & Pair Programming Partner
- **Status:** Approved Draft
- **Target Surface:** 
  - Backend: `MealMate.Api`, `MealMate.Application`, `MealMate.Domain`, `MealMate.Persistence`
  - Frontend: `MealMate.RestaurantDashboard` (`/restaurant/delivery/trips`)

---

## 1. Problem Statement & Scope

The restaurant dashboard currently features a multi-stop delivery trip management UI at `/restaurant/delivery/trips` and `/restaurant/delivery/trips/:tripId`. However, the frontend currently relies on mock data (`TRIPS_MOCK`), and the backend does not have operational API endpoints or CQRS handlers to manage delivery trips, stops, driver assignments, and candidate orders.

This subsystem provides the complete end-to-end backend logic and frontend integration for Restaurant Delivery Trips.

---

## 2. Goals & Success Criteria

1. **Backend CQRS & Endpoints:**
   - Query all delivery trips for a restaurant with status filtering (`all`, `assigned`, `in_progress`, `completed`, `cancelled`) and search.
   - Aggregate summary counts (`active`, `assigned`, `completed`).
   - Query unassigned candidate delivery orders for the day eligible for dispatch batching.
   - Query detailed view for a single trip including stops, customer zone, shift, assigned driver, and progress timeline.
   - Create a new delivery trip from selected candidate orders and an assigned driver.
   - Update trip status (`in_progress`, `completed`, `cancelled`).
   - Update stop status (`picked_up`, `delivered`, `failed`).

2. **Database & Domain Entities:**
   - Utilize existing `Trip` (`MealMate.Domain.DriverDelivery.Trip`).
   - Introduce `TripStop` entity to represent customer drop-offs associated with a `Trip`, linked to `DailyOrderId` / `SubscriptionDayId`, customer delivery zone, box count, and status (`Pending`, `PickedUp`, `Delivered`, `Failed`).
   - Link driver assignments via `DriverAssignment` and `DriverProfile`.

3. **Frontend Integration:**
   - Introduce `TripsApiService` in the restaurant dashboard communicating with `/api/v1/restaurants/delivery/trips`.
   - Update `TripsFacade` to load real trips and candidate stops from the API.
   - Support creating trips and reflecting updates reactively in the UI.

---

## 3. Domain Model

### Existing `Trip` Entity (`MealMate.Domain.DriverDelivery.Trip`)
- `Id`: `Guid`
- `RestaurantId`: `Guid`
- `DispatcherUserId`: `Guid?`
- `Code`: `string` (e.g. `TRP-101`)
- `Status`: `TripStatus` (`Draft = 0`, `Assigned = 1`, `InProgress = 2`, `Completed = 3`, `Cancelled = 4`)
- `PlannedDate`: `DateOnly`
- `ShiftName`: `string?` (e.g. `Morning`, `Evening`)
- `StartedAtUtc`: `DateTime?`
- `CompletedAtUtc`: `DateTime?`
- `Notes`: `string?`
- `Stops`: `ICollection<TripStop>`
- `DriverAssignments`: `ICollection<DriverAssignment>`
- `TrackingPoints`: `ICollection<DeliveryTrackingPoint>`

### New `TripStop` Entity (`MealMate.Domain.DriverDelivery.TripStop`)
- `Id`: `Guid`
- `TripId`: `Guid`
- `Trip`: `Trip`
- `DailyOrderId`: `Guid?`
- `SubscriptionDayId`: `Guid?`
- `OrderCode`: `string`
- `CustomerMaskedId`: `string`
- `CustomerName`: `string?`
- `CustomerPhone`: `string?`
- `ZoneNameAr`: `string`
- `ZoneNameEn`: `string`
- `MealSummaryAr`: `string`
- `MealSummaryEn`: `string`
- `BoxCount`: `int`
- `SlotLabel`: `string`
- `SequenceNumber`: `int`
- `Status`: `TripStopStatus` (`Pending = 0`, `PickedUp = 1`, `Delivered = 2`, `Failed = 3`)
- `DeliveredAtUtc`: `DateTime?`
- `FailureReason`: `string?`

---

## 4. Backend Architecture (Clean Architecture / CQRS)

### Endpoints (`MealMate.Api/Controllers/V1/RestaurantDeliveryTripsController.cs`)
- `GET /api/v1/restaurants/delivery/trips`
  - Query: `restaurantId` (optional, defaults to current restaurant user), `status` (optional), `search` (optional).
  - Returns: `RestaurantTripsDataDto` (containing `summaries`, `trips`, `candidates`).
- `GET /api/v1/restaurants/delivery/trips/{id:guid}`
  - Returns: `DeliveryTripDto`.
- `POST /api/v1/restaurants/delivery/trips`
  - Body: `CreateRestaurantTripCommand` (`DriverId`, `StopCandidateIds`, `ShiftLabel`, `Notes`).
  - Returns: `Guid` (TripId).
- `PUT /api/v1/restaurants/delivery/trips/{id:guid}/status`
  - Body: `UpdateTripStatusCommand` (`Status`).
  - Returns: `UnitResult`.
- `PUT /api/v1/restaurants/delivery/trips/{id:guid}/stops/{stopId:guid}/status`
  - Body: `UpdateTripStopStatusCommand` (`Status`, `FailureReason`).
  - Returns: `UnitResult`.

### Handlers & Queries (`MealMate.Application/Features/DriverDelivery/Trips/`)
- `GetRestaurantTripsQuery` -> Returns trips filtered by date/status, computes summaries, queries ready candidate orders from `SubscriptionDays` for today that have not been assigned to an active trip.
- `GetRestaurantTripDetailQuery` -> Returns single trip with full details, stops, driver info, and dispatch details.
- `CreateRestaurantTripCommand` -> Validates driver eligibility, creates `Trip`, creates `TripStop` for each order, sets `OrderHandover.TripId`, commits via Unit of Work.
- `UpdateTripStatusCommand` -> Validates status transition, records timestamp (`StartedAtUtc`, `CompletedAtUtc`).
- `UpdateTripStopStatusCommand` -> Updates stop status and `DeliveredAtUtc`.

---

## 5. Persistence & Migration

- Create `TripStopConfiguration` in `MealMate.Persistence/Configurations/DriverDelivery/DriverDeliveryConfigurations.cs`.
- Add `DbSet<TripStop> TripStops => Set<TripStop>();` to `IAppDbContext` and `AppDbContext`.
- Apply EF Core migration to add `TripStops` table and any new fields on `Trips` table (`Code`, `ShiftName`).

---

## 6. Frontend Integration (`restaurant-dashboard`)

- Create `TripsApiService` in `src/app/features/restaurant/trips/data/trips-api.service.ts`:
  - `getTrips(filter?: TripFilter, search?: string)`
  - `getTripById(id: string)`
  - `createTrip(payload: CreateTripApiRequest)`
  - `updateTripStatus(tripId: string, status: TripStatus)`
- Refactor `TripsFacade` in `src/app/features/restaurant/trips/data/trips.facade.ts`:
  - Inject `TripsApiService`.
  - Replace `setTimeout` with API call observable subscriptions updating `data` and `page` signals.
  - Implement `createTrip` invoking API, clearing draft, and triggering callback on success.

---

## 7. Verification & Acceptance Criteria

1. **Automated Verification:**
   - Solution builds with zero warnings/errors (`dotnet build MealMate.sln`).
   - Frontend builds without TypeScript or Angular compiler errors (`npm run build` in `restaurant-dashboard`).
2. **Integration Verification:**
   - `GET /api/v1/restaurants/delivery/trips` returns 200 OK with valid schema.
   - `POST /api/v1/restaurants/delivery/trips` successfully creates trip and persists to PostgreSQL database on VPS.
   - Visiting `http://localhost:4201/restaurant/delivery/trips` displays trips loaded from API.
