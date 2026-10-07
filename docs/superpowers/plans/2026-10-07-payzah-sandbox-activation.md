# Payzah Sandbox Activation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Follow repository delegation requirements; this plan does not authorize deployment or database changes by itself.

**Goal:** تشغيل دفع Payzah التجريبي في MealMate، بإدخال مفتاح الاختبار من الأدمن، وإيقاف البوابة القديمة في بيئة التشغيل مع الحفاظ على التاريخ المالي.

**Architecture:** التكامل الجديد موجود بالفعل: الأدمن يحفظ المفتاح مشفّرًا، والباك إند ينشئ محاولة دفع محفوظة ويعيد رابط Payzah المستضاف. تأكيد الاشتراك يعتمد على التحقق من حالة الدفع من خادم Payzah وربطها بالمحاولة الحالية. هذه خطة تفعيل وفحص للتكامل الموجود، وليست إعادة تنفيذه.

**Tech Stack:** ASP.NET Core / .NET 9، EF Core / PostgreSQL، Angular، Payzah hosted checkout.

**Spec:** F:/MealMate/backend/docs/superpowers/specs/2026-10-06-payzah-payment-gateway.md

## Global Constraints

- Preserve applied migrations and historical payment identifiers/providers.
- No mock business data in runtime.
- Old active provider code and credentials removed.
- مفتاح الاختبار يُدخل في الأدمن ولا يُكتب في Git أو المستندات أو المحادثة.
- تجربة Payzah تستخدم طريقة الدفع `3`؛ طريقة الإكمال المحلية `-1` لا تثبت نجاح التكامل مع Payzah.
- لا يبدأ اختبار التاجر قبل تحديد بيئة الاختبار وقاعدة البيانات المقصودة.
- لا يُعتبر ظهور «جاهز لإنشاء طلب دفع» دليلًا على صلاحية المفتاح؛ الإثبات هو دورة دفع Sandbox مكتملة.

---

## الحالة الحالية

- [x] تكامل Payzah جديد وإزالة MyFatoorah من مسار التشغيل والواجهة النشطة.
- [x] صفحة الأدمن `/admin/settings/payment-gateways` تدعم Sandbox ومفتاح Private Key.
- [x] API الإعدادات `/api/v1/settings/payment_gateways/payzah` يحفظ المفتاح مشفّرًا؛ الصلاحية المطلوبة `SettingsManage`.
- [x] ترحيل `20261006200145_ReplaceActiveGatewayWithPayzah` مُجهّز ومراجع، ولم يُطبّق في العمل السابق.
- [x] التحقق السابق: 47 اختبار Unit و61 اختبار API نجحت، وبناء لوحة التحكم نجح.
- [ ] حل خطأ البناء السابق `CS8954` في ملف المكالمات؛ الاختبارات السابقة استخدمت فصلًا مؤقتًا للمصدر دون تعديل الملف الأصلي.
- [ ] تطبيق الترحيل على قاعدة البيانات المختارة، ونشر النسخة، وإدخال مفتاح التاجر، وتجربة Payzah فعلية في Sandbox.

## Task 1: تجهيز نسخة قابلة للتشغيل

**Files:**
- Review: F:/MealMate/backend/src/MealMate.Application/Features/VoiceCalls/V2/Commands/VoiceCallV2LifecycleCommands.cs
- Review: F:/MealMate/backend/src/MealMate.Api/Program.cs
- Review: F:/MealMate/frontend/admin-dashboard/src/app/features/admin/settings/pages/payment-gateways-page/payment-gateways-page.component.ts

**Deliverable:** بناء عادي ناجح للـAPI والأدمن، بدون الاعتماد على تجاوز اختبار مؤقت.

- [ ] إصلاح تعريفَي namespace في ملف المكالمات: فصل أنواع namespace الاستعلامات في ملف مستقل، مع الحفاظ على السلوك والـusings والأنواع الموجودة. التغيير الخاص بالمكالمات يُراجع منفصلًا عن الدفع.
- [ ] من `F:/MealMate/backend` شغّل:

```powershell
dotnet build src/MealMate.Api/MealMate.Api.csproj --nologo
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --nologo --filter "FullyQualifiedName~PayzahReceiptTests|FullyQualifiedName~SubscriptionUpgradePaymentProcessorTests|FullyQualifiedName~SubscriptionFlowGapTests|FullyQualifiedName~SandboxCheckoutTests|FullyQualifiedName~SubscriptionPurchaseAndSnapshotTests"
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --nologo --filter "FullyQualifiedName~PayzahPaymentReturnControllerTests|FullyQualifiedName~CustomerCheckoutControllerTests|FullyQualifiedName~CustomerSubscriptionFlowControllerTests|FullyQualifiedName~CustomerCheckoutPaymentStatusTests|FullyQualifiedName~CustomerCheckoutGateControllerTests|FullyQualifiedName~CheckoutCouponFlowTests"
```

