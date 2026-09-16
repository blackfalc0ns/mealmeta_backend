# مواصفات واعتمادات تدفق اشتراك وإدارة العائلة (Family Subscription Draft & Flow Specifications)

> **تاريخ التحديث:** 16 سبتمبر 2026  
> **الفريق المستهدف:** فريق تطوير الموبايل (Flutter Team) وفريق الـ Backend  
> **الهدف:** توثيق التعديلات الفنية النهائية على تدفق مسودة اشتراك العائلة (Family Draft Flow)، الإجابة على استفسارات فريق الموبايل الـ 15، وتحديد العقود الرسمية للمسارات وحالات الأفراد وسلة الدفع.

---

## 📌 خلاصة النقاط الـ 4 الحرجة (Top 4 Priorities)

1. **حفظ وإلغاء اختيار العضو للدفع (`isSelectedForCheckout`):**  
   لا يحتاج النظام إلى حقل أو Endpoint منفصل. معمارية النظام تعتمد على قائمة الأفراد المستمرة (**Roster-based Architecture**).  
   * **اختيار العضو وتضمينه في الدفع:** يتم بمجرد حفظ خطته عبر `PUT .../members/{memberId}/plan`.  
   * **إلغاء الاختيار وإفراغ السلة:** يتم بإرسال قيم فارغة (`null`) لنفس الـ Endpoint لتصفير الخطة وإخراج العضو فوراً من الحسابات المالية وسلة الدفع.
2. **الشكل النهائي لـ `Summary Response`:**  
   معتمد وموحد تحت نموذج `FamilyCheckoutSummaryDto`، متضمناً `familyName`، و`members`، و`selectedMember` (عند التمرير بـ `?memberId={id}`)، وتفاصيل وجبات كل فرد، وخصم العائلة، ومانع الدفع `checkoutBlockReason`.
3. **مسار لوحة إدارة العائلة (Family Manager):**  
   المسار المعتمد والنهائي في الـ Controller على السيرفر هو:  
   `/api/v1/customer/family` (وليس `family-manager`).
4. **تدفق إضافة اشتراك لاحقاً لعضو محفوظ (Add-on Flow):**  
   يستخدم نفس مسارات `family-subscription-flow`. الأعضاء ذوو الاشتراكات النشطة يتم استبعادهم تلقائياً من الفوترة. يرسل التطبيق الخطة للعضو الجديد فقط، وتُحسب نسبة الخصم بناءً على إجمالي العائلة، بينما تعرض الفاتورة سعر العضو الجديد فقط.

---

## 1. آلية اختيار «اشترك الآن» وحفظ الاختيار / الإلغاء

* **المفهوم والمعمارية:**  
  يعتمد النظام معمارية **Roster-based Architecture**؛ فالعضو المضاف للعائلة يكون مسجلاً في القائمة العامة (`Roster Only`).  
  المعيار المعتمد في الـ Backend لاعتبار العضو مؤهلاً للدفع (`isPayable`) هو امتلاكه لخطة صالحة للتسعير عبر المعادلة:
  $$\text{HasPricablePlan} = (\text{ProgramId} \ne \text{null} \land \text{BundleId} \ne \text{null} \land \text{SelectedTier} \ne \text{null} \land \text{DurationDays} > 0 \land \text{MemberSubscriptionId} == \text{null})$$
* **بدء الاشتراك وتضمين العضو في الفاتورة:**  
  بمجرد إرسال خطة العضو عبر:  
  `PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan`  
  يصبح العضو تلقائياً مؤهلاً للدفع ويظهر في الـ Summary والخصومات.
* **إلغاء الاختيار أو إزالة العضو من سلة الدفع:**  
  لا حاجة لحذف الفرد من العائلة؛ يكفي إرسال تصفير للخطة (`null`) لنفس الـ Endpoint (كما هو موضح في البند 2)، فيتحول العضو فوراً إلى `NotStarted` ويخرج من الفاتورة وسلة الدفع.

---

## 2. مسح أو تغيير الخطة (Plan Reset)

