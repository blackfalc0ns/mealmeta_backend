# صلاحيات الفرد (08.21)

**السابق:** [الوصول](./04-access.md) · **التالي:** [تفاصيل الفرد](./03-member-detail.md) أو [التقويم](./06-calendar.md)

الشاشة موثقة ضمن تسليم العقد، حتى لو لم تكن بين طلبات التصميم. لا تعرض صلاحيات غير `groups[]` ولا تخترع controls.

## قراءة وحقول UI

`GET /api/v1/customer/family/members/{memberId}/permissions`. الجذر يحوي 8 booleans مطلوبة في PUT: `editDailyMeals`, `changeDeliveryTime`, `viewProgram`, `changeProgram`, `editMealsAndDays`, `editAddress`, `unitExchange`, `changePassword`; أيضاً `version`, و11 capability: `viewCalendar`, `viewInvoices`, `selectRestaurant`, `confirmMeals`, `requestFreeze`, `requestRenewal`, `requestUpgrade`, `cancelSubscription`, `viewFamilyCalendars`, `viewFamilyPlans`, `viewFamilyInvoices`. يعرض `groups[]`: `code`, `labelAr`, `labelEn`, `permissions[]`; العنصر `key`, `labelAr`, `labelEn`, `value`.

مثال اصطناعي صالح شكلياً: `{"memberId":"11111111-1111-4111-8111-111111111111","editDailyMeals":true,"changeDeliveryTime":false,"viewProgram":true,"changeProgram":false,"editMealsAndDays":false,"editAddress":false,"unitExchange":false,"changePassword":false,"version":4,"viewCalendar":true,"viewInvoices":false,"selectRestaurant":true,"confirmMeals":true,"requestFreeze":false,"requestRenewal":false,"requestUpgrade":false,"cancelSubscription":false,"viewFamilyCalendars":false,"viewFamilyPlans":true,"viewFamilyInvoices":false,"groups":[]}`.


## كتالوج المفاتيح الـ19 كما في DTO

| المجموعة | مفاتيح القراءة من `groups[]` | دلالة المسارات |
|---|---|---|
| `meals` | `viewCalendar`, `viewFamilyCalendars`, `selectRestaurant`, `editDailyMeals`, `confirmMeals`, `editMealsAndDays`, `unitExchange` | قراءة تقويم؛ اختيار مطعم؛ اختيار وجبة وإتمام الصندوق؛ تأكيد العميل. عمليات bulk edit وunit exchange غير مدعومة (`family.action_not_available` 403). |
| `program` | `viewProgram`, `viewFamilyPlans`, `changeProgram`, `requestFreeze`, `requestRenewal`, `requestUpgrade`, `cancelSubscription` | قراءة خطة/نطاق الأسرة؛ البرامج change غير مدعومة. بقية العمليات تستخدم permission keys الخاصة بها. |
| `delivery` | `changeDeliveryTime`, `editAddress` | تغيير وقت التوصيل غير مدعوم عبر family policy؛ تعديل العنوان capability منفصل. |
| `account` | `changePassword`, `viewInvoices`, `viewFamilyInvoices` | الحساب/الفواتير؛ family invoice scope منفصل عن invoice للفرد. |

تتكون المجموعة من 19 key إجمالاً. `FamilyPermissionCatalog` هو مصدر التسميات والمجموعات. المدير يحصل على إدارة مسموحة بوجه عام، لكن السياسة ترفض صراحة `ChangeProgram`, `EditMealsAndDays`, `ChangeDeliveryTime`, `UnitExchange` حتى للمدير؛ هذه تظل denied بغض النظر عن boolean المخزن. في calendar لا تساوي `canEdit` جميع الأذونات: اختيار المطعم يتطلب `selectRestaurant`؛ اختيار slot/complete يستخدم `editDailyMeals`؛ تأكيد العميل يستخدم `confirmMeals`. لا تخفِ/تظهر عنصر اعتماداً على `editDailyMeals` وحده إذا كان الفعل له capability محددة.
## حفظ وتعارض النسخة

`PUT /api/v1/customer/family/members/{memberId}/permissions`: أرسل الثمانية الأساسية دائماً، و`expectedVersion` إلزامي. capabilities الـ11 اختيارية حسب DTO. لا ترسل `groups`; لا تسقط حقلاً من الثمانية.

```json
{"editDailyMeals":true,"changeDeliveryTime":false,"viewProgram":true,"changeProgram":false,"editMealsAndDays":false,"editAddress":false,"unitExchange":false,"changePassword":false,"expectedVersion":4,"viewCalendar":true}
```

استخدم version من GET؛ عند success استبدل نسخة الشاشة برد PUT. `family.permissions_version_required` 400 عند غياب النسخة، `family.permissions_changed` 409 عند النسخة القديمة: أعد GET واعرض التعديل المتزامن واطلب مراجعة المستخدم. لا تعِد إرسالاً آلياً. صلاحيات المدير غير قابلة للتغيير (`family.manager_permissions_immutable`, 409).

بعض الأفعال غير مدعومة حتى للمدير: `ChangeProgram`, `EditMealsAndDays`, `ChangeDeliveryTime`, `UnitExchange` ترفضها policy بـ `family.action_not_available` (403). لا تظهر هذه toggles كأنها feature متاحة، حتى إن كانت مفاتيح legacy ظاهرة في groups؛ اعرض حالة تعطيل مع localization/سبب مناسب أو اخفها وفق UX. لا تحاول تجاوز القرار.

أخطاء أخرى: `family.member_not_found` 404، `family.not_manager` 403. Loading/empty/error لها حالات مترجمة؛ groups فارغة تعرض empty مع retry/إشارة عدم وجود capabilities. كل label يعاد اختياره عند تغيير locale. **قبول:** version concurrency آمن؛ المدير immutable؛ لا actions غير مدعومة؛ locale لا يغير permission key.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.
