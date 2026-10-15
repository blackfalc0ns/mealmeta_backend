# Mobile family management contract

This contract describes the repaired local backend. Deployment and migrations must be verified separately. There is one sign-in screen and one customer login endpoint for every customer.

## Sign-in and account ownership

1. `POST /api/v1/auth/customers/login` with the usual phone/country code/password.
2. If `isAuthenticated` is false and `nextStep` is `family_verify_otp`, keep the short-lived `loginChallengeToken` as a challenge, never as a bearer token.
3. `POST /api/v1/auth/customers/family-first-login/verify-otp` with `{loginChallengeToken, otpCode}`. Replace the challenge with the new one and follow `family_set_password`.
4. `POST /api/v1/auth/customers/family-first-login/set-password` with `{loginChallengeToken, newPassword}`. Store access/refresh tokens only from the authenticated response.
5. `GET /api/v1/customer/family/me` determines navigation from server identity. Never ask the user to pick a role to acquire permissions.

Resend: `POST /api/v1/auth/customers/family-first-login/resend-otp` with `{loginChallengeToken}`. Store the returned replacement token and expiry. `resend-phone-otp` is a different purpose and must not be used for this step. For an expired or invalid challenge, return to the same normal login screen to begin again.

Onboarding can complete in Draft, CheckoutPending, or Active groups. This creates a customer session, not a funded subscription. Subscription and calendar readiness remain separate.

A removed, disabled, paused or otherwise inaccessible unfinished onboarding account still cannot use obsolete temporary credentials for an ordinary session. Customer forgot/reset password can recover it through a verified password-reset OTP, replacing the password and clearing obsolete setup gates atomically. Viable onboarding must complete its own challenge flow. The configured fixed verification-code mode remains as explicitly requested; the API must not claim a phone message was sent when no real delivery provider is used.

Customers own their account passwords and password recovery. Family pause/disable limits family data, rather than shutting down an unrelated personal account or subscription. Do not expose a manager toggle as if it can remove identity ownership.

## Family context and states

`/family/me` returns every eligible context, including independent historical detached contexts. Context fields include:

- `groupId`, `isManager`, `memberId`, `memberSubscriptionId`.
- `isDetachedFromFamilyManagement`, `setupStatus`, `hasSeparateAccount`, `isPaused`, `requiresPasswordSetup`, `accessStatus`.
- `capabilities`: `action`, `allowed`, `reasonCode`, `requiredPermission`.

Paused/disabled contexts remain visible with state and denied family capabilities; they do not hide another valid context or imply the customer's global session is invalid. An empty array means no context exists, not a separate login mode. A null subscription ID means no permitted funded service was resolved. Pending onboarding still blocks protected API access until setup finishes.

Manager context uses `isManager: true` and manager APIs. Member context uses its own resolved subscription ID. Prefer this ID to a global `subscriptions/current` response when opening a selected member's calendar.

## Manager screens and member selection

- `GET /api/v1/customer/family/home`: manager home and service/payment/fulfillment state.
- `GET /api/v1/customer/family/members`: manager roster.
- `GET /api/v1/customer/family/members/{memberId}`: manager detail.
- `GET /api/v1/customer/family/switcher`: per-actor member discovery across eligible contexts; each row carries `groupId` and identity/visibility flags. A member sees own rows plus siblings allowed by read grants. Do not treat a sibling read row as editable or render manager account controls.
- `GET /api/v1/customer/family/members/{memberId}/capabilities`: effective server decisions for the actor and selected row.

Use `canEditDailyMeals`/read flags from the current actor's policy, not the target's own grants. Home/list/detail lifecycle distinguishes setup-required and detached state. Paid service can remain `PaidPreparingCalendar`; do not open an empty calendar as if provisioning succeeded.

Add/plan/start-date/address/preferences/purchase use `/api/v1/customer/family-subscription-flow/...`. `POST /customer/family/members` intentionally redirects the product flow with `family.use_checkout_flow`.

## Provision a login without detaching

```http
PUT /api/v1/customer/family/members/{memberId}/access
```

```json
{
  "hasSeparateAccount": true,
  "generateTemporaryPassword": true,
  "credentialOperationKey": "<new unique UUID for this deliberate operation>"
}
```

A unique operation key is required for enabling access or explicit credential generation. Reuse the same key only when retrying that exact operation. Do not reuse it for a later deliberate reset. Enabling an established account can link its existing login without resetting its password; `generateTemporaryPassword: true` explicitly requests replacement.

The first completed credential response may contain `generatedPassword`. GET never returns plaintext. Redact passwords, OTPs, bearer/refresh/challenge tokens from request/response logs, analytics and crash reports. Display the returned canonical linked phone; do not substitute an edited member-card phone.

A completed replay returns no password and has `passwordAlreadyIssued: true`, `requiresExplicitReset: true`. This means the earlier operation completed and the plaintext cannot be recovered. Explain that outcome and offer an explicit new reset with a new key. Never retry by generating a fresh key automatically. Duplicate keys remain protected even after later credential operations.

Changed linked phone numbers require a verified identity-change flow. Ordinary member edits reject a different linked phone with `family.member_phone_change_requires_verification`; the response phone remains the actual login phone.

## Permissions

