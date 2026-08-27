# F12 Invoice / Barcode / Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist admin document templates (invoice, box_label, meal_label) and auto-generate per-box invoice + barcode + box/meal labels at −24h for restaurant print and driver-ready barcodes.

**Architecture:** Admin CRUD publishes layout JSON. A scheduled job creates idempotent `MealBarcode` / `MealInvoice` / `MealLabel` rows with embedded layout+payload snapshots (including MealMate + restaurant logos). Restaurant APIs read DB flags and prep-documents for browser print. No server PDF in v1.

**Tech Stack:** .NET 9 / EF Core / PostgreSQL / MediatR, Angular admin + restaurant dashboards, existing `ScheduledJob` pattern, admin designer models in `document-template.model.ts`.

## Global Constraints

- Template kinds: exactly `invoice` | `box_label` | `meal_label`
- One published template per kind
- Logos (MealMate + restaurant) on all three document types
- One barcode per `SubscriptionDay`; same value for print and driver scan
- Invoice: short address only; no phone; labels: no customer identity
- Missing published template → skip day + log (no silent fallback)
- Idempotent generation; reissue keeps barcode unless `newBarcode=true`
- Embed template layout into snapshots at generation time
- Do not commit `publish/` or `tmp-manual-deploy/`

## File map

| Path | Responsibility |
|------|----------------|
| `src/MealMate.Domain/RestaurantOperations/DocumentTemplate.cs` | New template entity |
| `src/MealMate.Domain/Enums/Enums.cs` (or nearby) | `DocumentTemplateKind`, `DocumentTemplateStatus`, `MealLabelKind` |
| `src/MealMate.Domain/RestaurantOperations/MealInvoice.cs` | Per-day invoice fields |
| `src/MealMate.Domain/RestaurantOperations/MealLabel.cs` | Kind + meal + template refs |
| `src/MealMate.Persistence/Configurations/...` | EF configs + indexes |
| Migration `AddDocumentTemplatesAndPrepDocumentFields` | Schema |
| `src/MealMate.Application/Features/DocumentTemplates/*` | Admin template handlers |
| `src/MealMate.Api/Controllers/V1/AdminDocumentTemplatesController.cs` | Admin HTTP |
| `src/MealMate.Application/Features/PrepDocuments/*` | Payload builders + generator service + job |
| `RestaurantOrderHandlers.cs` + controller | upcoming-24h flags + prep-documents |
| Admin `invoice-labels-store.ts` + models + nav | Wire HTTP + kinds |
| Restaurant labels feature | Wire prep-documents print |

---

### Task 1: Domain + migration for templates and Meal* extensions

**Files:**
- Create: `src/MealMate.Domain/RestaurantOperations/DocumentTemplate.cs`
- Modify: `src/MealMate.Domain/RestaurantOperations/MealInvoice.cs`
- Modify: `src/MealMate.Domain/RestaurantOperations/MealLabel.cs`
- Modify: `src/MealMate.Domain/Enums/Enums.cs` (add enums)
- Modify: `src/MealMate.Persistence/AppDbContext.cs`
- Modify: `src/MealMate.Persistence/Configurations/RestaurantOperations/RestaurantOperationsConfigurations.cs`
- Create: migration via `dotnet ef migrations add AddDocumentTemplatesAndPrepDocumentFields`
- Test: `tests/MealMate.UnitTests/RestaurantOperations/DocumentTemplateModelTests.cs` (optional compile/smoke) — prefer schema assert via building model in unit test if project already has similar patterns; otherwise verify with `dotnet build`

**Interfaces:**
- Produces: `DocumentTemplate`, `DocumentTemplateKind`, `DocumentTemplateStatus`, `MealLabelKind`; MealInvoice/`SubscriptionDayId`/`TemplateId`/`TemplateVersion`/`BarcodeValue`; MealLabel/`Kind`/`MealId`/`TemplateId`/`TemplateVersion`; unique index on `MealBarcodes.SubscriptionDayId`

- [ ] **Step 1: Add enums**

In `Enums.cs` add:

```csharp
public enum DocumentTemplateKind
{
    Invoice = 1,
    BoxLabel = 2,
    MealLabel = 3,
}

public enum DocumentTemplateStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3,
}

public enum MealLabelKind
{
    Box = 1,
    Meal = 2,
}
```

- [ ] **Step 2: Add `DocumentTemplate` entity**

