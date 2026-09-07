# دليل ربط الاشتراكات غير المكتملة والمؤرشفة — تطبيق الموبايل (Flutter / Mobile)

> **توثيق تقني معتمد لميزة: عرض الاشتراكات غير المكتملة / المؤرشفة، مسحها، وإكمال الاشتراك**  
> هذا الملف مخصص لمطوري الموبايل (Flutter Team) لإضافة قسم الاشتراكات غير المكتملة داخل شاشة **"الاشتراكات والفواتير"**.

---

## 1. نظرة عامة وتجربة المستخدم (User Experience & Flow)

عندما يبدأ العميل عملية اشتراك ولم يُكمل عملية الدفع (مثلاً: أغلق التطبيق، أو ألغى الدفع في MyFatoorah، أو انقطعت الجلسة)، يتم حفظ الاشتراك في النظام بالحالات:
- `PendingPayment` (بانتظار الدفع)
- `PaymentFailed` (فشل الدفع)
- `Draft` (مسودة)

### السلوك الجديد المعتمد:
1. **كارت الاشتراك الرئيسي (`GET /current`):**
   - يقتصر **فقط** على الاشتراكات المدفوعة والنشطة (`Active`, `Scheduled`, `Frozen`, `Paid`).
   - لا تظهر أي مسودة أو اشتراك غير مدفوع في الكارت الرئيسي.
2. **قسم الاشتراكات غير المكتملة (`GET /uncompleted`):**
   - يظهر كقسم منفصل (أو قائمة كروت) أسفل كارت الاشتراك الرئيسي وفوق قسم الفواتير.
   - يعرض المسودات مع تفاصيل الباقة، السعر، وحالة مهلة حجز المطعم (`isHoldActive`).
3. **لكل اشتراك غير مكتمل يوجد إجراءان:**
   - **زر "مسح" (Delete):** لحذف المسودة فوراً، وفك حجز مقاعد المطبخ، وفك أي حظر عن العميل حتى يتمكن من الاشتراك من جديد دون ظهور خطأ `checkout.existing_subscription`.
   - **زر "إكمال الاشتراك" (Resume):** يفتح شاشة **Checkout / ملخص الاشتراك** بنفس بيانات الاشتراك المحفوظة، طالما مهلة الـ 15 دقيقة لسه سارية. بعد 15 دقيقة النظام يلغي الاشتراك غير المدفوع تلقائياً (زي زر مسح) فيختفي من القائمة ويقدر العميل يبدأ اشتراك جديد.

---

## 2. استدعاء الـ APIs بالتوازي عند فتح الشاشة (Lifecycle Execution)

عند فتح شاشة "الاشتراكات والفواتير"، يقوم التطبيق باستدعاء الـ APIs بالتوازي:

```dart
final results = await Future.wait([
  apiService.getCurrentSubscription(),        // 1. كارت الاشتراك النشط/المدفوع فقط
  apiService.getUncompletedSubscriptions(),   // 2. قائمة الاشتراكات غير المكتملة (المسودات)
  apiService.getCustomerInvoices(),           // 3. قائمة الفواتير
  apiService.getFreezePolicy(),               // 4. سياسة التجميد
  apiService.getDefaultPaymentMethod(),       // 5. وسيلة الدفع الافتراضية
]);
```

---

## 3. تفاصيل الـ Endpoints الخاصة بالميزة

### 1) استعلام قائمة الاشتراكات غير المكتملة (List Uncompleted)
- **Endpoint:** `GET /api/v1/customer/subscriptions/uncompleted`  
  *(متاح أيضاً بالاسم البديل: `GET /api/v1/customer/subscriptions/archived`)*
