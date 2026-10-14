# 00 — تدفق إدارة الأسرة والعقد المشترك

المصدر: checkout الإصدار `83fc8d05f05c478266d41e949b9e285c65b63bd8`. الأسماء والمسارات: FamilyManager controllers/contracts/handlers، FamilyCheckout flow DTOs/services، Calendar controllers/contracts/engine. لا تعتبر وثائق `frontend/admin-dashboard` مرجعاً للعقد الحالي.

| الترتيب | الشاشة | التالي |
|---|---|---|
| 08.01 الرئيسية | [01](./01-home.md) | 08.02 أو 08.03 |
| 08.02 الأفراد | [02](./02-members.md) | 08.03 أو M08.22 |
| 08.03 تفاصيل الفرد | [03](./03-member-detail.md) | 08.04، 08.20، M08.22 |
| M08.22 تعديل الفرد | [11](./11-member-edit.md) | 08.03 |
| 08.20 الوصول | [04](./04-access.md) | 08.21 |
| 08.21 الصلاحيات | [05](./05-permissions.md) | عودة للتفاصيل/التقويم |
| 08.04 التقويم | [06](./06-calendar.md) | 08.06 |
| 08.06 تفاصيل اليوم | [07](./07-day-details.md) | 08.07، 08.09، 08.10 |
| 08.07 المطاعم | [08](./08-restaurants.md) | 08.09 |
| 08.09 الوجبات | [09](./09-meals.md) | 08.10 |
| 08.10 المراجعة والتأكيد | [10](./10-review-confirm.md) | 08.04 |

## مفاتيح الهوية والحالة

احتفظ بـ `memberId`, `memberSubscriptionId`, `upcomingSubscriptionId` والتاريخ كقيم مختلفة. APIs التقويم تستخدم subscriptionId. استخدم `effectiveCalendarSubscriptionId = memberSubscriptionId ?? upcomingSubscriptionId` لعرض/قراءة الخدمة الجارية أو القادمة. وجود upcoming مع current null لا يعني أن العضو غير مشترك. الخدمة القادمة تعرض تاريخها وحالتها الفعلية، ولا تعرض تجديداً أو جدول اليوم الحالي. `remainingDays` قيمة خادم؛ لا تعِد حسابها من ساعة الجهاز. `lifecycle` يفصل `setupStatus`, `checkoutSelected`, `paymentStatus`, `fulfillmentStatus`, `serviceStatus`, `accessStatus`, `displayStatus`. الـ enum قد يتسلسل كسلسلة enum name حسب serializer؛ لا تتعامل معه كرقم حصراً ولا تترجم قيمة الطلب.

## إضافة فرد وبدء checkout

استخدم `POST /api/v1/customer/family-subscription-flow/members` لإنشاء فرد مسودة، ثم `PUT /members/{memberId}` للبيانات الأساسية. هذا لا يشتري خطة ولا يبدأ checkout. لا تستخدم `POST /api/v1/customer/family/members`: يرجع `409 family.use_checkout_flow`.

اختيار الخطة يكون من catalog حقيقي. احفظ تفضيلات الحساسية، disliked ingredients، dietary، والعنوان كلٌ عبر endpoint الخاص به. خيارات «لا يوجد» صريحة: `selectedCodes:["NO_ALLERGIES"]` و`selectedCodes:["NO_DIETARY_RESTRICTIONS"]` حسب نموذج كل endpoint؛ لا تستنتجها من قائمة فارغة. `PUT /members/{memberId}/checkout-selection` مع `{"subscribeNow":true}` يسجل اختياراً صريحاً فقط. اقرأ `FamilyCheckoutSelectionDto`: `batchId`, `batchVersion`, `memberId`, `checkoutSelected`, `selectedNewSubscriberCount`, nullable `pendingCheckout`, `retryPaymentEndpoint`, `nextScreen`, `selectionApplied`. إذا أعاد pendingCheckout أو retryPaymentEndpoint/nextScreen فاتبع الاستجابة واستأنف الدفع المعلق بدلاً من بدء checkout جديد. خلاف ذلك اعرض `GET /summary`، ثم `POST /checkout` عند `CanCheckout` وبعد عرض السعر. `FamilyCheckoutSummaryDto` يحوي `batchId`, `batchVersion`, `finalPrice`, `currencyCode`, `canCheckout`, `checkoutBlockReason`, group/member schedules/meal overview. أرسل `POST /checkout` مع `callbackUrl`, `errorUrl`, و`batchId`/`batchVersion` من summary، و`clientContractVersion` عند العقد الحالي؛ `paymentMethodId` default بالخادم. عند `family.quote_changed` أعد summary واطلب مراجعة المستخدم. لا تخصم تلقائياً، ولا تصفّر roles أو سياق المدير عند الرجوع.

M08.22 يكتب DTO محدوداً: `displayName`, `relationshipCode`, `gender`, `ageYears`, `deliveryAddressId`, مع `phone` و`occupation` اختياريين. لا توجد nutritionalGoal/activity/program/bundle/tier/duration/includeFriday في هذا PUT؛ الخادم يحافظ على الهدف والنشاط. null للعنوان أو الهاتف يحافظ على القيمة، والـ occupation null يحافظ بينما string فارغ يمسح. استخدم relationship codes من catalog (مثل `child` لا `son`). تفاصيل حقول DTO والتفضيلات المنفصلة في [M08.22](./11-member-edit.md).


