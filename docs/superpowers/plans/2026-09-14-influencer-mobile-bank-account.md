# Influencer Mobile Bank Account & Payouts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement mobile bank account management and smart withdrawal resolution in .NET backend, and deliver a production-ready Flutter mobile architecture with a dedicated Dart file for each screen, models, and API service.

**Architecture:** 
- Backend: Clean Architecture in `MealMate.Application` and `MealMate.Api` extending `IInfluencerProfileService`, MediatR handlers, and `InfluencerPartnerController` with IBAN normalization and profile fallback.
- Mobile: Modular Flutter architecture with dedicated screen files, strongly typed Dart models, and a resilient Dio API service in `docs/mobile/influencer/`.

**Tech Stack:** .NET 9, C# 13, EF Core, FluentValidation, MediatR, Flutter 3.x / Dart 3.x, Dio.

**Spec:** `docs/superpowers/specs/2026-09-14-influencer-mobile-bank-account-design.md`

---

## File Structure

### Backend:
- `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileContracts.cs`: DTOs, Queries, Commands, and Validators.
- `src/MealMate.Application/Features/IdentityAccess/Influencer/IInfluencerProfileService.cs`: Service interface methods.
- `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileService.cs`: Service implementation with IBAN masking and persistence.
- `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileHandlers.cs`: MediatR handlers.
- `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletContracts.cs`: Make withdrawal bank fields optional.
- `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletService.cs`: Fallback to saved profile bank account.
- `src/MealMate.Api/Controllers/V1/InfluencerPartnerController.cs`: Controller endpoints for bank account.
- `tests/MealMate.UnitTests/Influencer/InfluencerBankAccountTests.cs`: Unit test suite.

### Mobile (Flutter / Dart):
- `docs/mobile/influencer/models/influencer_models.dart`: Strongly-typed Dart DTO models.
- `docs/mobile/influencer/services/influencer_api_service.dart`: Complete Dio-based API client.
- `docs/mobile/influencer/screens/influencer_dashboard_screen.dart`: Main dashboard screen.
- `docs/mobile/influencer/screens/influencer_bank_account_screen.dart`: Bank account management screen.
- `docs/mobile/influencer/screens/influencer_withdrawal_request_screen.dart`: Withdrawal submission screen.
- `docs/mobile/influencer/screens/influencer_withdrawals_history_screen.dart`: Withdrawal history screen.
- `docs/mobile/influencer/screens/influencer_analytics_screen.dart`: Analytics & performance screen.
- `docs/mobile/influencer/screens/influencer_social_links_screen.dart`: Social media links screen.
- `docs/mobile/influencer/README.md`: Integration guide and route configuration for Flutter developers.

---

## Tasks

### Task 1: Backend Contracts, Queries, Commands & Validator

**Files:**
- Modify: `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileContracts.cs`
- Modify: `src/MealMate.Application/Common/Localization/MessageCodes.cs`

- [ ] **Step 1: Add error message code for missing bank account**
  In `MessageCodes.Influencers`, add `public const string BankAccountRequired = "influencers.bank_account_required";`.
- [ ] **Step 2: Update InfluencerBankInfoDto and add Bank Account Commands/Queries**
  In `InfluencerProfileContracts.cs`:
  - Update `InfluencerBankInfoDto(string? AccountHolderName, string? BankName, string? Iban, string? MaskedIban = null, bool IsConfigured = true)`.
  - Add `GetInfluencerBankAccountQuery() : IQuery<InfluencerBankInfoDto>`.
  - Add `UpdateInfluencerBankAccountCommand(string BankName, string AccountHolderName, string Iban) : ICommand<InfluencerBankInfoDto>`.
  - Add `DeleteInfluencerBankAccountCommand() : ICommand`.
  - Add `UpdateInfluencerBankAccountCommandValidator` checking not-empty, length limits, and alphanumeric IBAN format.
- [ ] **Step 3: Compile verification**
  Run `dotnet build src/MealMate.Application/MealMate.Application.csproj` to ensure syntax is clean.
- [ ] **Step 4: Commit**
  `git add src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileContracts.cs src/MealMate.Application/Common/Localization/MessageCodes.cs; git commit -m "feat(influencer): define bank account contracts and validators"`

