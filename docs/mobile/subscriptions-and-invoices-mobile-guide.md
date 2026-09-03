# دليل ربط شاشة الاشتراكات والفواتير — تطبيق الموبايل (Flutter / Mobile)

> **توثيق تقني معتمد لشاشة "الاشتراكات والفواتير"**  
> هذا الملف موجّه لمطوري الموبايل (Flutter Team) للربط المباشر مع الـ Backend، ومطابق بنسبة 100% للتصميم المعروض في واجهة التطبيق.

---

## 1. نظرة عامة على الشاشة ومكونات الواجهة (UI Mapping)

| العنصر في الواجهة (UI) | الحقل في رد الـ API | الوصف والقيمة في التصميم |
| :--- | :--- | :--- |
| **عنوان الشاشة** | ثابت في التطبيق | `الاشتراكات والفواتير` مع زر رجوع `Back` |
| **اسم البرنامج والباقة** | `subscription.bundleNameAr` | مثال: `"باقة كيتو خفيفة (وجبة رئيسية + سناك)"` |
| **شارة التصنيف (Tier)** | `subscription.tierNameAr` | `"أساسي"` (أو بلاتينيوم / إيليت) |
| **حالة الاشتراك** | `subscription.statusNameAr` + `subscription.status` | شارة خضراء مع أيقونة تحقق: `"مجدول"` (`Scheduled`) أو `"نشط"` (`Active`) |
| **تاريخ التجديد القادم** | `subscription.nextRenewalDate` | أيقونة تقويم + نص: `"تاريخ التجديد القادم: 18 سبتمبر 2026"` |
| **الأيام المتبقية** | `subscription.remainingServiceDays` | نص: `"متبقي 12 يوم"` |
| **شريط الاستخدام والتقدم** | `completedServiceDays` من `totalServiceDays` | نص: `"استخدام الخطة هذا الشهر - 0 من 12 يوم"` مع `progressPercentage` |
| **لوجو المطعم** | `subscription.restaurant.logoUrl` | الأيقونة العلوية في يمين الكارت (لوجو المطعم المختار) |
| **أزرار الإجراءات السريعة (4 أزرار)** | كائن `subscription.actions` | 1. ترقية التصنيف / 2. تجميد / 3. تجديد / 4. إلغاء |
| **قسم الفواتير** | قائمة من `GET /api/v1/customer/invoices` | رقم الفاتورة `7141919`، المبلغ `KWD 70.50`، الحالة `"تم الدفع"`، التاريخ `2 سبتمبر 2026` |
| **شريط تنبيه سياسة التجميد** | `GET /api/v1/customer/subscriptions/freeze-policy` | صندوق برتقالي: `"يمكن تجميد الاشتراك لمدة حتى 7 أيام، ويبدأ التجميد بعد 3 أيام على الأقل."` |
| **الزر الرئيسي السفلي** | زر التنقل لإدارة الاشتراك | `"إدارة الاشتراك"` ينقل لشاشة الإدارة التفصيلية |
| **الشريط السفلي (Notice Bar)** | يظهر عندما `subscription.pendingUpgrade != null` | نص أسود عائم: `"يوجد طلب ترقية معلق قيد المتابعة حالياً."` (عند وجود ترقية بانتظار استكمال الدفع) |

---

## 2. قواعد التشغيل (Business Rules) الهامة

> [!IMPORTANT]
> **التجميد والترقية هما عمليتان مؤتمتتان بالكامل (Self-Service) ولا يتطلبان موافقة يدوية من الأدمن:**
> 1. **التجميد (Freeze):** يتم اعتماده وتطبيقه تلقائياً بمجرد إرسال العميل للطلب طالما استوفى شروط السياسة (يبدأ بعد مهلة الـ 72 ساعة، ومدته لا تتجاوز 7 أيام، ولا يقع في أيام مقفلة مع المطابخ). يتم تمديد تاريخ نهاية الاشتراك فوراً دون انتظار أي موافقة أدمن.
> 2. **ترقية التصنيف (Upgrade):** تتم آلياً بالدفع؛ يختار العميل الفئة الأعلى -> يحسب النظام فرق السعر التناسبي -> يُفتح رابط دفع MyFatoorah -> فور نجاح الدفع يتم ترقية تصنيف الاشتراك فوراً.
> 3. **التنبيه السفلي بالصورة ("يوجد طلب ترقية معلق قيد المتابعة حالياً"):** يظهر فقط إذا بدأ العميل عملية ترقية وكان الدفع معلقاً أو قيد المعالجة من بوابة الدفع (`VerificationStatus.Submitted`). بمجرد تأكيد الدفع يختفي التنبيه وتصبح الباقة بالفئة الجديدة فوراً.

