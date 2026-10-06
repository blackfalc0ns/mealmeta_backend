# Delivery Manager Mobile: First-Time Phone Lookup & Account Setup Guide

> **Target Audience:** Mobile Flutter Developers & Backend Integrators  
> **Feature:** Restaurant Delivery Manager (مسؤول التوصيل) Onboarding & Mobile Authentication Flow  
> **Updated:** 2026-10-03  

---

## 1. Flow Overview

The **Delivery Manager (مسؤول التوصيل)** is the restaurant staff member responsible for grouping meal boxes, planning routes, and distributing delivery orders to drivers.

When a restaurant registers a new Delivery Manager via the Restaurant Dashboard:
1. The restaurant supplies:
   - Full Name (الاسم الكامل)
   - Mobile Phone Number (رقم الموبايل)
   - Email Address (البريد الإلكتروني)
   - Responsible Coverage Area (المنطقة المسؤولة: تغطية المطعم كلها / محافظة معينة / منطقة محددة)
   - National ID number + Expiry date + Front & Back photos (الهوية الوطنية وتاريخ الانتهاء وصورة وش وضهر)
   - Employment Contract + Expiry date + Document photo (عقد العمل بالمطعم وتاريخ الانتهاء والوثيقة)
2. **No platform admin approval is required.** The account is created immediately in `Active` state (`AccountStatus = 3`), and the registration record is stored with automatic onboarding eligibility (`VerificationStatus.Approved`, no reviewer). The registration still carries the uploaded documents for record-keeping; the documents themselves are **not** marked as verified.
3. The Delivery Manager **does not** receive a temporary password.
4. Instead, the Delivery Manager opens the mobile app, enters their **phone number**, receives an SMS OTP, verifies the OTP, and sets their own password.
5. Upon setting the password, the Delivery Manager is **immediately logged in** (receives JWT `accessToken` & `refreshToken`).
6. First-time setup detection is based on `HasSetPassword` (not the account status). An `Active` account whose password was never set still goes through OTP first-time setup.
7. Operational access is re-checked on **every** authorized API request by the backend:
   - The delivery manager account must be `Active` and linked to the token's restaurant.
   - The restaurant must be `Approved` and operationally active.
   - A suspended/closed account or an inactive restaurant is blocked regardless of how the account was created.

> **Legacy compatibility:** accounts created before 2026-10-03 may still be in `Registered` (`AccountStatus = 1`) pending the old manual review. They keep working through the same first-time setup: the phone lookup treats `!HasSetPassword || AccountStatus == Registered` as first-time setup. No new registrations produce that state.

> **Retired endpoints:** `POST /api/v1/admin/delivery-managers/registrations/{id}/approve`, `/reject`, and `/request-changes` now always return `409 Conflict` with error code `delivery_manager.admin_approval_not_required`. The mobile app must never call them.

---

## 2. API Endpoints

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `/api/v1/auth/delivery-managers/lookup-phone` | Anonymous | Check phone number & dispatch OTP if first-time setup is required |
| `POST` | `/api/v1/auth/delivery-managers/first-time-setup` | Anonymous | Verify OTP, set password, & issue JWT session |
| `POST` | `/api/v1/auth/login` | Anonymous | Standard login for delivery managers who have already set their password |
| `GET` | `/api/v1/auth/me` | Bearer JWT | Fetch current user identity, roles, restaurantId, and status |

---

## 3. Sequence Diagram

```mermaid
sequenceDiagram
    autonumber
    actor DM as Delivery Manager (Mobile App)
    participant API as MealMate Backend
    participant SMS as SMS Gateway (Twilio / Mock)

    DM->>API: POST /api/v1/auth/delivery-managers/lookup-phone { phone }
    alt Manager not found
        API-->>DM: 404 DeliveryManagerNotFound
    else Needs First-Time Setup (no password set)
        API->>SMS: Dispatch 6-digit OTP
        API-->>DM: 200 OK { needsFirstTimeSetup: true, deliveryManagerName, challengeExpiresAtUtc }
        DM->>DM: Display OTP Input + New Password screen
        DM->>API: POST /api/v1/auth/delivery-managers/first-time-setup { phone, otp, newPassword, confirmPassword }
        API-->>DM: 200 OK { accessToken, refreshToken, user }
        DM->>DM: Navigate to Home (account is Active; restaurant must also be operational)
    else Already has password
        API-->>DM: 200 OK { needsFirstTimeSetup: false, deliveryManagerName }
        DM->>DM: Display Password input field
        DM->>API: POST /api/v1/auth/login { identifier: phone, password }
        API-->>DM: 200 OK { accessToken, refreshToken, user }
    end
```

