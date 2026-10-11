# تسليم تكامل الاشتراك الفردي للموبايل — 2026-10-10

هذه وثيقة تنفيذ ومراجعة للموبايل، مبنية على العقود الموجودة في الـ backend. لم يُشغّل تطبيق الموبايل ولم تُختبر المهام المستضافة في بيئة إنتاج. أسماء الحقول والمسارات والأكواد أدناه معرفات تقنية لا تُترجم.

## اكتشاف الخيارات والتغطية

استخدم الرمز المصادق للعميل. العنوان يأتي من عنوان التوصيل الفعال المحفوظ؛ لا ترسل منطقة بديلة لتجاوز التغطية.

بادئة الاكتشاف: `/api/v1/customer/subscription-flow`.

### جاهزية المطاعم والبرنامج والباقة — تحديث 2026-10-11

- `canSelect=false` يمنع اختيار البرنامج أو الباقة أو الفئة. `subscription_flow.no_prepared_restaurants` يعني وجود تغطية لكن عدم وجود مطعم يملك وجبات مؤهلة كافية وأسعارًا صالحة لهذه الخيارات. قوائم `restaurants` والعداد المعروض على البطاقة تشمل المطاعم الجاهزة فقط؛ عند التعطيل لا تعرض شعارات محفوظة من طلب سابق.
- لا تستعمل `availability.coveredRestaurantCount` لعرض شعارات أو لإثبات الجاهزية؛ العدد يصف التغطية وقد يكون موجبًا رغم تعطيل الخيار. غياب العنوان أو عدم تأكيد الحساسية/التفضيلات يعطي قوائم شعارات فارغة أيضًا.
- مسار `/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants` يقبل الآن `durationDays`, `startDate`, `includeFriday`. أرسل نفس تاريخ الاختيار والمدة والجمعة المستخدمين في السمري؛ الباك يحوّل التاريخ إلى أول توصيل بنفس القاعدة. هذه المعلمات تعمل أيضًا مع المسار الذي لا يتضمن bundleId ومع تقسيم الصفحات؛ الأفضل تحديد الباقة صراحةً.
- بدون المدة والتاريخ، المطاعم المعروضة جاهزة من حيث التغطية والأسعار ومخزون الوجبات، لكن لا يوجد وعد بالسعة أو القوائم لكل أيام فترة لم تُحدد. عند تحديد الفترة أعد تحميل المطاعم بهذه المعلمات، أو استخدم `/restaurant-availability` للحصول على `availability.failureReasons` مع القائمة المؤهلة والعداد الصحيح.
- تاريخ البداية اختياري في استعراض المطاعم: يمكن إرسال `durationDays=26&includeFriday=false` بدون `startDate` قبل خطوة التاريخ. تُراجع صلاحية المدة، وتُعرض المطاعم الجاهزة للعنوان والبرنامج والباقة والفئة دون فحص أيام الفترة. بعد اختيار التاريخ، أرسله مع المدة لفحص الفترة كاملة؛ التاريخ وحده دون مدة صالحة لا يكفي. السمري وreadiness والدفع ما زالوا يحتاجون التاريخ. إذا أصبح المطعم غير جاهز أو لم يعد يغطي العنوان، مسارات التفاصيل لا تسمح بالوصول له اعتمادًا على معرف قديم.
- عند `includeFriday=true` بدون تاريخ، استعراض المطاعم يعرض فقط المطاعم ذات جدول جمعة صريح يعمل؛ المطعم المغلق الجمعة أو بدون جدول جمعة يُستبعد قبل حساب `totalCount` وتقسيم الصفحات، حتى إذا أُرسلت معرفاته في `restaurantIds`. أعد تحميل القائمة عند تغيير الجمعة، وامسح المطعم المختار إن خرج من النتائج. بعد تحديد التاريخ، فحص الفترة يراجع أيضًا استثناءات التشغيل لكل يوم.
- أعد readiness/summary قبل الدفع، لأن بيانات التشغيل قد تتغير بعد آخر استعراض. فحص الفترة النهائي يظل مطلوبًا.

### تحديث متطلبات الاختيار — 2026-10-11

ابدأ بالعنوان، ثم تأكيد الحساسية، ثم تأكيد التفضيلات الغذائية، وبعدها اختيار البرنامج والباقة والفئة. تأكيد قائمة فارغة يعني اكتمال الخطوة؛ لا تستنتج الاكتمال من عدد العناصر. قوائم الكتالوج تظل تعرض الخيارات لكن `availability.canSelect=false` يمنع اختيارها حتى استكمال المتطلبات.