---

### Task 2: Service Layer & MediatR Handlers for Bank Account Management

**Files:**
- Modify: `src/MealMate.Application/Features/IdentityAccess/Influencer/IInfluencerProfileService.cs`
- Modify: `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileService.cs`
- Modify: `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileHandlers.cs`

- [ ] **Step 1: Add interface methods in `IInfluencerProfileService`**
  - `Task<Result<InfluencerBankInfoDto>> GetBankAccountAsync(Guid userId, CancellationToken ct = default);`
  - `Task<Result<InfluencerBankInfoDto>> UpdateBankAccountAsync(Guid userId, UpdateInfluencerBankAccountCommand command, CancellationToken ct = default);`
  - `Task<Result> DeleteBankAccountAsync(Guid userId, CancellationToken ct = default);`
- [ ] **Step 2: Implement methods in `InfluencerProfileService`**
  - Normalize IBAN by removing spaces and hyphens and converting to uppercase.
  - Compute `MaskedIban` (e.g. `KW82 •••• •••• 7890` or first 4 and last 4 chars).
  - Update or clear profile fields (`BankName`, `AccountHolderName`, `Iban`).
- [ ] **Step 3: Add MediatR Query/Command Handlers in `InfluencerProfileHandlers.cs`**
  - `GetInfluencerBankAccountQueryHandler`
  - `UpdateInfluencerBankAccountCommandHandler`
  - `DeleteInfluencerBankAccountCommandHandler`
- [ ] **Step 4: Compile verification**
  Run `dotnet build src/MealMate.Application/MealMate.Application.csproj`.
- [ ] **Step 5: Commit**
  `git add src/MealMate.Application/Features/IdentityAccess/Influencer/; git commit -m "feat(influencer): implement bank account service and handlers"`

---

### Task 3: Smart Withdrawal Resolution from Saved Bank Account

**Files:**
- Modify: `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletContracts.cs`
- Modify: `src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletService.cs`

- [ ] **Step 1: Relax `SubmitInfluencerWithdrawalCommand` required fields**
  In `InfluencerWalletContracts.cs`, change `BankName`, `AccountHolderName`, and `Iban` to nullable `string?` with default `= null`.
  In `SubmitInfluencerWithdrawalCommandValidator`, only validate them when provided (`When(x => !string.IsNullOrWhiteSpace(x.BankName), ...)`).
- [ ] **Step 2: Update `SubmitWithdrawalAsync` in `InfluencerWalletService`**
  - If request bank fields are empty, check `profile.BankName`, `profile.AccountHolderName`, and `profile.Iban`.
  - If profile has no saved bank details, return `Error.Validation(MessageCodes.Influencers.BankAccountRequired)`.
  - Use the resolved bank details when creating `InfluencerWithdrawalRequest`.
- [ ] **Step 3: Compile verification**
  Run `dotnet build src/MealMate.Application/MealMate.Application.csproj`.
- [ ] **Step 4: Commit**
  `git add src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletContracts.cs src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletService.cs; git commit -m "feat(influencer): enable withdrawal to saved bank account"`

---

### Task 4: Expose Mobile API Endpoints in `InfluencerPartnerController`

**Files:**
- Modify: `src/MealMate.Api/Controllers/V1/InfluencerPartnerController.cs`

- [ ] **Step 1: Add Bank Account endpoints in `InfluencerPartnerController`**
  - `[HttpGet("me/bank-account")]` returning `InfluencerBankInfoDto`.
  - `[HttpPut("me/bank-account")]` taking `[FromBody] UpdateInfluencerBankAccountRequest`.
  - `[HttpDelete("me/bank-account")]` returning success.
- [ ] **Step 2: Update `SubmitWithdrawalApiRequest`**
  - Make `string? BankName = null`, `string? AccountHolderName = null`, `string? Iban = null` optional.
- [ ] **Step 3: Compile verification**
  Run `dotnet build src/MealMate.Api/MealMate.Api.csproj`.
- [ ] **Step 4: Commit**
  `git add src/MealMate.Api/Controllers/V1/InfluencerPartnerController.cs; git commit -m "feat(api): expose mobile influencer bank account endpoints"`

---

### Task 5: Backend Automated Unit Tests