```csharp
public sealed class DocumentTemplate : AuditableEntity
{
    public DocumentTemplateKind Kind { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string Language { get; set; } = "both"; // ar|en|both
    public DocumentTemplateStatus Status { get; set; } = DocumentTemplateStatus.Draft;
    public int PageWidthPx { get; set; }
    public int PageHeightPx { get; set; }
    public int MarginPx { get; set; }
    public string PageSizeLabel { get; set; } = "A5";
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public string AccentColor { get; set; } = "#0B6E4F";
    public bool ShowAppLogo { get; set; } = true;
    public bool ShowRestaurantLogo { get; set; } = true;
    public string LayoutJson { get; set; } = "[]";
    public int Version { get; set; } = 1;
    public DateTime? PublishedAtUtc { get; set; }
}
```

- [ ] **Step 3: Extend MealInvoice / MealLabel / MealBarcode indexes**

`MealInvoice` add: `Guid? SubscriptionDayId`, `Guid? TemplateId`, `int? TemplateVersion`, `string? BarcodeValue`.  
`MealLabel` add: `MealLabelKind Kind`, `Guid? MealId`, `Guid? TemplateId`, `int? TemplateVersion`.  
`MealBarcodeConfiguration`: unique index on `SubscriptionDayId`.

- [ ] **Step 4: EF configuration + DbSet**

Add `DbSet<DocumentTemplate> DocumentTemplates`. Configure table `DocumentTemplates`, jsonb `LayoutJson`, indexes on `(Kind, Status)`, unique filtered index for one published per kind if PostgreSQL supports it (`HasFilter("\"Status\" = 2")` on `Kind`) OR enforce in application publish logic (application enforcement required either way).

- [ ] **Step 5: Create migration**

Run:

```powershell
dotnet ef migrations add AddDocumentTemplatesAndPrepDocumentFields --project src/MealMate.Persistence/MealMate.Persistence.csproj --startup-project src/MealMate.Api/MealMate.Api.csproj
```

Expected: migration contains `CreateTable DocumentTemplates` and `AddColumn` only on Meal* — **not** full schema recreate. Manually edit if EF emits a full dump.

- [ ] **Step 6: Build**

```powershell
dotnet build src/MealMate.Persistence/MealMate.Persistence.csproj --nologo
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add src/MealMate.Domain src/MealMate.Persistence
git commit -m "feat(persistence): add document templates and prep document fields"
```

---

### Task 2: Admin DocumentTemplates API (CRUD + publish/archive)

**Files:**
- Create: `src/MealMate.Application/Features/DocumentTemplates/DocumentTemplateDtos.cs`
- Create: `src/MealMate.Application/Features/DocumentTemplates/DocumentTemplateHandlers.cs`
- Create: `src/MealMate.Api/Controllers/V1/AdminDocumentTemplatesController.cs`
- Modify: `src/MealMate.Application/DependencyInjection.cs` if explicit registration needed (MediatR assembly scan usually enough)
- Test: `tests/MealMate.UnitTests/DocumentTemplates/DocumentTemplateHandlersTests.cs`

**Interfaces:**
- Consumes: `DocumentTemplate` entity, `IAppDbContext`
- Produces:
  - `GetDocumentTemplatesQuery(DocumentTemplateKind? Kind, DocumentTemplateStatus? Status)`
  - `GetDocumentTemplateQuery(Guid Id)`
  - `CreateDocumentTemplateCommand(...)`
  - `UpdateDocumentTemplateCommand(...)`
  - `PublishDocumentTemplateCommand(Guid Id)`
  - `ArchiveDocumentTemplateCommand(Guid Id)`
  - DTO `DocumentTemplateDto` mirroring admin frontend fields (`layoutJson` as string or parsed json)

- [ ] **Step 1: Write failing unit tests**

```csharp
[Fact]
public async Task Publish_archives_previous_published_of_same_kind()
{
    // arrange two invoice templates: A published, B draft
    // act publish B
    // assert A.Status == Archived, B.Status == Published, B.Version incremented
}

[Fact]
public async Task Update_rejects_when_status_is_published()
{
    // assert failure / conflict
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DocumentTemplateHandlersTests" --nologo
```

- [ ] **Step 3: Implement handlers + controller**

Controller routes:

```csharp
[Route("api/v1/admin/operations/document-templates")]
[Authorize] // same admin ops policy as sibling controllers
public sealed class AdminDocumentTemplatesController : ApiControllerBase
{
    [HttpGet] public Task<IActionResult> List([FromQuery] DocumentTemplateKind? kind, [FromQuery] DocumentTemplateStatus? status, CancellationToken ct);
    [HttpPost] public Task<IActionResult> Create([FromBody] CreateDocumentTemplateRequest body, CancellationToken ct);
    [HttpGet("{id:guid}")] public Task<IActionResult> Get(Guid id, CancellationToken ct);
    [HttpPut("{id:guid}")] public Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentTemplateRequest body, CancellationToken ct);
    [HttpPost("{id:guid}/publish")] public Task<IActionResult> Publish(Guid id, CancellationToken ct);
    [HttpPost("{id:guid}/archive")] public Task<IActionResult> Archive(Guid id, CancellationToken ct);
}
```

Publish logic: load template; if not found fail; set any other `Status==Published && Kind==same` to `Archived`; set this to `Published`, `PublishedAtUtc=now`, `Version++`.

- [ ] **Step 4: Run tests — expect PASS**

- [ ] **Step 5: Seed helper (optional in handler or startup)**

Add `DocumentTemplateSeed` static JSON minimal layouts (with `mealmate_logo` + `restaurant_logo` elements) invoked once from API `POST .../seed-defaults` **or** from migration/`EnsureSeed` in job — prefer idempotent seed method called from publish tests and a one-shot admin endpoint guarded for admin only:

`POST /api/v1/admin/operations/document-templates/seed-defaults` creates draft+publish for missing kinds only.

- [ ] **Step 6: Commit**

```powershell
git commit -m "feat(admin): document template CRUD publish archive API"
```

---

### Task 3: PrepDocuments payload builder + generator service

**Files:**
- Create: `src/MealMate.Application/Features/PrepDocuments/PrepDocumentPayloads.cs`
- Create: `src/MealMate.Application/Features/PrepDocuments/IPrepDocumentsGenerator.cs`
- Create: `src/MealMate.Application/Features/PrepDocuments/PrepDocumentsGenerator.cs`
- Test: `tests/MealMate.UnitTests/PrepDocuments/PrepDocumentsGeneratorTests.cs`

**Interfaces:**
- Consumes: published templates by kind; subscription day + meals + restaurant logos; `IDateTimeProvider`
- Produces:
  - `Task<Result> GenerateForDayAsync(Guid subscriptionDayId, bool forceReissue = false, bool rotateBarcode = false, CancellationToken ct)`
  - Payload records include `mealMateLogoUrl`, `restaurantLogoUrl`, `barcodeValue`, meals[], shortAddress (invoice only), `incompleteNutrition`, `embeddedLayoutJson`

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public async Task Generate_creates_barcode_invoice_box_label_and_meal_labels()
{
    // day with 2 meals, all 3 templates published
    // assert 1 barcode, 1 invoice, 1 box label, 2 meal labels
    // assert snapshots contain mealMateLogoUrl + restaurantLogoUrl
    // assert invoice snapshot has no phone property
}

[Fact]
public async Task Generate_is_idempotent()
{
    await sut.GenerateForDayAsync(dayId);
    await sut.GenerateForDayAsync(dayId);
    // assert still single barcode/invoice/box label
}