حالات النقص بالترتيب هي `address_required` ثم `allergies_required` ثم `dietary_preferences_required`. اقرأ `messageCode` و`missingStep` و`actionEndpoint` من `availability` لتوجيه المستخدم؛ آخر حقلين اختياريان ولا يظهران عند عدم وجود خطوة ناقصة. مسار `actionEndpoint` الخاص بالحساسية والتفضيلات يفتح قراءة البيانات؛ تأكيدها يتم بعملية الحفظ الخاصة بالشاشة.

المسار القديم `/programs/{programId}/bundles` يحتفظ بغلاف `bundles` وحقول `hasDeliveryAddress`, `hasAllergies`, `hasDietaryPreferences`, `isProfileComplete`. مسار `/catalog/programs/{programId}/bundles` يحتفظ بـ`items[].option` و`items[].availability`. لا تبدّل شكل أحد الردين بالآخر، واحترم `canSelect` في كليهما. حقول اكتمال الخطوات مستقلة؛ نقص خطوة لا يلغي خطوة مؤكدة سابقًا.

البرنامج يظل قابلًا للاختيار إذا كانت له باقة واحدة على الأقل يعرضها مطعم معتمد وفعّال يغطي العنوان ولديه أسعار صالحة ووجبات مؤهلة كافية. الباقات غير الجاهزة تبقى ظاهرة ومعطلة، وقائمة مطاعم الباقة تخص البرنامج والباقة والعنوان معًا. صلاحية القوائم والسعة لكل الأيام تُراجع عند اختيار التاريخ والمدة ثم readiness/summary/checkout. لا تعرض نجاح الدفع اعتمادًا على التغطية وحدها.

بعد تغيير العنوان أو تأكيد الحساسية أو التفضيلات، ألغِ الاختيارات اللاحقة القديمة وأعد تحميل الكتالوج. اربط كل طلب بالسياق الحالي وتجاهل رد أي طلب سابق؛ أعد فحص الجاهزية والملخص قبل الشراء. اختبار قبول الموبايل يشمل نقص كل خطوة، تأكيد قوائم فارغة، وجود باقة متاحة وأخرى معطلة، تغيير العنوان، وعدم تحويل رسالة نقص الحساسية إلى رسالة نقص السعر.

| العملية | المسار GET | العقد |
|---|---|---|
| نوع الاشتراك | `/types` | قائمة؛ احترم `isAvailable` و`disabledReason` |
| المتطلبات | `/prerequisites` | العنوان والحساسية والتفضيلات؛ أخطاء المتطلبات تحتوي `missingStep` و`actionEndpoint` |
| تغطية العنوان العامة والبرامج | `/catalog/programs` | `addressCoverage` منفصل عن `items[].availability` و`hasDeliveryAddress`؛ العد قبل تقسيم الصفحات |
| الباقات | `/catalog/programs/{programId}/bundles` | الخيارات الفعالة بما فيها غير المتاحة، داخل `items[].option` |
| المدد | `/programs/{programId}/bundles/{bundleId}/durations?includeFriday=...` | قائمة مدد وإعداد الجمعة |
| الفئات | `/catalog/programs/{programId}/bundles/{bundleId}/tiers?durationDays=...&startDate=...&includeFriday=...` | غلاف، حالة التغطية أو الفترة لكل فئة |
| الفئات القديمة | `/programs/{programId}/bundles/{bundleId}/tiers` | قائمة مسطحة تحتفظ بشكلها وتضيف `availability` للفردي؛ المتطلبات السابقة ما زالت مطلوبة |
| المطاعم للفترة | `/restaurant-availability` | نفس سياق `SubscriptionSummaryRequest`؛ قائمة المطاعم المؤهلة و`availability` و`totalCount` |
| جاهزية الشراء | `/readiness` | نفس السياق؛ الحكم النهائي لتفعيل الدفع |
| الملخص | `/summary` | السعر الفعلي، التاريخ والسياق الحالي |
| وسائل الدفع | `/payment-methods?amount=...` | القائمة الحقيقية من مزود الدفع |

المسار القديم `/programs` يبقى قائمة مسطحة (أو `PagedResult` عند `page/pageSize`) مع بيانات توفر على البطاقة؛ لا تستبدله بغلاف كتالوج دون تعديل المستهلك. للحصول على التغطية العامة استخدم `/catalog/programs`. كذلك تحقق من العنوان قبل حفظه بواسطة `POST /api/v1/customers/me/addresses/preview-coverage` أو `GET /api/v1/customers/me/addresses/{id}/preview-coverage`.

