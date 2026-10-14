# 11 — إعلانات المطعم: المزايدة والدفع والمادة والإيقاف

هذه شاشة المطعم وليست شاشة العميل الذي يشاهد الإعلان. جميع المسارات تحت `/api/v1/restaurant/advertising` وتتطلب توكن المطعم والصلاحية المناسبة؛ لا ترسل معرف مطعم لتجاوز الهوية.

## القراءة والمزايدة

- `GET /opportunities`: مساحات المزادات المتاحة وأسعار الحد الأدنى وخطوة المزايدة والفترات.
- `POST /auctions/{auctionId}/bids`: جسم `{amount, idempotencyKey}`. أعد استخدام المفتاح نفسه لإعادة محاولة نفس المزايدة، ولا تعرض قبولًا قبل نجاح الخادم.
- `GET /bids`: يرجع `MyAdBidDto` مع `bidId`, `auctionId`, `auctionStatus`, `amount`, `currencyCode`, `isWinning`, وفترات العرض. الحقل الجديد `placementId` معرف الموضع المملوك للمطعم أو `null`؛ لا تستخدم `bidId` مكانه.

## بدء الدفع والتحقق بعد العودة

1. للمزايدة الفائزة بحالة `AwaitingPayment`، استدعِ `POST /auctions/{auctionId}/payment-session` دون جسم.
2. الاستجابة `{paymentUrl, paymentHandle, amount, currencyCode}` تنشئ محاولة دفع حقيقية على الخادم. انتقل إلى `paymentUrl` بعد حفظ المرجع غير السري `paymentHandle` والمبلغ والعملة للمحاولة والمزاد نفسه؛ هذه ليست نتيجة الدفع.
3. بعد العودة، استدعِ `POST /auctions/{auctionId}/payment` بجسم `{paymentProvider: "Payzah", providerTransactionId: paymentHandle, amount, currencyCode}` مستخدمًا قيم الجلسة. الخادم يتحقق من حالة المزود ومن ربط المحاولة بالمزاد والمبلغ والعملة؛ لا يثبت نجاح الدفع اعتمادًا على بيانات الجهاز أو رابط العودة.
4. امسح مرجع الاستئناف عند نجاح التحقق، ثم أعد تحميل المزايدات. عند فشل الشبكة أو عدم تأكيد الدفع، احتفظ به لإعادة التحقق. لا تبدأ جلسة بديلة تلقائيًا عند وجود محاولة معلقة.

واجهة الويب تحتفظ بمرجع الاستئناف في `sessionStorage` حسب المزاد، لذلك يلزم الرجوع إلى نفس جلسة المتصفح. يحتاج تطبيق الموبايل تخزينًا مناسبًا للمحاولة يربطها بالمطعم والمزاد ويحذفها عند الخروج؛ لا يخزن مفاتيح المزود أو أسرار webhook.

## المادة والإيقاف

- بعد حالة `Awarded` يمكن رفع صورة مملوكة للحساب عبر API الرفع، ثم `POST /auctions/{auctionId}/creative` بالحقول `creativeStorageKey`, `targetType`, `targetId`, `displayStartsAtUtc`, `displayEndsAtUtc`. يجب أن تطابق الفترة فترة المزاد. الخادم يأخذ معرف المزاد من المسار.
- النوع المعروف `restaurant` يُطبع لهذه الصيغة؛ عند غياب `targetId` يأخذ الخادم معرف المطعم المالك المحفوظ. الإعلان يمر بالمراجعة قبل العرض.
- `POST /placements/{placementId}/stop` بجسم `{reason}`. استخدم `placementId` القادم من بيانات المطعم، وأخفِ زر الإيقاف عند غيابه. معالجة الاسترداد والتسوية تتبع سياسة الخادم؛ نجاح الإيقاف لا يعني تنفيذ استرداد تلقائي.

اعرض الرسائل من رموز دلالية تُترجم حسب اللغة النشطة؛ يجب أن يتغير نص الخطأ أو النجاح المفتوح عند `ar → en → ar`. تنسيق لوحة الويب الحالية عربي/إنجليزي بالتوقيت التجاري للكويت `+03:00`، مع إبقاء أوقات API بصيغة UTC. لست بحاجة إلى تحويل العملة حسب لغة الجهاز.

**المصدر:** [RestaurantAdvertisingController.cs](../../../src/MealMate.Api/Controllers/Restaurant/RestaurantAdvertisingController.cs)، [RestaurantAdvertisingCommands.cs](../../../src/MealMate.Application/Features/Advertising/Restaurant/RestaurantAdvertisingCommands.cs).
