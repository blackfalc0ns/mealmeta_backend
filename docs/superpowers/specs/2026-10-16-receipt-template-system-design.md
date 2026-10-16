# Receipt Template Management & Thermal Printing System — Design Specification

## 1. Overview & Purpose
This system provides a production-ready, operational receipt template management and thermal printing solution for MealMate. It allows Administrators to manage and publish receipt templates, assign or default them globally or per restaurant, and enables Restaurants to select their authorized designs and print 80mm operational receipts with real Code 128 barcodes and real QR codes with dual-logo branding.

Critically, these receipts are **restaurant-facing operational preparation & delivery tickets**, completely devoid of any subscription, pricing, or financial information.

---

## 2. Supported Receipt Templates (Exactly Three)
Faithfully matching the approved reference design:

1. **`classic-minimal` (Left design)**:
   - Clean monochrome thermal styling.
   - Dual header logos: Restaurant logo (with fallback initials like `HW`) beside MealMate application logo.
   - Centered bilingual order header (`Order ID / رقم الطلب` and bold `ORD-20261016-118`).
   - Top scannable Code 128 barcode (`MM-ORD-20261016-118`).
   - Dashed horizontal dividers.
   - Two-column customer details (Customer name, delivery area, street address, delivery date, time window).
   - Structured meal rows with circular numbered badges, dish photos, Arabic/English names, and 4-metric nutrition row (`Calories`, `Protein`, `Carbs`, `Fat`).
   - Bottom QR code with centered MealMate leaf logo + bilingual scan CTA and closing message.

2. **`organic-elegant` (Middle design)**:
   - Botanical leaf illustrations and framing accents.
   - Centered dual-branding header.
   - Forest green rounded container (`rounded-2xl`) for Order Details and high-contrast barcode box.
   - Customer and delivery information panel.
   - Soft, structured meal cards with chef hat header.
   - Branded QR section with centered MealMate logo and slogan: "معاً لحياة أكثر صحة / A Healthier You, Every Day".

3. **`premium-dark` (Right design)**:
   - Botanical hand-drawn line-art flourishes in the header.
   - Dual-branding header.
   - High-contrast black pill banner for Order ID (`ORD-20261016-118`) with Code 128 barcode underneath.
   - Sharp two-column customer and delivery details.
   - Structured meal rows with solid black circular numbered badges.
   - Dedicated print-safe monochrome mode for thermal printing (using CSS `@media print` with crisp black-and-white borders and high-contrast lines so dark blocks print cleanly without ink bleeding or burning thermal heads).
   - Bottom branded QR code and "Thanks for choosing MealMate" closing badge.

---

## 3. Strict Zero Financial & Zero Subscription Data Policy
- **Excluded**:
  - Subscription days, cycle days, progress (e.g. Day 12 of 26).
  - Subscription plan/tier names and pricing.
  - Platform commissions, fees, taxes.
  - Item prices, discounts, subtotal, payable amount, total.
- **Included (Operational Only)**:
  - Restaurant name (Ar/En) and logo.
  - MealMate logo and slogan.
  - Order Code and Barcode text.
  - Scannable QR code.
  - Number of meals.
  - Customer name.
  - Delivery area and address.
  - Delivery date and time window.
  - Meal items: meal names (Ar/En), real meal image, quantity, nutrition metrics (calories, protein, carbs, fat), optional allergen/preparation instructions.

---

## 4. Backend Architecture (.NET / EF Core)

### Domain Entities
- `ReceiptTemplate` (`MealMate.Domain.RestaurantOperations`):
  - `Id` (Guid, PK)
  - `Key` (string, e.g. "classic-minimal", "organic-elegant", "premium-dark")
  - `NameAr` (string)
  - `NameEn` (string)
  - `DescriptionAr` (string?)
  - `DescriptionEn` (string?)
  - `IsEnabled` (bool)
  - `IsPublished` (bool)
  - `IsRestaurantVisible` (bool)
  - `IsGlobalDefault` (bool)
  - `Version` (int)
  - `ConfigurationJson` (string)
  - `CreatedAtUtc`, `UpdatedAtUtc`

- `RestaurantReceiptSettings` (`MealMate.Domain.RestaurantOperations`):
  - `Id` (Guid, PK)
  - `RestaurantId` (Guid, Unique)
  - `SelectedTemplateId` (Guid?, FK to ReceiptTemplate)
  - `AdminAssignedTemplateId` (Guid?, FK to ReceiptTemplate)
  - `AllowRestaurantSelection` (bool)
  - `CreatedAtUtc`, `UpdatedAtUtc`

