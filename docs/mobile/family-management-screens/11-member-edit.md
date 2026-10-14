# 11 — بيانات الفرد الأساسية (M08.22)

**السابق:** [التفاصيل](./03-member-detail.md) أو [الأفراد](./02-members.md) · **التالي:** ارجع للتفاصيل بعد نجاح الحفظ.

## API وDTO الدقيق

قراءة المسودة/الأسرة: `GET /api/v1/customer/family-subscription-flow/current`. قراءة إدارة الفرد: `GET /api/v1/customer/family/members/{memberId}`. الكتابة: `PUT /api/v1/customer/family-subscription-flow/members/{memberId}`، جسم `FamilyMemberWriteDto`:

| الحقل | النوع/السلوك |
|---|---|
| `displayName` | string مطلوب، لا يكون فارغاً |
| `relationshipCode` | string مطلوب من relationship catalog: `self`, `spouse`, `father`, `mother`, `child`, `sibling`, `friend`, `parent`, `other` |
| `gender` | enum `FamilyMemberGender` كما في catalog/العقد |
| `ageYears` | int من 1 إلى 120؛ لا DOB |
| `deliveryAddressId` | GUID nullable positional؛ استخدم عنواناً صالحاً من قائمة العناوين |
| `phone` | string اختياري |
| `occupation` | string اختياري؛ null يترك الموجود كما هو، وقيمة فارغة تمسحه |

```json
{"displayName":"عضو توضيحي","relationshipCode":"child","gender":"Female","ageYears":12,"deliveryAddressId":null,"phone":null,"occupation":null}
```

JSON توضيحي اصطناعي. `deliveryAddressId:null` أو empty GUID لا يمسح العنوان الحالي في handler؛ لا تقدم زر مسح موهوماً بهذه القيمة. قبل تغيير العنوان افتح قائمة عناوين العضو: `GET /api/v1/customer/family-subscription-flow/members/{memberId}/addresses`، ثم أرسل ID صالحاً. `phone` الفارغ لا يمسح الرقم الحالي؛ `occupation:null` يحافظ عليه بينما string فارغ يمسحه.

هذا PUT لا يحتوي nutritional goal أو activity level ولا program/bundle/tier/duration/includeFriday؛ هذه ليست حقول شاشة M08.22. لذلك يحافظ الخادم على الهدف ومستوى النشاط الموجودين. لا تضف مفاتيح من شاشة checkout لهذا DTO.

## تفضيلات الطعام المنفصلة

كل منها GET وPUT منفصل:

- Allergies: `GET/PUT /api/v1/customer/family-subscription-flow/members/{id}/allergies`. request الحقول المدعومة تشمل `allergies` أو `selected` عناصر، و`selectedCodes`/`codes` حسب التكامل. response: `selected`, `completed`, `selectedCodes`, `hasNoAllergies`, `noneOptionCode`, `selectedOptionCodes`.
- Disliked ingredients: `GET/PUT .../members/{id}/disliked-ingredients`. response `selected`, `completed`. ليس هو خيار «لا حساسية».
- Dietary: `GET/PUT .../members/{id}/dietary`; request `dietaryPreferenceCodes` أو `selectedCodes`; response `selected`, `completed`, `hasNoDietaryRestriction`, `noneOptionCode`, `selectedOptionCodes`.

قيم الاختيار الصريح: `NO_ALLERGIES` و`NO_DIETARY_RESTRICTIONS`. اقرأ `Completed` و`SelectedOptionCodes`: القائمة الفارغة غير المكتملة ليست اختيار «لا يوجد». ارسل رمز none بمكانه الصحيح فقط ولا تمزجه مع خيارات فعلية. لا تخمن الحقول خارج DTO؛ تحقّق من OpenAPI والـ request المحدد لكل endpoint.

## حالات وأخطاء

حمّل القيم قبل التحرير؛ امنع النقر المكرر؛ احتفظ بالقيم بعد validation/network error. بعد نجاح PUT أبطل cache القائمة والتفاصيل والرئيسية ثم أعد GET. حفظ بيانات لا يبدأ checkout أو خصم. أخطاء متحققة من validator: `family.display_name_required` (400)، `family.age_invalid` (400)، `family.relationship_invalid` (400)، `family.address_invalid` (400). عضو غير موجود `family.member_not_found` (404). لا تخترع `family.group_not_active` لهذا endpoint.

**قبول:** تغيير الاسم/العمر لا يمسح goal/activity؛ لا DOB؛ null address/phone يحافظ على الموجود؛ occupation null يحافظ عليه والفارغ يمسحه؛ preference none صريح ومختلف عن إجابة لم تكتمل؛ لا يبدأ دفع.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.

### أمثلة طلبات/استجابات تفضيلات الأسرة

Allergies:

```http
PUT /api/v1/customer/family-subscription-flow/members/11111111-1111-4111-8111-111111111111/allergies
Content-Type: application/json
{"selectedCodes":["NO_ALLERGIES"]}
```

Dietary:

```http
PUT /api/v1/customer/family-subscription-flow/members/11111111-1111-4111-8111-111111111111/dietary
Content-Type: application/json
{"selectedCodes":["NO_DIETARY_RESTRICTIONS"]}
```

Disliked ingredients لا تملك none code خاصاً؛ إرسال قائمة فارغة يمسح dislikes ويحفظ الاختيار الفارغ:

```http
PUT /api/v1/customer/family-subscription-flow/members/11111111-1111-4111-8111-111111111111/disliked-ingredients
Content-Type: application/json
{"selectedCodes":[]}
```

PUT الثلاثة ترجع `true` عند النجاح. أعد GET الموافق للتحقق من `Completed`, `NoneOptionCode`, `SelectedOptionCodes` أو `EatsEverything`. أمثلة GET: Allergy: `{"selected":[],"completed":true,"selectedCodes":[],"hasNoAllergies":true,"noneOptionCode":"NO_ALLERGIES","selectedOptionCodes":["NO_ALLERGIES"]}`. Dietary: `{"selected":[],"completed":true,"selectedCodes":[],"hasNoDietaryRestriction":true,"noneOptionCode":"NO_DIETARY_RESTRICTIONS","selectedOptionCodes":["NO_DIETARY_RESTRICTIONS"]}`. Disliked: `{"selected":[],"completed":true,"eatsEverything":true}`. أمثلة شكلية اصطناعية؛ أسماء GUIDs أعلاه غير حقيقية.
