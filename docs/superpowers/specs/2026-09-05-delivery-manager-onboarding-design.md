# Restaurant Delivery Manager Onboarding & Mobile Authentication Design

> **Status:** Draft (Approved in Brainstorming)  
> **Date:** 2026-09-05  
> **Authors:** MealMate Engineering Team  
> **Scope:** Backend (.NET 9), Restaurant Dashboard (Angular 19), Admin Dashboard (Angular 19), Mobile App Integration (Flutter)  

---

## 1. Problem Statement & Motivation

In MealMate, restaurants employ **Delivery Managers (مسؤولو التوصيل)** who oversee driver dispatching and assign meal orders to drivers. Currently, restaurants can only enter basic info (Name, Email, Phone) and an invite is sent via email. 

To maintain security, platform compliance, and unified driver management:
1. The restaurant must provide verified credentials for the Delivery Manager:
   - **Full Name (الاسم)**
   - **Phone Number (رقم الموبايل)**
   - **Email Address (البريد الإلكتروني)**
   - **National ID (الرقم الوطني / المدني)** + Expiry Date + Front & Back Photos (وش وضهر)
   - **Restaurant Employment Contract (عقد العمل)** + Expiry Date + Document/Photo
2. The Delivery Manager account must be vetted and approved by the Platform Admin (`PendingApproval` status upon submission).
3. The Delivery Manager should be able to activate and access their account via mobile using their **phone number and SMS OTP**, setting their own password and logging in immediately with a `PendingApproval` state banner until admin approval is granted.

---

## 2. Architecture & Domain Model

### 2.1 Domain Entities & Storage

#### DeliveryManagerRegistration / Profile Fields
```csharp
public class DeliveryManagerRegistration
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public DateTime? NationalIdExpiryUtc { get; set; }
    public DateTime? ContractExpiryUtc { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;
    public string? RejectionReason { get; set; }
    public string? ChangeRequestNotes { get; set; }
    public Guid? ResultingUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}
```

#### Document Types Required (3 Documents)
1. `DocumentType.NationalIdFront = 10`: صورة الهوية الأمامية
2. `DocumentType.NationalIdBack = 11`: صورة الهوية الخلفية
3. `DocumentType.EmploymentContract = 12`: صورة عقد العمل مع المطعم

Each document has `ExpiresAtUtc` set according to the entered expiry date.

---

## 3. Detailed Workflow

```mermaid
sequenceDiagram
    autonumber
    actor Rest as Restaurant Owner
    participant Web as Restaurant Dashboard
    participant API as MealMate Backend
    actor DM as Delivery Manager
    actor Admin as Platform Admin

    Rest->>Web: Fill Delivery Manager form & upload 3 docs
    Web->>API: POST /api/v1/restaurants/delivery-managers/register
    API->>API: Validate uniqueness (phone, email, nationalId) & expiries in future
    API-->>Web: 201 Created (Status: PendingApproval)
    
    DM->>API: POST /api/v1/auth/delivery-managers/lookup-phone { phone }
    API-->>DM: 200 OK { needsFirstTimeSetup: true, managerName, challengeExpiresAtUtc }
    DM->>API: POST /api/v1/auth/delivery-managers/first-time-setup { phone, otp, newPassword }
    API-->>DM: 200 OK (JWT session, AccountStatus: PendingApproval)
    Note over DM: App displays "Pending Admin Approval" banner; order dispatch locked

    Admin->>API: GET /api/v1/admin/delivery-managers/registrations/{id}
    Note over Admin: Reviews National ID front/back, contract, expiries
    Admin->>API: POST /api/v1/admin/delivery-managers/registrations/{id}/approve
    API->>API: Set AccountStatus = Active
    Note over DM: Account approved; banner clears; order dispatch unlocked
```

---

## 4. API Specifications