---

## 3. دورة استدعاء الـ APIs عند فتح الشاشة (Lifecycle Execution)

عند فتح شاشة "الاشتراكات والفواتير"، يقوم التطبيق بتنفيذ الاستدعاءات بالتوازي (`Future.wait`):

```dart
final results = await Future.wait([
  apiService.getCurrentSubscription(),
  apiService.getCustomerInvoices(),
  apiService.getFreezePolicy(),
]);
```

### 1) استعلام الاشتراك الحالي
- **Endpoint:** `GET /api/v1/customer/subscriptions/current`
- **Headers:** `Authorization: Bearer <Customer_JWT>`
- **Response `200 OK`:**
```json
{
  "subscription": {
    "subscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
    "status": "Scheduled",
    "statusNameAr": "مجدول",
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
    "startDate": "2026-09-06",
    "endDate": "2026-09-17",
    "nextRenewalDate": "2026-09-18",
    "totalServiceDays": 12,
    "completedServiceDays": 0,
    "remainingServiceDays": 12,
    "progressPercentage": 0.0,
    "paidAmount": 70.50,
    "currencyCode": "KWD",
    "restaurant": {
      "restaurantId": "e1b07384-d113-4672-8888-000000000003",
      "nameAr": "دايت سنتر",
      "nameEn": "Diet Center",
      "logoUrl": "https://api.mealmate.com/uploads/restaurants/diet-center.png"
    },
    "pendingFreeze": null,
    "pendingUpgrade": {
      "requestId": "a5c07384-d113-4672-8888-000000000099",
      "fromTier": "Basic",
      "toTier": "Platinum",
      "priceDifference": 15.50,
      "status": "Submitted"
    },
    "actions": {
      "canRenew": true,
      "canFreeze": true,
      "canCancel": true,
      "canUpgrade": false,
      "renewUnavailableReason": null,
      "freezeUnavailableReason": null,
      "cancelUnavailableReason": null,
      "upgradeUnavailableReason": "upgrade_request_pending",
      "freezeStartDelayDays": 3,
      "maximumFreezeDays": 7,
      "freezeExtendsSubscriptionEnd": true,
      "renewalEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/renewal-requests",
      "freezeEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/freeze-requests",
      "cancellationEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/cancellation-requests",
      "upgradeEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/upgrade-requests"
    }
  },
  "emptyStateCode": null
}
```

> **ملاحظة لشريط التنبيه السفلي:**  
> إذا كان `subscription.pendingUpgrade != null`، يتم إظهار الشريط الأسود العائم في أسفل الشاشة:  
> `"يوجد طلب ترقية معلق قيد المتابعة حالياً."`

---

### 2) استعلام قائمة الفواتير
- **Endpoint:** `GET /api/v1/customer/invoices`
- **Response `200 OK`:**
```json
[
  {
    "receiptId": "f9a17384-d113-4672-8888-000000000010",
    "invoiceNumber": "7141919",
    "paymentId": "1002026245899",
    "issuedAtUtc": "2026-09-02T10:15:30Z",
    "amount": 70.50,
    "currencyCode": "KWD",
    "status": "PAID",
    "statusNameAr": "تم الدفع",
    "gateway": "MyFatoorah",
    "maskedCardNumber": null,
    "canDownload": false,
    "downloadUnavailableReason": "invoice_document_not_available"
  }
]
```

---

