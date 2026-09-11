# دليل التحميل المتوازي لشاشة الباقة والمدة والتصنيف (فلو العائلة)

> **خاص بفلو العائلة فقط:** `https://maelmate.runasp.net/api/v1/customer/family-subscription-flow`

---

## 1. الفكرة والهدف
في شاشة اختيار خطة فرد العائلة، تحتاج الشاشة لعرض:
1. قائمة **الباقات** (Bundles).
2. قائمة **المدد** (Durations).
3. قائمة **التصنيفات** ومطاعمها (Tiers).

**سابقاً:** كان طلب المدد والتصنيفات يُلزم وجود `bundleId` و `durationDays`، مما يضطر الموبايل لانتظار تحميل الباقات واختيار واحدة أولاً ثم طلب المدد ثم التصنيفات.  
**الآن:** الـ 3 طلبات أصبحت **مستقلة تماماً على مستوى البرنامج (`programId`)**، ويمكن للموبايل إطلاقها **معاً بالتوازي (Parallel)** بمجرد فتح الشاشة.

---

## 2. الـ Endpoints الثلاثة

### أ) طلب الباقات (Bundles)
```http
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles
```
- **المعاملات المطلوبة:** `programId` في المسار.
- **الاستجابة:** قائمة كروت الباقات المتاحة للبرنامج.

---

### ب) طلب المدد (Durations)
```http
GET /api/v1/customer/family-subscription-flow/programs/{programId}/durations?includeFriday=false
```
- **المعاملات:**
  - `programId` (إلزامي في المسار).
  - `includeFriday` (اختياري، الافتراضي `false`).
  - `bundleId` (اختياري كـ query param): إن لم يُرسل، يعتمد الباك إند أول باقة نشطة تلقائياً.
- **مسار بديل عند اختيار باقة محددة:**
  ```http
  GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
  ```

---

### ج) طلب التصنيفات (Tiers)
```http
GET /api/v1/customer/family-subscription-flow/programs/{programId}/tiers?familyMemberId={memberId}&includeFriday=false
```
- **المعاملات:**
  - `programId` (إلزامي في المسار).
  - `familyMemberId` (إلزامي لفحص منطقة توصيل الفرد وأهليته).
  - `includeFriday` (اختياري).
  - `bundleId` (اختياري): إن لم يُرسل، يعتمد أول باقة نشطة تلقائياً.
  - `durationDays` (اختياري): إن لم يُرسل، ترجع كروت التصنيفات والمطاعم فوراً بدون أسعار محددة.
- **مسار بديل عند تحديد باقة ومدة لحساب الأسعار:**
  ```http
  GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=false&familyMemberId={memberId}
  ```

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
