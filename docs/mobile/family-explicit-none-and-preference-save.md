# حفظ الحساسية والتفضيلات واختيار لا يوجد

## الأكواد الصريحة

الحساسية: `NO_ALLERGIES` = «لا توجد حساسية» / `No allergies`.
التفضيلات: `NO_DIETARY_RESTRICTIONS` = «لا توجد قيود غذائية» / `No dietary restrictions`.

الأكواد عناصر تحكم في الاختيار وليست تعريفات حساسية أو وجبات. لا يخزن الباك صفًا وهميًا بهذه الأكواد؛ يمسح الاختيارات الفعلية للقسم ويسجل وقت اكتماله. `NONE` القديم والقائمة الفارغة الصريحة ما زالا مدعومين للتوافق.

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies
```

```json
{"selectedCodes":["NO_ALLERGIES"]}
```

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/dietary
```

```json
{"selectedCodes":["NO_DIETARY_RESTRICTIONS"]}
```

الرد الناجح يبقى `true` للتوافق. أرسل الطلب فور اختيار العنصر، دون زر تأكيد، ثم استخدم GET المقابل للتحقق أو استعادة الشاشة. الحقول `hasNoAllergies` و`hasNoDietaryRestriction` حقول قراءة؛ لا تعتمد على إرسالها وحدها للحفظ، واستخدم selectedCodes كما سبق.

## الاسترجاع بدون فقد حالة لا يوجد

```http
GET /api/v1/customer/family-subscription-flow/members/{memberId}/allergies
GET /api/v1/customer/family-subscription-flow/members/{memberId}/dietary
```

بعد حفظ لا توجد حساسية:

```json
{
  "selected": [],
  "selectedCodes": [],
  "completed": true,
  "hasNoAllergies": true,
  "noneOptionCode": "NO_ALLERGIES",
  "selectedOptionCodes": ["NO_ALLERGIES"]
}
```

بعد حفظ لا توجد قيود غذائية:

```json
{
  "selected": [],
  "selectedCodes": [],
  "completed": true,
  "hasNoDietaryRestriction": true,
  "noneOptionCode": "NO_DIETARY_RESTRICTIONS",
  "selectedOptionCodes": ["NO_DIETARY_RESTRICTIONS"]
}
```

JSON أعلاه يوضح الحقول ذات الصلة؛ قد يتضمن الرد حقولًا أخرى قائمة. selected وselectedCodes يبقيان التعريفات الفعلية فقط للتوافق. استخدم selectedOptionCodes لحالة تحديد عناصر الواجهة؛ عند وجود اختيارات فعلية يحتوي أكوادها، وعند عدم اكتمال القسم والقائمة فارغة يكون فارغًا، ولا يعتبر الباك ذلك إعلانًا بعدم وجود قيود.

`GET .../metadata` يتضمن `noAllergiesCode` و`noDietaryRestrictionsCode`. `GET .../current` يضيف لكل فرد `selectedAllergyOptionCodes` و`selectedDietaryOptionCodes`، إلى جانب flags والاختيارات القائمة. لا تسترجع تحديد فرد من كتالوج اختيارات حساب المدير؛ استخدم GET الفرد أو بطاقة ذلك الفرد في current.

## قواعد الاختيار والطلبات

- لا تجمع كود لا يوجد مع أكواد فعلية في نفس القسم. الباك يرفض الجمع بـ400 `family.preference_none_conflict` ويحافظ على البيانات المحفوظة.
- عند اختيار لا يوجد، امسح تحديد عناصر ذلك القسم في UI وأرسل كوده وحده. عند اختيار عنصر فعلي، ألغِ تحديد لا يوجد وأرسل القائمة الفعلية الكاملة.
- أرسل اسم حقل واحد: selectedCodes. تم إصلاح حالة selectedCodes=[] التي كانت تغطي على codes غير الفارغة، لكن إرسال حقول متنافسة ليس عقدًا موصى به.
- لا تمسح الأطعمة غير المرغوبة أو تفضيلات قسم آخر ضمن طلب الحساسية. تحديث لا يوجد يخص القسم المحدد فقط.
- نفذ queue مستقلة لكل فرد وقسم، وتخلص من نتائج الطلبات السابقة عند تبديل الفرد. الحفظ الفوري لا يعني إرسال PUT متزامنة قد تعكس ترتيب التغييرات.
- انتظر الحفظ قبل التنقل؛ لا تعرض الطلب الفاشل كمحفوظ. عند العودة أعد قراءة الحالة من السيرفر.

## أثناء الدفع المعلق

يمكن الآن حفظ الحساسية والتفضيلات والأطعمة غير المرغوبة لفرد مؤجل خارج المشاركين في الفاتورة المعلقة أو التي تحتاج مراجعة. لم يعد قفل الدفع يمنع الجميع لمجرد أنهم في العائلة نفسها.

إذا كان الفرد داخل فاتورة PaymentPending/ReviewRequired، يظل الحفظ مرفوضًا بـ409؛ التغييرات قد تؤثر على أهلية الوجبات المحجوزة. الحماية تشمل الفرد الذي يشارك ملف تفضيلات مع مشارك آخر في الفاتورة. يجب حسم المحاولة أو انتهاء حجز آمن وإعادة فحص الخطة قبل تعديل تلك القيود. لا تفسر هذا الرفض على أنه حفظ ناجح.

المستودع المتاح هو الباك؛ أضف نصي كل خيار إلى موارد Flutter بالعربية والإنجليزية، مع مفاتيح semantic مثل family.preferences.noAllergies وfamily.preferences.noDietaryRestrictions، واختبر تبديل اللغة وحالة الحفظ والخطأ.
