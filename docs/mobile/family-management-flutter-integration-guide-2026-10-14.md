# دليل تعديلات الموبايل (Flutter) — إدارة العائلة والتقويم (2026-10-14)

**الجمهور المستهدف:** فريق تطوير تطبيق العميل (Flutter / Mobile Team)  
**الموضوع:** حل مشكلات إدارة العائلة، فصل حساب المدير عن الأفراد، نقطة التبديل الجديدة (Switcher)، وحفظ وجبات التقويم الأساسي.

---

## 1. المشكلات التي تم حلها في الـ Backend

1. **ظهور المدير كفرد داخل إدارة العائلة:**  
   شاشات إدارة العائلة (`/family/home` و `/family/members`) مخصصة الآن حصرياً لإدارة **أفراد العائلة التابعين**. تم استبعاد حساب المدير من هذه القوائم لأن للمدير اشتراكه وتقويمه الأساسي المستقل في الصفحة الرئيسية للتطبيق.
2. **تكرار وجبات المدير عند الأفراد:**  
   كان التطبيق يقع في fallback لاستخدام معرّف اشتراك المدير عند فتح تقويم فرد اشتراكه مستقبلي أو معرّفه `null`. تم إطلاق endpoint جديد مخصص للتبديل (Switcher) يوفر معرّف الاشتراك الجاهز لكل شخص.
3. **عدم حفظ الوجبات في التقويم الأساسي للمدير:**  
   تم تفعيل **الربط التلقائي للمطعم من الوجبة** (`Auto-resolve restaurant from meal`). عند اختيار وجبة عبر `POST .../meals/slot`، يتعرف السيرفر تلقائياً على المطعم ويحفظه مع الوجبة حتى لو لم يتم استدعاء اختيار المطعم مسبقاً.

---

## 2. الـ Endpoint الجديد: Switcher (التبديل بين الحسابات)

عند الحاجة لعرض قائمة التبديل بين الحسابات (أفاتار المدير + أفراد العائلة) في شريط علوي (Chip / Switcher):

```http
GET /api/v1/customer/family/switcher
```
*(أو المسار البديل: `GET /api/v1/customer/family/members/switcher`)*

### مثال الاستجابة (Response):

```json
[
  {
    "memberId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "displayName": "أحمد (المدير)",
    "role": 1,
    "isManager": true,
    "memberSubscriptionId": "e6306e2e-ae7c-47a7-bb6a-38ca8a3be144",
    "upcomingSubscriptionId": null,
    "upcomingStartDate": null,
    "programNameAr": "متوازن",
    "programNameEn": "Balanced",
    "profileImageUrl": "https://api.trymealmate.com/api/v1/uploads/content?key=...",
    "isPaused": false,
    "canEditDailyMeals": true,
    "setupStatus": 3
  },
  {
    "memberId": "8b17a7e1-8812-4217-a068-07db1855e901",
    "displayName": "يوسف",
    "role": 2,
    "isManager": false,
    "memberSubscriptionId": "7c284fa2-9388-4444-99aa-881122334455",
    "upcomingSubscriptionId": null,
    "upcomingStartDate": null,
    "programNameAr": "متوازن",
    "programNameEn": "Balanced",
    "profileImageUrl": null,
    "isPaused": false,
    "canEditDailyMeals": true,
    "setupStatus": 3
  }
]
```

### مواصفات الحقول في الـ Switcher:

| الحقل | النوع | الوصف والاستخدام في Flutter |
|---|---|---|
| `memberId` | `Guid` | معرّف الفرد في العائلة |
| `displayName` | `String` | الاسم الظاهر |
| `role` | `int` | `1` = مدير (Manager) ، `2` = فرد تابع (Member) |
| `isManager` | `bool` | علامة صريحة: `true` لحساب المدير الحالي، `false` للأفراد |
| `memberSubscriptionId` | `Guid?` | **المعرّف الذي يُستخدم لفتح التقويم** (`/customer/subscriptions/{id}/calendar`). هذا المعرّف محلول مسبقاً (سواء كان حالياً أو اشتراكاً قادماً). |
| `upcomingSubscriptionId` | `Guid?` | معرّف الاشتراك القادم إن وجد |
| `upcomingStartDate` | `DateOnly?` | تاريخ بدء الاشتراك القادم (YYYY-MM-DD) |
| `profileImageUrl` | `String?` | رابط الصورة الشخصية للأفاتار |
| `isPaused` | `bool` | هل الحساب موقوف مؤقتاً |
| `canEditDailyMeals` | `bool` | هل يملك صلاحية تعديل واختيار الوجبات |
| `setupStatus` | `int` | `1` NotStarted · `2` InProgress · `3` Ready |

---

## 3. تعديلات شاشات إدارة العائلة (Family Management)

### أ) شاشة قائمة الأفراد (`GET /api/v1/customer/family/members`) وشاشة الرئيسية (`GET /api/v1/customer/family/home`)
- **ما تغير في السيرفر:** القائمة تُرجع الآن **الأفراد فقط** (`isManager == false`). لن يظهر كارت المدير الفردي داخل إدارة العائلة.
- **المطلوب في Flutter:**
  - عرض كروت الأفراد التابعين فقط في شاشة إدارة الأفراد.
  - المدير يتابع تقويمه واشتراكه من شاشة التطبيق الرئيسية (Home Dashboard) عبر `GET /api/v1/customer/subscriptions/current`.

