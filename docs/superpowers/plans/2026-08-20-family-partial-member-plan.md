# Family Partial Member Plan Implementation Plan

> **For agentic workers:** Execute inline in this session (user requested immediate implementation). Steps use checkbox syntax.

**Goal:** Allow `PUT .../members/{id}/plan` to accept partial fields, merge on the server, save on each selection, and clear only incompatible dependents.

**Architecture:** Introduce `Optional<T>` JSON semantics (omit=keep, null=clear). Rewrite `SetFamilyMemberPlan` merge/validate. Keep checkout Ready rules unchanged. Update API tests and mobile docs.

**Tech Stack:** ASP.NET Core, System.Text.Json, EF Core, xUnit/FluentAssertions

## Global Constraints

- Same endpoint path; no PATCH verb
- Family catalog flags still required
- Checkout still requires every member Ready
- Commit only if user asks

## File map

| File | Role |
| --- | --- |
| `src/MealMate.Application/Common/Serialization/Optional.cs` | Optional\<T\> + JsonConverterFactory |
| `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftCommands.cs` | Nullable/optional plan request |
| `src/MealMate.Application/Features/FamilyCheckout/FamilyPlanEligibilityService.cs` | Program-only + pair helpers |
| `src/MealMate.Application/Features/FamilyCheckout/FamilyDraftHandlers.cs` | Merge + compatibility + staged validation |
| `tests/.../CustomerFamilySubscriptionFlowControllerTests.cs` | Acceptance tests |
| `docs/mobile/*.md` | Partial PUT contract |

### Task 1: Optional\<T\> + request DTO

- [ ] Add Optional + converter
- [ ] Change SetFamilyMemberPlanRequest to Optional fields + Full(...) helper for tests
- [ ] Register converter if not attribute-driven

### Task 2: Eligibility helpers + handler merge

- [ ] ValidateProgramForFamilyAsync / ValidateBundleForFamilyAsync
- [ ] Rewrite SetFamilyMemberPlan handler per spec
- [ ] Keep SaveChangesAsync

### Task 3: Tests

- [ ] programId-only persists InProgress
- [ ] two members each programId
- [ ] program change clears incompatible bundle/tier
- [ ] full plan still Ready
- [ ] empty body → family.plan_empty
- [ ] non-family program → catalog_not_available
- [ ] Update existing Full(...) call sites

### Task 4: Mobile docs

- [ ] Partial PUT examples; save-on-tap; member strip
- [ ] Mark design spec status implemented

### Task 5: Verify

- [ ] `dotnet test --filter FullyQualifiedName~CustomerFamilySubscriptionFlowControllerTests`