**Files:**
- Create: `tests/MealMate.UnitTests/Influencer/InfluencerBankAccountTests.cs`

- [ ] **Step 1: Write unit tests covering:**
  - `GetBankAccountAsync_ReturnsConfiguredAccount_WithMaskedIban`
  - `UpdateBankAccountAsync_NormalizesIban_AndSavesDetails`
  - `DeleteBankAccountAsync_ClearsAccountFields`
  - `SubmitWithdrawalAsync_UsesSavedBankAccount_WhenFieldsAreNull`
  - `SubmitWithdrawalAsync_Fails_WhenNoBankSavedAndNoneProvided`
- [ ] **Step 2: Run tests**
  Run `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~InfluencerBankAccountTests"`.
  Verify 100% pass.
- [ ] **Step 3: Commit**
  `git add tests/MealMate.UnitTests/Influencer/InfluencerBankAccountTests.cs; git commit -m "test(influencer): add unit tests for bank account and smart withdrawal"`

---

### Task 6: Mobile Flutter Dart Models & API Service

**Files:**
- Create: `docs/mobile/influencer/models/influencer_models.dart`
- Create: `docs/mobile/influencer/services/influencer_api_service.dart`

- [ ] **Step 1: Create `influencer_models.dart`**
  Implement Dart classes with `fromJson` and `toJson`:
  - `InfluencerPartnerProfile`
  - `InfluencerBankAccount`
  - `UpdateBankAccountRequest`
  - `InfluencerWallet`
  - `WithdrawalRecord`
  - `SubmitWithdrawalRequest`
  - `InfluencerLinks`
  - `InfluencerAnalytics`
- [ ] **Step 2: Create `influencer_api_service.dart`**
  Implement `InfluencerApiService` using Dio:
  - `getProfile()`
  - `getBankAccount()`
  - `updateBankAccount(...)`
  - `deleteBankAccount()`
  - `getWallet()`
  - `submitWithdrawal(...)`
  - `getWithdrawals(...)`
  - `getLinks()`
  - `getAnalytics(...)`
- [ ] **Step 3: Commit**
  `git add docs/mobile/influencer/models/ docs/mobile/influencer/services/; git commit -m "feat(mobile): add influencer dart models and api service"`

---

### Task 7: Mobile Flutter Screens (A File per Screen)

**Files:**
- Create: `docs/mobile/influencer/screens/influencer_dashboard_screen.dart`
- Create: `docs/mobile/influencer/screens/influencer_bank_account_screen.dart`
- Create: `docs/mobile/influencer/screens/influencer_withdrawal_request_screen.dart`
- Create: `docs/mobile/influencer/screens/influencer_withdrawals_history_screen.dart`
- Create: `docs/mobile/influencer/screens/influencer_analytics_screen.dart`
- Create: `docs/mobile/influencer/screens/influencer_social_links_screen.dart`
- Create: `docs/mobile/influencer/README.md`

- [ ] **Step 1: Create `influencer_bank_account_screen.dart`**
  Complete Flutter widget for managing bank account (Status card, Add/Edit form, IBAN formatting, Save button, Delete option).
- [ ] **Step 2: Create `influencer_withdrawal_request_screen.dart`**
  Complete Flutter widget for requesting payouts directly to saved bank account (Balance header, quick chips, destination account card, submit button).
- [ ] **Step 3: Create `influencer_withdrawals_history_screen.dart`**
  Complete Flutter widget showing past payouts with status badges and detail sheet.
- [ ] **Step 4: Create `influencer_dashboard_screen.dart`**
  Complete Flutter home screen for influencers with quick stats, link sharing, and navigation to bank and wallet.
- [ ] **Step 5: Create `influencer_analytics_screen.dart`**
  Complete Flutter widget for performance charts and date filtering.
- [ ] **Step 6: Create `influencer_social_links_screen.dart`**
  Complete Flutter widget for updating social media handles.
- [ ] **Step 7: Create `docs/mobile/influencer/README.md`**
  Integration guide with routing setup, dependency list, and usage examples.
- [ ] **Step 8: Commit**
  `git add docs/mobile/influencer/; git commit -m "feat(mobile): add full flutter screen-by-screen influencer implementation"`