أسبقية العرض: غياب العنوان يعني طلب عنوان، وليس عدم توصيل. افحص `addressCoverage` العام لتمييز «لا يوجد توصيل لهذا العنوان» عن عدم توفر برنامج/باقة/فئة معينة. افحص `availability.scope`: `coverage` لا يثبت صلاحية الفترة؛ `eligibleRestaurantCount=null` يعني عدم إجراء فحص الفترة. `selection_required` يسمح باستكمال الاختيار فقط. مع المدة والتاريخ، `period` يفحص جميع أيام الخدمة والقوائم والسعة والوجبات الآمنة وتسعير التوريد. لا تفسر عدد المطاعم المغطية أو شعارها على أنه موافقة للدفع. حالة `pricing_unavailable` تمنع الاختيار ولا تعني سعرًا صفرًا. `canSelect=false` يمنع الانتقال ويحافظ على البطاقة وسببها، حتى عندما توجد فئات أخرى متاحة. القائمة الفارغة الحقيقية لا تُملأ ببيانات افتراضية.

### تسلسل المتطلبات السابقة للعميل وتوفر الكتالوج — تحديث 2026-10-11

- **ترتيب التحقق من المتطلبات السابقة إلزامي وثابت:**
  1. **عنوان التوصيل النشط (`ActiveDeliveryAddressId`):** إذا لم يكن محددًا -> `status = "address_required"`, `canSelect = false`, `messageCode = "checkout.address_required"`, `missingStep = "ADDRESS"`, `actionEndpoint = "POST /api/v1/customers/me/addresses"`.
  2. **تأكيد الحساسية (`AllergiesCompletedAtUtc`):** إذا لم يكتمل بعد وجود العنوان -> `status = "allergies_required"`, `canSelect = false`, `messageCode = "checkout.allergies_required"`, `missingStep = "ALLERGIES"`, `actionEndpoint = "GET /api/v1/customers/me/food-preferences/allergies"`.
  3. **تأكيد التفضيلات الغذائية (`DietaryPreferencesCompletedAtUtc`):** إذا لم يكتمل بعد تأكيد الحساسية -> `status = "dietary_preferences_required"`, `canSelect = false`, `messageCode = "checkout.dietary_preferences_required"`, `missingStep = "DIETARY_PREFERENCES"`, `actionEndpoint = "GET /api/v1/customers/me/food-preferences/dietary"`.
  4. **القوائم الفارغة المؤكدة:** إذا أكد العميل عدم وجود حساسية أو تفضيلات (الطابع الزمني غير فارغ مع قائمة فارغة) يُعتبر المتطلب مستوفى بالكامل (`canSelect = true` حسب التغطية).
- **حقول غلاف الكتالوج وبطاقات الخيارات:**
  - `SubscriptionCatalogOptionAvailabilityDto`:
    - `scope`: `"coverage"` أو `"period"`.
    - `status`: `"address_required"`, `"allergies_required"`, `"dietary_preferences_required"`, `"no_restaurants_for_address"`, `"no_restaurants_for_selection"`, `"pricing_unavailable"`, `"selection_required"`, `"available"`.
    - `canSelect`: `false` عند نقص أي متطلب سابق أو عدم وجود تغطية/تسعير/أهلية. البطاقات غير المتاحة تبقى ظاهرة ولا تُخفى.
    - `coveredRestaurantCount`: عدد المطاعم المتاحة؛ في غياب العنوان يعيد عدد المطاعم العارضة للبطاقة دون تصفية جغرافية.
    - `eligibleRestaurantCount`: `null` في نطاق التغطية، ورقم فعلي فقط عند فحص الفترة الكاملة.
    - `missingStep`: خطوة النقص (`"ADDRESS"`, `"ALLERGIES"`, `"DIETARY_PREFERENCES"`) عند `canSelect = false` بسبب متطلب سابق.
    - `actionEndpoint`: المسار المستهدف لإكمال الخطوة عند النقر على البطاقة المعطلة.
  - غلاف البرامج `SubscriptionCatalogProgramsEnvelopeDto`: `addressCoverage.canSelect = false` عند غياب العنوان أو عند كون التغطية العامة للعنوان صفرًا.
- **تحديد توفر البرنامج من الباقات:**
  - يكون البرنامج قابلاً للاختيار (`canSelect = true`) فقط إذا كانت المتطلبات مكتملة وتوجد باقة صالحة واحدة على الأقل تملك مطاعم جاهزة تغطي العنوان. إذا كانت كل باقاته بلا تغطية يصبح معطلاً بحالة `no_restaurants_for_address`؛ وإذا وجدت تغطية بلا جاهزية يرجع `subscription_flow.no_prepared_restaurants` ويظل معطلاً.
