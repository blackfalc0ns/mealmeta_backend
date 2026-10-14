# تفاصيل الفرد (08.03)

**السابق:** [الأفراد](./02-members.md) · **التالي:** [الوصول](./04-access.md)، [الصلاحيات](./05-permissions.md)، [التقويم](./06-calendar.md)، أو [M08.22](./11-member-edit.md).

## API والحقول

`GET /api/v1/customer/family/members/{memberId}` → `FamilyMemberDetailDto`: `memberId`, `displayName`, `role`, `relationshipCode`, `gender`, `ageYears`, `nutritionalGoalCode`, `activityLevel`, `setupStatus`, `programId`, `bundleId`, `selectedTier`, `durationDays`, `includeFriday`, `memberSubscriptionId`, `programNameAr/En`, `isPaused`, `hasSeparateAccount`, `linkedCustomerUserId`, phone fields, `remainingDays`, `startDate`, `endDate`, `canRenew`, `renewalPath`, `todayNutrition`, `lifecycle`, `upcomingSubscriptionId`, `upcomingStartDate`.

الحقول الاختيارية/null تظل nullable في UI. `todayNutrition:null` تعني لا توجد بيانات تغذية مؤكدة لهذا اليوم؛ لا تعرض دوائر بقيمة صفر. enums قد تسلسل كسلاسل enum names. قيم relationship تأتي من catalog لا من labels مترجمة. مثال جزئي توضيحي: `{"memberId":"11111111-1111-4111-8111-111111111111","displayName":"عضو توضيحي","role":"Member","relationshipCode":"child","gender":"Female","ageYears":12,"nutritionalGoalCode":"balanced","activityLevel":"Moderate","setupStatus":"Ready","programId":null,"bundleId":null,"selectedTier":null,"durationDays":null,"includeFriday":false,"memberSubscriptionId":null,"programNameAr":null,"programNameEn":null,"isPaused":false,"hasSeparateAccount":false,"linkedCustomerUserId":null,"phoneNumber":null,"remainingDays":0,"startDate":null,"endDate":null,"canRenew":false,"renewalPath":null,"todayNutrition":null,"upcomingSubscriptionId":"22222222-2222-4222-8222-222222222222","upcomingStartDate":"2026-11-01","lifecycle":{"setupStatus":"Ready","checkoutSelected":true,"paymentStatus":"Paid","fulfillmentStatus":"Pending","serviceStatus":"Scheduled","accessStatus":"Enabled","displayStatus":"PaidPreparingCalendar"}}`.

## أفعال وحالات

`effectiveCalendarSubscriptionId = memberSubscriptionId ?? upcomingSubscriptionId`. إذا الحالية null والقادمة موجودة، اعرض موعدها وحالة lifecycle الفعلية مثل `Scheduled`/`Preparing`؛ لا تصف العضو بأنه غير مشترك. لا تعرض today schedule أو زر renewal للخدمة القادمة. التجديد فقط إذا `canRenew:true`، واستخدم `renewalPath` من الرد حرفياً؛ لا تركب المسار. M08.22 يعدل حقول بيانات أساسية محددة فقط.

Loading، not-found state، retry على فشل الشبكة؛ عند تبديل memberId تجاهل response قديم. `family.member_not_found` 404 يمسح التفاصيل ويرجع للقائمة. `family.not_manager` 403. انتهاء جلسة 401. renewal code/http يجب أن يعرض بحسب response الفعلي، لا تنسخ code من نسخة فردية قديمة.

**قبول:** لا خلط بين IDs؛ upcoming يبقى ظاهر؛ لا DOB؛ null macro ليس صفراً؛ لا renew للخدمة القادمة؛ bilingual field اختياري حسب Accept-Language.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.

