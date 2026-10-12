# ترتيب الربط واختبارات قبول الموبايل

## تسلسل الفلو

1. اقرأ metadata ثم current، واعرض الأفراد مع lifecycle الفعلي.
2. أضف/عدّل الفرد من family-subscription-flow، واستخدم lookup/relationships/catalogs من السيرفر.
3. احفظ البرنامج والباندل والمدة والفئة والعنوان والتفضيلات من DTOs؛ حدّث current بعد الكتابة.
4. اجعل «اشترك الآن» اختيارًا صريحًا عبر checkout-selection. الفرد المؤجل يبقى في إدارة العيلة دون اشتراك مدفوع.
5. اقرأ summary واستعمل batchId/batchVersion الحاليين. عند blocker اعرض الرسالة المحلية ولا تبدأ الدفع.
6. اقرأ payment-methods وأرسل checkout مع clientContractVersion=1 ومفتاح idempotency ثابت لنفس النية. تغيير النية يحتاج مفتاحًا جديدًا.
7. افتح paymentUrl، ثم أعد قراءة بيانات السيرفر عند العودة/استئناف التطبيق. لا تفعّل الخدمة محليًا.
8. إدارة العيلة: home/members/detail ثم access/permissions/capabilities. أعد القراءة عند تعارض expectedVersion.
9. التقويم يستعمل subscriptionId الصحيح: calendar → days/{date} → restaurants → meals → restaurant/slot → complete/confirm. أسماء slot تأتي من السيرفر. إجراءات الكتابة تتبع capabilities وحالة اليوم.
10. التجديد: renewal-requests ثم family renewals/{renewalRequestId}/checkout. لا تستبدل الاشتراك الحالي بالقادم قبل تاريخ بدايته.
11. freeze/upgrade/cancel: استعمل preview/options/quote ثم request والعقد المقابل؛ بعد النجاح أعد قراءة management والفواتير. الاسترداد يتبع payer المثبت على السيرفر.

## قبول إلزامي

| السيناريو | النتيجة المطلوبة |
|---|---|
| إضافة فرد وتأجيله | يظهر في الإدارة، دون تفعيل خدمة أو مطالبة دفع فردية عامة |
| اختيار بعض الأفراد | الفاتورة تشمل الاختيار المحفوظ فقط |
| selected بدون حساب مرتبط | يمنع checkout مع family.member_account_required |
| batchVersion قديم | إعادة قراءة summary، دون إعادة دفع عمياء |
| انقطاع الشبكة بعد checkout | نفس idempotency key لنفس الطلب، دون إنشاء نية جديدة |
| رجوع WebView قبل callback | انتظار/قراءة حالة السيرفر، دون تفعيل محلي |
| paid + ReviewRequired | يعرض الدفع والمراجعة، دون منح الخدمة الجديدة |
| إيقاف إنشاء الفواتير | يعرض family.invoice_creation_stopped ويحافظ على متابعة الدفع الموجود |
| calendar grant فقط | لا يفتح الخطة أو الفواتير |
| family calendar grant فقط | لا يمنح family plans/invoices |
| رفض الفاتورة 404 | رسالة عدم الإتاحة دون retry تلقائي أو تسريب تفاصيل |
| pause access | لا يعرضه كـfreeze، والمدير يحتفظ بإدارته المصرح بها |
| renewal upcoming | الحالي مستمر حتى آخر يوم شامل؛ القادم مستقل |
| تعديل عنوان بيوم محمي | يعرض affected/skipped من السيرفر دون تعديل محلي لليوم المحمي |
| إلغاء ممول من المدير | لا يعد الفرد باسترداد؛ يعتمد payer/net وحالة التسوية المثبتة |
| تغيير صلاحيات أثناء الجلسة | إعادة القراءة عند conflict/denial وتحديث الأزرار |
| ar → en → ar | RTL/LTR والـerrors/dialogs/loading/empty تتحدث دون reload |

استخدم بيانات بيئة الاختبار الحقيقية؛ لا تعامل هذه السيناريوهات كسجلات نموذجية أو إثبات تنفيذ Flutter.


## تصحيح مهم للصلاحيات

إدارة المدير لا تعني أن المصدر يحظر كل إجراء على الفرد: FamilyActionPolicy يمنح بعض الكتابات للفرد على صفه عند منح المفتاح. اعتمد capabilities والـendpoint authorization لكل عملية؛ لا تستنتج حق كتابة من view grants.
