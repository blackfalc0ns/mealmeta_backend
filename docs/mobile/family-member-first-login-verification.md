# Family member first-login verification — 2026-10-14

Implemented by Luna and reviewed by the parent agent. The existing customer login endpoint accepts the temporary password and returns a pending OTP step without session tokens. OTP verification uses the existing configured fixed phone code, as explicitly requested. Password setup is a separate single-use step; only completion permits session issuance.

## Changed areas

- `backend/src/MealMate.Application/Features/IdentityAccess/Auth/FamilyMemberFirstLogin.cs`: pending login, OTP/password challenges, transactions and family group locks.
- Existing `LoginRefreshLogout.cs` and `OtpAndPassword.cs`: ordinary customer entry and recovery/phone-verification bypass guards.
- `backend/src/MealMate.Application/Features/FamilyManager/FamilyManagerHandlers.cs` and `FamilyManagerContracts.cs`: real account provisioning, explicit temporary-password regeneration, manager protection and pending access response.
- `FamilyActionPolicy.cs`, new `FamilyMemberSessionContext.cs`, API controllers, `Program.cs` and new `FamilyPasswordSetupMiddleware.cs`: effective capabilities, authenticated `/family/me`, and old-token setup restrictions.
- `FamilyMemberAccess.cs`, `AuthSessionService.cs`, migration `20261014065653_AddFamilyMemberPasswordSetup` and the EF snapshot: persisted setup flag and issue/refresh restrictions, including removed/cancelled/disabled/soft-deleted access.
- `RoutingOtpDeliveryService.cs`: permits the requested configured-code mode; fails when neither a configured code nor phone delivery exists.
- Arabic/English message resources and focused unit, API and PostgreSQL regression tests.

## Final checks

All commands below ran from `F:\MealMate\backend` against the final implementation. Independent test commands ran serially.

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~FamilyActionPolicyTests|FullyQualifiedName~FamilyCalendarAccessGateTests|FullyQualifiedName~FamilyManagerHandlerTests|FullyQualifiedName~FamilyMemberFirstLoginServiceTests|FullyQualifiedName~FamilyOtpDeliveryCapabilityTests|FullyQualifiedName~AuthDomainTests' --nologo --verbosity minimal -m:1 -clp:ErrorsOnly
# Passed: 81; failed/skipped: 0.

dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --no-restore --filter 'FullyQualifiedName~FamilyApiBoundaryTests' --nologo --verbosity minimal -m:1 -clp:ErrorsOnly
# Passed: 8; failed/skipped: 0.

dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~FamilyFirstLoginPersistenceTests|FullyQualifiedName~FamilyPasswordGateTests|FullyQualifiedName~AuthLoginChallengeServiceTests|FullyQualifiedName~AuthSessionServiceTests|FullyQualifiedName~AuthSessionServiceRefreshTests' --nologo --verbosity minimal -m:1 -clp:ErrorsOnly
# Passed: 36; failed/skipped: 0. Includes real PostgreSQL containers and actual migrations.

dotnet build src/MealMate.Api/MealMate.Api.csproj --no-restore --nologo --verbosity minimal -m:1 -clp:ErrorsOnly
# Succeeded: 0 warnings, 0 errors in this incremental final build.

git diff --check
# Passed.
```

The PostgreSQL tests verify configured-code login/OTP progression without a delivery provider, challenge persistence before transaction release, password-rejection rollback, committed failed-OTP attempts, and denial of session issue/refresh while setup is pending. Unit tests cover the ordinary login handler, successful OTP/password/session progression, replay/expiry, member state changes and manager identity protection. API tests verify own capability context and localized `ar → en → ar` rejection of pending access, including old tokens and soft-deleted records.

The new message codes have matching nonempty Arabic and English resource entries. The root i18n audit self-test passed; its diagnostic scan reported zero catalog parity mismatches or parser diagnostics, with existing dashboard findings outside this backend change. That scanner is not a blocking backend localization gate.

Changed production code was inspected for source authenticity and forbidden business fixtures; the added-line/new-file scans returned zero matches. Fixed verification codes come from existing configuration rather than production source literals. Test values remain in test projects.

## Delivery scope

The migration was applied only to isolated test databases. No live member account, production database or deployment was changed. No Flutter source was available or edited; the application must consume the documented `nextStep` and `/family/me` contracts from its regular sign-in page. Browser/UI language switching, RTL/LTR and live notification delivery were not exercised.

The implementation contract is in `family-member-first-login.md`. Unrelated concurrent advertising/offers changes were preserved and are not included in this review.
