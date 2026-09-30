# دليل أرشيف الاشتراكات لتطبيق الموبايل (Flutter)
# Mobile Subscription Archive & Uncompleted Flows Guide

هذا المستند يحدد العقد النهائي والتوجيهات التقنية لشاشات أرشيف الاشتراكات (Archived Subscriptions) والمسودات غير المكتملة (Uncompleted Subscriptions) في تطبيق Flutter.

---

## 1. الفصل بين الأرشيف والمسودات غير المكتملة

تم فصل مسار الأرشيف عن المسودات غير المكتملة في السيرفر لضمان تجربة مستخدم واضحة:

| المسار (Endpoint) | الوظيفة | الحالات المعادة (Statuses) | الإجراءات المتاحة |
|---|---|---|---|
| `GET /api/v1/customer/subscriptions/archived` | الاشتراكات التاريخية المنتهية | `Completed`, `Expired`, `Cancelled`, `Upgraded` | عرض التفاصيل، إعادة الاشتراك، عرض الفاتورة |
| `GET /api/v1/customer/subscriptions/uncompleted` | المسودات والمدفوعات الجارية/المعلقة | `Draft`, `PendingPayment`, `PaymentFailed` | استكمال الاشتراك (Resume)، حذف المسودة (Delete) |

> [!IMPORTANT]
> تم إلغاء الاسم المستعار السابق `GET /api/v1/customer/subscriptions/archived` الذي كان يعيد المسودات.
> مسار `/archived` الآن يعيد فقط الاشتراكات التاريخية المكتملة أو الملغاة أو المنتهية.

---

## 2. عقد استجابة الأرشيف (`CustomerSubscriptionSlimDto`)

يعيد المسار `GET /api/v1/customer/subscriptions/archived` قائمة مصفوفة من العناصر بالهيكل التالي:

```json
[
  {
    "subscriptionId": "a1b2c3d4-0000-0000-0000-000000000001",
    "status": "Completed",
    "statusNameAr": "مكتمل",
    "programId": "b2c3d4e5-0000-0000-0000-000000000001",
    "programNameAr": "برنامج الكيتو المتوازن",
    "programNameEn": "Balanced Keto Program",
    "bundleId": "c3d4e5f6-0000-0000-0000-000000000001",
    "bundleNameAr": "باقة 3 وجبات",
    "bundleNameEn": "3 Meals Bundle",
    "tier": "Basic",
    "tierNameAr": "أساسي",
    "tierNameEn": "Basic",
    "tierLogoUrl": "https://api.mealmate.com/uploads/admin/tiers/basic.webp",
    "mealsPerDay": 3,
    "imageUrl": "https://api.mealmate.com/uploads/meals/hero.webp",
    "startDate": "2026-06-01",
    "endDate": "2026-06-26",
    "nextRenewalDate": "2026-06-27",
    "totalServiceDays": 26,
    "completedServiceDays": 26,
    "remainingServiceDays": 0,
    "progressPercentage": 100.0,
    "paidAmount": 80.000,
    "currencyCode": "KWD",
    "restaurant": {
      "restaurantId": "d4e5f6a7-0000-0000-0000-000000000001",
      "nameAr": "مطعم دايت سنتر",
      "nameEn": "Diet Center",
      "logoUrl": "https://api.mealmate.com/uploads/restaurant/logo.webp"
    },
    "actions": {
      "canRenew": true,
      "canFreeze": false,
      "canCancel": false,
      "canUpgrade": false,
      "renewUnavailableReason": null,
      "freezeUnavailableReason": "freeze_not_available_for_status",
      "cancelUnavailableReason": "cancellation_not_available_for_status",
      "upgradeUnavailableReason": "upgrade_not_available_for_status",
      "renewalEndpoint": "/api/v1/customer/subscriptions/a1b2c3d4-0000-0000-0000-000000000001/renewal-requests"
    },
    "kind": "INDIVIDUAL",
    "familyGroupId": null,
    "memberCount": 1,
    "familyName": null
  },
  {
    "subscriptionId": "e5f6a7b8-0000-0000-0000-000000000002",
    "status": "Completed",
    "statusNameAr": "مكتمل",
    "programId": "b2c3d4e5-0000-0000-0000-000000000001",
    "programNameAr": "برنامج الكيتو المتوازن",
    "programNameEn": "Balanced Keto Program",
    "bundleId": "c3d4e5f6-0000-0000-0000-000000000001",
    "bundleNameAr": "باقة 3 وجبات",
    "bundleNameEn": "3 Meals Bundle",
    "tier": "Basic",
    "tierNameAr": "أساسي",
    "tierNameEn": "Basic",
    "tierLogoUrl": "https://api.mealmate.com/uploads/admin/tiers/basic.webp",
    "mealsPerDay": 3,
    "imageUrl": "https://api.mealmate.com/uploads/meals/hero.webp",
    "startDate": "2026-07-01",
    "endDate": "2026-07-26",
    "nextRenewalDate": "2026-07-27",
    "totalServiceDays": 26,
    "completedServiceDays": 26,
    "remainingServiceDays": 0,
    "progressPercentage": 100.0,
    "paidAmount": 150.000,
    "currencyCode": "KWD",
    "kind": "FAMILY",
    "familyGroupId": "f7a8b9c0-0000-0000-0000-000000000001",
    "memberCount": 2,
    "familyName": "عائلة الأحمد"
  }
]
```

---

## 3. مواصفات واجهة المستخدم في Flutter (UI Guidelines)

### أ. التبويبات الثلاثة (Filter Tabs)
في شاشة الأرشيف، اعرض شريط تبويبات علوي (Filter Chips / TabBar):
1. **الكل (All)**: يعرض كل العناصر المستلمة من `GET /api/v1/customer/subscriptions/archived`.
2. **فردي (Individual)**: تصفية محلية للعناصر التي يكون فيها `kind == "INDIVIDUAL"`.
3. **عائلي (Family)**: تصفية محلية للعناصر التي يكون فيها `kind == "FAMILY"`.

### ب. تمييز البادج (Badge & Card Details)
- **اشتراك فردي**:
  - بادج بلون رمادي/هادئ: `اشتراك فردي`.
  - اسم العميل وعنوان التوصيل الفردي.
- **اشتراك عائلي**:
  - بادج بلون أزرق/مميز مع أيقونة العائلة: `اشتراك عائلي (${item.memberCount} أفراد)`.
  - اسم العائلة: `item.familyName` (مثال: "عائلة الأحمد").
  - السعر الإجمالي للباقة العائلية المعروضة: `item.paidAmount`.
  - يتم دمج اشتراكات الأفراد تلقائياً من السيرفر لكل دفعة اشتراك عائلية في بطاقة واحدة، ولا تتكرر بطاقات أفراد نفس العائلة.

### ج. قسم المسودات غير المكتملة
- تُعرض في شاشة مستقلة أو أسفل تبويب الاشتراكات تحت عنوان **"الاشتراكات غير المكتملة"**:
- تستدعي `GET /api/v1/customer/subscriptions/uncompleted`.
- إذا كانت المصفوفة فارغة `[]`، لا يظهر القسم.
- تحتوي كل مسودة على زري:
  - **متابعة الاشتراك**: يفتح شاشة الدفع أو ضبط العائلة حسب `actions.nextScreen` و `actions.resumeEndpoint`.
  - **إلغاء المسودة**: يرسل `DELETE /api/v1/customer/subscriptions/{id}` أو نقطة حذف مسودة العائلة لإلغاء الحجز فوراً وتحرير السعة.