- [ ] من `F:/MealMate/frontend/admin-dashboard` شغّل `npm run typecheck` ثم `npm run build`.
- [ ] شرط النجاح: لا أخطاء بناء، وكل الاختبارات المختارة ناجحة، ولا تغيير غير مقصود في أعمال المشروع الأخرى.

## Task 2: تجهيز قاعدة البيانات ورابط العودة وإزالة الإعدادات القديمة

**Files:**
- Review: F:/MealMate/backend/src/MealMate.Persistence/Migrations/20261006200145_ReplaceActiveGatewayWithPayzah.cs
- Configure through deployment settings: `Payments:PublicBaseUrl`.
- Review: F:/MealMate/backend/src/MealMate.Api/Program.cs، خصوصًا `Database:ApplyMigrations` و`Database:ApplyMigrationsOnStartup` ومسار التهيئة.

**Deliverable:** نسخة API تخدم جداول Payzah ورابط العودة عبر HTTPS على البيئة المحددة.

- [ ] تحديد عنوان الـAPI المنشور وقاعدة البيانات المستهدفة، وأخذ نسخة احتياطية قبل إزالة جدول الإعدادات القديمة.
- [ ] حصر جميع الترحيلات غير المطبقة؛ تشغيل التطبيق قد يطبق ترحيلات تلقائيًا، فلا تستخدم تشغيله لاكتشاف الحالة قبل مراجعة إعدادات الترحيل.
- [ ] توليد SQL للدلتا الخاصة بالبوابة للمراجعة من `F:/MealMate/backend` بعد نجاح البناء:

```powershell
dotnet ef migrations script 20261006055704_AddFamilyPaymentAttemptIdentity 20261006200145_ReplaceActiveGatewayWithPayzah --project src/MealMate.Persistence --startup-project src/MealMate.Api --no-build --output payzah-gateway-review.sql
```

- [ ] التأكد أن الدلتا تنشئ `PayzahGatewayConfigs` و`PaymentGatewayAttempts`، وتحذف `MyFatoorahGatewayConfigs`، وتوقف إعداد البوابة المحاسبي القديم، وتنقل استرجاعاتها المعلّقة للمراجعة. لا تحذف جداول المعاملات أو الترحيلات التاريخية.
- [ ] تطبيق الترحيلات المراجعة في البيئة المحددة من خلال إجراء النشر المعتمد. استهداف ترحيل Payzah يطبق أيضًا أي ترحيلات سابقة غير مطبقة، فلا تتجاوز مراجعتها.
- [ ] ضبط متغير بيئة السيرفر `Payments__PublicBaseUrl` على عنوان الـAPI الحقيقي بـHTTPS، بدون مسار endpoint العودة؛ الكود يضيف `/api/v1/payments/payzah/return`.
- [ ] إزالة أسرار MyFatoorah من إعدادات الاستضافة/Secret Store إن كانت ما زالت موجودة؛ حذفها من الكود لا يزيلها من الاستضافة تلقائيًا.
- [ ] نشر الـAPI والأدمن المتوافقين ثم التأكد أن GET إعدادات Payzah يعمل، وأن رابط العودة متاح من الإنترنت. فتح رابط العودة بدون محاولة دفع لا يجب أن يظهر نجاحًا.

## Task 3: إدخال مفتاح الاختبار من الأدمن

**Files:**
- Existing UI: F:/MealMate/frontend/admin-dashboard/src/app/features/admin/settings/pages/payment-gateways-page/payment-gateways-page.component.ts
- Existing API: F:/MealMate/backend/src/MealMate.Api/Controllers/V1/PaymentGatewaysSettingsController.cs
- Existing settings: F:/MealMate/backend/src/MealMate.Application/Features/Settings/PayzahSettingsHandlers.cs

**Deliverable:** مفتاح حساب Sandbox محفوظ مشفّرًا، مع إعداد صحيح للبيئة ورابط العودة.

- [ ] الحصول على **Private Key الخاص بـSandbox** من حساب التاجر في Payzah، وليس مفتاح التشغيل الفعلي.
- [ ] تسجيل الدخول بالأدمن الذي يملك صلاحية إدارة الإعدادات، ثم فتح **الإعدادات ← إعدادات بوابة مدفوعات Payzah**، أو `/admin/settings/payment-gateways`.
- [ ] تفعيل **بيئة الاختبار Sandbox**، وإدخال المفتاح كما استُلم في خانة **المفتاح الخاص Private Key**، ثم الضغط على **حفظ الإعدادات**. لا تحوّله يدويًا إلى Base64؛ الباك إند ينفذ ترميز Authorization.
- [ ] التأكد أن الحقل يفرغ بعد الحفظ، وأن المعروض للمفتاح عبارة عن إخفاء فقط، وأن رابط العودة يشير إلى الـAPI المنشور الصحيح.
- [ ] تحديث الصفحة والتأكد أن `isSandbox=true` و`hasPrivateKey=true` و`isReady=true` في استجابة الإعدادات، وأن الاستجابة لا تحتوي المفتاح الخام أو المشفّر.
- [ ] عند فشل الحفظ، فحص صلاحيات الأدمن ونتيجة الطلب والجداول ومفتاح تشفير الأسرار قبل بدء الدفع. لا تستخدم endpoint اختبار الاتصال كإثبات؛ الاختبار الآمن للاتصال غير متاح في التكامل الحالي.

