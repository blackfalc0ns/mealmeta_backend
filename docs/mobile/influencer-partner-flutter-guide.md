# دليل الموبايل (Flutter) — نظام المؤثرين والشراكة التسويقية

> **الوثيقة المرجعية للمواصفات المعمارية:**  
> [`influencer-partner-mobile-spec.md`](./influencer-partner-mobile-spec.md)

**التاريخ:** 2026-09-09  
**الجمهور:** فريق Flutter / Customer App  
**الهدف:** الدليل العملي الكامل لإنشاء شاشات وموديلات وخدمات وضع المؤثر (Influencer Partner) في تطبيق الموبايل بدون أي تعارض أو فجوات برمجية.

---

## 1. Base URL وعقود الـ Endpoints

```text
/api/v1/influencer-partner
```

| الميثود | Endpoint | الاستخدام | الشاشة المرتبطة |
|---|---|---|---|
| `GET` | `/me` | جلب بيانات المؤثر، الكود، الحالة، الحساب البنكي الافتراضي | `CustomerProfileBanner`, `InfluencerProfileScreen` |
| `PUT` | `/me/social-links` | حفظ وتحديث وسائل التواصل الستة | `InfluencerSocialConnectScreen` |
| `GET` | `/me/home` | إجمالي العمولات، الـ 4 مؤشرات، الكود، وأحدث النشاطات | `InfluencerHomeScreen` |
| `GET` | `/me/links` | كود الخصم، رابط الإحالة، الـ QR، وقوالب نصوص المشاركة | `InfluencerLinksScreen` |
| `GET` | `/me/analytics?period={7d\|30d\|90d\|6m\|1y}` | رسوم بيانية للأرباح وتفاصيل الباقات الأكثر مبيعاً | `InfluencerAnalyticsScreen` |
| `GET` | `/me/wallet` | الرصيد المتاح، الأرباح المعلقة، ملخص الشهر، والمعاملات | `InfluencerWalletScreen` |
| `POST` | `/me/withdrawals` | إرسال طلب سحب جديد | `WithdrawalRequestScreen` |
| `GET` | `/me/withdrawals?status={All\|Requested\|...}` | قائمة سجل السحوبات مفلترة | `WithdrawalHistoryScreen` |
| `GET` | `/me/withdrawals/{id}` | تفاصيل عملية سحب واحدة | `WithdrawalDetailsScreen` |

---

## 2. Checklist للمطور في Flutter

### أ) إدارة الصلاحيات والتنقل (Auth & Mode Switch)
- [ ] فحص حقل `isInfluencer` في نموذج المستخدم `CurrentUserDto` بعد تسجيل الدخول.
- [ ] عرض بانر *"كن شريك مؤثر مع MealMate"* في شاشة البروفايل فقط إذا كان `isInfluencer == true`.
- [ ] عند النقر على البانر:
  - إذا كان `hasCompletedSocialOnboarding == false` → الانتقال إلى `InfluencerSocialConnectScreen`.
  - إذا كان `true` → الانتقال إلى `InfluencerDashboardShell`.
- [ ] توفير زر بارز في تبويب البروفايل الخاص بالمؤثر: *"التبديل إلى حساب العميل"* يعيد التنقل فوراً لشاشات طلب الوجبات العادية دون فقدان الجلسة (`Session`).

### ب) شاشات وموديلات الـ 5 Tabs
- [ ] إنشاء الـ Shell الرئيسي `InfluencerDashboardShell` مع `BottomNavigationBar` مخصص بـ 5 عناصر:
  1. الرئيسية (`Icons.home_outlined`)
  2. روابط (`Icons.link`)
  3. إحصائيات (`Icons.bar_chart_outlined`)
  4. المحفظة (`Icons.account_balance_wallet_outlined`)
  5. البروفايل (`Icons.person_outline`)
- [ ] دعم النسخ بنقرة واحدة لـ `influencerCode` و `referralLink` مع إظهار `SnackBar` تأكيدي.
- [ ] تكامل مكتبة توليد الـ QR Code (مثل `qr_flutter`) لعرض كود الـ QR ديناميكياً من رابط الإحالة.
- [ ] تكامل مكتبة المشاركة السريعة (مثل `share_plus` أو `url_launcher`) لمشاركة الرسائل المعدة مسبقاً عبر واتساب، تليجرام، وتطبيقات التواصل.

