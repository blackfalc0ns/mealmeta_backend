# عقد الإعلانات للمطعم والعميل — handoff 2026-10-13

> دورة حياة حقيقية: مزاد مفتوح → فائز ينتظر الدفع → دفع مؤكد → مادة → مراجعة
> أدمن → مجدول → نشط → منتهي/موقوف. لا عرض بدون دفع مؤكد واعتماد.

## أدمن — `api/v1/admin/advertising` (صلاحيات Campaigns*)
- `GET/PUT settings` — المفتاح العام `auctionsEnabled` مع `expectedVersion`
  و`reason` إلزامي (versioned optimistic).
- `POST/GET commercial-policy` — اعتماد سياسة: `currencyCode, minimumBid, bidStep,
  winnerPaymentDeadlineMinutes, stopSettlementRule` (NoRefund |
  ProrataUnservedDays | FullRefundBeforeStart). لا مزاد بدون سياسة معتمدة.
- `POST/GET spaces` — `placementKey` تقني (lowercase/dot/dash)، `nameAr/nameEn`، أبعاد.
- `POST/GET auctions` — نوافذ منفصلة: مزايدة `[bidsOpen, bidsClose)` ثم عرض
  `[displayStarts, displayEnds)`؛ الإغلاق قبل بدء العرض.
- `POST placements/{id}/review {approve|notes}` و `POST placements/{id}/stop {reason}`.
- `POST sweep` — إغلاق/تفعيل/إنهاء يدوي (والـ worker يشغّله كل دقيقة).

## مطعم — `api/v1/restaurant/advertising` (Authorize، هوية المطعم من التوكن)
- `GET opportunities` — المزادات المفتوحة + minimumBid/bidStep من السياسة + أعلى
  مزايدة **لي** فقط (لا هويات منافسين أبدًا).
- `POST auctions/{id}/bids {amount, idempotencyKey}` — key يولده التطبيق مرة لكل
  محاولة ويزيد فقط عند تغيير المبلغ. نفس key = نفس النتيجة (idempotent replay).
  أخطاء: `BidBelowMinimum`, `BidStepInvalid`, `BiddingClosed`, `BiddingNotOpen`,
  `AuctionsDisabled`, `AuctionNotOpen`.
- `GET bids` — مزايداتي وحالتها (IsWinning بعد الإغلاق).
- `POST auctions/{id}/payment {paymentProvider, providerTransactionId, amount, currencyCode}`
  — بعد تنفيذ الدفع عبر بوابة الدفع المعتادة؛ يطابق مبلغ/عملة الفوز، idempotent
  بـ providerTransactionId، ويرفع حالة المزاد إلى Awarded.
- `POST auctions/{id}/creative {creativeStorageKey (من رفع الملفات الحالي),
  targetType, targetId, displayStartsAtUtc, displayEndsAtUtc}` — يبقى UnderReview
  حتى اعتماد الأدمن. `PaymentRequired` إن لم يكتمل الدفع.
- `POST placements/{id}/stop {reason}` — إيقاف اختياري بتسوية وفق السياسة
  المعتمدة (ledger يسجل الاسترداد وينفذ عبر job إعادة المحاولة).

## عميل — `api/v1/customer/advertising`
- `GET placements?placementKey=` — المدفوع المعتمد النشط داخل نافذته فقط،
  `isSponsored:true` دائمًا، رابط قراءة مؤقت. المفتاح العام المغلق **لا يخفي**
  الإعلانات المدفوعة — الإيقاف المنفصل فقط يخفيها.
- `POST events {placementId, eventId, eventType}` — idempotent.

## قواعد ثابتة
- المزايدة فترة `[open, close)` بوقت الخادم؛ عند التعادل يفوز **أسبق قبول**.
- الفائز غير الدافع بعد مهلة السياسة ⇒ المزاد NoFill؛ **لا خصم تلقائي من
  مزايد آخر أبدًا**.
- عمولات وحد العرض تأتي من السياسة المعتمدة فقط — لا قيم افتراضية في العقد.
