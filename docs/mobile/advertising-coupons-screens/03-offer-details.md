# 03 — تفاصيل العرض

مرّر معرّف العرض الذي أعاده الـ hub؛ لا تستخدم الاسم المترجم أو رابط الصورة كمعرّف.

## عقد API

- مسار تفاصيل العميل: `GET /api/v1/customer/offers/{offerId}/details`، مسار عام `[AllowAnonymous]`، ويعيد غلافًا بالشكل `{ "offer": OfferDto, "serverNowUtc": "..." }` عبر `CustomerOfferDetailsDto`.
- معاملات الاستعلام الاختيارية مطابقة لقائمة العروض: `cartAmount`, `durationDays`, `deliveryGovernorate`, `programId`, `bundleId`, `restaurantId`.
- يجب أن يستند هذا المسار إلى استعلام العروض المتاحة نفسه؛ العرض غير النشط أو المنتهي أو غير المؤهل لا يظهر كبيانات إدارية. عند عدم العثور على العرض، ارجع إلى القائمة واعرض رسالة تعذر التوفر مترجمة ثم حدّث البيانات.
- احتفظ بالمسار القديم `GET /api/v1/customer/offers/{id:guid}` لاستجابته المسطحة المتوافقة للخلف (`OfferDto`)؛ لا تعتمد عليه لشاشة التفاصيل ذات غلاف `serverNowUtc`.

## عرض التفاصيل والتنقل

اعرض الاسم والوصف والصورة والشروط والفترة والحدود وحقائق الخصم من `OfferDto` مع اختيار `nameAr/nameEn` و`descriptionAr/descriptionEn` و`termsAndConditionsAr/termsAndConditionsEn` حسب اللغة النشطة. لا تحسب سعر الشراء النهائي من نسبة الخصم. استخدم `sourceType` للتمييز بين عرض محفوظ (`Offer`) وإسقاط حملة (`Campaign`). حقول `eligibleProgramIds`, `eligibleBundleIds`, `eligibleRestaurantIds` إرشادات للتنقل؛ الخادم يظل صاحب قرار الأهلية.

إذا كان `sourceType` يساوي `Campaign`، استخدم `campaignId` للانتقال إلى [دفع الحملة](10-campaign-checkout.md). إسقاط الحملة غير المتزامن مع سجل عرض يستخدم معرف الحملة أيضًا في `offer.id`؛ أما العرض المحفوظ المرتبط بحملة فيحتفظ بمعرف العرض الخاص به، لذلك لا تفترض مساواة المعرفين. إذا كان العرض يتطلب رمزًا، اعرض `promoCode` عند وجوده وانتقل إلى تدفق الاشتراك الفردي ليعيد الخادم تقييمه. لا تعد بتطبيق العرض تلقائيًا.

## مثال طلب

قالب رمزي فقط. أرسل المرشحات المطابقة لاختيار العميل عند توفرها:

```http
GET /api/v1/customer/offers/{OFFER_ID}/details?durationDays=30&programId={PROGRAM_ID}&bundleId={BUNDLE_ID}
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
X-Bilingual: true
```

**مصادر التنفيذ:** [CustomerOffersController.cs](../../../src/MealMate.Api/Controllers/Customer/CustomerOffersController.cs)، [GetCustomerOfferDetailsQuery.cs](../../../src/MealMate.Application/Features/Offers/Queries/GetCustomerOfferDetailsQuery.cs)، [GetCustomerAvailableOffersQuery.cs](../../../src/MealMate.Application/Features/Offers/Queries/GetCustomerAvailableOffersQuery.cs)، [OfferDtos.cs](../../../src/MealMate.Application/Features/Offers/DTOs/OfferDtos.cs).
