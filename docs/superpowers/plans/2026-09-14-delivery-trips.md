# Delivery Trips Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the end-to-end backend delivery trips logic and connect the restaurant dashboard delivery trips page (`http://localhost:4201/restaurant/delivery/trips`) with real database persistence.

**Architecture:** Domain-driven CQRS with MediatR in ASP.NET Core (.NET 9), PostgreSQL via EF Core, and Angular 19 signals-based reactive facade calling an HTTP service.

**Tech Stack:** C# .NET 9, EF Core, PostgreSQL 16 (Npgsql), Angular 19 (TypeScript, Signals).

**Spec:** `f:\MealMate\backend\docs\superpowers\specs\2026-09-14-delivery-trips-design.md`

## Global Constraints
- Target Restaurant ID must resolve to current authenticated user's restaurant or explicit query param.
- Non-PII customer masking on all responses (consistent with `TripCandidateStop` and `TripStop`).
- Preserve Clean Architecture layer separation (Domain -> Application -> Persistence -> Api).
- Ensure all endpoints return bilingual DTOs (`LocalizedText` or Arabic/English fields) compatible with Angular UI.

---

### Task 1: Domain Entities & Persistence Configuration

**Files:**
- Modify: `src/MealMate.Domain/DriverDelivery/Trip.cs`
- Create: `src/MealMate.Domain/DriverDelivery/TripStop.cs`
- Create: `src/MealMate.Domain/Enums/TripEnums.cs` (or update existing)
- Modify: `src/MealMate.Domain/DriverDelivery/DriverAssignment.cs`
- Modify: `src/MealMate.Application/Common/Abstractions/IAppDbContext.cs`
- Modify: `src/MealMate.Persistence/AppDbContext.cs`
- Modify: `src/MealMate.Persistence/Configurations/DriverDelivery/DriverDeliveryConfigurations.cs`

**Interfaces:**
- Consumes: `AuditableEntity`, `Trip`
- Produces: `TripStop` entity, `DbSet<TripStop> TripStops`, EF Core mappings for `Trip` and `TripStop`.

- [x] **Step 1: Define `TripStop` and update `Trip` with required fields**
  Add `Code`, `ShiftName`, and `Stops` collection to `Trip.cs`. Define `TripStop.cs` with `TripId`, `DailyOrderId`, `SubscriptionDayId`, `OrderCode`, `CustomerMaskedId`, `CustomerName`, `ZoneNameAr`, `ZoneNameEn`, `MealSummaryAr`, `MealSummaryEn`, `BoxCount`, `SlotLabel`, `SequenceNumber`, `Status`, `DeliveredAtUtc`, `FailureReason`.

- [x] **Step 2: Add EF Core configuration for `TripStop` and update `TripConfiguration`**
  Map `TripStop` table in `DriverDeliveryConfigurations.cs` with keys, indices, and foreign keys. Add `DbSet<TripStop> TripStops` in `IAppDbContext` and `AppDbContext`.

- [x] **Step 3: Verify migration or schema update on PostgreSQL**
  Apply schema changes to ensure tables `Trips` and `TripStops` exist and match the entity models.

- [x] **Step 4: Build backend and verify zero compilation errors**
  Run `dotnet build src/MealMate.Persistence/MealMate.Persistence.csproj` and verify success.

---

### Task 2: Application CQRS (DTOs, Queries, and Commands)

**Files:**
- Create: `src/MealMate.Application/Features/DriverDelivery/Trips/DeliveryTripDtos.cs`
- Create: `src/MealMate.Application/Features/DriverDelivery/Trips/GetRestaurantTripsQuery.cs`
- Create: `src/MealMate.Application/Features/DriverDelivery/Trips/GetRestaurantTripDetailQuery.cs`
- Create: `src/MealMate.Application/Features/DriverDelivery/Trips/CreateRestaurantTripCommand.cs`
- Create: `src/MealMate.Application/Features/DriverDelivery/Trips/UpdateTripStatusCommand.cs`

**Interfaces:**
- Consumes: `IAppDbContext`, `ICurrentUser`
- Produces: `RestaurantTripsDataDto`, `DeliveryTripDto`, `CreateRestaurantTripCommand`, `UpdateTripStatusCommand`.

- [x] **Step 1: Create `DeliveryTripDtos.cs` matching frontend JSON contracts**
  Include `RestaurantTripsDataDto`, `DeliveryTripDto`, `TripStopDto`, `TripCandidateStopDto`, `TripSummaryDto`, `TripDriverOptionDto`.

- [x] **Step 2: Implement `GetRestaurantTripsQuery` and handler**
  Query trips for the restaurant, calculate summaries (`active`, `assigned`, `completed`), query candidate unassigned orders for today from `SubscriptionDays` / `RestaurantDailyOrders`.

- [x] **Step 3: Implement `GetRestaurantTripDetailQuery` and handler**
  Retrieve single trip by ID, include driver profile, stops ordered by sequence, and tracking data.

- [x] **Step 4: Implement `CreateRestaurantTripCommand` and handler**
  Validate driver, generate trip code (`TRP-XXX`), map selected candidate orders to `TripStop` entities, assign driver in `DriverAssignments`, set order handover/linkage.

- [x] **Step 5: Implement `UpdateTripStatusCommand` and handler**
  Handle transitions (`InProgress`, `Completed`, `Cancelled`), recording timestamps accordingly.

- [x] **Step 6: Build `MealMate.Application`**
  Run `dotnet build src/MealMate.Application/MealMate.Application.csproj` to confirm clean compilation.

---

### Task 3: API Controller & Routing

**Files:**
- Create: `src/MealMate.Api/Controllers/V1/RestaurantDeliveryTripsController.cs`

**Interfaces:**
- Consumes: MediatR queries/commands from Task 2
- Produces: HTTP endpoints under `/api/v1/restaurants/delivery/trips`

- [x] **Step 1: Implement `RestaurantDeliveryTripsController`**
  Add endpoints:
  - `GET /api/v1/restaurants/delivery/trips`
  - `GET /api/v1/restaurants/delivery/trips/{id:guid}`
  - `POST /api/v1/restaurants/delivery/trips`
  - `PUT /api/v1/restaurants/delivery/trips/{id:guid}/status`
  - `PUT /api/v1/restaurants/delivery/trips/{id:guid}/stops/{stopId:guid}/status`

- [x] **Step 2: Build and restart backend API**
  Run `dotnet build src/MealMate.Api/MealMate.Api.csproj`. Restart API daemon and verify endpoints respond via curl.

---

### Task 4: Frontend API Service & Facade Integration

**Files:**
- Create: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\trips\data\trips-api.service.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\trips\data\trips.facade.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\trips\trip-detail.page.ts`

**Interfaces:**
- Consumes: Backend `/api/v1/restaurants/delivery/trips` endpoints
- Produces: Reactive signals in `TripsFacade` backed by real API calls.

- [x] **Step 1: Create `TripsApiService`**
  Provide methods `getTrips(filter?, search?)`, `getTripById(id)`, `createTrip(payload)`, `updateTripStatus(id, status)`.

- [x] **Step 2: Refactor `TripsFacade`**
  Replace mock timeout in `load()` with `tripsApi.getTrips()`. Update `createTrip()` to call API and reload state on completion.

- [x] **Step 3: Update `TripDetailPageComponent`**
  Ensure trip detail page fetches single trip from API if not already in memory.

- [x] **Step 4: Build and test frontend**
  Run `npm.cmd run build` in `f:\MealMate\frontend\restaurant-dashboard`.
  Verify `http://localhost:4201/restaurant/delivery/trips` in browser without console errors.