### ج) مسار السحب المالي (Withdrawal Flow)
- [ ] التحقق في شاشة `WithdrawalRequestScreen`:
  - المبلغ لا يقل عن `10.0 د.ك` (الحد الأدنى).
  - المبلغ لا يتجاوز `availableBalance`.
  - لا يوجد طلب سابق بحالة `Requested` (قيد المراجعة).
- [ ] تعبئة بيانات البنك تلقائياً إذا كانت موجودة مسبقاً في `defaultBankInfo`.
- [ ] شاشة المراجعة `WithdrawalReviewScreen` تعرض الصافي والرسوم (0 د.ك) وتنبيه الـ 1 إلى 3 أيام عمل.
- [ ] شاشة النجاح `WithdrawalSuccessScreen` تُظهر رقم العملية `WD-XXXX` والحالة المبدئية `قيد المراجعة`.
- [ ] شاشة سجل السحوبات تدعم 5 تبويبات للفلترة: (الكل، قيد المراجعة، جاري التحويل، مكتملة، مرفوضة).

---

## 3. نماذج البيانات الكاملة (Dart Models)

```dart
// 1. بروفايل المؤثر
class InfluencerProfileDto {
  final String influencerProfileId;
  final String displayName;
  final String influencerCode;
  final double commissionRate;
  final bool isActive;
  final bool hasCompletedSocialOnboarding;
  final BankInfoDto? bankInfo;
  final InfluencerSocialLinksDto socialLinks;

  InfluencerProfileDto({
    required this.influencerProfileId,
    required this.displayName,
    required this.influencerCode,
    required this.commissionRate,
    required this.isActive,
    required this.hasCompletedSocialOnboarding,
    this.bankInfo,
    required this.socialLinks,
  });

  factory InfluencerProfileDto.fromJson(Map<String, dynamic> json) => InfluencerProfileDto(
    influencerProfileId: json['influencerProfileId'] ?? '',
    displayName: json['displayName'] ?? '',
    influencerCode: json['influencerCode'] ?? '',
    commissionRate: (json['commissionRate'] as num?)?.toDouble() ?? 0.0,
    isActive: json['isActive'] ?? false,
    hasCompletedSocialOnboarding: json['hasCompletedSocialOnboarding'] ?? false,
    bankInfo: json['bankInfo'] != null ? BankInfoDto.fromJson(json['bankInfo']) : null,
    socialLinks: InfluencerSocialLinksDto.fromJson(json['socialLinks'] ?? {}),
  );
}

// 2. الحساب البنكي
class BankInfoDto {
  final String accountHolderName;
  final String bankName;
  final String iban;

  BankInfoDto({
    required this.accountHolderName,
    required this.bankName,
    required this.iban,
  });

  factory BankInfoDto.fromJson(Map<String, dynamic> json) => BankInfoDto(
    accountHolderName: json['accountHolderName'] ?? '',
    bankName: json['bankName'] ?? '',
    iban: json['iban'] ?? '',
  );

  Map<String, dynamic> toJson() => {
    'accountHolderName': accountHolderName,
    'bankName': bankName,
    'iban': iban,
  };
}

// 3. وسائل التواصل
class InfluencerSocialLinksDto {
  final String? tiktok;
  final String? instagram;
  final String? whatsapp;
  final String? telegram;
  final String? youtube;
  final String? snapchat;

  InfluencerSocialLinksDto({
    this.tiktok,
    this.instagram,
    this.whatsapp,
    this.telegram,
    this.youtube,
    this.snapchat,
  });

  factory InfluencerSocialLinksDto.fromJson(Map<String, dynamic> json) => InfluencerSocialLinksDto(
    tiktok: json['tiktok'],
    instagram: json['instagram'],
    whatsapp: json['whatsapp'],
    telegram: json['telegram'],
    youtube: json['youtube'],
    snapchat: json['snapchat'],
  );

  Map<String, dynamic> toJson() => {
    'tiktok': tiktok,
    'instagram': instagram,
    'whatsapp': whatsapp,
    'telegram': telegram,
    'youtube': youtube,
    'snapchat': snapchat,
  };
}

// 4. المحفظة
class InfluencerWalletDto {
  final double availableBalance;
  final double pendingBalance;
  final double lifetimeEarnings;
  final double thisMonthEarned;
  final double thisMonthWithdrawn;
  final bool hasActiveWithdrawalRequest;
  final double minimumWithdrawalAmount;
  final List<WalletTransactionDto> recentTransactions;

  InfluencerWalletDto({
    required this.availableBalance,
    required this.pendingBalance,
    required this.lifetimeEarnings,
    required this.thisMonthEarned,
    required this.thisMonthWithdrawn,
    required this.hasActiveWithdrawalRequest,
    required this.minimumWithdrawalAmount,
    required this.recentTransactions,
  });

  factory InfluencerWalletDto.fromJson(Map<String, dynamic> json) => InfluencerWalletDto(
    availableBalance: (json['availableBalance']?['amount'] as num?)?.toDouble() ?? 0.0,
    pendingBalance: (json['pendingBalance']?['amount'] as num?)?.toDouble() ?? 0.0,
    lifetimeEarnings: (json['lifetimeEarnings']?['amount'] as num?)?.toDouble() ?? 0.0,
    thisMonthEarned: (json['thisMonthEarned']?['amount'] as num?)?.toDouble() ?? 0.0,
    thisMonthWithdrawn: (json['thisMonthWithdrawn']?['amount'] as num?)?.toDouble() ?? 0.0,
    hasActiveWithdrawalRequest: json['hasActiveWithdrawalRequest'] ?? false,
    minimumWithdrawalAmount: (json['minimumWithdrawalAmount'] as num?)?.toDouble() ?? 10.0,
    recentTransactions: (json['recentTransactions'] as List? ?? [])
        .map((e) => WalletTransactionDto.fromJson(e))
        .toList(),
  );
}

// 5. سجل عملية سحب (Zero Hardcoded Localization)
class WithdrawalRecordDto {
  final String withdrawalId;
  final String referenceNumber;
  final double amount;
  final double transferFee;
  final double netAmount;
  final String status; // Requested | UnderReview | Approved | Paid | Rejected | Cancelled
  final String method; // BankTransfer
  final BankInfoDto bankDetails;
  final DateTime requestedAtUtc;
  final DateTime? reviewedAtUtc;
  final DateTime? completedAtUtc;
  final String? rejectionReason;
  final int estimatedMinBusinessDays;
  final int estimatedMaxBusinessDays;

  WithdrawalRecordDto({
    required this.withdrawalId,
    required this.referenceNumber,
    required this.amount,
    required this.transferFee,
    required this.netAmount,
    required this.status,
    required this.method,
    required this.bankDetails,
    required this.requestedAtUtc,
    this.reviewedAtUtc,
    this.completedAtUtc,
    this.rejectionReason,
    required this.estimatedMinBusinessDays,
    required this.estimatedMaxBusinessDays,
  });

  factory WithdrawalRecordDto.fromJson(Map<String, dynamic> json) => WithdrawalRecordDto(
    withdrawalId: json['withdrawalId'] ?? '',
    referenceNumber: json['referenceNumber'] ?? '',
    amount: (json['amount']?['amount'] as num?)?.toDouble() ?? 0.0,
    transferFee: (json['transferFee']?['amount'] as num?)?.toDouble() ?? 0.0,
    netAmount: (json['netAmount']?['amount'] as num?)?.toDouble() ?? 0.0,
    status: json['status'] ?? 'Requested',
    method: json['method'] ?? 'BankTransfer',
    bankDetails: BankInfoDto.fromJson(json['bankDetails'] ?? {}),
    requestedAtUtc: DateTime.tryParse(json['requestedAtUtc'] ?? '') ?? DateTime.now(),
    reviewedAtUtc: json['reviewedAtUtc'] != null ? DateTime.tryParse(json['reviewedAtUtc']) : null,
    completedAtUtc: json['completedAtUtc'] != null ? DateTime.tryParse(json['completedAtUtc']) : null,
    rejectionReason: json['rejectionReason'],
    estimatedMinBusinessDays: json['estimatedMinBusinessDays'] ?? 1,
    estimatedMaxBusinessDays: json['estimatedMaxBusinessDays'] ?? 3,
  );
}
```

