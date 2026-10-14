# Family member first login

The requested verification mode uses the existing configured fixed code (`Otp:WhatsAppFixedCode`, with `Otp:FixedCode` as its fallback), including deployed environments. No new fixed value is hardcoded in the feature, and no phone message is assumed to be delivered. A persisted, expiring OTP challenge is still required, and wrong-code attempts are limited. If neither a configured code nor a real phone provider is available, the family flow returns a delivery failure instead of claiming a successful send.

This flow uses the regular customer sign-in screen and the existing customer login endpoint. A family manager enables a separate login for a non-manager member with a phone number; the API creates a Customer account if needed and returns the temporary password once in the access response. If a phone already belongs to a customer account that has not logged in or set a password, enabling the separate login also provisions a temporary password. An established customer account is linked without changing its password unless the manager explicitly requests a temporary-password reset.

The family manager can deliberately reset an existing member login by sending `generateTemporaryPassword: true` together with `hasSeparateAccount: true` to `PUT /api/v1/customer/family/members/{memberId}/access`. The manager must deliver the returned `generatedPassword` to the member through an appropriate private channel. The password is stored only as an Identity hash and omitted from server authentication audit events. Client request/response loggers must redact passwords, OTPs and challenge tokens, including `generatedPassword` in this response. Manager accounts cannot be provisioned as member accounts.

## Sign-in sequence

1. Submit the member's phone and temporary password through the standard `POST /api/v1/auth/customers/login` endpoint. A pending family account receives no access or refresh token. The response has `nextStep: "family_verify_otp"`, a `loginChallengeToken`, and its expiry. The API persists a purpose-specific OTP challenge using the configured phone verification code.
2. Submit `loginChallengeToken` and `otpCode` to `POST /api/v1/auth/customers/family-first-login/verify-otp`. Success returns `nextStep: "family_set_password"` and a new one-time challenge token, still without access or refresh tokens.
3. Submit `loginChallengeToken` and `newPassword` to `POST /api/v1/auth/customers/family-first-login/set-password`. Success returns the normal authenticated customer response, including access and refresh tokens.
4. After sign-in, call `GET /api/v1/customer/family/me` to determine family navigation and capabilities. It returns an array of the authenticated customer's own contexts: `groupId`, `isManager`, `memberId`, `memberSubscriptionId` and `capabilities`. Each capability contains `action`, `allowed`, `reasonCode` and `requiredPermission`. An ordinary customer without a family context receives an empty array. Manager contexts have `isManager: true` and use the existing manager screens; a manager member ID can be null if no manager roster row exists. Member contexts include effective decisions for their own row and do not contain the manager's pricing or roster payload. Use those capabilities to show or hide app actions. The existing per-member `/api/v1/customer/family/members/{memberId}/capabilities` endpoint remains the authorization source for member-specific operations; do not treat account roles as family permission grants.

Example login response while the first step is pending:

```json
{
  "userType": "Customer",
  "accessToken": null,
  "refreshToken": null,
  "nextStep": "family_verify_otp",
  "loginChallengeToken": "<short-lived challenge>",
  "loginChallengeExpiresAtUtc": "<UTC timestamp>"
}
```

Example OTP verification request:

```json
{
  "loginChallengeToken": "<challenge from login>",
  "otpCode": "123456"
}
```

Example password setup request:

```json
{
  "loginChallengeToken": "<challenge from OTP verification>",
  "newPassword": "<new password>"
}
```

Challenges are short-lived, single-use, and bound to the member account. The API rechecks account and family membership state for OTP verification and password setup. If the member is paused, disabled, removed, or the group is inactive, the setup cannot continue. The member must use the normal customer login endpoint again after the manager restores access. Generic customer phone verification and password recovery cannot bypass mandatory setup.

When the manager regenerates a temporary password, old refresh tokens and pending authentication challenges are revoked. Previously issued access tokens are denied while password setup is pending. An account with `requiresPasswordSetup: true` cannot use regular API actions or refresh an existing session until password setup is complete.
