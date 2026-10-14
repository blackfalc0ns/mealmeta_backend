# إدارة الأسرة — تسليم شاشات الموبايل

مرجع الشاشات الأحد عشر على API الإصدار `83fc8d05f05c478266d41e949b9e285c65b63bd8`. استخدم [تدفق الإدارة والعقد المشترك](./00-flow-and-contract.md) قبل التنفيذ.

- الرئيسية: [01](./01-home.md) · الأفراد: [02](./02-members.md) · تفاصيل العضو: [03](./03-member-detail.md)
- الوصول: [04](./04-access.md) · الصلاحيات: [05](./05-permissions.md) · التقويم: [06](./06-calendar.md)
- تفاصيل اليوم: [07](./07-day-details.md) · المطاعم: [08](./08-restaurants.md) · الوجبات: [09](./09-meals.md) · مراجعة وتأكيد: [10](./10-review-confirm.md)
- تعديل الفرد M08.22: [11](./11-member-edit.md) · قبول الموبايل: [12](./12-mobile-acceptance-checklist.md)

المسارات: `GET /api/v1/customer/family/*` للإدارة، `family-subscription-flow` لإضافة الفرد وخدمته، `customer/subscriptions/{subscriptionId}` للتقويم والوجبات. Authorization Bearer لمستخدم عميل، و`Accept-Language: ar|en`. لا تستخدم docs التاريخية `frontend/admin-dashboard` كمرجع حالي للعقد.

## قواعد مشتركة

- العضو الجاري والقادم مستقلان: `memberSubscriptionId` و`upcomingSubscriptionId`. القادم لا يعني غير مشترك عند current=null. لا تعرض renewal أو جدول اليوم الجاري للقادم.
- wallet اختصار unavailable (`wallet.available=false`)؛ لا توجد قيمة رصيد للعرض. كلمة مرور المدير غير مطلوبة؛ في تفعيل الحساب قد يعيد PUT كلمة مرور منشأة مرة واحدة فقط.
- يدعم JSON enum string حسب إعداد serializer؛ لا تفترض أرقام enum فقط. API identifiers/enums/slot/permission keys غير مترجمة.
- API code هو مفتاح المنطق؛ النص من ProblemDetails مترجم حسب لغة الطلب. كل copy موبايل يضاف في قاموس عربي وإنجليزي reactive.
- تاريخ الأعمال Asia/Kuwait وKWD بثلاث منازل عشرية حسب `currencyCode`.

**ملاحظة الأمثلة:** JSON في الملفات توضيحي اصطناعي، بمعرفات وقيم غير حقيقية؛ لا تستخدمه كبيانات إنتاج/seed. وثائق العقد لا تستبدل اختبارات التطبيق باللغتين.