### معالجة الترجمة داخل تطبيق Flutter (Zero Hardcoded Backend Strings)
> **هام للمطور:** الباكيند يرسل بيانات وأكواد فقط (`Machine Codes`) دون أي ترجمة نصوص ثابتة داخل الـ API. الترجمة تتم بالكامل داخل تطبيق Flutter بالاعتماد على حزم التعريب (`l10n` / `easy_localization` / `intl`):
> 
> ```dart
> // مثال دالة مساعدة لتعريب الحالات داخل Flutter
> String getWithdrawalStatusLocalized(BuildContext context, String status) {
>   switch (status) {
>     case 'Requested':
>       return context.tr('withdrawal_status_requested'); // قيد المراجعة
>     case 'UnderReview':
>       return context.tr('withdrawal_status_under_review'); // جاري المراجعة
>     case 'Approved':
>       return context.tr('withdrawal_status_approved'); // تمت الموافقة
>     case 'Paid':
>       return context.tr('withdrawal_status_paid'); // مكتملة / تم التحويل
>     case 'Rejected':
>       return context.tr('withdrawal_status_rejected'); // مرفوضة
>     case 'Cancelled':
>       return context.tr('withdrawal_status_cancelled'); // ملغية
>     default:
>       return status;
>   }
> }
> 
> String getEstimatedDaysText(BuildContext context, int minDays, int maxDays) {
>   return context.tr('estimated_business_days_range', args: [minDays.toString(), maxDays.toString()]);
>   // مثال: "من {0} إلى {1} أيام عمل من تاريخ الاعتماد"
> }
> ```