* **هل إرسال `null` يمسح الخطة؟**  
  **نعم، 100%.** الكلاس الداخلي `SetFamilyMemberPlanRequest` يتعامل مع الحقول كـ `Optional<T>`:
  * الحقل الذي لا يتم إرساله إطلاقاً في الـ JSON يحتفظ بقيمته الحالية.
  * الحقل الذي يُرسل بقيمة `null` صريحة يقوم السيرفر بمسحه وتصفيره في قاعدة البيانات.
* **طلب مسح الخطة وإلغاء العضو من السلة:**
  ```http
  PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan
  Content-Type: application/json
  Authorization: Bearer <token>

  {
    "programId": null,
    "bundleId": null,
    "tier": null,
    "durationDays": null
  }
  ```
  هذا الطلب يقوم بمسح خطة العضو بالكامل، ويعيد حالته فوراً إلى `SetupStatus: "NotStarted"`، ويستبعده مباشرة من الـ Quote وحسابات الدفع دون حذفه من سجل العائلة.

---

## 3. تعريف حالات العضو بدقة (`SetupStatus`)

القيم الرسمية والمعتمدة للـ Enum هي:

| الحالة (SetupStatus) | القيمة الرقمية | المعنى والجاهزية |
|---|:---:|---|
| **`NotStarted`** | `0` | عضو مسجل في العائلة فقط بدون أي بنود خطة (Unsubscribed / Roster Only). خارج سلة الدفع تماماً. |
| **`InProgress`** | `1` | تم اختيار جزء من الخطة، أو اكتملت الخطة (البرنامج + الباقة + التصنيف + المدة) ولكن ينقصه **عنوان التوصيل** (`deliveryAddressId`) أو الاسم/العمر. |
| **`Ready`** | `2` | تعني اكتمال المتطلبات الأساسية:<br>1. البرنامج (`programId`)<br>2. الباقة (`bundleId`)<br>3. تصنيف المطاعم (`selectedTier`)<br>4. مدة الاشتراك (`durationDays > 0`)<br>5. عنوان التوصيل المعتمد (`deliveryAddressId`)<br>6. الاسم والعمر. |

> **ملاحظة فحص الحساسيات:**  
> تُفحص الحساسيات في بوابة الدفع (`FamilyCheckoutGate`). إذا لم تكتمل لأحد المشتركين، يُرجع السيرفر في الملخص `canCheckout: false` مع كود المنع `checkoutBlockReason: "family.allergies_required"`.

* **هل `memberSubscriptionId != null` يعني دائمًا اشتراكًا نشطًا؟**  
  في تدفق اشتراك العائلة (`family-subscription-flow`)، وجود قيمة لـ `memberSubscriptionId` يعني أن العضو مدفوع مسبقاً ولديه اشتراك مفعل بالفعل، وبالتالي لا يمكن تعديل خطته من هذا التدفق (`family.plan_locked`). أما في شاشات `Family Manager` العامة، فيتم فحص صلاحية الاشتراك وتاريخ نهايته عبر حقل `remainingDays` وحالة `canRenew`.

---

## 4. الـ Response الخاص بـ Metadata وتوحيد الأسماء

* **الحقول الرسمية المعتمدة للعلاقات:**
  ```json
  {
    "code": "spouse",
    "nameAr": "زوج / زوجة",
    "nameEn": "Spouse",
    "forDependents": false
  }
  ```
* **Endpoints الـ Metadata:**  
  كلا الخيارين متاح ويعمل بالسيرفر للتوافق الكامل:
  1. `GET /api/v1/customer/family-subscription-flow/metadata` **(الموصى به):** يرجع باقات العائلة، قائمة العلاقات، والحد الأقصى للأفراد في طلب واحد موحد.
  2. `GET /api/v1/customer/family-subscription-flow/relationships`
  3. `GET /api/v1/customer/family-subscription-flow/packages`

---

## 5. إضافة عضو (Add Member)

**المسار:** `POST /api/v1/customer/family-subscription-flow/members`  
**نموذج الإرسال المعتمد:**
```json
{
  "displayName": "سارة أحمد",
  "relationshipCode": "spouse",
  "gender": 2,
  "ageYears": 28,
  "phone": "+96551111111",
  "deliveryAddressId": null,
  "occupation": "مهندسة"
}
```

