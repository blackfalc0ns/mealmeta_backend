# Receipt Template Management & Thermal Printing System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a complete, production-ready, zero-mock-data Receipt Template Management & 80mm Thermal Printing System for MealMate across .NET Backend, Admin Dashboard, and Restaurant Dashboard, featuring exactly the three approved templates (`classic-minimal`, `organic-elegant`, `premium-dark`).

**Architecture:** Domain entities (`ReceiptTemplate`, `RestaurantReceiptSettings`, `ReceiptPrintLog`) in Clean Architecture backend with EF Core migration; typed operational receipt data queries excluding financial fields; dual-branding logo resolution; Angular 19 components with real Code 128 barcodes and centered-logo QR codes; 80mm isolated thermal printing engine.

**Tech Stack:** .NET 9 / C#, Entity Framework Core / PostgreSQL, Angular 19, Tailwind CSS, `bwip-js`, `qrcode`, `@ng-icons/lucide`.

**Spec:** [docs/superpowers/specs/2026-10-16-receipt-template-system-design.md](file:///F:/MealMate/backend/docs/superpowers/specs/2026-10-16-receipt-template-system-design.md)

---

## Global Constraints
- **Zero Mock Data Policy**: All data must originate from the real persistence layer. Zero hardcoded business data. Empty database = empty response.
- **Strictly Operational**: No subscription days, plans, prices, fees, subtotals, commissions, totals, or discounts in any receipt contract or view.
- **Exactly Three Templates**: Only `classic-minimal`, `organic-elegant`, and `premium-dark`.
- **Dual-Logo Branding**: Dynamic MealMate logo in header and centered in QR; Dynamic Restaurant logo with initial fallback in header.
- **Target Print Size**: 80mm thermal receipt printer (`@page { size: 80mm auto; margin: 0; }`).

---

### Task 1: Domain Entities & Database Persistence (EF Core)

**Files:**
- Create: `F:\MealMate\backend\src\MealMate.Domain\RestaurantOperations\ReceiptTemplate.cs`
- Create: `F:\MealMate\backend\src\MealMate.Domain\RestaurantOperations\RestaurantReceiptSettings.cs`
- Create: `F:\MealMate\backend\src\MealMate.Domain\RestaurantOperations\ReceiptPrintLog.cs`
- Create: `F:\MealMate\backend\src\MealMate.Persistence\Configurations\RestaurantOperations\ReceiptTemplateConfigurations.cs`
- Modify: `F:\MealMate\backend\src\MealMate.Persistence\AppDbContext.cs`
- Test: `F:\MealMate\backend\tests\MealMate.UnitTests\ReceiptPrinting\ReceiptTemplateDomainTests.cs`

- [x] **Step 1: Write Domain Entities**
  Define `ReceiptTemplate`, `RestaurantReceiptSettings`, and `ReceiptPrintLog` inheriting `AuditableEntity`.
- [x] **Step 2: Write Entity Configurations**
  Map tables `receipt_templates`, `restaurant_receipt_settings`, `receipt_print_logs` with unique constraints and foreign keys.
- [x] **Step 3: Register in AppDbContext & Seed Initial 3 Templates**
  Add `DbSet<ReceiptTemplate>`, `DbSet<RestaurantReceiptSettings>`, `DbSet<ReceiptPrintLog>` to `AppDbContext` and seed initial published definitions for `classic-minimal` (Global Default), `organic-elegant`, and `premium-dark`.
- [x] **Step 4: Create EF Core Migration**
  Run `dotnet ef migrations add AddReceiptTemplateManagementSystem --project src/MealMate.Persistence --startup-project src/MealMate.Api`.
- [x] **Step 5: Verify Build and Unit Tests**
  Run `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj`.

---

### Task 2: Application Layer (DTOs, Service & Effective Template Resolution)

**Files:**
- Create: `F:\MealMate\backend\src\MealMate.Application\Features\ReceiptPrinting\ReceiptPrintingDtos.cs`
- Create: `F:\MealMate\backend\src\MealMate.Application\Features\ReceiptPrinting\IReceiptPrintingService.cs`
- Create: `F:\MealMate\backend\src\MealMate.Application\Features\ReceiptPrinting\ReceiptPrintingService.cs`
- Test: `F:\MealMate\backend\tests\MealMate.UnitTests\ReceiptPrinting\ReceiptResolutionServiceTests.cs`

- [x] **Step 1: Define Receipt DTOs**
  Create `OperationalReceiptDto`, `ReceiptMealItemDto`, `ReceiptBrandDto`, `ReceiptTemplateDto`, `RestaurantReceiptSettingsDto`. Ensure zero financial fields exist on these DTOs.
- [x] **Step 2: Implement Effective Template Resolution**
  Implement resolution hierarchy: Admin forced template -> Restaurant preference (if allowed) -> Published global default.
- [x] **Step 3: Implement Operational Order Data Builder**
  Query `RestaurantDailyOrder` with `SubscriptionDay`, `CustomerProfile`, `CustomerAddress`, `DailyMealSelections`, and `Meal`.
  Resolve MealMate logo from configuration and Restaurant logo from `Documents` table (with dynamic initials fallback if missing).
- [x] **Step 4: Add Unit Tests for Resolution Logic and Zero Financial Leakage**
  Write unit tests verifying resolution fallbacks and absence of financial data.

---

### Task 3: Backend API Endpoints & Security

**Files:**
- Create: `F:\MealMate\backend\src\MealMate.Api\Controllers\V1\AdminReceiptTemplatesController.cs`
- Create: `F:\MealMate\backend\src\MealMate.Api\Controllers\V1\RestaurantReceiptController.cs`
- Test: `F:\MealMate\backend\tests\MealMate.ApiTests\ReceiptPrinting\ReceiptApiControllerTests.cs`

- [x] **Step 1: Admin Receipt Templates Controller**
  Implement CRUD, publish, visibility toggle, default toggle, preview order fetch, and restaurant assignment endpoints secured with `AdminPermissions.settingsManage` / `AdminPermissions.operationsView`.
- [x] **Step 2: Restaurant Receipt Controller**
  Implement allowed templates query, settings get/update, operational receipt payload for order, and print audit logging endpoint secured with `RestaurantStaffPolicy`.
- [x] **Step 3: Verify Controller Tests & Role Security**
  Ensure restaurant cannot access another restaurant's receipt or unpermitted templates.

---

### Task 4: Shared Receipt Template Components & 80mm Printing Engine

**Files:**
- Create in Admin and Restaurant shared component paths:
  - `receipt-classic-minimal.component.ts` & `.html`
  - `receipt-organic-elegant.component.ts` & `.html`
  - `receipt-premium-dark.component.ts` & `.html`
  - `receipt-barcode.component.ts` (Code 128 via `bwip-js`)
  - `receipt-qr-code.component.ts` (Level-H QR with centered MealMate logo)
  - `receipt-thermal-print.service.ts` (80mm iframe printing)

- [x] **Step 1: Build Receipt Barcode & QR Components**
  Generate Code 128 barcode and Level-H QR code with centered MealMate leaf icon.
- [x] **Step 2: Build Template 1 (`classic-minimal`)**
  Implement dashed dividers, two-column layout, compact meal rows, and dual-logo header.
- [x] **Step 3: Build Template 2 (`organic-elegant`)**
  Implement botanical accents, rounded forest-green order details card, soft meal cards, and slogan footer.
- [x] **Step 4: Build Template 3 (`premium-dark`)**
  Implement line-art botanical illustrations, solid dark pill order banner, sharp contrast, and thermal-safe print mode.
- [x] **Step 5: Implement 80mm Thermal Print Service**
  Isolated iframe print service with print-specific stylesheet (`@page { size: 80mm auto; margin: 0; }`).

---

### Task 5: Admin Dashboard — Receipt Templates Management Module

**Files:**
- Create: `F:\MealMate\frontend\admin-dashboard\src\app\features\admin\settings\pages\receipt-templates-page\receipt-templates-page.component.ts` & `.html`
- Create: `F:\MealMate\frontend\admin-dashboard\src\app\features\admin\settings\services\receipt-templates-admin.service.ts`
- Modify: `F:\MealMate\frontend\admin-dashboard\src\app\core\navigation\admin-nav.config.ts`
- Modify: `F:\MealMate\frontend\admin-dashboard\src\app\features\admin\admin-stub.routes.ts`

- [x] **Step 1: Admin Route & Navigation Configuration**
  Register `/admin/settings/receipt-templates` in `admin-nav.config.ts` and `admin-stub.routes.ts`.
- [x] **Step 2: Build Receipt Templates Gallery & Controls**
  Display exactly the 3 template cards with status badges, thumbnail previews, active/default toggles, visibility toggles, and live order preview modal.
- [x] **Step 3: Restaurant Template Assignment Drawer**
  Allow admin to assign a template to a specific restaurant and toggle `allowRestaurantSelection`.
- [x] **Step 4: Build & Lint Check**
  Run `npm run typecheck` in `admin-dashboard`.

---

### Task 6: Restaurant Dashboard — Receipt Design & Order Printing Integration

**Files:**
- Create: `F:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\settings\receipt-design\receipt-design.page.ts` & `.html`
- Create: `F:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\settings\data\restaurant-receipt.service.ts`
- Modify: `F:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\restaurant-workspace.routes.ts`
- Modify: `F:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\daily-orders\detail\order-detail.page.ts` & `.html`

- [x] **Step 1: Restaurant Route & Navigation**
  Add route `/restaurant/settings/receipt-design`.
- [x] **Step 2: Build Receipt Design Selection Page**
  Display permitted templates for the restaurant with live preview and save default button.
- [x] **Step 3: Integrate Thermal Print Action into Order Details**
  Add "طباعة الإيصال / Print Receipt" button in `order-detail.page.html` that triggers 80mm print preview dialog and sends print job.
- [x] **Step 4: Build & Lint Check**
  Run `npm run typecheck` in `restaurant-dashboard`.

---

### Task 7: End-to-End Verification & Zero-Mock-Data Audit

- [x] **Step 1: Run Backend Build & Tests**
  Run `dotnet build` and `dotnet test`.
- [x] **Step 2: Run Frontend Typechecks**
  Run `npm run typecheck` on both `admin-dashboard` and `restaurant-dashboard`.
- [x] **Step 3: Verify Zero-Mock-Data Compliance**
  Execute mock-term scan across all modified files and confirm real data sources.
- [x] **Step 4: Final Confirmation Block**
  Provide explicit Zero-Mock-Data check output.

