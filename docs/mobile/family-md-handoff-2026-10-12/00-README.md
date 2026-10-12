# تسليم الموبايل: Markdown وJSON داخل الشرح فقط

التاريخ 2026-10-12. هذه ملفات ربط **نسخة family-launch-readiness**، وليست تأكيدًا أن التغييرات منشورة على API الحالي. ثبت build المستهدف قبل الربط.

- `01-flow-and-changes.md`: ترتيب الفلو والتغييرات وسيناريوهات القبول.
- `02-setup-selection-payment.md`: تجهيز العيلة والاختيار والـsummary والدفع.
- `03-management-access-permissions.md`: الإدارة والحساب والصلاحيات وcapabilities.
- `04-calendar-restaurants-meals.md`: التقويم والاختيار والتأكيد وحالات اليوم.
- `05-subscription-renewal-upgrade-freeze-cancel-invoices.md`: العمليات المالية والخدمة والعنوان المدفوع.
- `06-addresses-and-food-preferences.md`: العناوين والكتالوجات والتفضيلات.
- `07-route-reference.md`: HTTP method والمسار وbody/query ونوع response لكل عملية.
- `08-errors-and-special-json.md`: أخطاء التعارض والرفض والـanonymous responses والـpartial patch.

كل JSON داخل fenced block. الرموز مثل <UUID> توضيحية؛ لا تُرسلها كقيم. الجداول توضح nullable/defaults، والأمثلة ليست بيانات حقيقية أو أسعارًا أو منح صلاحيات. لم يتم تنفيذ Flutter أو إرسال طلبات دفع. المرجع المصدر محلي، وليس OpenAPI مولدًا أو إثبات سلوك staging.