- **Headers:** `Authorization: Bearer <Customer_JWT>`
- **Response `200 OK`:**
```json
[
  {
    "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "status": "PendingPayment",
    "statusNameAr": "بانتظار الدفع",
    "programId": "d3b07384-d113-4672-8888-000000000001",
    "programNameAr": "برنامج الكيتو",
    "programNameEn": "Keto Program",
    "bundleId": "b2b07384-d113-4672-8888-000000000002",
    "bundleNameAr": "باقة كيتو خفيفة (وجبة رئيسية + سناك)",
    "bundleNameEn": "Light Keto (Main Meal + Snack)",
    "tier": "Basic",
    "tierNameAr": "أساسي",
    "tierNameEn": "Basic",
    "tierLogoUrl": "https://api.mealmate.com/uploads/tiers/basic.png",
    "mealsPerDay": 2,
    "imageUrl": "https://api.mealmate.com/uploads/programs/keto.png",
    "startDate": "2026-09-10",
    "endDate": "2026-09-22",
    "durationDays": 12,
    "includeFriday": false,
    "amount": 70.50,
    "currencyCode": "KWD",
    "deliveryAddress": "حولي، قطعة 3، شارع 12",
    "createdAtUtc": "2026-09-06T19:30:00Z",
    "isHoldActive": false,
    "holdRemainingSeconds": null,
    "holdExpiresAtUtc": "2026-09-06T19:45:00Z",
    "actions": {
      "canDelete": true,
      "canResume": true,
      "deleteEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "resumeEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume",
      "nextScreen": "checkout",
      "payEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume"
    }
  }
]
```

> **ملاحظة:** إذا لم يكن لدى العميل أي مسودات أو اشتراكات معلقة، ترجع مصفوفة فارغة `[]` مع كود `200 OK`. في هذه الحالة لا تعرض القسم في الشاشة.

مسودة **اشتراك عائلي** (`FamilyGroup` بحالة `Draft` أو `CheckoutPending`) تظهر في نفس القائمة:

| حقل | قيمة العيلة |
|------|-------------|
| `kind` | `"FAMILY"` (الفردي = `"INDIVIDUAL"` أو غائب) |
| `familyGroupId` / `subscriptionId` | معرّف العيلة |
| `statusNameAr` | `اشتراك عائلي غير مكتمل` أو `اشتراك عائلي بانتظار الدفع` |
| `actions.deleteEndpoint` | `POST /api/v1/customer/family-subscription-flow/abandon-draft` (المسودة فقط؛ `canDelete: false` أثناء CheckoutPending) |
| `actions.resumeEndpoint` | `GET /api/v1/customer/family-subscription-flow/current` |
| `actions.nextScreen` | `"family_setup"` — **لا** تفتح Checkout الفردي |

لو العميل فتح اشتراك فردي وفي عنده مسودة عيلة: `GET /subscription-flow/prerequisites` يرجع `409` `{ "error": "family.incomplete", "missingStep": "FAMILY", "actionEndpoint": "GET /api/v1/customer/subscriptions/uncompleted" }`. اعرض: عندك اشتراك عائلي لسه مكتملش، وودّيه لأرشيف الاشتراكات.

---

### 2) مسح الاشتراك غير المكتمل (Delete Action)
عندما يضغط العميل على أيقونة أو زر **"مسح" / "حذف"** على كارت المسودة:

- **Endpoint:** `DELETE /api/v1/customer/subscriptions/{subscriptionId}`
- **Headers:** `Authorization: Bearer <Customer_JWT>`
- **Response:**
  - `204 No Content`: تم الحذف وإلغاء المسودة بنجاح.
  - `404 Not Found`: الاشتراك غير موجود أو لا يخص هذا العميل.
  - `409 Conflict`: الاشتراك مدفوع أو نشط بالفعل ولا يمكن حذفه بهذا الـ Endpoint.

#### ما يحدث في الـ Backend عند الحذف:
1. يتم فك أي حجز مقاعد بالمطابخ فوراً (`CapacityHoldStatus.Released`).
2. يتم تحويل حالة الاشتراك إلى `Cancelled`.
3. يزول حظر `checkout.existing_subscription` عن العميل فوراً ويستطيع الاشتراك في أي باقة جديدة بحرية.
4. يختفي الكارت من قائمة الاشتراكات غير المكتملة.

---

### 3) فحص ومعاينة الاستئناف (Resume Preview)
عندما يضغط العميل على زر **"إكمال الاشتراك"**:

- **Endpoint:** `GET /api/v1/customer/subscriptions/{subscriptionId}/resume`
- **Headers:** `Authorization: Bearer <Customer_JWT>`

> **عقد التنقل المعتمد:** `nextScreen` دائماً `"checkout"`. افتح شاشة Checkout/الملخص الموجودة وعبّئها من رد الـ GET. **لا** تستدعِ `GET /customer/subscription-flow/summary` ولا `POST /customer/subscriptions/checkout` — الاثنان يرجعان `409 checkout.existing_subscription` لأن المسودة نفسها هي الاشتراك الحاجب. زر الدفع في شاشة Checkout هو الذي يستدعي `POST payEndpoint`.

