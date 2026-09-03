# دليل التعديلات الجديدة لمطور الفلاتر — التجميد، الإلغاء، وتوحيد مهلة المطبخ

> **تاريخ التحديث:** سبتمبر 2026  
> **الهدف:** توضيح التعديلات الجديدة فقط المتعلقة بحل خطأ تجميد الأيام المقفولة (`freeze.locked_days_cannot_be_frozen`)، وتوحيد مهلة طلبات المطبخ عبر إعدادات الأدمن.

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
- **الحل الجذري المضاف:** أصبح الـ Backend يرسل حقل **`earliestFreezeStartDate`** ليعرف التطبيق بدقة ما هو **أول تاريخ مسموح باختياره**.

---

## 2. الحقول الجديدة المضافة للـ API (New Fields)

### أ. في كائن الـ `actions` داخل تفاصيل الاشتراك:
- **Endpoints:**
  - `GET /api/v1/customer/subscriptions/current`
  - `GET /api/v1/customer/subscriptions/{id}/management`
- **الحقل الجديد:**
  ```json
  "actions": {
    "canFreeze": true,
    "freezeStartDelayDays": 3,
    "maximumFreezeDays": 7,
    "freezeExtendsSubscriptionEnd": true,
    "earliestFreezeStartDate": "2026-09-06", // 👈 الجديد: أول تاريخ متاح لبدء التجميد (YYYY-MM-DD)
    "freezeEndpoint": "/api/v1/customer/subscriptions/{id}/freeze-requests"
  }
  ```

### ب. في endpoint استعلام سياسة التجميد:
- **Endpoint:** `GET /api/v1/customer/subscriptions/freeze-policy`
- **الحقل الجديد:**
  ```json
  {
    "defaultDurationDays": 7,
    "startDelayDays": 3,
    "extendSubscriptionEnd": true,
    "earliestAllowedStartDate": "2026-09-06", // 👈 الجديد: أول تاريخ مسموح به
    "messageAr": "يمكن تجميد الاشتراك لمدة حتى 7 أيام، ويبدأ التجميد بعد 3 أيام على الأقل من تاريخ 2026-09-06.",
    "messageEn": "You can freeze your subscription for up to 7 days. Freezing can start after at least 3 days from 2026-09-06."
  }
  ```

---

## 3. كود الفلاتر المطلوب لتفادي الخطأ (UI Implementation)

عند فتح منتقي التواريخ (`DateRangePicker`) لطلب التجميد، قم بتمرير `earliestFreezeStartDate` كـ `firstDate`:

```dart
Future<void> onFreezeButtonPressed(BuildContext context, CustomerManagedSubscription subscription) async {
  // 1. استخراج أول تاريخ مسموح به من الاستجابة مباشرة
  final earliestDate = DateTime.parse(subscription.actions.earliestFreezeStartDate);
  final subscriptionEnd = DateTime.parse(subscription.endDate);

  // 2. فتح منتقي التواريخ مقيداً بالتاريخ الصحيح لمنع اختيار الأيام المقفولة
  final pickedRange = await showDateRangePicker(
    context: context,
    firstDate: earliestDate, // 👈 يمنع العميل نهائياً من اختيار أي يوم مقفول للمطبخ
    lastDate: subscriptionEnd,
    helpText: 'اختر فترة التجميد (بحد أقصى ${subscription.actions.maximumFreezeDays} أيام)',
  );

  if (pickedRange == null) return;

  // 3. التحقق من الحد الأقصى للأيام
  final selectedDays = pickedRange.end.difference(pickedRange.start).inDays + 1;
  if (selectedDays > subscription.actions.maximumFreezeDays) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('الحد الأقصى للتجميد هو ${subscription.actions.maximumFreezeDays} أيام متتالية')),
    );
    return;
  }

  // 4. إرسال الطلب (POST)
  await submitFreezeRequest(
    endpoint: subscription.actions.freezeEndpoint,
    startDate: DateFormat('yyyy-MM-dd').format(pickedRange.start),
    endDate: DateFormat('yyyy-MM-dd').format(pickedRange.end),
  );
}
```

---

## 4. توحيد مهلة طلبات المطبخ مع إعدادات الأدمن

- **سابقاً:** كانت هناك ازدواجية (72 ساعة للتجميد و 48 ساعة للإلغاء).
- **الآن:** تم **التوحيد الكامل**؛ كلاهما يتبع إعداد مهلة المطبخ المحدد من قِبل الأدمن في لوحة التحكم (`startDelayDays` - افتراضياً 3 أيام / 72 ساعة).
- **في الإلغاء (`GET /api/v1/customer/subscriptions/{id}/cancellation-quote`):**
  - الرسالة ترجع ديناميكياً بحسب إعداد الأدمن:
    ```json
    {
      "lockedKitchenDays": 3,
      "refundableDays": 9,
      "policyMessageAr": "طلبات مهلة المطبخ (72 ساعة - 3 يوم) مؤكدة مع المطبخ وسيتم توصيلها، ويسترد ثمن باقي الأيام (9 يوم) بقيمة 52.875 KWD."
    }
    ```
  - لا تقم بتثبيت نص "48 ساعة" في التطبيق؛ بل اعرض `policyMessageAr` القادم من الـ API مباشرة في نافذة التأكيد.

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
  final String earliestFreezeStartDate; // 👈 الجديد
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
      earliestFreezeStartDate: json['earliestFreezeStartDate'] as String? ?? '', // 👈
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
  final String earliestAllowedStartDate; // 👈 الجديد
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
      earliestAllowedStartDate: json['earliestAllowedStartDate'] as String? ?? '', // 👈
      messageAr: json['messageAr'] as String,
      messageEn: json['messageEn'] as String,
    );
  }
}
```