- **إبطال الكاش عند تغيير العنوان:**
  - عند إضافة أو تعديل أو تغيير عنوان التوصيل النشط على الموبايل، يجب إبطال الكاش المحلي لجميع مسارات الكتالوج فوراً (`/catalog/programs`, `/catalog/programs/{programId}/bundles`, `/catalog/programs/{programId}/bundles/{bundleId}/tiers`) وإعادة جلب البيانات لعرض التغطية المحدثة للعنوان الجديد.

أعد الفحص بعد تغيير العنوان أو البرنامج أو الباقة أو الفئة أو المدة أو الجمعة أو التاريخ أو تفضيلات الطعام. تجاهل استجابات الطلبات القديمة. قوائم الاستعراض القديمة للمطاعم ليست بديلًا لفحص كامل الفترة. مسار العائلة له معالجة عنوان لكل عضو ولا يطبق هذا التغيير عليه.

في readiness استخدم `canOpenSummary` لفتح الملخص و`canCheckout` لتفعيل الشراء؛ `isComplete` يصف اكتمال المتطلبات ولا يحل محل جاهزية الفترة. اعرض `checkoutBlockReason` أو `nextAction` بمعناه المحلي، ووجّه إلى `items[].endpoint` عند نقص خطوة. عند `401/403` عالج المصادقة والصلاحية، وعند `400/409` اعرض الكود الدلالي وأعد قراءة السياق، وعند `502` لبوابة الدفع أتح إعادة المحاولة دون اصطناع وسائل دفع. لا تحول خطأ الشبكة إلى قائمة خيارات فارغة؛ للحالة الفارغة استجابة ناجحة مستقلة.

## التاريخ والدفع والاستئناف

`startDate` في checkout/summary والفئات واستعراض المطاعم هو تاريخ الاختيار الذي يستخدمه الخادم لحساب أول توصيل؛ أرسل نفس تاريخ الاختيار في هذه المسارات واعرض التاريخ الناتج دون إعادة حسابه محليًا. لا تُدخل تاريخ أول التوصيل الناتج مرة أخرى بوصفه تاريخ اختيار جديد عند استئناف دفع قائم. حافظ على تقويم العمل والمنطقة الزمنية للكويت وعملة `KWD` ودقتها.

إنشاء الشراء: `POST /api/v1/customer/subscriptions/checkout`. حالة الدفع: `GET /api/v1/customer/subscriptions/{id}/payment`. معاينة الاستئناف: `GET /api/v1/customer/subscriptions/{id}/resume`؛ التنفيذ: `POST` للمسار نفسه. احترم `canPayDirectly` وسبب الاستئناف والتاريخ المقترح: عند انتهاء حجز السعة أو مرور تاريخ البداية اطلب عرضًا جديدًا، ولا تنقل التواريخ صامتًا. بعد رجوع بوابة الدفع أعد قراءة الحالة من الخادم؛ الرجوع وحده ليس إثبات دفع. نجاح الدفع لا يعني اكتمال تهيئة الوجبات: انتظر `isFulfillmentReady` أو حالة الخادم المناسبة قبل إتاحة تعديل التقويم. الفشل في التهيئة حالة قابلة للمراجعة وليست نجاحًا؛ إعادة التهيئة الإدارية تستخدم `POST /api/v1/admin/subscriptions/{id}/retry-fulfillment` ولا تُعرض كعملية عميل.

## التقويم والاختيار والتجميد

### تحديث نافذة الاستبدال وحساسية البوكس — 2026-10-11