* **الحقول الإجبارية:**
  * `displayName` (اسم العضو)
  * `relationshipCode` (رمز العلاقة مثل spouse, son, daughter, ...)
  * `ageYears` (العمر بين 1 و 120)
  * `gender` (`1` = ذكر، `2` = أنثى)
* **الحقول الاختيارية:**
  * `phone`: **اختياري.** إذا تم تركه فارغاً (أو أُدخل رقم المدير نفسه)، يعتبره السيرفر فرداً تابعاً (طفل / كبير سن) وينشئ له حساب ظل (`Shadow Dependent Account`) تلقائياً.
  * `deliveryAddressId`: اختياري عند الإضافة ويمكن ربطه لاحقاً في خطوة العناوين.
* **بخصوص `nutritionalGoalCode` و `activityLevel`:**  
  **غير مطلوبين نهائياً في تدفق الدفع.** تم إلغاؤهما من نموذج `FamilyMemberWriteDto` لتسريع الشاشات وتقليل خطوات الإدخال، وتُضبط تلقائياً بقيم افتراضية داخل السيرفر.

---

## 6. بيانات الحساب الموجود عند `found: true` في الـ Lookup

* عند استدعاء `POST /api/v1/customer/family-subscription-flow/members/lookup` وإرجاع `found: true`:
  * يقوم تطبيق الموبايل بتعبئة الاسم والعمر والنوع تلقائياً في الشاشة للمستخدم.
* **إمكانية التعديل:**  
  مسموح لمدير العائلة التعديل على البيانات عند إضافة الفرد، وإذا أرسل التطبيق قيماً معدلة في `displayName` أو `ageYears` فإن السيرفر **يعتمد القيم المرسلة من التطبيق**.

---

## 7 & 8. الشكل النهائي لـ Summary Response وتفاصيل الأفراد

**المسار:** `GET /api/v1/customer/family-subscription-flow/summary?memberId={memberId}`

```json
{
  "familyGroupId": "7f0a8d3e-91c2-4a55-8321-111111111111",
  "familyName": "عائلة أحمد",
  "selectedMemberId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "selectedMember": {
    "memberId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "displayName": "أحمد",
    "role": "Manager",
    "isManager": true,
    "roleAr": "مدير العائلة",
    "roleEn": "Family Manager",
    "setupStatus": "Ready",
    "isReady": true,
    "programId": "8fa85f64-5717-4562-b3fc-2c963f66afa6",
    "programNameAr": "البرنامج الصحي المتوازن",
    "programNameEn": "Balanced Healthy Program",
    "bundleId": "9fa85f64-5717-4562-b3fc-2c963f66afa6",
    "bundleNameAr": "باقة الغداء والعشاء",
    "bundleNameEn": "Lunch & Dinner Bundle",
    "selectedTier": 2,
    "tierNameAr": "مطاعم متميزة",
    "tierNameEn": "Premium Tier",
    "durationDays": 26,
    "durationLabelAr": "26 يوم",
    "durationLabelEn": "26 Days",
    "includeFriday": false,
    "breakfast": false,
    "lunch": true,
    "snack": false,
    "dinner": true,
    "mealsPerDay": 2,
    "breakfastCount": 0,
    "lunchCount": 26,
    "snackCount": 0,
    "dinnerCount": 26,
    "memberPrice": 120.000
  },
  "members": [
    {
      "memberId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "displayName": "أحمد",
      "role": "Manager",
      "relationshipCode": "self",
      "gender": 1,
      "ageYears": 35,
      "setupStatus": "Ready",
      "programId": "8fa85f64-5717-4562-b3fc-2c963f66afa6",
      "bundleId": "9fa85f64-5717-4562-b3fc-2c963f66afa6",
      "selectedTier": 2,
      "durationDays": 26,
      "includeFriday": false,
      "deliveryAddressId": "4fa85f64-5717-4562-b3fc-2c963f66afa6"
    }
  ],
  "subtotal": 240.000,
  "familyDiscountPercent": 0.15,
  "familyDiscountAmount": 36.000,
  "finalPrice": 204.000,
  "currencyCode": "KWD",
  "mealOverview": [
    {
      "memberId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "displayName": "أحمد",
      "role": "Manager",
      "isManager": true,
      "setupStatus": "Ready",
      "isReady": true,
      "breakfast": false,
      "lunch": true,
      "snack": false,
      "dinner": true,
      "mealsPerDay": 2
    }
  ],
  "canCheckout": true,
  "checkoutBlockReason": null,
  "checkoutEndpoint": "POST /api/v1/customer/family-subscription-flow/checkout",
  "managerPrepaid": false,
  "managerSubscriptionId": null,
  "newMembersCount": 2,
  "newMembersSubtotal": 240.000
}
```