### 3) استعلام سياسة التجميد (الصندوق البرتقالي)
- **Endpoint:** `GET /api/v1/customer/subscriptions/freeze-policy`
- **Response `200 OK`:**
```json
{
  "defaultDurationDays": 7,
  "startDelayDays": 3,
  "extendSubscriptionEnd": true,
  "messageAr": "يمكن تجميد الاشتراك لمدة حتى 7 أيام، ويبدأ التجميد بعد 3 أيام على الأقل.",
  "messageEn": "You can freeze your subscription for up to 7 days. Freezing can start after at least 3 days."
}
```

---

## 4. الإجراءات التفاعلية للأزرار الأربعة (Quick Actions)

### 1. زر "ترقية التصنيف" (Upgrade Tier)
- **الخطوة 1:** جلب خيارات الترقية المتاحة:
  `GET /api/v1/customer/subscriptions/{id}/upgrade-options`
- **الخطوة 2:** يعرض التطبيق BottomSheet لاختيار الفئة المستهدفة (`Platinum` أو `Elite`) مع فرق السعر التناسبي (`priceDifference`).
- **الخطوة 3:** إرسال طلب الترقية وبدء الدفع:
  `POST /api/v1/customer/subscriptions/{id}/upgrade-requests`
  ```json
  {
    "targetTier": 1,
    "paymentMethodId": 2,
    "callbackUrl": "mealmate://payment-callback",
    "errorUrl": "mealmate://payment-error"
  }
  ```
- **الخطوة 4:** يرجع الـ Backend رابط دفع `paymentUrl` (ماي فاتورة). يفتح التطبيق صفحة الدفع عبر WebView.
- عند إتمام الدفع بنجاح:
  يتم تحديث الفئة إلى الفئة الجديدة فوراً، ويختفي التنبيه السفلي المعلق.

---

### 2. زر "تجميد" (Freeze)
- **قاعدة مهلة المطبخ (Kitchen Lead Time Window):**
  - يتحكم الأدمن بالكامل في مهلة تحضير المطبخ (`startDelayDays` - القيمة الافتراضية 3 أيام أي 72 ساعة) من لوحة تحكم الأدمن.
  - يرسل الـ Backend تاريخ البداية الفعلي المسموح به في حقل:
    `subscription.actions.earliestFreezeStartDate` (مثال: `"2026-09-06"`).
  - **مهم جداً لمطور فلاتر في الـ DateRangePicker:**
    يجب ضبط `firstDate` لمنتقي التواريخ على هذا التاريخ:
    ```dart
    final earliestDate = DateTime.parse(subscription.actions.earliestFreezeStartDate);
    final pickedRange = await showDateRangePicker(
      context: context,
      firstDate: earliestDate,
      lastDate: DateTime.parse(subscription.endDate),
    );
    ```
    > ⚠️ **تنبيه:** إذا حاول المستخدم اختيار تواريخ سابقة لهذا التاريخ أو بها وجبات جاري تحضيرها بالفعل في المطبخ، سيرجع الـ Backend خطأ:
    > `400 Bad Request` مع الكود `freeze.locked_days_cannot_be_frozen` والرسالة:
    > `"لا يمكن تجميد الأيام المقفولة لتحضير المطبخ (X يوم). أقرب تاريخ متاح لبدء التجميد هو YYYY-MM-DD."`
  - **الحد الأقصى للمدة:** محدد بحقل `actions.maximumFreezeDays` (افتراضياً 7 أيام متتالية).
- إرسال الطلب:
  `POST /api/v1/customer/subscriptions/{id}/freeze-requests`
  ```json
  {
    "startDate": "2026-09-06",
    "endDate": "2026-09-12",
    "reason": "سفر"
  }
  ```
- **النتيجة:** ترجع الاستجابة فوراً بـ `200 OK` مع حالة `Approved`، ويتم تمديد تاريخ انتهاء الاشتراك تلقائياً بمقدار 7 أيام. لا ينتظر موافقة أحد!

---

### 3. زر "تجديد" (Renew)
- إرسال طلب نية التجديد:
  `POST /api/v1/customer/subscriptions/{id}/renewal-requests`
- **Response `202 Accepted`:**
  يرجع تفاصيل الاشتراك مع تاريخ البدء المقترح (`EndDate + 1 day`).