### الطلبات الفعلية للاختيار والدفع

اختيار الشراء:

```http
PUT /api/v1/customer/family-subscription-flow/members/11111111-1111-4111-8111-111111111111/checkout-selection
Content-Type: application/json
{"subscribeNow":true}
```

الـ response `FamilyCheckoutSelectionDto`: `batchId`, `batchVersion`, `memberId`, `checkoutSelected`, `selectedNewSubscriberCount`, `pendingCheckout`, `retryPaymentEndpoint`, `nextScreen`, `selectionApplied`. إذا `pendingCheckout` موجود أو أعادت الاستجابة retry endpoint/next screen، استأنف على الرابط/الشاشة الواردة ولا تبدأ عملية جديدة.

ملخص الدفع: `GET /api/v1/customer/family-subscription-flow/summary?memberId={memberId}`. `FamilyCheckoutSummaryDto` يتضمن `batchId`, `batchVersion`, `subtotal`, family discount/amount, `finalPrice`, `currencyCode`, `canCheckout`, `checkoutBlockReason`, عدد المشاركين/المقاعد، أفراد الأسرة، member schedules, restaurant availability, meal overview. اعرض هذا المصدر كما هو. عند اختلاف `batchVersion` أو quote (`family.quote_changed`, 409) اقرأ الملخص وأعد عرض السعر ليوافق المستخدم.

```http
POST /api/v1/customer/family-subscription-flow/checkout
Content-Type: application/json
```

```json
{"callbackUrl":"https://mobile.mealmate.example/payment/complete","errorUrl":"https://mobile.mealmate.example/payment/error","paymentMethodId":3,"idempotencyKey":"client-operation-key","batchId":"22222222-2222-4222-8222-222222222222","batchVersion":7,"clientContractVersion":1}
```

JSON توضيحي فقط: callback/error URL يجب أن تكونا configured فعلياً بالتطبيق/البيئة؛ لا تستخدم example URL. الحقول required: `callbackUrl`, `errorUrl`; `paymentMethodId` default=3 إن حذف؛ `idempotencyKey`, `batchId`, `batchVersion`, `clientContractVersion` nullable اختيارية في DTO، لكن استخدم batchId/version من الملخص عند checkout العائلي. اقبل `FamilyCheckoutStartedDto`: `familyGroupId`, `paymentAnchorSubscriptionId`, `paymentUrl`, `holdExpiresAtUtc`, `finalPrice`, `batchId`, `batchVersion`. افتح paymentUrl، وبعد callback حدّث GET family/home وmembers. لا تعتبر POST ناجحاً نهائياً قبل إكمال بوابة الدفع والتحقق من حالة الخادم.
## الصلاحيات

الشاشة 08.21 تبقى مرجعاً للعقد، وإن لم تكن ضمن الشاشات المطلوبة من المنتج. اعرض `groups` كما وردت من الخادم. طلب التحديث يتطلب مفاتيح الجذر الثمانية (`editDailyMeals`, `changeDeliveryTime`, `viewProgram`, `changeProgram`, `editMealsAndDays`, `editAddress`, `unitExchange`, `changePassword`) مع `expectedVersion` إلزامي. 11 مفتاحاً إضافياً اختيارية كما في DTO. استخدم `version` من GET. المدير غير قابل للتعديل. بعض الأفعال غير مدعومة ومرفوضة حتى للمدير؛ لا تعرضها كميزات قابلة للتمكين. تعارض النسخة `family.permissions_changed` يعني GET ومراجعة، لا إعادة كتابة صامتة.

## لغة وتنسيق

كل copy جديد له مفاتيح عربية وإنجليزية متوازنة، ويتفاعل مع locale النشط دون reload. أرسل `Accept-Language: ar|en`. قيم enums، slots، role، error codes، IDs، permission keys وAPI identifiers لا تتغير ترجمتها. اختر `nameAr/nameEn` حسب اللغة، ثم البديل غير الفارغ، ثم label مترجم. التوقيت التشغيلي Asia/Kuwait؛ تنسيق التاريخ لا يبدل `serviceDate` من الخادم. KWD بثلاث منازل عشرية حسب `currencyCode`؛ لا تجمع الأسعار محلياً.

## استجابات وأخطاء

`ApiControllerBase`: validation 400، NotFound 404، Conflict 409، Unauthorized 401، Forbidden 403، failure/other 422. استخدم `ProblemDetails.code` للمنطق والنص المحلي للعرض؛ لا تعرض `detail` الخام كنسخة منتج. رموز وصول عائلة متحققة: `family.permission_required`, `family.access_paused`, `family.access_disabled`, `family.action_not_available`, `family.member_not_found`, `family.not_member`. التوقيت/قفل التعديل يأتي من API وسياسة cutoff الديناميكية؛ لا تثبت 72 ساعة بالعميل.

> JSON في بقية الملفات توضيحي اصطناعي بأسماء وGUIDs غير حقيقية، ولا يمثل بيانات إنتاج أو seed. لا تنسخه كبيانات أعمال.