### ب) التعامل مع الاشتراكات الحالية والمستقبلية للأفراد:
- في `FamilyHomeMemberDto` و `FamilyMemberListItemDto`:
  - `memberSubscriptionId`: يكون `Guid` فقط إذا كان الاشتراك سارياً حالياً (`StartDate <= today`).
  - إذا كان الاشتراك مدفوعاً ومؤكداً لكن تاريخ بدئه في المستقبل، تكون قيمة `memberSubscriptionId` = `null` وتكون قيمة `upcomingSubscriptionId` = المعرّف الفعلي للاشتراك القادم.
- **قاعدة الأمان في كود Flutter:**
  ```dart
  // المعرف الصحيح لفتح شاشة التقويم أو الوجبات للفرد:
  final calendarSubId = member.memberSubscriptionId ?? member.upcomingSubscriptionId;
  ```
  > ⚠️ **تنبيه هام جداً:** ممنوع تماماً استخدام معرّف اشتراك المدير كـ Fallback عند عدم وجود اشتراك للفرد. إذا كان كلاهما `null`، فهذا يعني أن الفرد غير مشترك أو لم يكتمل اشتراكه، ويجب عرض حالة "غير مشترك" أو توجيهه للاشتراك.

---

## 4. التقويم واختيار الوجبات (Calendar & Meals Selection)

### أ) استدعاء اختيار الوجبة مباشرة:
```http
POST /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/meals/slot
Content-Type: application/json

{
  "mealId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "slot": "lunch"
}
```

- **سلوك السيرفر الجديد:**
  - إذا كان اليوم لم يتم اختيار مطعم له بعد (`restaurantId == null`):
    - يقوم السيرفر بالبحث عن المطعم التابع له الـ `mealId`.
    - يربط اليوم بهذا المطعم تلقائياً.
    - يحفظ اختيار المطعم في سجلات `DailyRestaurantSelection`.
    - يحفظ اختيار الوجبة في `DailyMealSelections`.
    - يُرجع اليوم بحالة `200 OK` محدثاً بالكامل.
  - لم يعد ظهور خطأ `DayUnavailable` عائقاً إذا كان العميل يختار وجبة من قائمة الوجبات المتاحة لليوم.

---

## 5. Dart Models المقترحة لـ Flutter

```dart
class FamilySwitcherItem {
  final String memberId;
  final String displayName;
  final int role;
  final bool isManager;
  final String? memberSubscriptionId;
  final String? upcomingSubscriptionId;
  final String? upcomingStartDate;
  final String? programNameAr;
  final String? programNameEn;
  final String? profileImageUrl;
  final bool isPaused;
  final bool canEditDailyMeals;
  final int setupStatus;

  FamilySwitcherItem({
    required this.memberId,
    required this.displayName,
    required this.role,
    required this.isManager,
    this.memberSubscriptionId,
    this.upcomingSubscriptionId,
    this.upcomingStartDate,
    this.programNameAr,
    this.programNameEn,
    this.profileImageUrl,
    required this.isPaused,
    required this.canEditDailyMeals,
    required this.setupStatus,
  });

  factory FamilySwitcherItem.fromJson(Map<String, dynamic> json) {
    return FamilySwitcherItem(
      memberId: json['memberId'] as String,
      displayName: json['displayName'] as String? ?? '',
      role: json['role'] as int? ?? 2,
      isManager: json['isManager'] as bool? ?? false,
      memberSubscriptionId: json['memberSubscriptionId'] as String?,
      upcomingSubscriptionId: json['upcomingSubscriptionId'] as String?,
      upcomingStartDate: json['upcomingStartDate'] as String?,
      programNameAr: json['programNameAr'] as String?,
      programNameEn: json['programNameEn'] as String?,
      profileImageUrl: json['profileImageUrl'] as String?,
      isPaused: json['isPaused'] as bool? ?? false,
      canEditDailyMeals: json['canEditDailyMeals'] as bool? ?? true,
      setupStatus: json['setupStatus'] as int? ?? 1,
    );
  }

  /// المعرف النهائي للاستخدام مع endpoints التقويم
  String? get targetSubscriptionId => memberSubscriptionId ?? upcomingSubscriptionId;
}
```

---

## 6. قائمة مراجعة سريعة (Checklist لمهندس الموبايل)

- [ ] استدعاء `GET /api/v1/customer/family/switcher` لبناء شريط اختيار/تبديل الأفراد والمدير.
- [ ] عند النقر على أي فرد في الـ Switcher، استخدام `item.targetSubscriptionId` لتحميل تقويم ذلك الفرد.
- [ ] إزالة أي كود يقوم بوضع معرّف المدير افتراضياً للفرد عند غياب اشتراك الفرد.
- [ ] التأكد من أن شاشة إدارة العائلة لا تعرض المدير كعضو قابل للإدارة، والاعتماد على التقويم الأساسي للمدير في الشاشة الرئيسية.
- [ ] عند اختيار وجبة في التقويم، إرسال `POST .../meals/slot` مباشرة مع الاعتماد على الحفظ التلقائي للمطعم في الباكيند.