- `ReceiptPrintLog` (`MealMate.Domain.RestaurantOperations`):
  - `Id` (Guid, PK)
  - `OrderId` (Guid)
  - `RestaurantId` (Guid)
  - `TemplateId` (Guid)
  - `TemplateVersion` (int)
  - `PrintedByUserId` (Guid)
  - `PrintedAtUtc` (DateTime)
  - `PrintStatus` (string)
  - `ClientIp` (string?)

### Effective Template Resolution Algorithm
```csharp
public async Task<ReceiptTemplate> ResolveEffectiveTemplateAsync(Guid restaurantId, CancellationToken ct)
{
    var settings = await _db.RestaurantReceiptSettings
        .Include(s => s.AdminAssignedTemplate)
        .Include(s => s.SelectedTemplate)
        .FirstOrDefaultAsync(s => s.RestaurantId == restaurantId, ct);

    // 1. Forced Admin Assignment
    if (settings?.AdminAssignedTemplate is { IsEnabled: true, IsPublished: true } adminTemplate)
        return adminTemplate;

    // 2. Restaurant Preference (if permitted)
    if (settings is { AllowRestaurantSelection: true } &&
        settings.SelectedTemplate is { IsEnabled: true, IsPublished: true, IsRestaurantVisible: true } selectedTemplate)
        return selectedTemplate;

    // 3. Fallback to Published Global Default
    var defaultTemplate = await _db.ReceiptTemplates
        .FirstOrDefaultAsync(t => t.IsGlobalDefault && t.IsEnabled && t.IsPublished, ct)
        ?? await _db.ReceiptTemplates
            .Where(t => t.IsEnabled && t.IsPublished)
            .OrderBy(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    if (defaultTemplate is null)
        throw new InvalidOperationException("No published receipt template available.");

    return defaultTemplate;
}
```

### API Endpoints
**Admin (`MealMate.Api.Controllers.V1.AdminReceiptTemplatesController`)**:
- `GET /api/v1/admin/receipt-templates`
- `GET /api/v1/admin/receipt-templates/{id}`
- `PUT /api/v1/admin/receipt-templates/{id}/configuration`
- `POST /api/v1/admin/receipt-templates/{id}/publish`
- `PUT /api/v1/admin/receipt-templates/{id}/visibility`
- `PUT /api/v1/admin/receipt-templates/default`
- `GET /api/v1/admin/receipt-templates/preview-order`
- `GET /api/v1/admin/restaurants/{restaurantId}/receipt-settings`
- `PUT /api/v1/admin/restaurants/{restaurantId}/receipt-settings`

**Restaurant (`MealMate.Api.Controllers.V1.RestaurantReceiptController` & `RestaurantOrdersController`)**:
- `GET /api/v1/restaurant/receipt-templates`
- `GET /api/v1/restaurant/receipt-settings`
- `PUT /api/v1/restaurant/receipt-settings`
- `GET /api/v1/restaurant/orders/{orderId}/receipt`
- `POST /api/v1/restaurant/orders/{orderId}/receipt/print-log`

---

## 5. Frontend Architecture (Angular 19)

### Shared Receipt Components (`frontend/shared` or dashboard shared)
- `ReceiptClassicMinimalComponent`: 100% faithful representation of Template 1.
- `ReceiptOrganicElegantComponent`: 100% faithful representation of Template 2.
- `ReceiptPremiumDarkComponent`: 100% faithful representation of Template 3.
- `ReceiptBarcodeComponent`: Code 128 rendering via `bwip-js` SVG or canvas.
- `ReceiptQrCodeComponent`: QR Level-H with centered MealMate app logo SVG overlay.
- `ReceiptPrintService`: Manages isolated iframe printing with 80mm page CSS (`@page { size: 80mm auto; margin: 0; }`).

### Admin Dashboard (`frontend/admin-dashboard`)
- Route: `/admin/settings/receipt-templates`
- Features:
  - 3 Template Cards showing status, thumbnail, active/default badges.
  - Side-by-side live comparison toggle.
  - Live preview modal with real authorized order data.
  - Settings drawer for operational field toggles (allergens, prep notes).
  - Restaurant assignment modal to enforce templates or manage selection permissions.

### Restaurant Dashboard (`frontend/restaurant-dashboard`)
- Route: `/restaurant/settings/receipt-design`
  - Shows allowed templates.
  - Live preview of each design with restaurant's real logo and branding.
  - Save default preference button.
- Order Management Integration (`/restaurant/orders/daily` & detail page):
  - "طباعة الإيصال / Print Receipt" action.
  - Opens 80mm print preview dialog.
  - Automatically sends print job to thermal printer and logs print audit.

---

## 6. Zero Mock Data Verification Check
At every stage:
- No hardcoded customer names, orders, or fake nutrition facts.
- Logos dynamically resolved from `_configuration["Branding:MealMateLogoUrl"]` and `Documents` table.
- Empty states return clean HTTP 404 / empty lists without fabricating fallback items.