- نافذة الاستبدال التي يفتحها الأدمن مدتها 24 ساعة من الفتح الفعلي. `selectionDeadlineUtc` في استجابة التقويم واليوم يمثل المهلة الفعالة لهذه النافذة؛ لا تضف 24 ساعة محليًا ولا تستخدم تاريخ إنشاء طلب المطعم. يظل موعد الـ72 ساعة الأصلي محفوظًا في الباك، واستثناء أول يوم من الدفع كما هو.
- استخدم `canEdit` و`editBlockReason` بعد كل خطوة، بما فيها تغيير المطعم، اختيار الوجبات، إكمال البوكس والتأكيد. هذه الخطوات لا تمدد النافذة. إعادة طلب الفتح أثناء نافذة نشطة تعيد نفس الموعدين. عند النهاية بالضبط تصبح الكتابة مقفولة، وتتعامل مهمة القفل مع البوكس المحفوظ أو الاختيار التلقائي وفق السياسة الحالية.
- حفظ أو تأكيد البوكس داخل النافذة لا يعني أنه أُرسل للمطعم فورًا. أعد قراءة الحالة: `CustomerSelected` حفظ، و`SentToRestaurant` إرسال. طلب المطعم القديم يدخل مسار الاستبدال حتى يُرسل البديل، ويُلغى عند إرسال بديل لمطعم آخر. يمكن إعادة استخدام المطعم الأول في استبدال لاحق.
- إذا رجعت عملية التأكيد أو الاختيار التلقائي بخطأ `calendar.no_safe_box` بعد تغير الحساسية، أعد قراءة اليوم والتقويم حتى مع HTTP فاشل: الحالة المحفوظة تصبح `ReplacementRequired` والسبب `calendar.no_safe_box`. لا تثبت وجبات بديلة محليًا؛ الباك يتراجع عن أي إصلاح جزئي ولا يرسل طلبًا غير آمن ولا ينقل حجز السعة في هذه الحالة.
- يلزم نشر migration `20261011090000_AddSubscriptionDayReplacementWindow` لحفظ موعدين UTC اختياريين. السجلات القديمة دون مواعيد لا تحصل على نافذة مصطنعة من تاريخ إنشاء الطلب؛ افتح نافذة فعلية عبر الأدمن إذا لزم.
- اختبارات قبول الموبايل: افتح نافذة بعد القفل القديم؛ بدّل المطعم واختر الوجبات وأكمل البوكس؛ أعد الفتح وتأكد أن العد لم يتمدد؛ عند حد النهاية أعد القراءة وتأكد من القفل؛ اختبر الاستبدال `A → B → A`؛ ثم غيّر الحساسية بعد حفظ البوكس وتأكد أن إعادة القراءة تعرض الاستبدال وسبب الفشل. هذه الحالات لم تُنفذ على تطبيق الموبايل ضمن تعديل الباك.

بادئة التقويم `/api/v1/customer/subscriptions/{subscriptionId}`. اقرأ `GET /calendar` و`GET /days/{date}`. المطاعم `GET /days/{date}/restaurants`؛ وجبات المطعم `GET /days/{date}/restaurants/{restaurantId}/meals`؛ الوجبات المتاحة `GET /days/{date}/meals/available`. الكتابة: `POST /days/{date}/restaurant` مع `restaurantId`، و`POST /days/{date}/meals/slot` مع `mealId, slot`، ثم `/complete` (يدعم `idempotencyKey` أو `Idempotency-Key`) و`/confirm` و`/auto-select` بحسب الإجراء المطلوب.

القاعدة: الأيام العادية تحتاج مهلة لا تقل عن 72 ساعة قبل الخدمة؛ اليوم الأول فقط له 24 ساعة من الدفع. استخدم `selectionDeadlineUtc` و`lockRemainingSeconds` للعد التنازلي. `canEdit` هو سلطة الخادم، وليس رقم الساعات المحسوب على الهاتف. أعد قراءة اليوم بعد كل كتابة، وعند عودة التطبيق وبعد انتهاء العد. ترجم `editBlockReason`: `selection_deadline_passed`, `day_locked`, `day_frozen`, `day_cancelled`؛ `fulfillment_not_ready` حالة للشاشة. لا تستنتج القفل من نجاح الدفع أو وجود حجز سعة.

الاختيار التلقائي يجب أن يحاول البدائل المؤهلة عند رفض السعة. عند استنفاد السعة أو عدم وجود وجبة آمنة اعرض `failureReasonCode` الحالي ولا تؤكد الصندوق محليًا ولا تعرض إرسالًا للمطعم. قد تستلزم الحالة تدخل التشغيل؛ تحدد استجابة الخادم الخطوة التالية. النجاح بعد تحويل المطعم يستلزم تحديث المطعم والوجبات من الاستجابة.

التجميد: `GET /api/v1/customer/subscriptions/freeze-policy` و`GET /api/v1/customer/subscriptions/{id}/freeze-details` ثم `POST /api/v1/customer/subscriptions/{id}/freeze-requests`. طلب العميل يُعتمد ويُطبق فورًا إذا نجح التحقق وتجهيز أيام التعويض. افحص الحالة الراجعة؛ عند `Approved` أعد تحميل التقويم والتواريخ مباشرة. مسار موافقة الإدارة يعالج الطلبات المعلقة القديمة؛ أيام التعويض تحتاج قوائم صالحة لتواريخها وحجز سعة وتبقى دون تأكيد تلقائي. عند رفض التحضير لا تعرض تمديدًا ناجحًا؛ يجب أن تبقى الحالة السابقة متسقة وتظهر رسالة السبب. لا تعيد تأكيد أيام بديلة اعتمادًا على اليوم المجمد.

