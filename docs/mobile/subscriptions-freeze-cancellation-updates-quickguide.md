# دليل التعديلات الجديدة وتنبيه التجميد لمطور الفلاتر

> **تاريخ التحديث:** سبتمبر 2026  
> **الهدف:** توضيح التعديلات الجديدة فقط المتعلقة بحل خطأ تجميد الأيام المقفولة (`freeze.locked_days_cannot_be_frozen`)، وتوحيد مهلة طلبات المطبخ، وعرض تنبيه توضيحي يشرح للعميل ما يحدث بدقة عند التجميد.

---

## 1. حل مشكلة خطأ التجميد (400 Bad Request)

### سبب الخطأ الذي ظهر في تطبيق الفلاتر:
```json
{
  "title": "راجع البيانات المدخلة",
  "status": 400,
  "detail": "لا يمكن تجميد الأيام المقفولة لتحضير المطبخ (X يوم). أقرب تاريخ متاح لبدء التجميد هو YYYY-MM-DD.",
  "code": "freeze.locked_days_cannot_be_frozen"
}
```
- **السبب:** منتقي التواريخ في التطبيق كان يسمح باختيار تاريخ اليوم أو الغد أو أيام بدأت المطابخ تجهيزها فعلياً (`Locked72h`).
- **الحل المضاف:** يرسل الـ Backend حقل **`earliestFreezeStartDate`** ليعرف التطبيق بدقة ما هو **أول تاريخ مسموح باختياره**.

---

## 2. الحقول الجديدة المضافة للـ API (New Fields)

### أ. في كائن الـ `actions` داخل تفاصيل الاشتراك:
- **Endpoints:**
  - `GET /api/v1/customer/subscriptions/current`
  - `GET /api/v1/customer/subscriptions/{id}/management`
- **الحقول الجديدة:**
  ```json
  "actions": {
    "canFreeze": true,
    "freezeStartDelayDays": 3,
    "maximumFreezeDays": 7,
    "freezeExtendsSubscriptionEnd": true,
    "earliestFreezeStartDate": "2026-09-06", // 👈 أول تاريخ متاح لبدء التجميد (YYYY-MM-DD)
    "freezeNoticeAr": "عند تجميد الاشتراك: سيتم تمديد تاريخ انتهاء اشتراكك تلقائياً بعدد أيام التجميد. طلبات مهلة المطبخ (72 ساعة القادمة) مؤكدة وسيتم تسليمها، ويتوقف توصيل الوجبات مؤقتاً خلال فترة التجميد ويُستأنف تلقائياً بعدها.", // 👈 رسالة التنبيه والشرح
    "freezeNoticeEn": "When freezing: Subscription end date will be extended automatically. Orders within the kitchen lead window (72h) are confirmed and delivered, and deliveries pause during freeze and resume automatically.",
    "freezeEndpoint": "/api/v1/customer/subscriptions/{id}/freeze-requests"
  }
  ```

### ب. في endpoint استعلام سياسة التجميد:
- **Endpoint:** `GET /api/v1/customer/subscriptions/freeze-policy`
- **الحقول الجديدة:**
  ```json
  {
    "defaultDurationDays": 7,
    "startDelayDays": 3,
    "extendSubscriptionEnd": true,
    "earliestAllowedStartDate": "2026-09-06",
    "messageAr": "يمكن تجميد الاشتراك لمدة حتى 7 أيام، ويبدأ التجميد بعد 3 أيام على الأقل من تاريخ 2026-09-06.",
    "messageEn": "You can freeze your subscription for up to 7 days. Freezing can start after at least 3 days from 2026-09-06.",
    "noticeAr": "عند تجميد الاشتراك: سيتم تمديد تاريخ انتهاء اشتراكك تلقائياً بعدد أيام التجميد. طلبات مهلة المطبخ (72 ساعة القادمة) مؤكدة وسيتم تسليمها كالمعتاد، ويتوقف التوصيل مؤقتاً أثناء فترة التجميد ويُستأنف تلقائياً بعدها.",
    "noticeEn": "When freezing: Subscription end date will be extended automatically. Orders within kitchen lead window (72h) are confirmed and delivered, and deliveries pause during freeze and resume automatically."
  }
  ```

---

## 3. رسالة التنبيه وشرح ما يحدث عند التجميد (What Happens on Freeze)

عندما يفتح العميل نافذة التجميد أو قبل تأكيد الطلب، يجب عرض بطاقة تنبيه (`Notice Box / Confirmation Dialog`) تشرح له النقاط التالية:

```
┌────────────────────────────────────────────────────────────────────────┐
│ ℹ️ تنبيه هام بخصوص تجميد الاشتراك:                                      │
│                                                                        │
│ 1. 📅 تمديد مدة الاشتراك:                                              │
│    سيتم تمديد تاريخ انتهاء اشتراكك تلقائياً بمقدار عدد أيام التجميد.   │
│                                                                        │
│ 2. 🍳 طلبات مهلة المطبخ (الـ 72 ساعة القادمة):                         │
│    الوجبات الواقعة ضمن مهلة المطبخ مؤكدة وجاري تجهيزها، وستصلك كالمعتاد.│
│                                                                        │
│ 3. ⏸️ الإيقاف المؤقت والاستئناف التلقائي:                               │
│    سيتوقف توصيل الوجبات أثناء فترة التجميد وتُستأنف تلقائياً فور        │
│    انتهاء المدة دون الحاجة لأي تدخل منك.                               │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 4. كود الفلاتر المكتمل (DateRangePicker + نافذة التنبيه والتأكيد)

```dart
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

