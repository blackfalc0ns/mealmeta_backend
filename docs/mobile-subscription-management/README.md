> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# ربط شاشة إدارة الاشتراك والفواتير

هذا المجلد هو المرجع التفصيلي لفريق الموبايل لتنفيذ الشاشة التالية:

- كارت الاشتراك الحالي.
- نسبة الاستخدام والأيام المتبقية.
- التجديد والتجميد وطلب الإلغاء.
- وسيلة الدفع عند توفر بياناتها.
- قائمة الفواتير وحالات الدفع.
- الاشتراكات السابقة والحالة الفارغة.

Base URL المستخدم حاليًا:

```text
http://maelmate.runasp.net/api/v1
```

## ترتيب قراءة الملفات

1. [01-api-contracts.md](./01-api-contracts.md): جميع المسارات والـrequests والـresponses بأمثلة JSON.
2. [02-screen-states-and-actions.md](./02-screen-states-and-actions.md): كيفية رسم الشاشة وتفعيل الأزرار والتعامل مع الحالات والأخطاء.
3. [03-flutter-dio-integration.md](./03-flutter-dio-integration.md): Models وData Source وRepository وCubit وأمثلة Dio.
4. [04-integration-checklist.md](./04-integration-checklist.md): سيناريوهات الاختبار ومعايير قبول الربط.

## Endpoint الرئيسي للشاشة

```http
GET /customer/subscriptions/management?includeHistory=true
```

هذا الطلب يكفي لتحميل الشاشة أول مرة؛ لا يحتاج الموبايل إلى تجميع النسبة أو الأيام أو صلاحيات الأزرار بنفسه.

## قواعد لا يجب تجاوزها في الموبايل

- استخدم `currentSubscription` للكارت الرئيسي، وليس أول عنصر داخل `subscriptions`.
- إذا كانت `currentSubscription=null` اعرض الحالة الفارغة حتى لو كانت هناك اشتراكات سابقة.
- لا تحسب الأيام أو النسبة محليًا؛ استخدم قيم الباك.
- لا تفعّل أي إجراء إلا من خلال `actions.can*`.
- لا تعتبر طلب الإلغاء إلغاءً نهائيًا؛ الاستجابة تعني أن الطلب تم تسجيله للمراجعة فقط.
- التجديد يفتح فلو اشتراك جديد ولا يسحب مبلغًا تلقائيًا.
- لا تعرض مطاعم جديدة داخل اشتراك قائم؛ المطعم المرجع Snapshot مثبتة وقت Checkout.
- لا تخزن أو ترسل بيانات بطاقة إلى MealMate؛ الدفع يتم داخل صفحة MyFatoorah.
- تعامل مع قيم Enum كنص، مع Parser يقبل الرقم مؤقتًا للتوافق مع الإصدارات القديمة.

## الملفات العامة المرتبطة

- الفلو قبل الدفع: [mobile-customer-subscription-flow.md](../mobile-customer-subscription-flow.md)
- الملخص المختصر القديم: [mobile-subscription-management.md](../mobile-subscription-management.md)
- ملف OpenAPI: [mealmate-api.openapi.json](../mealmate-api.openapi.json)