- ينقل التطبيق العميل فوراً لشاشة الـ `Checkout` المعبأة مسبقاً بنفس الخيارات (البرنامج، الباقة، العنوان) لسرعة الدفع والتجديد.

---

### 4. زر "إلغاء" (Cancel)
- **قاعدة الاسترداد ومهلة المطبخ (Unified Kitchen Window):**
  - موحدة مع نفس مهلة المطبخ التي يحددها الأدمن (`startDelayDays` - 72 ساعة / 3 أيام افتراضياً).
  - طلبات المطبخ الواقعة خلال المهلة لا يمكن استرداد قيمتها لأن المطبخ بدأ بتجهيزها، وستصل للعميل كالمعتاد.
  - الأيام الواقعة بعد المهلة يتم استرداد قيمتها بالكامل تناسبياً (`DailyBoxPrice = PaidAmount / TotalDays`).
- **الخطوة 1 — جلب معاينة وحسبة الاسترداد (Cancellation Quote):**
  عند ضغط العميل على زر "إلغاء"، يستدعي التطبيق:
  `GET /api/v1/customer/subscriptions/{id}/cancellation-quote`
  
  **مثال الاستجابة `200 OK`:**
  ```json
  {
    "subscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
    "totalServiceDays": 12,
    "deliveredDays": 0,
    "lockedKitchenDays": 3,
    "refundableDays": 9,
    "dailyBoxPrice": 5.875,
    "refundAmount": 52.875,
    "currencyCode": "KWD",
    "effectiveCancellationDate": "2026-09-09",
    "policyMessageAr": "طلبات مهلة المطبخ (72 ساعة - 3 يوم) مؤكدة مع المطبخ وسيتم توصيلها، ويسترد ثمن باقي الأيام (9 يوم) بقيمة 52.875 KWD."
  }
  ```

- **الخطوة 2 — نافذة التأكيد (Confirmation Dialog):**
  يعرض التطبيق نافذة للعميل توضح:
  - الأيام المستردة: `9 أيام`
  - المبلغ المسترد: `52.875 د.ك`
  - نص التنبيه الديناميكي القادم من الـ Backend: `quote.policyMessageAr`
  - حقل اختياري لسبب الإلغاء (`reason`).

- **الخطوة 3 — تأكيد الإلغاء الفعلي:**
  عند ضغط العميل على "تأكيد الإلغاء":
  `POST /api/v1/customer/subscriptions/{id}/cancellation-requests`
  ```json
  {
    "reason": "سفر للخارج",
    "requestedEffectiveDate": null
  }
  ```
  **استجابة `200 OK`:**
  ```json
  {
    "requestId": "9c827384-d113-4672-8888-000000000055",
    "subscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
    "status": "CancellationPending",
    "statusNameAr": "إلغاء قيد المتابعة",
    "totalServiceDays": 12,
    "deliveredDays": 0,
    "lockedKitchenDays": 2,
    "refundableDays": 10,
    "dailyBoxPrice": 5.875,
    "refundAmount": 58.750,
    "currencyCode": "KWD",
    "effectiveCancellationDate": "2026-09-08",
    "messageAr": "تم تأكيد طلب الإلغاء. سيتم تسليم طلبات اليومين القادمين (2 يوم) واسترداد مبلغ 58.750 KWD لباقي الأيام (10 يوم) خلال 3-5 أيام عمل.",
    "timestampUtc": "2026-09-03T14:55:00Z"
  }
  ```
  *(ملاحظة: إذا لم تكن هناك أيام في نافذة المطبخ الـ 48 ساعة، تصبح الحالة `Cancelled` فوراً).*

---

### 5. زر "إدارة الاشتراك" (Manage Subscription CTA)
- ينقل العميل إلى شاشة الإدارة المتقدمة:
  `GET /api/v1/customer/subscriptions/{id}/management`

---

## 5. نماذج كود دارت الجاهزة (Flutter / Dart Models)

انسخ هذه النماذج مباشرة إلى مشروع فلاتر:

```dart
// customer_subscription_models.dart

class CustomerCurrentSubscriptionResponse {
  final CustomerSubscriptionSlim? subscription;
  final String? emptyStateCode;

  CustomerCurrentSubscriptionResponse({
    this.subscription,
    this.emptyStateCode,
  });

  factory CustomerCurrentSubscriptionResponse.fromJson(Map<String, dynamic> json) {
    return CustomerCurrentSubscriptionResponse(
      subscription: json['subscription'] != null
          ? CustomerSubscriptionSlim.fromJson(json['subscription'])
          : null,
      emptyStateCode: json['emptyStateCode'] as String?,
    );
  }
}

class CustomerSubscriptionSlim {
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
  final String nextRenewalDate;
  final int totalServiceDays;
  final int completedServiceDays;
  final int remainingServiceDays;
  final double progressPercentage;
  final double paidAmount;
  final String currencyCode;
  final CustomerRestaurant? restaurant;
  final CustomerPendingFreeze? pendingFreeze;
  final CustomerPendingUpgrade? pendingUpgrade;
  final CustomerSubscriptionActions actions;

  CustomerSubscriptionSlim({
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
    required this.nextRenewalDate,
    required this.totalServiceDays,
    required this.completedServiceDays,
    required this.remainingServiceDays,
    required this.progressPercentage,
    required this.paidAmount,
    required this.currencyCode,
    this.restaurant,
    this.pendingFreeze,
    this.pendingUpgrade,
    required this.actions,
  });

  factory CustomerSubscriptionSlim.fromJson(Map<String, dynamic> json) {
    return CustomerSubscriptionSlim(
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
      nextRenewalDate: json['nextRenewalDate'] as String,
      totalServiceDays: json['totalServiceDays'] as int,
      completedServiceDays: json['completedServiceDays'] as int,
      remainingServiceDays: json['remainingServiceDays'] as int,
      progressPercentage: (json['progressPercentage'] as num).toDouble(),
      paidAmount: (json['paidAmount'] as num).toDouble(),
      currencyCode: json['currencyCode'] as String,
      restaurant: json['restaurant'] != null ? CustomerRestaurant.fromJson(json['restaurant']) : null,
      pendingFreeze: json['pendingFreeze'] != null ? CustomerPendingFreeze.fromJson(json['pendingFreeze']) : null,
      pendingUpgrade: json['pendingUpgrade'] != null ? CustomerPendingUpgrade.fromJson(json['pendingUpgrade']) : null,
      actions: CustomerSubscriptionActions.fromJson(json['actions']),
    );
  }
}

class CustomerPendingUpgrade {
  final String requestId;
  final String fromTier;
  final String toTier;
  final double priceDifference;
  final String status;

  CustomerPendingUpgrade({
    required this.requestId,
    required this.fromTier,
    required this.toTier,
    required this.priceDifference,
    required this.status,
  });

  factory CustomerPendingUpgrade.fromJson(Map<String, dynamic> json) {
    return CustomerPendingUpgrade(
      requestId: json['requestId'] as String,
      fromTier: json['fromTier'] as String,
      toTier: json['toTier'] as String,
      priceDifference: (json['priceDifference'] as num).toDouble(),
      status: json['status'] as String,
    );
  }
}

class CustomerRestaurant {
  final String restaurantId;
  final String nameAr;
  final String nameEn;
  final String? logoUrl;

  CustomerRestaurant({
    required this.restaurantId,
    required this.nameAr,
    required this.nameEn,
    this.logoUrl,
  });

  factory CustomerRestaurant.fromJson(Map<String, dynamic> json) {
    return CustomerRestaurant(
      restaurantId: json['restaurantId'] as String,
      nameAr: json['nameAr'] as String,
      nameEn: json['nameEn'] as String,
      logoUrl: json['logoUrl'] as String?,
    );
  }
}

class CustomerSubscriptionActions {
  final bool canRenew;
  final bool canFreeze;
  final bool canCancel;
  final bool canUpgrade;
  final String? renewUnavailableReason;
  final String? freezeUnavailableReason;
  final String? cancelUnavailableReason;
  final String? upgradeUnavailableReason;
  final int freezeStartDelayDays;
  final int maximumFreezeDays;
  final bool freezeExtendsSubscriptionEnd;
  final String earliestFreezeStartDate;
  final String renewalEndpoint;
  final String freezeEndpoint;
  final String cancellationEndpoint;
  final String upgradeEndpoint;
  final String? cancellationQuoteEndpoint;

  CustomerSubscriptionActions({
    required this.canRenew,
    required this.canFreeze,
    required this.canCancel,
    required this.canUpgrade,
    this.renewUnavailableReason,
    this.freezeUnavailableReason,
    this.cancelUnavailableReason,
    this.upgradeUnavailableReason,
    required this.freezeStartDelayDays,
    required this.maximumFreezeDays,
    required this.freezeExtendsSubscriptionEnd,
    required this.earliestFreezeStartDate,
    required this.renewalEndpoint,
    required this.freezeEndpoint,
    required this.cancellationEndpoint,
    required this.upgradeEndpoint,
    this.cancellationQuoteEndpoint,
  });

  factory CustomerSubscriptionActions.fromJson(Map<String, dynamic> json) {
    return CustomerSubscriptionActions(
      canRenew: json['canRenew'] as bool,
      canFreeze: json['canFreeze'] as bool,
      canCancel: json['canCancel'] as bool,
      canUpgrade: json['canUpgrade'] as bool,
      renewUnavailableReason: json['renewUnavailableReason'] as String?,
      freezeUnavailableReason: json['freezeUnavailableReason'] as String?,
      cancelUnavailableReason: json['cancelUnavailableReason'] as String?,
      upgradeUnavailableReason: json['upgradeUnavailableReason'] as String?,
      freezeStartDelayDays: json['freezeStartDelayDays'] as int,
      maximumFreezeDays: json['maximumFreezeDays'] as int,
      freezeExtendsSubscriptionEnd: json['freezeExtendsSubscriptionEnd'] as bool,
      earliestFreezeStartDate: json['earliestFreezeStartDate'] as String? ?? '',
      renewalEndpoint: json['renewalEndpoint'] as String,
      freezeEndpoint: json['freezeEndpoint'] as String,
      cancellationEndpoint: json['cancellationEndpoint'] as String,
      upgradeEndpoint: json['upgradeEndpoint'] as String,
      cancellationQuoteEndpoint: json['cancellationQuoteEndpoint'] as String?,
    );
  }
}

class CustomerPendingFreeze {
  final String requestId;
  final String startDate;
  final String endDate;
  final int freezeDays;
  final String status;

  CustomerPendingFreeze({
    required this.requestId,
    required this.startDate,
    required this.endDate,
    required this.freezeDays,
    required this.status,
  });

  factory CustomerPendingFreeze.fromJson(Map<String, dynamic> json) {
    return CustomerPendingFreeze(
      requestId: json['requestId'] as String,
      startDate: json['startDate'] as String,
      endDate: json['endDate'] as String,
      freezeDays: json['freezeDays'] as int,
      status: json['status'] as String,
    );
  }
}

class CustomerInvoiceItem {
  final String receiptId;
  final String? invoiceNumber;
  final String? paymentId;
  final DateTime issuedAtUtc;
  final double amount;
  final String currencyCode;
  final String status;
  final String statusNameAr;
  final String gateway;

  CustomerInvoiceItem({
    required this.receiptId,
    this.invoiceNumber,
    this.paymentId,
    required this.issuedAtUtc,
    required this.amount,
    required this.currencyCode,
    required this.status,
    required this.statusNameAr,
    required this.gateway,
  });

  factory CustomerInvoiceItem.fromJson(Map<String, dynamic> json) {
    return CustomerInvoiceItem(
      receiptId: json['receiptId'] as String,
      invoiceNumber: json['invoiceNumber'] as String?,
      paymentId: json['paymentId'] as String?,
      issuedAtUtc: DateTime.parse(json['issuedAtUtc'] as String),
      amount: (json['amount'] as num).toDouble(),
      currencyCode: json['currencyCode'] as String,
      status: json['status'] as String,
      statusNameAr: json['statusNameAr'] as String,
      gateway: json['gateway'] as String,
    );
  }
}

class CustomerFreezePolicy {
  final int defaultDurationDays;
  final int startDelayDays;
  final bool extendSubscriptionEnd;
  final String earliestAllowedStartDate;
  final String messageAr;
  final String messageEn;

  CustomerFreezePolicy({
    required this.defaultDurationDays,
    required this.startDelayDays,
    required this.extendSubscriptionEnd,
    required this.earliestAllowedStartDate,
    required this.messageAr,
    required this.messageEn,
  });

  factory CustomerFreezePolicy.fromJson(Map<String, dynamic> json) {
    return CustomerFreezePolicy(
      defaultDurationDays: json['defaultDurationDays'] as int,
      startDelayDays: json['startDelayDays'] as int,
      extendSubscriptionEnd: json['extendSubscriptionEnd'] as bool,
      earliestAllowedStartDate: json['earliestAllowedStartDate'] as String? ?? '',
      messageAr: json['messageAr'] as String,
      messageEn: json['messageEn'] as String,
    );
  }
}

class CustomerCancellationQuote {
  final String subscriptionId;
  final int totalServiceDays;
  final int deliveredDays;
  final int lockedKitchenDays;
  final int refundableDays;
  final double dailyBoxPrice;
  final double refundAmount;
  final String currencyCode;
  final String effectiveCancellationDate;
  final String policyMessageAr;

  CustomerCancellationQuote({
    required this.subscriptionId,
    required this.totalServiceDays,
    required this.deliveredDays,
    required this.lockedKitchenDays,
    required this.refundableDays,
    required this.dailyBoxPrice,
    required this.refundAmount,
    required this.currencyCode,
    required this.effectiveCancellationDate,
    required this.policyMessageAr,
  });

  factory CustomerCancellationQuote.fromJson(Map<String, dynamic> json) {
    return CustomerCancellationQuote(
      subscriptionId: json['subscriptionId'] as String,
      totalServiceDays: json['totalServiceDays'] as int,
      deliveredDays: json['deliveredDays'] as int,
      lockedKitchenDays: json['lockedKitchenDays'] as int,
      refundableDays: json['refundableDays'] as int,
      dailyBoxPrice: (json['dailyBoxPrice'] as num).toDouble(),
      refundAmount: (json['refundAmount'] as num).toDouble(),
      currencyCode: json['currencyCode'] as String,
      effectiveCancellationDate: json['effectiveCancellationDate'] as String,
      policyMessageAr: json['policyMessageAr'] as String,
    );
  }
}

class CustomerCancellationResult {
  final String requestId;
  final String subscriptionId;
  final String status;
  final String statusNameAr;
  final int totalServiceDays;
  final int deliveredDays;
  final int lockedKitchenDays;
  final int refundableDays;
  final double dailyBoxPrice;
  final double refundAmount;
  final String currencyCode;
  final String? effectiveCancellationDate;
  final String messageAr;
  final DateTime timestampUtc;

  CustomerCancellationResult({
    required this.requestId,
    required this.subscriptionId,
    required this.status,
    required this.statusNameAr,
    required this.totalServiceDays,
    required this.deliveredDays,
    required this.lockedKitchenDays,
    required this.refundableDays,
    required this.dailyBoxPrice,
    required this.refundAmount,
    required this.currencyCode,
    this.effectiveCancellationDate,
    required this.messageAr,
    required this.timestampUtc,
  });

  factory CustomerCancellationResult.fromJson(Map<String, dynamic> json) {
    return CustomerCancellationResult(
      requestId: json['requestId'] as String,
      subscriptionId: json['subscriptionId'] as String,
      status: json['status'] as String,
      statusNameAr: json['statusNameAr'] as String,
      totalServiceDays: json['totalServiceDays'] as int,
      deliveredDays: json['deliveredDays'] as int,
      lockedKitchenDays: json['lockedKitchenDays'] as int,
      refundableDays: json['refundableDays'] as int,
      dailyBoxPrice: (json['dailyBoxPrice'] as num).toDouble(),
      refundAmount: (json['refundAmount'] as num).toDouble(),
      currencyCode: json['currencyCode'] as String,
      effectiveCancellationDate: json['effectiveCancellationDate'] as String?,
      messageAr: json['messageAr'] as String,
      timestampUtc: DateTime.parse(json['timestampUtc'] as String),
    );
  }
}