Future<void> onFreezeButtonPressed(BuildContext context, CustomerManagedSubscription subscription) async {
  final earliestDate = DateTime.parse(subscription.actions.earliestFreezeStartDate);
  final subscriptionEnd = DateTime.parse(subscription.endDate);

  // 1. اختيار تواريخ التجميد مقيدة بأول تاريخ متاح
  final pickedRange = await showDateRangePicker(
    context: context,
    firstDate: earliestDate, // 👈 يمنع اختيار الأيام المقفولة للمطبخ
    lastDate: subscriptionEnd,
    helpText: 'اختر فترة التجميد (بحد أقصى ${subscription.actions.maximumFreezeDays} أيام)',
  );

  if (pickedRange == null) return;

  final freezeDays = pickedRange.end.difference(pickedRange.start).inDays + 1;
  if (freezeDays > subscription.actions.maximumFreezeDays) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('الحد الأقصى للتجميد هو ${subscription.actions.maximumFreezeDays} أيام متتالية')),
    );
    return;
  }

  // 2. عرض نافذة التأكيد مع الشرح والتنبيه
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (ctx) => AlertDialog(
      title: const Row(
        children: [
          Icon(Icons.info_outline, color: Colors.orange),
          SizedBox(width: 8),
          Text('تأكيد تجميد الاشتراك'),
        ],
      ),
      content: SingleChildScrollView(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('فترة التجميد: من ${DateFormat('yyyy-MM-dd').format(pickedRange.start)} إلى ${DateFormat('yyyy-MM-dd').format(pickedRange.end)} ($freezeDays أيام)'),
            const SizedBox(height: 12),
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: Colors.amber.shade50,
                borderRadius: BorderRadius.circular(8),
                border: Border.all(color: Colors.amber.shade300),
              ),
              child: Text(
                subscription.actions.freezeNoticeAr ??
                'عند تجميد الاشتراك: سيتم تمديد تاريخ انتهاء اشتراكك تلقائياً بعدد أيام التجميد، وستستمر الوجبات المؤكدة لمهلة المطبخ بالوصول كالمعتاد، وستتوقف الوجبات مؤقتاً أثناء التجميد وتُستأنف تلقائياً بعدها.',
                style: const TextStyle(fontSize: 13, color: Colors.black87, height: 1.4),
              ),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('إلغاء')),
        ElevatedButton(
          style: ElevatedButton.styleFrom(backgroundColor: Colors.orange, foregroundColor: Colors.white),
          onPressed: () => Navigator.pop(ctx, true),
          child: const Text('تأكيد التجميد'),
        ),
      ],
    ),
  );

  if (confirmed != true) return;

  // 3. إرسال الطلب للـ Backend
  await submitFreezeRequest(
    endpoint: subscription.actions.freezeEndpoint,
    startDate: DateFormat('yyyy-MM-dd').format(pickedRange.start),
    endDate: DateFormat('yyyy-MM-dd').format(pickedRange.end),
  );
}
```

---

## 5. نماذج Dart المحدثة (Ready to Copy)

### تحديث `CustomerSubscriptionActions`:
```dart
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
  final String? freezeNoticeAr; // 👈 نص التنبيه بالعربي
  final String? freezeNoticeEn; // 👈 نص التنبيه بالإنجليزي
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
    this.freezeNoticeAr,
    this.freezeNoticeEn,
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
      freezeNoticeAr: json['freezeNoticeAr'] as String?,
      freezeNoticeEn: json['freezeNoticeEn'] as String?,
      renewalEndpoint: json['renewalEndpoint'] as String,
      freezeEndpoint: json['freezeEndpoint'] as String,
      cancellationEndpoint: json['cancellationEndpoint'] as String,
      upgradeEndpoint: json['upgradeEndpoint'] as String,
      cancellationQuoteEndpoint: json['cancellationQuoteEndpoint'] as String?,
    );
  }
}
```

### تحديث `CustomerFreezePolicy`:
```dart
class CustomerFreezePolicy {
  final int defaultDurationDays;
  final int startDelayDays;
  final bool extendSubscriptionEnd;
  final String earliestAllowedStartDate;
  final String messageAr;
  final String messageEn;
  final String? noticeAr; // 👈 نص التنبيه
  final String? noticeEn;

  CustomerFreezePolicy({
    required this.defaultDurationDays,
    required this.startDelayDays,
    required this.extendSubscriptionEnd,
    required this.earliestAllowedStartDate,
    required this.messageAr,
    required this.messageEn,
    this.noticeAr,
    this.noticeEn,
  });

  factory CustomerFreezePolicy.fromJson(Map<String, dynamic> json) {
    return CustomerFreezePolicy(
      defaultDurationDays: json['defaultDurationDays'] as int,
      startDelayDays: json['startDelayDays'] as int,
      extendSubscriptionEnd: json['extendSubscriptionEnd'] as bool,
      earliestAllowedStartDate: json['earliestAllowedStartDate'] as String? ?? '',
      messageAr: json['messageAr'] as String,
      messageEn: json['messageEn'] as String,
      noticeAr: json['noticeAr'] as String?,
      noticeEn: json['noticeEn'] as String?,
    );
  }
}
```