الأكواد المتحققة في المصدر: `freeze.preparation_unavailable`, `freeze.replacement_pool_unavailable`, `freeze.replacement_capacity_unavailable`, `freeze.replacement_restaurants_unavailable`, `freeze.no_service_days`, `freeze.locked_days_cannot_be_frozen`. الأولان يحتاجان متابعة التشغيل، والثالث يخص سعة أيام التعويض، والرابع يعني عدم وجود بديل صالح بعد التحقق. الحالة `calendar.no_safe_box` تخص تعذر إنشاء صندوق آمن؛ `calendar.fulfillment_not_ready` تخص عدم اكتمال التهيئة، بينما `fulfillment_not_ready` بدون البادئة هو سبب حجب الشاشة الموصوف في عقد التقويم. حافظ على هذه المعرفات كما هي عند ربطها بالقاموس.

## الرسائل والتعريب

هذه مفاتيح دلالية مقترحة لقاموس الموبايل، وليست نصوصًا جديدة في runtime backend. اربط أكواد الخادم بالمفاتيح ولا تعرض `detail/title/message` الخام. خزّن الكود والمعلمات وأعد الترجمة عند تغيير اللغة، بما في ذلك الحوار المفتوح. حقول الأعمال `nameAr/nameEn` محفوظة؛ استخدم غير الفارغ من اللغة الأخرى عند نقص الاسم، ثم تسمية محلية للبيانات المفقودة. لا تترجم API identifiers.

| المفتاح / المصدر | العربية | English |
|---|---|---|
| `addressRequired` / `checkout.address_required` | أضف عنوان التوصيل للمتابعة. | Add a delivery address to continue. |
| `allergiesRequired` / `checkout.allergies_required` | أكّد الحساسية للمتابعة. | Confirm your allergies to continue. |
| `dietaryPreferencesRequired` / `checkout.dietary_preferences_required` | أكّد التفضيلات الغذائية للمتابعة. | Confirm your dietary preferences to continue. |
| `noPreparedRestaurants` / `subscription_flow.no_prepared_restaurants` | لا توجد مطاعم تغطي عنوانك ولديها وجبات مؤهلة كافية وأسعار معتمدة لهذه الخيارات. | No restaurants covering your address have enough eligible meals and valid pricing for these options. |
| `noDelivery` / `subscription_flow.no_delivery_to_address` | لا توجد مطاعم توصل إلى هذا العنوان حاليًا. | No restaurants currently deliver to this address. |
| `optionUncovered` / `no_restaurants_for_address` | لا توجد مطاعم لهذه الخيارات توصل إلى عنوانك. | No restaurants for these options deliver to your address. |
| `periodUnavailable` / `no_restaurants_for_selection` | لا توجد مطاعم تستطيع خدمة كامل الفترة بهذه الخيارات. غيّر الخيارات وحاول مجددًا. | No restaurants can serve the entire period with these options. Change your options and try again. |
| `selectionRequired` | اختر المدة وتاريخ البداية للتحقق من كامل الفترة. | Choose the duration and start date to check the entire period. |
| `fridayUnavailable` / `subscription_flow.friday_unavailable` | لا تتوفر خدمة الجمعة لهذه الفئة. | Friday service is unavailable for this tier. |
| `pricingUnavailable` / `subscription_flow.pricing_unavailable` | السعر غير متاح لهذه الخيارات حاليًا. | Pricing is currently unavailable for these options. |
| `deadlinePassed` | انتهت مهلة تعديل هذا اليوم. | The editing deadline for this day has passed. |
| `firstDayDeadline` | يمكنك تعديل اليوم الأول خلال 24 ساعة من الدفع، وفق المهلة المعروضة. | You can edit the first day within 24 hours of payment, subject to the displayed deadline. |
| `ordinaryDeadline` | يجب إكمال اختيار الأيام التالية قبل الخدمة بـ72 ساعة على الأقل. | Complete selections for later days at least 72 hours before service. |
| `fulfillmentPending` | جارٍ تجهيز اشتراكك. سنعرض التقويم عند اكتمال التجهيز. | Your subscription is being prepared. The calendar will appear when preparation is complete. |
| `autoSelectionFailed` | تعذر إكمال الاختيار التلقائي. راجع حالة اليوم قبل المتابعة. | Automatic selection could not be completed. Review the day’s status before continuing. |
| `freezePreparationFailed` | تعذر تجهيز أيام التعويض. لم يُعتمد التغيير. | Replacement days could not be prepared. The change was not approved. |
| `replacementCapacityUnavailable` / `freeze.replacement_capacity_unavailable` | لا تتوفر سعة كافية لأيام التعويض. لم يُعتمد التغيير. | Replacement days do not have enough capacity. The change was not approved. |
| `replacementRestaurantsUnavailable` / `freeze.replacement_restaurants_unavailable` | لا يوجد مطعم مؤهل لخدمة أيام التعويض. لم يُعتمد التغيير. | No eligible restaurant can serve the replacement days. The change was not approved. |
| `emptyOptions` | لا توجد خيارات متاحة حاليًا. | No options are currently available. |
| `loading` | جارٍ التحميل… | Loading… |
| `requestFailed` | تعذر تحميل البيانات. حاول مجددًا. | Data could not be loaded. Try again. |