#### السيناريو (أ): مهلة الـ 15 دقيقة ما زالت سارية وتاريخ البدء صالح
```json
{
  "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "status": "PendingPayment",
  "statusNameAr": "بانتظار الدفع",
  "programId": "d3b07384-d113-4672-8888-000000000001",
  "programNameAr": "برنامج الكيتو",
  "bundleNameAr": "باقة كيتو خفيفة (وجبة رئيسية + سناك)",
  "tier": "Basic",
  "tierNameAr": "أساسي",
  "durationDays": 12,
  "amount": 70.50,
  "currencyCode": "KWD",
  "canPayDirectly": true,
  "reason": null,
  "holdRemainingSeconds": 620,
  "holdExpiresAtUtc": "2026-09-06T20:00:00Z",
  "suggestedStartDate": "2026-09-10",
  "resumeEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume",
  "nextScreen": "checkout",
  "payEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume",
  "checkoutStartDate": "2026-09-10"
}
```
> في هذه الحالة (`canPayDirectly == true`): افتح Checkout بتاريخ `checkoutStartDate` (= `startDate` الأصلي). لا تفتح MyFatoorah قبل زر الدفع.

#### السيناريو (ب): مهلة الـ 15 دقيقة انتهت أو تاريخ البدء قديم
```json
{
  "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "status": "PendingPayment",
  "statusNameAr": "بانتظار الدفع",
  "canPayDirectly": false,
  "reason": "capacity_hold_expired",
  "holdRemainingSeconds": null,
  "holdExpiresAtUtc": null,
  "suggestedStartDate": "2026-09-10",
  "resumeEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume",
  "nextScreen": "checkout",
  "payEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume",
  "checkoutStartDate": "2026-09-10"
}
```
> في هذه الحالة (`canPayDirectly == false`): افتح Checkout وعبّئ تاريخ البدء بـ `checkoutStartDate` (= `suggestedStartDate`). اسمح للعميل بتأكيد/تعديل التاريخ في الشاشة ثم ادفع.

---

### 4) تنفيذ الاستئناف وتوليد رابط الدفع (Resume Execution)

يُستدعى هذا الـ Endpoint **فقط من زر الدفع في شاشة Checkout** بعد أن يرى العميل تفاصيل اشتراكه. يقوم بتجديد حجز المقاعد بالمطابخ لمدة 15 دقيقة جديدة وتوليد رابط الدفع عبر MyFatoorah:

- **Endpoint:** `POST /api/v1/customer/subscriptions/{subscriptionId}/resume`
- **Headers:** 
  - `Authorization: Bearer <Customer_JWT>`
  - `Content-Type: application/json`
- **Request Body (اختياري / يمكن إرسال كائن فارغ `{}`):**
```json
{
  "startDate": "2026-09-10",
  "callbackUrl": "mealmate://checkout/callback",
  "errorUrl": "mealmate://checkout/error",
  "paymentMethodId": 1
}
```

أرسل `startDate` = `checkoutStartDate` من رد الـ GET (أو التاريخ الذي أكّده العميل في شاشة Checkout).

- **Response `200 OK`:**
```json
{
  "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "paymentUrl": "https://portal.myfatoorah.com/KWT/PayInvoice/Checkout?invoiceKey=...",
  "holdExpiresAtUtc": "2026-09-06T20:15:00Z",
  "finalPrice": 70.50,
  "paymentStatus": "PendingPayment"
}
```

> بعد استلام `paymentUrl` من شاشة Checkout فقط، يفتح التطبيق رابط الدفع عبر WebView أو متصفح خارجي.

---

## 4. نماذج دارت الجاهزة للنسخ (Dart / Flutter Models)

