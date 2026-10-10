# تسليم تكامل الاشتراك الفردي للموبايل — 2026-10-10

هذه وثيقة تنفيذ ومراجعة للموبايل، مبنية على العقود الموجودة في الـ backend. لم يُشغّل تطبيق الموبايل ولم تُختبر المهام المستضافة في بيئة إنتاج. أسماء الحقول والمسارات والأكواد أدناه معرفات تقنية لا تُترجم.

## اكتشاف الخيارات والتغطية

استخدم الرمز المصادق للعميل. العنوان يأتي من عنوان التوصيل الفعال المحفوظ؛ لا ترسل منطقة بديلة لتجاوز التغطية.

بادئة الاكتشاف: `/api/v1/customer/subscription-flow`.

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

أعد الفحص بعد تغيير العنوان أو البرنامج أو الباقة أو الفئة أو المدة أو الجمعة أو التاريخ أو تفضيلات الطعام. تجاهل استجابات الطلبات القديمة. قوائم الاستعراض القديمة للمطاعم ليست بديلًا لفحص كامل الفترة. مسار العائلة له معالجة عنوان لكل عضو ولا يطبق هذا التغيير عليه.

في readiness استخدم `canOpenSummary` لفتح الملخص و`canCheckout` لتفعيل الشراء؛ `isComplete` يصف اكتمال المتطلبات ولا يحل محل جاهزية الفترة. اعرض `checkoutBlockReason` أو `nextAction` بمعناه المحلي، ووجّه إلى `items[].endpoint` عند نقص خطوة. عند `401/403` عالج المصادقة والصلاحية، وعند `400/409` اعرض الكود الدلالي وأعد قراءة السياق، وعند `502` لبوابة الدفع أتح إعادة المحاولة دون اصطناع وسائل دفع. لا تحول خطأ الشبكة إلى قائمة خيارات فارغة؛ للحالة الفارغة استجابة ناجحة مستقلة.

## التاريخ والدفع والاستئناف

`startDate` في سياق checkout/summary هو تاريخ الاختيار الذي يستخدمه الخادم لحساب أول توصيل؛ اعرض التاريخ الناتج ولا تُعد حسابه محليًا. فحص الفئات للفترة يستخدم التاريخ الممرر إليه مباشرة؛ اربط الشاشة بتاريخ أول توصيل الذي أعاده الخادم قبل الحكم النهائي. لا تُدخل تاريخ أول التوصيل الناتج مرة أخرى بوصفه تاريخ اختيار جديد عند استئناف دفع قائم. حافظ على تقويم العمل والمنطقة الزمنية للكويت وعملة `KWD` ودقتها.

إنشاء الشراء: `POST /api/v1/customer/subscriptions/checkout`. حالة الدفع: `GET /api/v1/customer/subscriptions/{id}/payment`. معاينة الاستئناف: `GET /api/v1/customer/subscriptions/{id}/resume`؛ التنفيذ: `POST` للمسار نفسه. احترم `canPayDirectly` وسبب الاستئناف والتاريخ المقترح: عند انتهاء حجز السعة أو مرور تاريخ البداية اطلب عرضًا جديدًا، ولا تنقل التواريخ صامتًا. بعد رجوع بوابة الدفع أعد قراءة الحالة من الخادم؛ الرجوع وحده ليس إثبات دفع. نجاح الدفع لا يعني اكتمال تهيئة الوجبات: انتظر `isFulfillmentReady` أو حالة الخادم المناسبة قبل إتاحة تعديل التقويم. الفشل في التهيئة حالة قابلة للمراجعة وليست نجاحًا؛ إعادة التهيئة الإدارية تستخدم `POST /api/v1/admin/subscriptions/{id}/retry-fulfillment` ولا تُعرض كعملية عميل.

## التقويم والاختيار والتجميد

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