## قائمة قبول نهاية إلى نهاية — على فريق الموبايل تنفيذها

تحديث إصلاح التقويم 2026-10-10:

- منيو المطعم مرتبط بتاريخ اليوم نفسه. إذا غاب، يرجع `calendar.box_not_ready` ويظهر المطعم معطّلًا؛ لا تستخدم منيو يوم آخر أو نتائج مخزنة بتاريخ مختلف.
- الاختيار التلقائي يلتزم بأهلية قائمة المطاعم: لا يختار مطعمًا مشغولًا أو تجاوز حد التكرار عندما يوجد بديل مفعّل. تخفيف هذه القيود عند انعدام البدائل لا يتجاوز السعة أو الحساسية أو التغطية.
- قبل الحفظ/الإرسال يعاد التحقق من الأهلية، وعند الإرسال يعاد التحقق من منطقة العنوان الفعلي. عند `calendar.restaurant_coverage_unavailable` أعد قراءة اليوم والمطاعم؛ لا تعرض نجاح إرسال ولا تبقِ المطعم القديم كخيار متاح. القفل المجدول يجرب البديل مع نقل السعة، أو يسجّل طلب استبدال.
- اقرأ `failureReasonCode` من تفاصيل اليوم والملخص. لم يعد كل `replacement_required` يعني حساسية؛ تعامل مع السبب الفعلي. بيانات push الخاصة بالاستبدال تحتوي `failureReasonCode` عندما يكون محفوظًا؛ افتح اليوم للحصول على الحالة الحالية. الأيام القديمة التي لا تحمل سببًا محفوظًا تحتفظ بالتوافق السابق.
- لا تغيّر قواعد 72 ساعة للأيام العادية و24 ساعة بعد الدفع لليوم الأول. اعتمد `canEdit` والمهلة من الخادم، وأعد GET بعد كل عملية كتابة.

| كود السبب | العربية | English |
|---|---|---|
| `calendar.restaurant_coverage_unavailable` | لا يوجد مطعم مؤهل يغطي عنوان التوصيل لهذا اليوم. | No eligible restaurant covers your delivery address for this day. |
| `calendar.box_not_ready` | لا توجد قائمة معتمدة لهذا المطعم في يوم التوصيل. | This restaurant does not have an approved menu for the delivery day. |
| `RESTAURANT_CAPACITY_FULL` | سعة المطاعم ممتلئة أو غير متاحة في يوم التوصيل. | Restaurant capacity is full or unavailable for the delivery day. |
| `calendar.no_safe_box` | لا توجد وجبات كاملة آمنة للحساسية لهذا اليوم. | No full allergy-safe box is available for this day. |
| `calendar.restaurant_busy` | المطعم المختار مشغول في يوم التوصيل. | The selected restaurant is busy on the delivery day. |
| `calendar.quota_exhausted` | وصل المطعم المختار إلى حد تكرار الاختيار. | The selected restaurant has reached its selection repetition limit. |

اختبارات قبول إضافية للموبايل: منيو يوم مفقود مع منيو موجود في يوم آخر؛ مطعم مختار يصبح مشغولًا مع وجود بديل؛ بوكس محفوظ تُلغى تغطيته قبل القفل؛ تعطيل الاختيار التلقائي مع فقدان تغطية/منيو البوكس المحفوظ؛ تأكد من السبب الصحيح في التقويم والإشعار ومن عدم عرض نجاح وهمي. الجدول مواصفات ترجمة؛ يجب نقل النصوص إلى موارد الموبايل، مع تبديل اللغة الحي.

