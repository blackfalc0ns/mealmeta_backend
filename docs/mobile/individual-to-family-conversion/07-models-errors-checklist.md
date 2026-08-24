# 07 — Models · أكواد الأخطاء · Checklist للموبايل

## 1) حدّث / أضف Models

### `SubscriptionTypeOption` — حقول إضافية

```text
disabledReason: String?
requiresConversion: bool          // default false
conversionEndpoint: String?
```

### `ConversionOptions` — جديد

```text
subscriptionId, status, options[]
option: code, available, titleAr, titleEn, action?, disabledReason?
```

### `ConvertFromIndividualRequest` — جديد (اختياري)

```text
name?, familyPackageId?
```

### `FamilyCheckoutSummary` — حقول إضافية

```text
managerPrepaid: bool
managerSubscriptionId: String? / Guid?
newMembersCount: int
newMembersSubtotal: double
```

## 2) Endpoints جديدة / متغيرة

| Method | Path | جديد؟ |
|--------|------|--------|
| GET | `/api/v1/customer/subscription-flow/types` | حقول إضافية |
| GET | `/api/v1/customer/subscriptions/current/conversion-options` | **جديد** |
| POST | `/api/v1/customer/family-subscription-flow/convert-from-individual` | **جديد** |
| POST | `/api/v1/customer/family-subscription-flow/abandon-draft` | **جديد** |
| GET | `/api/v1/customer/family-subscription-flow/summary` | حقول إضافية |
| PUT | `/api/v1/customer/family-subscription-flow/members/{id}/plan` | سلوك قفل للمدير المحوّل |
| PUT | `/api/v1/customer/family-subscription-flow/group` | قد يرجع `conversion.convert_required` |

## 3) أكواد أخطاء للتعامل في UI

| كود | شاشة مقترحة |
|-----|-------------|
| `checkout.existing_subscription` | أنواع الاشتراك / محاولة checkout فردي |
| `conversion.convert_required` | محاولة `PUT group` بدل التحويل |
| `conversion.no_individual_subscription` | Chooser / convert |
| `conversion.already_in_family` | convert |
| `conversion.individual_not_convertible` | Chooser (PendingPayment) |
| `conversion.manager_plan_locked` | تعديل خطة المدير |
| `change_individual_plan.not_available` | خيار معطل في Chooser |
| `family.not_draft` | abandon على غير Draft |

## 4) Checklist تنفيذ Flutter

- [ ] تحديث parsing لـ `types`
- [ ] تعطيل فردي عند `!isAvailable`
- [ ] توجيه عائلة → Chooser عند `requiresConversion`
- [ ] شاشة Chooser + استدعاء `conversion-options`
- [ ] زر التحويل → `convert-from-individual` ثم فلو العائلة الحالي
- [ ] خيار «تغيير خطتي» ظاهر ومعطل
- [ ] قفل UI خطة المدير بعد التحويل
- [ ] حقول prepaid في شاشة الملخص/الدفع
- [ ] زر abandon مسودة + تأكيد
- [ ] معالجة `409 conversion.convert_required` لو مسار قديم لسه يستدعي `PUT group`
- [ ] عدم كسر فلو العائلة للعميل **بدون** اشتراك فردي

## 5) اختبارات يدوية سريعة

1. عميل بدون اشتراك → types عادي، عائلة بدون `requiresConversion`.
2. عميل Active فردي → فردي معطل، عائلة `requiresConversion`، convert ينجح، المدير سعره 0.
3. PendingPayment فقط → convert غير متاح.
4. بعد convert → تعديل خطة المدير → 409 أو UI مقفول.
5. abandon → الفردي يرجع ويظهر في الإدارة.
6. عضو جديد + checkout → المبلغ = الأعضاء الجدد فقط؛ تقويم المدير مستمر.