## Task 4: أول دورة دفع وتجارب الفشل والتكرار

**Files / contract:**
- F:/MealMate/backend/docs/mobile/payzah-payments.md
- F:/MealMate/backend/src/MealMate.Infrastructure/Services/PayzahPaymentService.cs
- F:/MealMate/backend/src/MealMate.Application/Features/CustomerCheckout/SubscriptionPaymentFlowServices.cs

**Deliverable:** دورة حقيقية عبر خوادم Payzah Sandbox تثبت الإنشاء والعودة والتحقق وتنفيذ الاشتراك مرة واحدة.

- [ ] استخدام حساب عميل واشتراك وأسعار وسعة موجودة فعليًا في قاعدة بيانات البيئة؛ لا تضف بيانات أعمال وهمية لتجاوز متطلبات checkout.
- [ ] التأكد أن تطبيق العميل يتعامل مع طريقة `3` / `PAYZAH` من endpoint طرق الدفع، ويفتح `paymentUrl`، ويدعم روابط `success` و`error` و`pending` و`review` الموثقة.
- [ ] إنشاء checkout من التطبيق والتأكد من حفظ TrackId وPaymentID الأصلي والمبلغ والعملة والمرجع في `PaymentGatewayAttempts` قبل معالجة العودة.
- [ ] إكمال الدفع في صفحة Payzah باستخدام بيانات الاختبار التي يوفرها التاجر/Payzah للـSandbox؛ لا تستخدم أرقام بطاقات مخترعة أو بطاقات تشغيل حقيقية.
- [ ] التأكد أن نتيجة Payzah موثقة في لوحة التاجر، ثم أن السيرفر يقرأ حالتها ويؤكد الاشتراك ويحفظ إيصالًا واحدًا؛ الرجوع للمتصفح وحده لا يكفي.
- [ ] إعادة فتح رابط العودة نفسه: لا يتكرر تأكيد السعة أو الإيصال أو تفعيل الاشتراك.
- [ ] إلغاء محاولة جديدة من صفحة Payzah والتحقق من معالجة الفشل بعد التحقق من الخادم. لو النتيجة غير محسومة، يظهر pending/review بدل نجاح أو طلب دفع جديد.
- [ ] اختبار الفردي ثم العائلي ثم ترقية الاشتراك على بيانات مناسبة موجودة، والتأكد أن إشعار محاولة قديمة لا يغير محاولة أحدث أو حجوزاتها.
- [ ] إن جُرّب الاسترجاع في Sandbox، يبقى محتاج مراجعة التاجر حتى التأكيد، ولا يُعاد إرسال الطلب تلقائيًا. لا تعتبر قبول الطلب تأكيدًا لإرجاع الأموال.

## Task 5: اعتماد Sandbox ثم التحويل للتشغيل لاحقًا

- [ ] تسجيل نتائج التجارب الفعلية ومعرّفات المحاولات في سجل داخلي دون المفاتيح أو بيانات البطاقة.
- [ ] اعتماد Sandbox فقط بعد نجاح البناء والترحيل والحفظ ودورة الدفع والتكرار والفشل.
- [ ] التحويل إلى Live يكون خطوة منفصلة: إلغاء Sandbox وإدخال المفتاح الخاص ببيئة Live ثم إجراء التحقق المعتمد للتاجر. الواجهة تطلب مفتاحًا جديدًا عند تبديل البيئة.
- [ ] عدم تبديل البيئة بينما توجد محاولات Sandbox تحتاج تحققًا؛ الكود يمنع التحقق من محاولة باستخدام بيئة مختلفة. إن ظهرت مشكلة، توقف إنشاء دفعات جديدة وراجع المعاملات المسجلة قبل إعادة المحاولة أو الرجوع لنسخة أقدم.

## Definition of Done

يستطيع الأدمن حفظ مفتاح Sandbox بأمان، ويستطيع العميل إنشاء دفع Payzah وإكماله، ويؤكد الخادم النتيجة وينفذ الاشتراك مرة واحدة. لا تظهر MyFatoorah كخيار نشط، ولا تستخدم أسرارها أو endpoints القديمة، ويظل التاريخ المالي محفوظًا. حالة التطبيق في شاشة الأدمن لا تُعتبر إثباتًا قبل تجربة التاجر الفعلية.

## Mock Data Check

- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
