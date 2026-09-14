# Influencer Mobile Bank Account & Payouts Implementation Plan

> **Design Specification:** [`docs/superpowers/specs/2026-09-14-influencer-mobile-bank-account-design.md`](../specs/2026-09-14-influencer-mobile-bank-account-design.md)

Implement mobile-friendly receiving bank account management and smart withdrawal payouts for influencers in MealMate, accompanied by a complete suite of screen-by-screen mobile integration documents and ready-to-use Flutter/Dart code files.

---

## Proposed Changes

### 1. Application Layer (Influencer Profile & Wallet)

#### [MODIFY] [InfluencerProfileContracts.cs](file:///f:/MealMate/backend/src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileContracts.cs)
- Update `InfluencerBankInfoDto` to include `MaskedIban` and `IsConfigured`.
- Add `GetInfluencerBankAccountQuery`, `UpdateInfluencerBankAccountCommand`, and `DeleteInfluencerBankAccountCommand`.
- Add `UpdateInfluencerBankAccountCommandValidator`.

#### [MODIFY] [IInfluencerProfileService.cs](file:///f:/MealMate/backend/src/MealMate.Application/Features/IdentityAccess/Influencer/IInfluencerProfileService.cs)
- Add signatures for `GetBankAccountAsync`, `UpdateBankAccountAsync`, `DeleteBankAccountAsync`.

#### [MODIFY] [InfluencerProfileService.cs](file:///f:/MealMate/backend/src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileService.cs)
- Implement `GetBankAccountAsync` returning masked IBAN.
- Implement `UpdateBankAccountAsync` normalizing IBAN and saving to `profile`.
- Implement `DeleteBankAccountAsync` clearing bank details.

#### [MODIFY] [InfluencerProfileHandlers.cs](file:///f:/MealMate/backend/src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerProfileHandlers.cs)
- Add MediatR / ICommandHandler & IQueryHandler implementations for the bank account queries/commands.

#### [MODIFY] [InfluencerWalletContracts.cs](file:///f:/MealMate/backend/src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletContracts.cs)
- Make `BankName`, `AccountHolderName`, `Iban` optional in `SubmitInfluencerWithdrawalCommand` and adjust its validator.

#### [MODIFY] [InfluencerWalletService.cs](file:///f:/MealMate/backend/src/MealMate.Application/Features/IdentityAccess/Influencer/InfluencerWalletService.cs)
- In `SubmitWithdrawalAsync`, resolve bank details from `profile` if not explicitly supplied in the command. Return validation error if neither is available.

---

### 2. API Layer (Controllers)

#### [MODIFY] [InfluencerPartnerController.cs](file:///f:/MealMate/backend/src/MealMate.Api/Controllers/V1/InfluencerPartnerController.cs)
- Add `[HttpGet("me/bank-account")]`.
- Add `[HttpPut("me/bank-account")]`.
- Add `[HttpDelete("me/bank-account")]`.
- Make bank details optional in `SubmitWithdrawalApiRequest`.

---

### 3. Unit Tests

#### [NEW] [InfluencerBankAccountTests.cs](file:///f:/MealMate/backend/tests/MealMate.UnitTests/Influencer/InfluencerBankAccountTests.cs)
- Verify `GetBankAccountAsync`, `UpdateBankAccountAsync`, `DeleteBankAccountAsync`, and `SubmitWithdrawalAsync` using saved bank account.

---

### 4. Mobile Screen Documentation (`docs/mobile/influencer-screens/`)

#### [NEW] [README.md](file:///f:/MealMate/docs/mobile/influencer-screens/README.md)
- Flow overview, screen index, and routing map.

#### [NEW] [01-influencer-dashboard.md](file:///f:/MealMate/docs/mobile/influencer-screens/01-influencer-dashboard.md)
- Screen 1: Dashboard, quick stats, affiliate links, and QR code.

#### [NEW] [02-bank-account-setup.md](file:///f:/MealMate/docs/mobile/influencer-screens/02-bank-account-setup.md)
- Screen 2: Bank account setup, IBAN validation, masked display, and update/delete actions.

#### [NEW] [03-withdrawals-and-wallet.md](file:///f:/MealMate/docs/mobile/influencer-screens/03-withdrawals-and-wallet.md)
- Screen 3: Wallet balance, instant withdrawal to saved bank, and withdrawal history status.

#### [NEW] [04-social-links-and-profile.md](file:///f:/MealMate/docs/mobile/influencer-screens/04-social-links-and-profile.md)
- Screen 4: Social media channels onboarding and profile details.

#### [NEW] [05-campaigns-and-analytics.md](file:///f:/MealMate/docs/mobile/influencer-screens/05-campaigns-and-analytics.md)
- Screen 5: Detailed performance charts, conversion rates, and earnings trends.

---

### 5. Flutter & Dart Production Deliverables

#### [NEW] [influencer_bank_account_models.dart](file:///f:/MealMate/docs/mobile/influencer_bank_account_models.dart)
- Dart models for Bank Account, Wallet, Withdrawals, and Profile.

#### [NEW] [influencer_api_service.dart](file:///f:/MealMate/docs/mobile/influencer_api_service.dart)
- Production-ready Dio service in Dart covering all influencer mobile endpoints.

---

## Verification Plan

### Automated Tests
```bash
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~Influencer"
```