[Fact]
public async Task Generate_skips_when_published_template_missing()
{
    // no meal_label published
    // assert no rows created + Result failure or logged skip
}
```

- [ ] **Step 2: Implement generator**

Algorithm:

1. Load day; require status in `{ ConfirmedByRestaurant, ... }` as per spec (at least ConfirmedByRestaurant).
2. Load published templates for three kinds; if any missing → return error / skip.
3. If barcode exists and not forceReissue → return success no-op.
4. Create/rotate barcode: `MM-{serviceDate:yyyyMMdd}-{dayId:N}` truncated to 100 chars if needed; ensure unique.
5. Build payloads; serialize JSON; persist invoice (`SubscriptionDayId`, `TotalBoxes=1`, `RestaurantId`, `ServiceDate`, embed layout), box label, meal labels.
6. SaveChanges once.

Logo resolution: restaurant profile image/logo URL if present; MealMate logo from config key `Branding:MealMateLogoUrl` (add to `appsettings.json` with placeholder path/URL).

- [ ] **Step 3: Tests PASS**

- [ ] **Step 4: Commit**

```powershell
git commit -m "feat(prep-documents): generate invoice barcode and labels payloads"
```

---

### Task 4: −24h background job + admin reissue endpoint

**Files:**
- Create: `src/MealMate.Application/Features/CustomerCheckout/BackgroundJobs/PrepDocumentsGenerationJob.cs`
- Modify: `src/MealMate.Application/DependencyInjection.cs` — `AddHostedService<PrepDocumentsGenerationJob>()`
- Modify: `AdminDocumentTemplatesController.cs` or new `AdminPrepDocumentsController.cs` for reissue
- Test: unit test selecting eligible days (clock-based) in `PrepDocumentsGenerationJobTests.cs` or generator eligibility helper tests

**Interfaces:**
- Produces: `PrepDocumentsGenerationJob` interval 5 minutes; eligibility = confirmed day where `ServiceDate` at Kuwait/UTC business rule: `nowUtc >= ServiceDate.ToDateTime(TimeOnly.MinValue, Utc) - 24h` AND no `MealBarcode` for day
- `ReissuePrepDocumentsCommand(Guid SubscriptionDayId, bool NewBarcode)`

- [ ] **Step 1: Implement eligibility helper + tests**

```csharp
public static bool IsInsidePrepWindow(DateOnly serviceDate, DateTime utcNow)
    => utcNow >= serviceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-24);
```

(Adjust if product uses end-of-day or delivery slot time — match existing −72h lock convention in `SubscriptionDayLockJob` / dispatch service; **read those files and mirror their timezone handling**.)

- [ ] **Step 2: Job ExecuteOnceAsync**

Query eligible days without barcodes; foreach call `GenerateForDayAsync`; log skip counts.

- [ ] **Step 3: Reissue endpoint**

`POST /api/v1/admin/operations/prep-documents/{subscriptionDayId}/reissue?newBarcode=false`

- [ ] **Step 4: Commit**

```powershell
git commit -m "feat(prep-documents): schedule -24h generation and admin reissue"
```

---

### Task 5: Restaurant upcoming-24h flags + prep-documents API

**Files:**
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderHandlers.cs` (upcoming-24h mapping ~721–770)
- Modify: `src/MealMate.Application/Features/RestaurantOrders/RestaurantOrderDtos.cs`
- Modify: `src/MealMate.Api/Controllers/V1/RestaurantOrdersController.cs`
- Create: query/handler `GetRestaurantPrepDocumentsQuery`
- Test: `tests/MealMate.UnitTests/RestaurantOrders/PrepDocumentsQueryTests.cs`

**Interfaces:**
- Produces: `GetRestaurantPrepDocumentsQuery(Guid DayId)` → DTO with `BarcodeValue`, `Invoice`, `BoxLabel`, `MealLabels[]` (each includes `layoutJson` + `payload`)

- [ ] **Step 1: Replace synthetic flags**

Remove `index % 2` style generation. For each day id:

```csharp
var barcodeIds = await db.MealBarcodes.Where(...).Select(x => x.SubscriptionDayId).ToListAsync();
var invoiceIds = await db.MealInvoices.Where(x => x.SubscriptionDayId != null && ...).Select(x => x.SubscriptionDayId!).ToListAsync();
var labelDayIds = await db.MealLabels.Where(...).Select(x => x.SubscriptionDayId).Distinct()...
InvoiceGenerated = invoiceIds.Contains(day.Id);
BarcodeGenerated = barcodeIds.Contains(day.Id);
LabelsGenerated = labelDayIds.Contains(day.Id); // requires box + all meals or at least one box label — prefer: box label exists AND meal label count == meal selection count
```

- [ ] **Step 2: Add GET prep-documents**

```csharp
[HttpGet("{id:guid}/prep-documents")]
public Task<IActionResult> GetPrepDocuments(Guid id, CancellationToken ct)
    => HandleResult(await Sender.Send(new GetRestaurantPrepDocumentsQuery(id), ct));
```

Authorize restaurant ownership of the day’s restaurant.

- [ ] **Step 3: Tests + commit**

```powershell
git commit -m "feat(restaurant): expose real prep document flags and print payload"
```

---

### Task 6: Admin dashboard — wire templates store + kinds + nav