Implementation details for all 17 permissions, their screen behavior, descriptions and API routes: [Family permissions mobile implementation guide](./family-permissions-mobile-implementation-guide.md).

Read: `GET members/{memberId}/permissions`. Write: `PUT members/{memberId}/permissions` with `expectedVersion` and only the changed keys. All permission booleans are nullable patch inputs: omissions preserve existing values. Never send `groups` as a request body substitute.

Each catalog item returns `labelAr`, `labelEn`, `descriptionAr`, and `descriptionEn`. Show the description under the permission name using the app's active language; when a field is missing, fall back to its counterpart in the other language. Do not hardcode a second mobile permission glossary. Example:

```json
{
  "key": "editMealsAndDays",
  "labelAr": "تعديل الوجبات والأيام",
  "labelEn": "Edit meals and days",
  "descriptionAr": "يسمح بإضافة وجبات إلى خانات اليوم المتاحة واستكمال وجبات اليوم، ولا يسمح وحده باستبدال وجبة مختارة.",
  "descriptionEn": "Lets the member add meals to available slots and fill a day's meals; by itself it does not allow replacing a selected meal.",
  "value": false,
  "isSupported": true,
  "isManagerConfigurable": true
}
```

```json
{ "expectedVersion": 4, "editDailyMeals": true }
```

Calendar write grants carry `viewCalendar` and `viewProgram` prerequisites; own calendar read and supported service-management grants carry own plan-read prerequisites. The persisted response returns these derived grants. Read access does not grant write access. Invoice access stays financially independent: plan permission does not grant invoice permission, and invoice permission alone does not grant plan data.

Catalog items include `isSupported`, `isManagerConfigurable`, and `requiredPermissions`. Hide or disable unsupported controls. `viewProgram` and `changePassword` are baseline permissions and not manager-configurable. `editMealsAndDays` adds meals to available slots and completes a day's selections; `unitExchange` replaces a selected meal in an open, editable day. `editDailyMeals` remains the broader legacy permission. `changeProgram` and `changeDeliveryTime` are not in the manager catalog, and granting either legacy field is rejected.

For newly initialized member access, minimum own plan/calendar reading is initialized. Existing saved deliberate permissions are preserved. On `409 family.permissions_changed`, refresh and show a conflict; never silently overwrite another session's edits. Capabilities are authorization, not a guarantee that a day/service operation is currently available.

## Detachment

`POST /api/v1/customer/family/members/{memberId}/detach` is independent of enabling login. It requires completed active login and no blocking payment/upgrade/renewal/freeze intent. It is idempotent.

After detachment:

- Member automatically receives every supported own subscription/account/calendar action, independent of old manager grants.
- Former manager retains approved read-only service/roster visibility and cannot edit credentials, grants, profile/preferences or service through member-management routes.
- Former manager cannot read the detached customer's private preference endpoints or full saved address book. Existing service delivery snapshots remain part of approved service visibility.
- Member has no sibling-family scope. Subscription, member/group lineage, days, dates and funded entitlement stay intact.
- Historical detached membership does not itself prevent a new managed membership. The old service remains on its original lineage; distinguish contexts by group/member/subscription IDs.
- Changes to the historical parent's group status do not take away the detached owner's authorized service. Detached status never grants former-manager control again.
- There is no automatic reattachment; toggling account access never restores family management. A consent-based reattachment feature is outside the current contract.

`remove: true` invokes removal/financial settlement and is not detachment. `isPaused` does not freeze service; freeze uses its own subscription endpoint.

## Subscription actions and errors

Open own service from resolved IDs. Subscription cards include `familyMemberId` and `familyMemberName`. Global current selection prefers the actor's own authorized service over siblings.

Use specific subscription routes for calendar, delivery address amendments, freeze, renewal, upgrade and cancellation. Paid delivery uses `{addressId,effectiveFromDate,expectedVersion}` and returns affected/skipped day IDs. Updating an address book entry does not replace committed delivery snapshots.

Finance writes and consent mutations recheck authority after shared locks. Auto-renew consent does not connect billing or enable recurring charges; `billingStatus: NotConnected` remains truthful. Renewal denials retain Forbidden/Conflict/NotFound HTTP semantics and localized ProblemDetails.

Use semantic `code`/`reasonCode`, plus status. Refresh cached context/capabilities after access, permissions, detachment and credential changes; server enforcement remains authoritative if another device changes state.

## Arabic/English and verification

Send `Accept-Language`. Product-owned copy belongs in the client's Arabic/English dictionary and must react to language changes. Preserve technical action keys, permission keys and IDs. Bilingual response serialization can emit the request-language field plus an unsuffixed localized field instead of both source-language fields; do not require both on every response.

Use the business calendar in Asia/Kuwait and KWD precision of three decimals. Verify `ar → en → ar` with error/dialog open, RTL/LTR, persisted language, token/challenge expiry and every blocked/empty/loading state. Backend tests do not substitute for tests of a Flutter source tree or the deployed server/database.

Local PostgreSQL tests can use a user/process environment variable `MEALMATE_TEST_POSTGRES_ADMIN` pointing to an authorized local administrator/test role with database-creation permission. Tests create uniquely named `mealmate_test_*` databases, migrate only those databases, and clean up only their own database. The configured application database is never migrated or dropped by this test helper. Without this variable, fixtures retain the Docker PostgreSQL path.