النشر يحتاج migration `20261010173000_AddSubscriptionDayFailureReasonCode` قبل خدمة الطلبات بالنسخة الجديدة، لأنها تضيف حقل سبب الفشل إلى الأيام. الباك يطبق migrations عند البدء افتراضيًا خارج بيئة Testing ما لم يُعطّل `Database:ApplyMigrationsOnStartup`. لم تُطبّق على قاعدة الإنتاج ضمن التعديل المحلي.

- تدرج المتطلبات السابقة للعميل: بدون عنوان (`address_required`, `missingStep: ADDRESS`, `canSelect=false`) -> مع عنوان وبدون حساسية (`allergies_required`, `missingStep: ALLERGIES`, `canSelect=false`) -> مع حساسية وبدون تفضيلات (`dietary_preferences_required`, `missingStep: DIETARY_PREFERENCES`, `canSelect=false`) -> اكتمال الحساب مع قوائم فارغة مؤكدة (`canSelect=true`). البطاقات غير المتاحة تظل ظاهرة ومعطلة.
- تسجيل دخول عميل: بلا عنوان ثم عنوان غير مغطى، ثم عنوان مغطى عمومًا ولكن غير مغطى لبرنامج/باقة/فئة؛ راجع الفرق بين رسائل الحالات ولا تختفِ الخيارات غير المتاحة.
- فئة ذات سعر مع عدد مطاعم صفر؛ الجمعة غير متاحة؛ تغطية موجودة مع غياب قائمة أو سعة أو وجبة آمنة أو سعر توريد في يوم واحد من الفترة؛ تحقق من تعطيل الاختيار والدفع والسبب الصحيح.
- اختيار كامل صالح، readiness ثم الملخص ثم checkout والدفع؛ رجوع البوابة مع fulfillment قيد التحضير، ونجاحه وفشله وإعادة المحاولة؛ لا توجد وجبات مؤكدة بسبب الدفع وحده.
- استئناف بحجز صالح يحافظ على أول توصيل؛ حجز منتهٍ أو تاريخ فائت يتطلب اختيارًا جديدًا دون تحريك التاريخ صامتًا.
- اليوم الأول عند حد 24 ساعة؛ اليوم التالي عند حد 72 ساعة؛ الزمن على الخادم وحدود الكويت؛ `canEdit=false` يمنع الكتابة حتى لو عداد الهاتف قديم.
- رفض سعة مطعم ثم نجاح بديل؛ استنفاد كل البدائل؛ وجبات غير آمنة؛ تحديث الحالة والفشل دون نجاح وهمي.
- تجميد ثم موافقة: إطلاق الحجز القديم وحجز الأيام الجديدة وقوائمها، مع عدم تأكيدها؛ فشل تجهيز يوم بديل يرجع العملية كاملة؛ أعد قراءة التقويم.
- العربية والإنجليزية و`ar → en → ar` دون إعادة تحميل، حفظ اللغة بعد التحديث، RTL/LTR، أرقام وتواريخ وعملة، قارئ الشاشة؛ بدّل اللغة أثناء loading/error/empty/dialog، وراجع المعلمات والرسائل المستمرة.

الأدلة الآلية والتقييدات تسجل في تقرير التنفيذ. هذه القائمة ليست ادعاء بأن التطبيق أو الدفع الحقيقي أو المهام المستضافة اجتازت اختبارًا حيًا.

دليل backend بتاريخ 2026-10-10: اختبار الانحدار قبل الإصلاح فشل في 3 حالات (الفئة غير المغطاة، الجمعة غير المتاحة، وغياب بيانات التوفر دون تاريخ). بعد الإصلاح اجتازت 142 حالة API ضمن اختبارات `CustomerSubscriptionFlowControllerTests`, `CustomerFamilySubscriptionFlowControllerTests`, `SubscriptionFlowPagedContractsTests`, `SubscriptionFlowMessageResourceTests` باستخدام مخرجات معزولة في `F:/MealMate/outputs/individual-discovery-tests`. هذه اختبارات عقود وخدمات backend وليست تشغيلًا للموبايل. فحص التعريب التشخيصي اجتاز self-test ولم يجد اختلافًا في تطابق القواميس؛ لم يُختبر تبديل اللغة في التطبيق.

### سعر الفئة غير المتاحة

عندما تكون الفئة غير قابلة للاختيار، يرجع الباك `totalPrice` و`dailyPrice` و`priceDifference` و`currencyCode` بقيمة `null` في مسارات الفئات القديمة والكتالوج. امسح السعر السابق من الكاش، ولا تعرض صفرًا أو زر اختيار، واحتفظ بسبب التعطيل. الفئات المعطلة لا تدخل في حساب فرق سعر الفئات المتاحة.