**Files:**
- Modify: `frontend/admin-dashboard/src/app/features/admin/operations/models/document-template.model.ts`
- Create: `.../data/document-templates-api.service.ts`
- Modify: `.../data/invoice-labels-store.ts`
- Modify: `.../pages/invoice-labels-workspace-page/*` (kind labels/filters)
- Modify: `src/app/core/navigation/admin-nav.config.ts`
- Modify: `src/app/core/api/admin-api-endpoints.ts` if extra paths needed (`publish`, `archive`, `seed-defaults`)

**Interfaces:**
- `DocumentTemplateKind = 'invoice' | 'box_label' | 'meal_label'` (migrate mock `label` → `box_label`)
- Element kinds add `'mealmate_logo' | 'restaurant_logo'` (map existing `logo` + `showAppLogo`/`showRestaurantLogo` flags)

- [ ] **Step 1: Extend model kinds**

Replace `'label'` with `'box_label' | 'meal_label'`. Update workspace create buttons to three kinds.

- [ ] **Step 2: API service**

```typescript
list(params?: { kind?: string; status?: string }): Observable<DocumentTemplate[]>
get(id: string): Observable<DocumentTemplate>
create(body: Partial<DocumentTemplate>): Observable<DocumentTemplate>
update(id: string, body: Partial<DocumentTemplate>): Observable<DocumentTemplate>
publish(id: string): Observable<DocumentTemplate>
archive(id: string): Observable<DocumentTemplate>
```

Map API enums/casing carefully (backend Pascal/int vs frontend strings — prefer string enums in API JSON via `JsonStringEnumConverter` if already used; **match existing API JSON style**).

- [ ] **Step 3: Store loads from API on init; optimistic local edit then PUT**

Keep designer UX; remove mock as source of truth. Keep mock file only as seed fallback if API empty (optional).

- [ ] **Step 4: Nav item**

Under operations: `{ path: '/admin/operations/invoices', labelAr: 'الفواتير والملصقات', labelEn: 'Invoices & Labels' }`

- [ ] **Step 5: Manual smoke** — create draft, publish, list shows one published per kind

- [ ] **Step 6: Commit in admin-dashboard repo**

```powershell
git commit -m "feat(admin): persist F12 document templates via API"
```

---

### Task 7: Restaurant dashboard — wire labels print to prep-documents

**Files:**
- Modify: `frontend/restaurant-dashboard/src/app/features/restaurant/**/labels*` (locate facade/api under orders/labels)
- Modify: upcoming-24h facade if it depends on flags
- Create/adjust: `prep-documents-api.service.ts` calling `GET .../orders/{id}/prep-documents`
- Test: limited unit/spec if project has them; otherwise manual checklist

- [ ] **Step 1: Replace mock load with API for a selected order id**

- [ ] **Step 2: Render print packs: invoice, box label, meal labels using returned layouts + barcode**

Reuse admin preview approach if shareable; otherwise minimal HTML print CSS.

- [ ] **Step 3: Commit**

```powershell
git commit -m "feat(restaurant): print prep documents from API snapshots"
```

---

### Task 8: Docs + verification sweep

**Files:**
- Create: `backend/docs/restaurant/PREP_DOCUMENTS_API.md` (short)
- Optionally note in `workflows/admin/F12_invoices_barcode_labels.md` that API is live

- [ ] **Step 1: Document endpoints + payload shape + print flow**

- [ ] **Step 2: Run backend unit tests for DocumentTemplates + PrepDocuments**

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DocumentTemplate|FullyQualifiedName~PrepDocument" --nologo
```

- [ ] **Step 3: Commit docs**

```powershell
git commit -m "docs: document restaurant prep-documents API for F12"
```

---

## Spec coverage check

| Spec item | Task |
|-----------|------|
| DocumentTemplates CRUD/publish | 2 |
| Three kinds + one published each | 2 |
| Logos on all three | 3 payloads + 6 designer |
| Meal* extensions + barcode uniqueness | 1 |
| −24h job | 4 |
| Idempotent + skip missing templates | 3 |
| Reissue keep/rotate barcode | 4 |
| upcoming-24h real flags | 5 |
| prep-documents API | 5 |
| Admin UI wire + nav | 6 |
| Restaurant print | 7 |
| Privacy (no phone / no customer on labels) | 3 tests |
| No server PDF | honored globally |

## Placeholder scan

None intentional. Timezone for −24h must mirror existing lock job (implementer must read `SubscriptionDayLockJob` / dispatch and copy convention — do not invent a second clock rule).