> **ملاحظة شاشة الملخص:** لتحديث بيانات بطاقة الفرد المعروضة في أسفل الشاشة، يمرر التطبيق المعامل `?memberId={id}` ليتم تعبئة تفاصيل العضو بالكامل داخل كائن `selectedMember`.

---

## 9. معادلة الـ Payable Members والـ Ready

التمييز المطبق بدقة في منطق السيرفر:
* **`isPayable`:** اكتمال `programId + bundleId + tier + durationDays > 0`. يدخل الفرد في الفاتورة والخصم فور اكتمال خطته.
* **`isReady`:** اكتمال الخطة + وجود عنوان توصيل صالح (`deliveryAddressId`) + الاسم والعمر.
* **`canCheckout`:** تصبح `true` فقط إذا كان كل الأعضاء الـ Payable لديهم خطط مكتملة وعناوين صالحة وتمت تعبئة الحساسية لهم، مع عدم وجود أسباب حظر.

---

## 10. الحساسيات والمكونات غير المفضلة

* **الحساسيات (Allergies - إلزامية لاجتياز الدفع):**
  * `PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies`
  * في حال عدم وجود أي حساسية، يتم إرسال كود `NONE`:
    ```json
    {
      "allergies": [
        { "code": "NONE" }
      ]
    }
    ```
  * كود `"NONE"` مستقل ولا يجوز دمجه مع حساسيات أخرى (إذا أُرسل مع غيره يقوم السيرفر بحذفه واعتماد الحساسيات الفعلية فقط).
* **المكونات غير المفضلة (Disliked Ingredients - اختيارية وليست مانعاً للدفع):**
  * الـ Endpoints متوفرة:
    * `PUT /api/v1/customer/family-subscription-flow/members/{memberId}/disliked-ingredients`
    * `GET /api/v1/customer/family-subscription-flow/members/{memberId}/disliked-ingredients`
  * **اختيارية تماماً ولا تعطل `canCheckout` نهائياً**، وذلك لتسريع تجربة الدفع لمجموعة أفراد.

---

## 11. العناوين والتغطية (Addresses & Coverage)

* **المسارات المعتمدة:**
  * `GET  /api/v1/customer/family-subscription-flow/members/{memberId}/addresses`
  * `POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses`
  * `POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/preview-coverage`
  * `GET  /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/preview-coverage`
  * `POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/set-default`
* **الحقول التي تؤكد صلاحية العنوان للتوصيل:**
  في كائن `AddressCoveragePreviewDto`:
  * `canSetAsDefault: true`
  * `resolvedRegionId != null`
  * `conflictMessage == null`
  * قائمة `servingRestaurants` تحتوي على مطاعم تغطي المنطقة المختارة.

---

## 12. مسارات لوحة إدارة العائلة (Family Manager Endpoints)

المسار الفعلي المعتمد في الـ Controller على السيرفر هو:  
**`/api/v1/customer/family`** (وليس `family-manager`):

1. **الرئيسية:** `GET /api/v1/customer/family/home`  
   يرجع `FamilyHomeDto` (حالة العائلة، الأعضاء، جدول وجبات اليوم، واختصارات الإدارة).
2. **قائمة الأعضاء:** `GET /api/v1/customer/family/members`  
   يرجع قائمة ببيانات الأعضاء وحالة اشتراكهم والأيام المتبقية.
3. **تفاصيل عضو:** `GET /api/v1/customer/family/members/{memberId}`  
   يرجع `FamilyMemberDetailDto` متضمناً تفاصيل الاشتراك والبرنامج وتاريخ البدء والانتهاء.

---

## 13. الدفع وتأكيد العملية (Checkout & Holds)

* **المسار:** `POST /api/v1/customer/family-subscription-flow/checkout`
* **المعامل `correlationId`:**
  * اختياري (`optional`).
  * ليس إلزامياً أن يكون UUID v4؛ يمكن استخدام أي String فريد للطلب.
