# دليل التحميل المتوازي لشاشة الباقة والمدة والتصنيف (فلو العائلة)

> **يدعم كلا المسارين بنفس المرونة:**
> - `https://maelmate.runasp.net/api/v1/customer/family-subscription-flow`
> - `https://maelmate.runasp.net/api/v1/customer/subscription-flow`

---

## 1. الفكرة والهدف
في شاشة اختيار خطة فرد العائلة، تحتاج الشاشة لعرض:
1. قائمة **الباقات** (Bundles).
2. قائمة **المدد** (Durations).
3. قائمة **التصنيفات** ومطاعمها مع الأسعار (Tiers).

**سابقاً:** كان طلب المدد والتصنيفات يُلزم وجود `bundleId` في الـ URL و `durationDays` لحساب السعر، مما يضطر الموبايل لانتظار تحميل واختيار الباقة ثم المدة أولاً.  
**الآن:** الـ 3 طلبات أصبحت **مستقلة تماماً على مستوى البرنامج (`programId`)**:
- لا يشترط إرسال `bundleId` (يتم اعتماد أول باقة نشطة افتراضياً).
- لا يشترط إرسال `durationDays` في فلو العائلة (يتم حساب الأسعار تلقائياً على المدة الأساسية الافتراضية 26 يوماً).
- يمكن للموبايل إطلاق الـ 3 طلبات **معاً بالتوازي (Parallel)** بمجرد فتح الشاشة.

---

## 2. الـ Endpoints الثلاثة الموازية

### أ) طلب الباقات (Bundles)
```http
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles
```
*(أو عبر `/api/v1/customer/subscription-flow/programs/{programId}/bundles`)*
- **المعاملات المطلوبة:** `programId` فقط في المسار.

---

### ب) طلب المدد (Durations)
```http
GET /api/v1/customer/family-subscription-flow/programs/{programId}/durations?includeFriday=false
```
*(أو عبر `/api/v1/customer/subscription-flow/programs/{programId}/durations?includeFriday=false`)*
- **بدون باقة في المسار!** يتم تحميل المدد فوراً بدون الحاجة لاختيار باقة مسبقاً.
- **المعاملات:**
  - `programId` (إلزامي في المسار).
  - `includeFriday` (اختياري، الافتراضي `false`).
  - `bundleId` (اختياري): إن رغبت بتمرير باقة محددة لاحقاً كـ query param أو كمسار `/bundles/{bundleId}/durations`.

---

### ج) طلب التصنيفات (Tiers)
```http
GET /api/v1/customer/family-subscription-flow/programs/{programId}/tiers?familyMemberId={memberId}&includeFriday=false
```
*(أو عبر `/api/v1/customer/subscription-flow/programs/{programId}/tiers?familyMemberId={memberId}&includeFriday=false`)*
- **بدون باقة في المسار!**
- **بدون إجبار على إرسال `durationDays`!**
  - في فلو العائلة (`familyMemberId`)، إن لم ترسل `durationDays`، يقوم السيرفر تلقائياً بحساب الأسعار وعرضها بالاعتماد على المدة الافتراضية (26 يوماً)، فترجع كروت التصنيفات محملة بالأسعار والمطاعم جاهزة للعرض فوراً!
- **المعاملات:**
  - `programId` (إلزامي في المسار).
  - `familyMemberId` (إلزامي لفحص منطقة توصيل الفرد وأهليته).
  - `includeFriday` (اختياري، الافتراضي `false`).
  - `durationDays` (اختياري): إذا ضغط المستخدم على مدة أخرى (مثلاً 12 يوم)، ترسل `durationDays=12` لتحديث الأسعار لتلك المدة.

---

## 3. مثال كود الموبايل (Flutter / Dart)

عند فتح الشاشة، اطلب الـ 3 Endpoints معاً عبر `Future.wait`:

```dart
Future<void> loadScreenData({
  required String programId,
  required String familyMemberId,
}) async {
  setState(() => isLoading = true);

  final results = await Future.wait([
    // 1. الباقات
    dio.get('/api/v1/customer/family-subscription-flow/programs/$programId/bundles'),

    // 2. المدد
    dio.get('/api/v1/customer/family-subscription-flow/programs/$programId/durations?includeFriday=false'),

    // 3. التصنيفات
    dio.get('/api/v1/customer/family-subscription-flow/programs/$programId/tiers?familyMemberId=$familyMemberId&includeFriday=false'),
  ]);

  final bundles = results[0].data;
  final durations = results[1].data;
  final tiers = results[2].data;

  setState(() {
    isLoading = false;
    // عرض البيانات في الشاشة مباشرة
  });
}
```

---

## 4. ماذا تفعل عند قيام المستخدم بتغيير الاختيارات؟
- **تغيير الجمعة (`includeFriday`):** أعد طلب `durations` و `tiers` بقيمة `includeFriday` الجديدة.
- **اختيار مدة محددة (`durationDays`):** أعد طلب `tiers` مع إرسال `durationDays={days}` لتحديث الأسعار المحسوبة لكل تصنيف.
- **اختيار باقة أخرى (`bundleId`):** أعد طلب `durations` و `tiers` مع تمرير `bundleId` المحدد.