### 4.1 Restaurant: Register Delivery Manager
- **Endpoint**: `POST /api/v1/restaurants/delivery-managers/register`
- **Auth**: Restaurant Owner JWT (`Role = RestaurantOwner`)
- **Payload**:
  ```json
  {
    "fullName": "نورة خالد العجمي",
    "phone": "+96551234567",
    "email": "noura.dispatch@burgerfactory.kw",
    "nationalId": "296051201234",
    "nationalIdExpiryUtc": "2028-12-31T00:00:00Z",
    "contractExpiryUtc": "2027-06-30T00:00:00Z",
    "nationalIdFrontDocumentStorageKey": "uploads/delivery-managers/dm-id-front.jpg",
    "nationalIdBackDocumentStorageKey": "uploads/delivery-managers/dm-id-back.jpg",
    "employmentContractDocumentStorageKey": "uploads/delivery-managers/dm-contract.pdf"
  }
  ```
- **Validation**:
  - `phone`, `email`, `nationalId` must be globally unique across users/registrations.
  - `nationalIdExpiryUtc` and `contractExpiryUtc` must be strictly in the future.
  - All 3 storage keys must be non-empty and exist in uploaded documents.

### 4.2 Mobile Auth: Phone Lookup
- **Endpoint**: `POST /api/v1/auth/delivery-managers/lookup-phone`
- **Auth**: Anonymous
- **Payload**:
  ```json
  {
    "phone": "+96551234567"
  }
  ```
- **Response**:
  ```json
  {
    "needsFirstTimeSetup": true,
    "managerName": "نورة خالد العجمي",
    "phone": "+96551234567",
    "challengeExpiresAtUtc": "2026-09-05T14:15:00Z"
  }
  ```

### 4.3 Mobile Auth: First-Time Setup
- **Endpoint**: `POST /api/v1/auth/delivery-managers/first-time-setup`
- **Auth**: Anonymous
- **Payload**:
  ```json
  {
    "phone": "+96551234567",
    "otp": "123456",
    "newPassword": "SecurePassword123!",
    "confirmPassword": "SecurePassword123!"
  }
  ```
- **Response**: `200 OK` with standard `LoginResultDto` (JWT tokens, user role: `DeliveryManager`, status: `PendingApproval`).

### 4.4 Admin: Approval & Review
- **Endpoints**:
  - `GET /api/v1/admin/delivery-managers/registrations`
  - `GET /api/v1/admin/delivery-managers/registrations/{id}`
  - `POST /api/v1/admin/delivery-managers/registrations/{id}/approve`
  - `POST /api/v1/admin/delivery-managers/registrations/{id}/reject`
  - `POST /api/v1/admin/delivery-managers/registrations/{id}/request-changes`

---

## 5. Frontend & UI Changes

### 5.1 Restaurant Dashboard (Angular 19)
- Upgrade `dispatch-officer-create-modal.component`:
  - Step 1: Basic Info (Full Name, Phone, Email, National ID).
  - Step 2: Contract & Document Uploads:
    - Civil ID Front Photo + Expiry Date.
    - Civil ID Back Photo.
    - Restaurant Contract Photo/PDF + Expiry Date.
  - Live image previews with drag-and-drop.
  - Status column in officers table displays: `قيد مراجعة الإدارة (Pending Approval)` / `مفعل (Active)`.

### 5.2 Admin Dashboard (Angular 19)
- In `delivery-manager-detail-page` or pending accounts list:
  - Display Delivery Manager's National ID, Expiry Date, Contract Expiry Date.
  - Document viewer with expiry badges:
    - 🟢 Green (> 60 days)
    - 🟡 Amber (< 60 days)
    - 🔴 Red (Expired)
  - Action buttons: Approve / Reject / Request Changes.

---

## 6. Testing & Verification
- Unit tests in `MealMate.UnitTests`:
  - Validation rules for registration command (phone, email, national ID uniqueness, future dates).
  - Phone lookup and first-time setup flow.
  - Admin approval transitions status to `Active`.
- Frontend build verification (`npm.cmd run build` in both dashboards).
