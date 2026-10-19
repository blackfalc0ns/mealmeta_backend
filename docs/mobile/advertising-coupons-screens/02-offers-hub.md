# 02 — قائمة العروض

حقول المال في `OfferDto` هي `discountRate`, `discountAmountKwd`, `platformShareRate`, `restaurantShareRate`, `minOrderAmountKwd`. قيم النسب كسور، مثل `0.10` لخصم 10٪. حقول المبالغ التي تنتهي بـ`Kwd` تخص العملة المتعاقد عليها في هذا API، ولا تحولها إلى SAR تبعًا لإعداد الجهاز. هذه قواعد وحدود للعرض؛ السعر النهائي يأتي من معاينة الخادم ثم الدفع.

## عقد API

- `GET /api/v1/customer/offers/hub` يعيد `{ "offers": [...], "serverNowUtc": "..." }`. معاملات الاستعلام الاختيارية: `cartAmount` (عشري، الافتراضي 0)، `durationDays` (عدد صحيح، الافتراضي 0)، `deliveryGovernorate`، و`programId` و`bundleId` و`restaurantId` (معرّفات UUID).
- يستخدم المسار استعلام أهلية العروض المتاحة مع المرشحات نفسها. عند وجود جلسة عميل، يستخرج الخادم معرف العميل منها. الطلب المجهول لا يثبت هوية عميل حقيقي.
- يتضمن `OfferDto` الحقول `id`, `nameAr`, `nameEn`, `descriptionAr`, `descriptionEn`, `bannerImageUrl`, `status`, `type`, `discountType`, `fundingMode`, نسب/مبالغ الخصم والمساهمة، `startsAtUtc`, `endsAtUtc`, الحدود والعدادات، `customerSegment`, `campaignId`, `allowPromoCodeStacking`, `remainingSpots`, `promoCode`, `termsAndConditionsAr`, `termsAndConditionsEn`، إضافة إلى `eligibleProgramIds`, `eligibleBundleIds`, `eligibleRestaurantIds`, و`sourceType`.
- `sourceType` نص مصدره `OfferDto`، وقيمته الافتراضية `Offer`؛ إسقاط الحملة يحدد `Campaign`. تبقى حقول التعداد مثل `status/type/discountType/fundingMode/customerSegment` قيمًا تقنية بحسب إعداد JSON في API؛ لا تستبدلها بقيم مترجمة عند الإرسال.

## السلوك والتنقل

قائمة العروض للاستكشاف ولا تعني تطبيق الخصم تلقائيًا في الاشتراك العادي. يدخل العميل الرمز في ملخص تدفق الاشتراك الفردي عند الحاجة. إذا كان العرض مشتقًا من حملة، انتقل إلى تدفق دفع الحملة المنفصل باستخدام `campaignId`؛ في الإسقاط الحالي يتطابق مع `offer.id`.

اعرض الاسم والوصف والشروط وفق اللغة النشطة وبطريقة تفاعلية عند تبديل اللغة. احسب العد التنازلي باستخدام ساعة الخادم وحدود UTC. عند عدم وجود عروض اعرض حالة فراغ مترجمة. عند انتهاء العرض أو تغير أهليته، حدّث القائمة واشرح أن التوفر قد يتغير. مرر معرف العرض نفسه إلى [تفاصيل العرض](03-offer-details.md).

المسار القديم `GET /api/v1/customer/offers/available` يقبل معاملات الاستعلام ذاتها ويعيد قائمة مباشرة دون غلاف `serverNowUtc`. واجهات إدارة `PromoCode` الإدارية ليست واجهة استكشاف عامة للعميل.

## مثال طلب توضيحي

هذا قالب بمعرفات رمزية؛ استبدلها بقيم من سياق التطبيق/الخادم، واحذف المعاملات غير المستخدمة:

```http
GET /api/v1/customer/offers/hub?cartAmount=0&durationDays=30&programId={PROGRAM_ID}&bundleId={BUNDLE_ID}
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
X-Bilingual: true
```

**مصادر التنفيذ:** [CustomerOffersController.cs](../../../src/MealMate.Api/Controllers/Customer/CustomerOffersController.cs)، [GetCustomerOffersHubQuery.cs](../../../src/MealMate.Application/Features/Offers/Queries/GetCustomerOffersHubQuery.cs)، [GetCustomerAvailableOffersQuery.cs](../../../src/MealMate.Application/Features/Offers/Queries/GetCustomerAvailableOffersQuery.cs)، [OfferDtos.cs](../../../src/MealMate.Application/Features/Offers/DTOs/OfferDtos.cs).