```dart
// uncompleted_subscription_models.dart

class CustomerUncompletedSubscription {
  final String subscriptionId;
  final String status;
  final String statusNameAr;
  final String programId;
  final String programNameAr;
  final String programNameEn;
  final String bundleId;
  final String bundleNameAr;
  final String bundleNameEn;
  final String tier;
  final String tierNameAr;
  final String tierNameEn;
  final String? tierLogoUrl;
  final int mealsPerDay;
  final String? imageUrl;
  final String startDate;
  final String endDate;
  final int durationDays;
  final bool includeFriday;
  final double amount;
  final String currencyCode;
  final String? deliveryAddress;
  final DateTime createdAtUtc;
  final bool isHoldActive;
  final int? holdRemainingSeconds;
  final DateTime? holdExpiresAtUtc;
  final CustomerUncompletedActions actions;

  CustomerUncompletedSubscription({
    required this.subscriptionId,
    required this.status,
    required this.statusNameAr,
    required this.programId,
    required this.programNameAr,
    required this.programNameEn,
    required this.bundleId,
    required this.bundleNameAr,
    required this.bundleNameEn,
    required this.tier,
    required this.tierNameAr,
    required this.tierNameEn,
    this.tierLogoUrl,
    required this.mealsPerDay,
    this.imageUrl,
    required this.startDate,
    required this.endDate,
    required this.durationDays,
    required this.includeFriday,
    required this.amount,
    required this.currencyCode,
    this.deliveryAddress,
    required this.createdAtUtc,
    required this.isHoldActive,
    this.holdRemainingSeconds,
    this.holdExpiresAtUtc,
    required this.actions,
  });

  factory CustomerUncompletedSubscription.fromJson(Map<String, dynamic> json) {
    return CustomerUncompletedSubscription(
      subscriptionId: json['subscriptionId'] as String,
      status: json['status'] as String,
      statusNameAr: json['statusNameAr'] as String,
      programId: json['programId'] as String,
      programNameAr: json['programNameAr'] as String,
      programNameEn: json['programNameEn'] as String,
      bundleId: json['bundleId'] as String,
      bundleNameAr: json['bundleNameAr'] as String,
      bundleNameEn: json['bundleNameEn'] as String,
      tier: json['tier'] as String,
      tierNameAr: json['tierNameAr'] as String,
      tierNameEn: json['tierNameEn'] as String,
      tierLogoUrl: json['tierLogoUrl'] as String?,
      mealsPerDay: json['mealsPerDay'] as int,
      imageUrl: json['imageUrl'] as String?,
      startDate: json['startDate'] as String,
      endDate: json['endDate'] as String,
      durationDays: json['durationDays'] as int,
      includeFriday: json['includeFriday'] as bool,
      amount: (json['amount'] as num).toDouble(),
      currencyCode: json['currencyCode'] as String,
      deliveryAddress: json['deliveryAddress'] as String?,
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      isHoldActive: json['isHoldActive'] as bool,
      holdRemainingSeconds: json['holdRemainingSeconds'] as int?,
      holdExpiresAtUtc: json['holdExpiresAtUtc'] != null
          ? DateTime.parse(json['holdExpiresAtUtc'] as String)
          : null,
      actions: CustomerUncompletedActions.fromJson(json['actions'] as Map<String, dynamic>),
    );
  }
}

class CustomerUncompletedActions {
  final bool canDelete;
  final bool canResume;
  final String deleteEndpoint;
  final String resumeEndpoint;
  final String nextScreen;
  final String payEndpoint;

  CustomerUncompletedActions({
    required this.canDelete,
    required this.canResume,
    required this.deleteEndpoint,
    required this.resumeEndpoint,
    required this.nextScreen,
    required this.payEndpoint,
  });

  factory CustomerUncompletedActions.fromJson(Map<String, dynamic> json) {
    return CustomerUncompletedActions(
      canDelete: json['canDelete'] as bool,
      canResume: json['canResume'] as bool,
      deleteEndpoint: json['deleteEndpoint'] as String,
      resumeEndpoint: json['resumeEndpoint'] as String,
      nextScreen: json['nextScreen'] as String,
      payEndpoint: json['payEndpoint'] as String,
    );
  }
}

class CustomerSubscriptionResumePreview {
  final String subscriptionId;
  final String status;
  final String statusNameAr;
  final String programId;
  final String programNameAr;
  final String bundleNameAr;
  final String tier;
  final String tierNameAr;
  final int durationDays;
  final double amount;
  final String currencyCode;
  final bool canPayDirectly;
  final String? reason;
  final int? holdRemainingSeconds;
  final DateTime? holdExpiresAtUtc;
  final String suggestedStartDate;
  final String resumeEndpoint;
  final String nextScreen;
  final String payEndpoint;
  final String checkoutStartDate;

  CustomerSubscriptionResumePreview({
    required this.subscriptionId,
    required this.status,
    required this.statusNameAr,
    required this.programId,
    required this.programNameAr,
    required this.bundleNameAr,
    required this.tier,
    required this.tierNameAr,
    required this.durationDays,
    required this.amount,
    required this.currencyCode,
    required this.canPayDirectly,
    this.reason,
    this.holdRemainingSeconds,
    this.holdExpiresAtUtc,
    required this.suggestedStartDate,
    required this.resumeEndpoint,
    required this.nextScreen,
    required this.payEndpoint,
    required this.checkoutStartDate,
  });

  factory CustomerSubscriptionResumePreview.fromJson(Map<String, dynamic> json) {
    return CustomerSubscriptionResumePreview(
      subscriptionId: json['subscriptionId'] as String,
      status: json['status'] as String,
      statusNameAr: json['statusNameAr'] as String,
      programId: json['programId'] as String,
      programNameAr: json['programNameAr'] as String,
      bundleNameAr: json['bundleNameAr'] as String,
      tier: json['tier'] as String,
      tierNameAr: json['tierNameAr'] as String,
      durationDays: json['durationDays'] as int,
      amount: (json['amount'] as num).toDouble(),
      currencyCode: json['currencyCode'] as String,
      canPayDirectly: json['canPayDirectly'] as bool,
      reason: json['reason'] as String?,
      holdRemainingSeconds: json['holdRemainingSeconds'] as int?,
      holdExpiresAtUtc: json['holdExpiresAtUtc'] != null
          ? DateTime.parse(json['holdExpiresAtUtc'] as String)
          : null,
      suggestedStartDate: json['suggestedStartDate'] as String,
      resumeEndpoint: json['resumeEndpoint'] as String,
      nextScreen: json['nextScreen'] as String,
      payEndpoint: json['payEndpoint'] as String,
      checkoutStartDate: json['checkoutStartDate'] as String,
    );
  }
}
```