---

## 4. نموذج خدمة الاتصال بالشبكة (Dio Repository Service)

```dart
class InfluencerRepository {
  final Dio dio;

  InfluencerRepository(this.dio);

  Future<InfluencerProfileDto> getProfile() async {
    final response = await dio.get('/api/v1/influencer-partner/me');
    return InfluencerProfileDto.fromJson(response.data);
  }

  Future<void> updateSocialLinks(InfluencerSocialLinksDto links) async {
    await dio.put('/api/v1/influencer-partner/me/social-links', data: links.toJson());
  }

  Future<InfluencerWalletDto> getWallet() async {
    final response = await dio.get('/api/v1/influencer-partner/me/wallet');
    return InfluencerWalletDto.fromJson(response.data);
  }

  Future<WithdrawalRecordDto> submitWithdrawal({
    required double amount,
    required String bankName,
    required String accountHolderName,
    required String iban,
    bool saveAsDefault = true,
  }) async {
    final response = await dio.post(
      '/api/v1/influencer-partner/me/withdrawals',
      data: {
        'amount': amount,
        'withdrawalMethod': 'BankTransfer',
        'bankName': bankName,
        'accountHolderName': accountHolderName,
        'iban': iban,
        'saveAsDefaultBank': saveAsDefault,
      },
    );
    return WithdrawalRecordDto.fromJson(response.data);
  }

  Future<List<WithdrawalRecordDto>> getWithdrawals({String status = 'All'}) async {
    final response = await dio.get(
      '/api/v1/influencer-partner/me/withdrawals',
      queryParameters: {'status': status},
    );
    return (response.data as List).map((e) => WithdrawalRecordDto.fromJson(e)).toList();
  }

  Future<WithdrawalRecordDto> getWithdrawalDetails(String withdrawalId) async {
    final response = await dio.get('/api/v1/influencer-partner/me/withdrawals/$withdrawalId');
    return WithdrawalRecordDto.fromJson(response.data);
  }
}
```

---

## 5. معالجة الأخطاء الشائعة (Error Handlers)

| كود الخطأ | رسالة السيرفر (`error.code`) | رسالة الموبايل التوضيحية للمستخدم |
|---|---|---|
| `400` | `influencer.withdrawal_below_minimum` | *"عفواً، الحد الأدنى لمبلغ السحب هو 10 د.ك"* |
| `400` | `influencer.insufficient_available_balance` | *"المبلغ المطلوب أكبر من رصيدك المتاح للسحب"* |
| `409` | `influencer.active_withdrawal_exists` | *"لديك طلب سحب قيد المراجعة حالياً. يرجى الانتظار حتى اعتماده"* |
| `400` | `influencer.invalid_iban` | *"يرجى التأكد من كتابة رقم الآيبان بشكل صحيح"* |