* **إعادة التنفيذ وتكرار الطلب (Idempotency):**
  * إذا كانت هناك عملية دفع جارية وحجوزات السعة لم تنتهِ بعد، فإن إعادة إرسال الطلب ترجع نفس رابط الدفع السابق (`paymentUrl`).
* **انتهاء مدة الحجز (`holdExpiresAtUtc`):**
  * في حال انتهت مدة الحجز (15 دقيقة)، يقوم السيرفر تلقائياً بإلغاء الحجوزات المنتهية، وإعادة حالة العائلة إلى مسودة (`Draft`)، وإنشاء جلسة دفع وحجز جديدة بالكامل عند إعادة المحاولة.
* **طريقة الدفع (`paymentMethodId`):**
  * لا يتم تثبيت القيمة على 1، بل يتم جلب الطرق المتاحة ديناميكياً عبر:  
    `GET /api/v1/customer/family-subscription-flow/payment-methods?amount={finalPrice}`  
    وتمرير الـ `paymentMethodId` الذي يختاره العميل في شاشة الدفع.

---

## 14. توحيد شكل أخطاء الـ API (RFC 7807 ProblemDetails)

تعتمد جميع الـ APIs معيار `ProblemDetails` الموحد:
```json
{
  "title": "Validation Failed",
  "status": 400,
  "detail": "Display name is required.",
  "code": "family.display_name_required"
}
```
* **كود الخطأ الثابت:** يوجد دائماً في الحقل `"code"`.
* **رسالة الخطأ المترجمة:** تأتي في `"detail"` بحسب ترويسة `Accept-Language` (`ar` أو `en`).

### جدول أكواد الأخطاء وحالات الـ HTTP:

| الحالة والوصف | كود الخطأ (`code`) | كود الـ HTTP |
|---|---|:---:|
| الفرد غير موجود في العائلة | `family.not_found` | **404** |
| رقم الهاتف مسجل بالفعل في عائلة أخرى | `family.already_in_family` | **409** |
| تجاوز الحد الأقصى المسموح به لأفراد الباقة | `family.max_members` | **409** |
| الفرد لديه اشتراك نشط ولا يمكن تعديل خطته هنا | `family.plan_locked` | **409** |
| خطة الفرد غير مكتملة (ينقصها بند) | `family.plan_incomplete` | **400** |
| اسم الفرد مطلوب | `family.display_name_required` | **400** |
| عمر الفرد غير صالح (خارج النطاق 1-120) | `family.age_invalid` | **400** |
| لا يوجد أي فرد محدد للاشتراك في السلة | `family.member_incomplete` | **422** |
| خطوة الحساسية مطلوبة لأحد الأفراد المحددين للدفع | `family.allergies_required` | **422** |
| العنوان مطلوب أو لا توجد تغطية مطاعم للعنوان | `family.address_required` / `restaurant.not_eligible` | **400** |

---

## 15. سيناريو إضافة عضو لاحقاً (Add-on Flow)

عند الضغط على **«اشترك الآن»** لعضو محفوظ بدون اشتراك (`Roster-only`) من داخل لوحة إدارة العائلة:
1. **نفس الـ Flow:** يتم استخدام نفس مسارات `family-subscription-flow` المعتمدة.
2. **تحديد أنه لعضو واحد:**  
   الأعضاء السابقون يملكون اشتراكات نشطة بالفعل (`memberSubscriptionId != null`) ويستبعدهم محرك الفوترة تلقائياً. يكتفي المدير بإرسال `PUT .../members/{memberId}/plan` للعضو الجديد المراد ضمه.
3. **حساب نسبة الخصم:**  
   السيرفر يحسب الخصم بناءً على:  
   $$\text{Total Family Size} = (\text{المشتركون النشطون الحاليون} + \text{العضو الجديد})$$  
   وبالتالي يستفيد العضو الجديد من نسبة خصم الحجم الكاملة للعائلة.
4. **قيمة الفاتورة في الـ Summary:**  
   تعرض **سعر العضو الجديد فقط** بعد تطبيق نسبة الخصم المستحقة، وليس إجمالي اشتراكات العائلة السابقة.