---

## 5. أمثلة دوال الاستدعاء في فلاتر (Flutter Service Functions)

```dart
// subscription_service.dart

class SubscriptionApiService {
  final String baseUrl = 'http://maelmate.runasp.net';
  final String token;

  SubscriptionApiService({required this.token});

  Map<String, String> get _headers => {
    'Authorization': 'Bearer $token',
    'Content-Type': 'application/json',
  };

  /// 1. استرجاع قائمة الاشتراكات غير المكتملة
  Future<List<CustomerUncompletedSubscription>> getUncompletedSubscriptions() async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/v1/customer/subscriptions/uncompleted'),
      headers: _headers,
    );

    if (response.statusCode == 200) {
      final List<dynamic> data = jsonDecode(response.body);
      return data.map((x) => CustomerUncompletedSubscription.fromJson(x)).toList();
    }
    return [];
  }

  /// 2. مسح الاشتراك غير المكتمل
  Future<bool> deleteUncompletedSubscription(String subscriptionId) async {
    final response = await http.delete(
      Uri.parse('$baseUrl/api/v1/customer/subscriptions/$subscriptionId'),
      headers: _headers,
    );
    return response.statusCode == 204;
  }

  /// 3. معاينة حالة الاستئناف
  Future<CustomerSubscriptionResumePreview?> getResumePreview(String subscriptionId) async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/v1/customer/subscriptions/$subscriptionId/resume'),
      headers: _headers,
    );

    if (response.statusCode == 200) {
      return CustomerSubscriptionResumePreview.fromJson(jsonDecode(response.body));
    }
    return null;
  }

  /// 4. إكمال الاشتراك وتوليد رابط الدفع MyFatoorah
  Future<String?> resumeSubscription({
    required String subscriptionId,
    String? startDate,
    String? callbackUrl,
    String? errorUrl,
  }) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/v1/customer/subscriptions/$subscriptionId/resume'),
      headers: _headers,
      body: jsonEncode({
        if (startDate != null) 'startDate': startDate,
        'callbackUrl': callbackUrl ?? 'mealmate://checkout/callback',
        'errorUrl': errorUrl ?? 'mealmate://checkout/error',
      }),
    );

    if (response.statusCode == 200) {
      final data = jsonDecode(response.body);
      return data['paymentUrl'] as String?;
    }
    return null;
  }
}
```