---

## 4. API Specifications

### 4.1 Lookup Phone & Trigger OTP

#### Request
`POST /api/v1/auth/delivery-managers/lookup-phone`
```json
{
  "phone": "+96551234567"
}
```
*Note: The phone number can be provided with or without country code. The backend normalizes numbers automatically.*

#### Response (First-Time User — OTP Dispatched)
`200 OK`
```json
{
  "needsFirstTimeSetup": true,
  "deliveryManagerName": "عبد الله أحمد الصباح",
  "phone": "+96551234567",
  "challengeExpiresAtUtc": "2026-09-05T14:15:00Z"
}
```
> In local development/mock environment, the OTP matches the configured test code `123456`.

#### Response (Existing User — Already Set Password)
`200 OK`
```json
{
  "needsFirstTimeSetup": false,
  "deliveryManagerName": "عبد الله أحمد الصباح",
  "phone": "+96551234567",
  "challengeExpiresAtUtc": null
}
```
*Action in App: Show password input field and call `/api/v1/auth/login`.*

#### Error Responses
- `404 Not Found`:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "No delivery manager found with this phone number."
}
```

---

### 4.2 First-Time Setup (Verify OTP & Set Password)

#### Request
`POST /api/v1/auth/delivery-managers/first-time-setup`
```json
{
  "phone": "+96551234567",
  "otp": "123456",
  "newPassword": "Password123!",
  "confirmPassword": "Password123!"
}
```

#### Response (Success — Authenticated Session Issued)
`200 OK`
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "d8f3b2a1...",
  "expiresAtUtc": "2026-09-05T15:15:00Z",
  "user": {
    "id": "8f3b97b0-7a56-42d4-a78b-03487f918e21",
    "email": "dispatch@restaurant.kw",
    "phone": "+96551234567",
    "fullName": "عبد الله أحمد الصباح",
    "userType": 5,
    "status": 3,
    "restaurantId": "5e174ef4-1234-4567-89ab-cdef01234567",
    "roles": ["DeliveryManager"]
  }
}
```

#### Error Responses
- `400 Bad Request` (Invalid or expired OTP):
```json
{
  "title": "Validation Failed",
  "status": 400,
  "detail": "Invalid or expired verification code."
}
```
- `400 Bad Request` (Password mismatch or weak):
```json
{
  "title": "Validation Failed",
  "status": 400,
  "detail": "Password does not meet complexity requirements."
}
```

---

### 4.3 Subsequent Logins (Standard Password Login)

For managers who already completed first-time setup:

#### Request
`POST /api/v1/auth/login`
```json
{
  "identifier": "+96551234567",
  "password": "Password123!"
}
```

#### Response
`200 OK`
Returns the same session payload containing `accessToken`, `refreshToken`, and `user`.

---

## 5. Mobile App State Handling

### Account Status Representation (`AccountStatus` enum)
| Status Value | Name | Mobile App Experience |
|---|---|---|
| `1` | `Registered` | Legacy state (pre-2026-10-03 registrations only). Same experience as `Active` once setup completes; distribution features are gated by the server per-request, so no client-side lock is required. |
| `2` | `PhonePending` | Phone confirmation still pending; the manager should complete OTP verification. |
| `3` | `Active` | **Fully eligible**; box grouping, trip creation, and driver assignment work while the restaurant remains operationally active. |
| `4` | `Suspended` | Access blocked; message directing user to restaurant administrator. The restaurant/admin can suspend accounts for governance reasons. |
| `5` | `Closed` | Access blocked permanently; account closed. |

### Important Notes
- **Never infer document approval from account status.** Documents uploaded at registration are stored but not automatically verified. The admin profile page shows real verification state.
- **Do not display any "admin approval pending" banner** for new managers — that workflow no longer exists.
- If the restaurant becomes inactive/suspended, API calls will start failing with authorization errors even though the account itself is `Active`; surface the server error message rather than a generic "pending approval" state.

### Recommended Flutter Storage Keys
- `auth_token`: String (JWT accessToken)
- `refresh_token`: String (JWT refreshToken)
- `account_status`: int (1 = Registered, 2 = PhonePending, 3 = Active, 4 = Suspended, 5 = Closed)
- `restaurant_id`: String (Restaurant GUID)
- `coverage_scope`: String (All / Governorate / Area name)
