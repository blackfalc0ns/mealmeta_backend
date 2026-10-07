> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# ربط Flutter باستخدام Dio وCubit

الأمثلة التالية مرجعية ويمكن تكييف أسماء المجلدات مع Clean Architecture الموجودة في تطبيق الموبايل.

## 1. هيكل الملفات المقترح

```text
features/subscription_management/
├── data/
│   ├── data_sources/subscription_management_remote_data_source.dart
│   ├── models/subscription_management_models.dart
│   └── repositories/subscription_management_repository_impl.dart
├── domain/
│   └── repositories/subscription_management_repository.dart
└── presentation/
    ├── cubit/subscription_management_cubit.dart
    ├── cubit/subscription_management_state.dart
    └── pages/subscription_management_page.dart
```

لا تضع حساب النسبة أو صلاحيات الأزرار داخل Widget.

## 2. إعداد Dio

```dart
final dio = Dio(
  BaseOptions(
    baseUrl: 'http://maelmate.runasp.net/api/v1',
    connectTimeout: const Duration(seconds: 30),
    receiveTimeout: const Duration(seconds: 30),
    sendTimeout: const Duration(seconds: 30),
    headers: const {
      'Accept': 'application/json',
      'Content-Type': 'application/json',
    },
  ),
);
```

أضف Interceptor الموجود في المشروع لإرسال:

```dart
options.headers['Authorization'] = 'Bearer $accessToken';
options.headers['Accept-Language'] = currentLanguageCode;
```

لا تطبع Header `Authorization` في Logger على Production.

## 3. Helpers للـJSON والـEnums

خلال مرحلة الانتقال، اجعل Parser يقبل النص والرقم:

```dart
String? readNullableString(dynamic value) {
  if (value == null) return null;
  final text = value.toString().trim();
  return text.isEmpty ? null : text;
}

int readInt(dynamic value, {int fallback = 0}) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '') ?? fallback;
}

double readDouble(dynamic value, {double fallback = 0}) {
  if (value is num) return value.toDouble();
  return double.tryParse(value?.toString() ?? '') ?? fallback;
}

bool readBool(dynamic value, {bool fallback = false}) {
  if (value is bool) return value;
  if (value is num) return value != 0;
  final text = value?.toString().toLowerCase();
  if (text == 'true' || text == '1') return true;
  if (text == 'false' || text == '0') return false;
  return fallback;
}

String readEnumCode(dynamic value, Map<int, String> legacyValues) {
  if (value is num) {
    return legacyValues[value.toInt()] ?? 'Unknown';
  }
  return value?.toString() ?? 'Unknown';
}
```

Legacy Subscription Status mapping:

```dart
const legacySubscriptionStatuses = <int, String>{
  1: 'Draft',
  2: 'PendingPayment',
  3: 'Paid',
  4: 'Scheduled',
  5: 'Active',
  6: 'Frozen',
  7: 'Completed',
  8: 'Cancelled',
  9: 'CancellationPending',
  10: 'Upgraded',
  11: 'Expired',
  12: 'SuspendedForReview',
  13: 'PaymentFailed',
};
```

Legacy Restaurant Tier mapping:

```dart
const legacyRestaurantTiers = <int, String>{
  1: 'Basic',
  2: 'Platinum',
  3: 'Elite',
};
```

## 4. Models

### Root response

```dart
class SubscriptionManagementScreenModel {
  const SubscriptionManagementScreenModel({
    required this.currentSubscription,
    required this.subscriptions,
    required this.invoices,
    required this.emptyStateCode,
  });

  final ManagedSubscriptionModel? currentSubscription;
  final List<ManagedSubscriptionModel> subscriptions;
  final List<SubscriptionInvoiceModel> invoices;
  final String? emptyStateCode;

  factory SubscriptionManagementScreenModel.fromJson(
    Map<String, dynamic> json,
  ) {
    final currentJson = json['currentSubscription'];
    return SubscriptionManagementScreenModel(
      currentSubscription: currentJson is Map<String, dynamic>
          ? ManagedSubscriptionModel.fromJson(currentJson)
          : null,
      subscriptions: (json['subscriptions'] as List? ?? const [])
          .whereType<Map<String, dynamic>>()
          .map(ManagedSubscriptionModel.fromJson)
          .toList(growable: false),
      invoices: (json['invoices'] as List? ?? const [])
          .whereType<Map<String, dynamic>>()
          .map(SubscriptionInvoiceModel.fromJson)
          .toList(growable: false),
      emptyStateCode: readNullableString(json['emptyStateCode']),
    );
  }
}
```

### Subscription

```dart
class ManagedSubscriptionModel {
  const ManagedSubscriptionModel({
    required this.subscriptionId,
    required this.status,
    required this.statusNameAr,
    required this.programId,
    required this.programNameAr,
    required this.programNameEn,
    required this.bundleId,
    required this.bundleNameAr,
    required this.bundleNameEn,
    required this.tier,
    required this.mealsPerDay,
    required this.imageUrl,
    required this.startDate,
    required this.endDate,
    required this.suggestedRenewalStartDate,
    required this.totalServiceDays,
    required this.completedServiceDays,
    required this.remainingServiceDays,
    required this.progressPercentage,
    required this.paidAmount,
    required this.currencyCode,
    required this.restaurant,
    required this.pendingFreeze,
    required this.actions,
    required this.invoices,
  });

  final String subscriptionId;
  final String status;
  final String statusNameAr;
  final String programId;
  final String programNameAr;
  final String programNameEn;
  final String bundleId;
  final String bundleNameAr;
  final String bundleNameEn;
  final String tier;
  final int mealsPerDay;
  final String? imageUrl;
  final DateTime startDate;
  final DateTime endDate;
  final DateTime suggestedRenewalStartDate;
  final int totalServiceDays;
  final int completedServiceDays;
  final int remainingServiceDays;
  final double progressPercentage;
  final double paidAmount;
  final String currencyCode;
  final FrozenRestaurantModel? restaurant;
  final PendingFreezeModel? pendingFreeze;
  final SubscriptionActionsModel actions;
  final List<SubscriptionInvoiceModel> invoices;

  double get normalizedProgress =>
      (progressPercentage / 100).clamp(0.0, 1.0);

  factory ManagedSubscriptionModel.fromJson(Map<String, dynamic> json) {
    final restaurantJson = json['restaurant'];
    final freezeJson = json['pendingFreeze'];
    return ManagedSubscriptionModel(
      subscriptionId: json['subscriptionId'].toString(),
      status: readEnumCode(json['status'], legacySubscriptionStatuses),
      statusNameAr: json['statusNameAr']?.toString() ?? '',
      programId: json['programId'].toString(),
      programNameAr: json['programNameAr']?.toString() ?? '',
      programNameEn: json['programNameEn']?.toString() ?? '',
      bundleId: json['bundleId'].toString(),
      bundleNameAr: json['bundleNameAr']?.toString() ?? '',
      bundleNameEn: json['bundleNameEn']?.toString() ?? '',
      tier: readEnumCode(json['tier'], legacyRestaurantTiers),
      mealsPerDay: readInt(json['mealsPerDay']),
      imageUrl: readNullableString(json['imageUrl']),
      startDate: DateTime.parse(json['startDate'].toString()),
      endDate: DateTime.parse(json['endDate'].toString()),
      suggestedRenewalStartDate:
          DateTime.parse(json['suggestedRenewalStartDate'].toString()),
      totalServiceDays: readInt(json['totalServiceDays']),
      completedServiceDays: readInt(json['completedServiceDays']),
      remainingServiceDays: readInt(json['remainingServiceDays']),
      progressPercentage: readDouble(json['progressPercentage']),
      paidAmount: readDouble(json['paidAmount']),
      currencyCode: json['currencyCode']?.toString() ?? 'KWD',
      restaurant: restaurantJson is Map<String, dynamic>
          ? FrozenRestaurantModel.fromJson(restaurantJson)
          : null,
      pendingFreeze: freezeJson is Map<String, dynamic>
          ? PendingFreezeModel.fromJson(freezeJson)
          : null,
      actions: SubscriptionActionsModel.fromJson(
        json['actions'] as Map<String, dynamic>? ?? const {},
      ),
      invoices: (json['invoices'] as List? ?? const [])
          .whereType<Map<String, dynamic>>()
          .map(SubscriptionInvoiceModel.fromJson)
          .toList(growable: false),
    );
  }
}
```

### Restaurant, Freeze and Actions

```dart
class FrozenRestaurantModel {
  const FrozenRestaurantModel({
    required this.restaurantId,
    required this.nameAr,
    required this.nameEn,
  });

  final String restaurantId;
  final String nameAr;
  final String nameEn;

  factory FrozenRestaurantModel.fromJson(Map<String, dynamic> json) =>
      FrozenRestaurantModel(
        restaurantId: json['restaurantId'].toString(),
        nameAr: json['nameAr']?.toString() ?? '',
        nameEn: json['nameEn']?.toString() ?? '',
      );
}

class PendingFreezeModel {
  const PendingFreezeModel({
    required this.requestId,
    required this.startDate,
    required this.endDate,
    required this.freezeDays,
    required this.status,
  });

  final String requestId;
  final DateTime startDate;
  final DateTime endDate;
  final int freezeDays;
  final String status;

  factory PendingFreezeModel.fromJson(Map<String, dynamic> json) =>
      PendingFreezeModel(
        requestId: json['requestId'].toString(),
        startDate: DateTime.parse(json['startDate'].toString()),
        endDate: DateTime.parse(json['endDate'].toString()),
        freezeDays: readInt(json['freezeDays']),
        status: json['status']?.toString() ?? 'Unknown',
      );
}

class SubscriptionActionsModel {
  const SubscriptionActionsModel({
    required this.canRenew,
    required this.canFreeze,
    required this.canCancel,
    required this.renewUnavailableReason,
    required this.freezeUnavailableReason,
    required this.cancelUnavailableReason,
    required this.freezeStartDelayDays,
    required this.maximumFreezeDays,
    required this.freezeExtendsSubscriptionEnd,
    required this.renewalEndpoint,
    required this.freezeEndpoint,
    required this.cancellationEndpoint,
  });

  final bool canRenew;
  final bool canFreeze;
  final bool canCancel;
  final String? renewUnavailableReason;
  final String? freezeUnavailableReason;
  final String? cancelUnavailableReason;
  final int freezeStartDelayDays;
  final int maximumFreezeDays;
  final bool freezeExtendsSubscriptionEnd;
  final String renewalEndpoint;
  final String freezeEndpoint;
  final String cancellationEndpoint;

  factory SubscriptionActionsModel.fromJson(Map<String, dynamic> json) =>
      SubscriptionActionsModel(
        canRenew: readBool(json['canRenew']),
        canFreeze: readBool(json['canFreeze']),
        canCancel: readBool(json['canCancel']),
        renewUnavailableReason:
            readNullableString(json['renewUnavailableReason']),
        freezeUnavailableReason:
            readNullableString(json['freezeUnavailableReason']),
        cancelUnavailableReason:
            readNullableString(json['cancelUnavailableReason']),
        freezeStartDelayDays: readInt(json['freezeStartDelayDays']),
        maximumFreezeDays: readInt(json['maximumFreezeDays']),
        freezeExtendsSubscriptionEnd:
            readBool(json['freezeExtendsSubscriptionEnd']),
        renewalEndpoint: json['renewalEndpoint']?.toString() ?? '',
        freezeEndpoint: json['freezeEndpoint']?.toString() ?? '',
        cancellationEndpoint:
            json['cancellationEndpoint']?.toString() ?? '',
      );
}
```

### Invoice

```dart
class SubscriptionInvoiceModel {
  const SubscriptionInvoiceModel({
    required this.receiptId,
    required this.invoiceNumber,
    required this.paymentId,
    required this.issuedAtUtc,
    required this.amount,
    required this.currencyCode,
    required this.status,
    required this.gateway,
    required this.maskedCardNumber,
    required this.canDownload,
    required this.downloadUnavailableReason,
  });

  final String receiptId;
  final String? invoiceNumber;
  final String? paymentId;
  final DateTime issuedAtUtc;
  final double amount;
  final String currencyCode;
  final String status;
  final String gateway;
  final String? maskedCardNumber;
  final bool canDownload;
  final String? downloadUnavailableReason;

  String get displayNumber => invoiceNumber ?? paymentId ?? '-';

  factory SubscriptionInvoiceModel.fromJson(Map<String, dynamic> json) =>
      SubscriptionInvoiceModel(
        receiptId: json['receiptId'].toString(),
        invoiceNumber: readNullableString(json['invoiceNumber']),
        paymentId: readNullableString(json['paymentId']),
        issuedAtUtc: DateTime.parse(json['issuedAtUtc'].toString()).toUtc(),
        amount: readDouble(json['amount']),
        currencyCode: json['currencyCode']?.toString() ?? 'KWD',
        status: json['status']?.toString() ?? 'UNKNOWN',
        gateway: json['gateway']?.toString() ?? '',
        maskedCardNumber: readNullableString(json['maskedCardNumber']),
        canDownload: readBool(json['canDownload']),
        downloadUnavailableReason:
            readNullableString(json['downloadUnavailableReason']),
      );
}
```

### Command responses

```dart
class RenewalRequestModel {
  const RenewalRequestModel({
    required this.requestId,
    required this.previousSubscriptionId,
    required this.status,
    required this.programId,
    required this.bundleId,
    required this.tier,
    required this.durationDays,
    required this.includeFriday,
    required this.regionId,
    required this.suggestedStartDate,
  });

  final String requestId;
  final String previousSubscriptionId;
  final String status;
  final String programId;
  final String bundleId;
  final String tier;
  final int durationDays;
  final bool includeFriday;
  final String? regionId;
  final DateTime suggestedStartDate;

  factory RenewalRequestModel.fromJson(Map<String, dynamic> json) =>
      RenewalRequestModel(
        requestId: json['requestId'].toString(),
        previousSubscriptionId: json['previousSubscriptionId'].toString(),
        status: json['status']?.toString() ?? 'Requested',
        programId: json['programId'].toString(),
        bundleId: json['bundleId'].toString(),
        tier: readEnumCode(json['tier'], legacyRestaurantTiers),
        durationDays: readInt(json['durationDays']),
        includeFriday: readBool(json['includeFriday']),
        regionId: readNullableString(json['regionId']),
        suggestedStartDate:
            DateTime.parse(json['suggestedStartDate'].toString()),
      );
}

class CancellationRequestModel {
  const CancellationRequestModel({
    required this.requestId,
    required this.subscriptionId,
    required this.status,
    required this.reason,
    required this.requestedAtUtc,
  });

  final String requestId;
  final String subscriptionId;
  final String status;
  final String? reason;
  final DateTime requestedAtUtc;

  factory CancellationRequestModel.fromJson(Map<String, dynamic> json) =>
      CancellationRequestModel(
        requestId: json['requestId'].toString(),
        subscriptionId: json['subscriptionId'].toString(),
        status: json['status']?.toString() ?? 'Submitted',
        reason: readNullableString(json['reason']),
        requestedAtUtc:
            DateTime.parse(json['requestedAtUtc'].toString()).toUtc(),
      );
}
```

يمكن إنشاء Freeze response model بنفس حقول JSON المذكورة في ملف عقود الـAPI، أو إعادة تحميل التفاصيل مباشرة بعد نجاح الطلب إذا لم تكن الشاشة تحتاج تفاصيل الاستجابة.

## 5. Remote Data Source

```dart
abstract interface class SubscriptionManagementRemoteDataSource {
  Future<SubscriptionManagementScreenModel> getManagement({
    bool includeHistory = true,
  });

  Future<ManagedSubscriptionModel> getSubscription(String subscriptionId);

  Future<List<SubscriptionInvoiceModel>> getInvoices(String subscriptionId);

  Future<void> requestFreeze({
    required String endpoint,
    required DateTime startDate,
    required DateTime endDate,
    String? reason,
  });

  Future<RenewalRequestModel> requestRenewal(String endpoint);

  Future<CancellationRequestModel> requestCancellation({
    required String endpoint,
    String? reason,
    DateTime? requestedEffectiveDate,
  });
}

class SubscriptionManagementRemoteDataSourceImpl
    implements SubscriptionManagementRemoteDataSource {
  SubscriptionManagementRemoteDataSourceImpl(this._dio);

  final Dio _dio;

  @override
  Future<SubscriptionManagementScreenModel> getManagement({
    bool includeHistory = true,
  }) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/customer/subscriptions/management',
      queryParameters: {'includeHistory': includeHistory},
    );
    return SubscriptionManagementScreenModel.fromJson(response.data!);
  }

  @override
  Future<ManagedSubscriptionModel> getSubscription(
    String subscriptionId,
  ) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/customer/subscriptions/$subscriptionId/management',
    );
    return ManagedSubscriptionModel.fromJson(response.data!);
  }

  @override
  Future<List<SubscriptionInvoiceModel>> getInvoices(
    String subscriptionId,
  ) async {
    final response = await _dio.get<List<dynamic>>(
      '/customer/subscriptions/$subscriptionId/invoices',
    );
    return (response.data ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(SubscriptionInvoiceModel.fromJson)
        .toList(growable: false);
  }

  @override
  Future<void> requestFreeze({
    required String endpoint,
    required DateTime startDate,
    required DateTime endDate,
    String? reason,
  }) async {
    await _dio.post<Map<String, dynamic>>(
      endpoint,
      data: {
        'startDate': _dateOnly(startDate),
        'endDate': _dateOnly(endDate),
        'reason': reason?.trim().isEmpty == true ? null : reason?.trim(),
      },
    );
  }

  @override
  Future<RenewalRequestModel> requestRenewal(String endpoint) async {
    final response = await _dio.post<Map<String, dynamic>>(endpoint);
    // Dio accepts 202 by default because it is within the 2xx range.
    return RenewalRequestModel.fromJson(response.data!);
  }

  @override
  Future<CancellationRequestModel> requestCancellation({
    required String endpoint,
    String? reason,
    DateTime? requestedEffectiveDate,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      endpoint,
      data: {
        'reason': reason?.trim().isEmpty == true ? null : reason?.trim(),
        'requestedEffectiveDate': requestedEffectiveDate == null
            ? null
            : _dateOnly(requestedEffectiveDate),
      },
    );
    return CancellationRequestModel.fromJson(response.data!);
  }

  String _dateOnly(DateTime value) {
    final year = value.year.toString().padLeft(4, '0');
    final month = value.month.toString().padLeft(2, '0');
    final day = value.day.toString().padLeft(2, '0');
    return '$year-$month-$day';
  }
}
```

استخدم `endpoint` المرجع من `actions`؛ هو يبدأ بـ`/api/v1`. إذا كان Dio baseUrl ينتهي أصلًا بـ`/api/v1`، فإن تمرير المسار المرجع كما هو قد يكرر الجزء حسب إعداد المشروع. الحل الأفضل توحيد المسار قبل الطلب:

```dart
String normalizeApiEndpoint(String endpoint) {
  const prefix = '/api/v1';
  return endpoint.startsWith(prefix)
      ? endpoint.substring(prefix.length)
      : endpoint;
}
```

ثم مرر `normalizeApiEndpoint(actions.freezeEndpoint)` إلى Dio.

## 6. Error Mapper

```dart
class ApiFailure implements Exception {
  const ApiFailure({
    required this.statusCode,
    required this.code,
    required this.message,
    this.traceId,
  });

  final int? statusCode;
  final String code;
  final String message;
  final String? traceId;
}

ApiFailure mapDioException(DioException exception) {
  final data = exception.response?.data;
  final body = data is Map<String, dynamic> ? data : const <String, dynamic>{};
  final status = exception.response?.statusCode;
  final code = readNullableString(body['error']) ??
      readNullableString(body['code']) ??
      'http_${status ?? 'unknown'}';

  return ApiFailure(
    statusCode: status,
    code: code,
    message: readNullableString(body['detail']) ?? _fallbackMessage(code),
    traceId: readNullableString(body['traceId']),
  );
}

String _fallbackMessage(String code) => switch (code) {
  'subscription.not_found' => 'الاشتراك غير موجود.',
  'subscription.freeze_request_already_pending' =>
    'يوجد طلب تجميد قيد المراجعة.',
  'subscription.renewal_not_available' =>
    'التجديد غير متاح حاليًا.',
  'subscription.cancellation_not_available' =>
    'طلب الإلغاء غير متاح حاليًا.',
  _ => 'حدث خطأ، حاول مرة أخرى.',
};
```

ضع Error Mapper في طبقة مشتركة إذا كان موجودًا بالفعل، ولا تنشئ Mapper ثانيًا لنفس التطبيق.

## 7. Repository

```dart
abstract interface class SubscriptionManagementRepository {
  Future<SubscriptionManagementScreenModel> getManagement();
  Future<ManagedSubscriptionModel> getSubscription(String id);
  Future<void> requestFreeze({
    required SubscriptionActionsModel actions,
    required DateTime startDate,
    required DateTime endDate,
    String? reason,
  });
  Future<RenewalRequestModel> requestRenewal(
    SubscriptionActionsModel actions,
  );
  Future<CancellationRequestModel> requestCancellation({
    required SubscriptionActionsModel actions,
    String? reason,
    DateTime? effectiveDate,
  });
}
```

قبل إرسال أي Command، تحقق في Repository أيضًا من `canRenew/canFreeze/canCancel` لمنع خطأ برمجي في الـUI، مع بقاء الباك مصدر التحقق النهائي.

## 8. Cubit State

```dart
sealed class SubscriptionManagementState {
  const SubscriptionManagementState();
}

final class SubscriptionManagementInitial
    extends SubscriptionManagementState {
  const SubscriptionManagementInitial();
}

final class SubscriptionManagementLoading
    extends SubscriptionManagementState {
  const SubscriptionManagementLoading();
}

final class SubscriptionManagementLoaded
    extends SubscriptionManagementState {
  const SubscriptionManagementLoaded({
    required this.data,
    this.actionInProgress,
    this.message,
  });

  final SubscriptionManagementScreenModel data;
  final String? actionInProgress;
  final String? message;

  SubscriptionManagementLoaded copyWith({
    SubscriptionManagementScreenModel? data,
    String? actionInProgress,
    bool clearAction = false,
    String? message,
  }) => SubscriptionManagementLoaded(
    data: data ?? this.data,
    actionInProgress:
        clearAction ? null : actionInProgress ?? this.actionInProgress,
    message: message,
  );
}

final class SubscriptionManagementFailure
    extends SubscriptionManagementState {
  const SubscriptionManagementFailure(this.failure);
  final ApiFailure failure;
}
```

## 9. Cubit

```dart
class SubscriptionManagementCubit
    extends Cubit<SubscriptionManagementState> {
  SubscriptionManagementCubit(this._repository)
      : super(const SubscriptionManagementInitial());

  final SubscriptionManagementRepository _repository;

  Future<void> load() async {
    emit(const SubscriptionManagementLoading());
    try {
      final data = await _repository.getManagement();
      emit(SubscriptionManagementLoaded(data: data));
    } on DioException catch (error) {
      emit(SubscriptionManagementFailure(mapDioException(error)));
    }
  }

  Future<void> refresh() async {
    final old = state;
    try {
      final data = await _repository.getManagement();
      emit(SubscriptionManagementLoaded(data: data));
    } on DioException catch (error) {
      if (old is SubscriptionManagementLoaded) {
        emit(old.copyWith(message: mapDioException(error).message));
      } else {
        emit(SubscriptionManagementFailure(mapDioException(error)));
      }
    }
  }

  Future<RenewalRequestModel?> renew() async {
    final currentState = state;
    if (currentState is! SubscriptionManagementLoaded) return null;
    final subscription = currentState.data.currentSubscription;
    if (subscription == null || !subscription.actions.canRenew) return null;

    emit(currentState.copyWith(actionInProgress: 'renew'));
    try {
      final result = await _repository.requestRenewal(subscription.actions);
      emit(currentState.copyWith(clearAction: true));
      return result;
    } on DioException catch (error) {
      emit(currentState.copyWith(
        clearAction: true,
        message: mapDioException(error).message,
      ));
      return null;
    }
  }

  Future<bool> freeze({
    required DateTime startDate,
    required DateTime endDate,
    String? reason,
  }) async {
    final currentState = state;
    if (currentState is! SubscriptionManagementLoaded) return false;
    final subscription = currentState.data.currentSubscription;
    if (subscription == null || !subscription.actions.canFreeze) return false;

    emit(currentState.copyWith(actionInProgress: 'freeze'));
    try {
      await _repository.requestFreeze(
        actions: subscription.actions,
        startDate: startDate,
        endDate: endDate,
        reason: reason,
      );
      final data = await _repository.getManagement();
      emit(SubscriptionManagementLoaded(
        data: data,
        message: 'تم إرسال طلب التجميد للمراجعة.',
      ));
      return true;
    } on DioException catch (error) {
      emit(currentState.copyWith(
        clearAction: true,
        message: mapDioException(error).message,
      ));
      return false;
    }
  }

  Future<bool> cancel({
    String? reason,
    DateTime? effectiveDate,
  }) async {
    final currentState = state;
    if (currentState is! SubscriptionManagementLoaded) return false;
    final subscription = currentState.data.currentSubscription;
    if (subscription == null || !subscription.actions.canCancel) return false;

    emit(currentState.copyWith(actionInProgress: 'cancel'));
    try {
      await _repository.requestCancellation(
        actions: subscription.actions,
        reason: reason,
        effectiveDate: effectiveDate,
      );
      final data = await _repository.getManagement();
      emit(SubscriptionManagementLoaded(
        data: data,
        message: 'تم إرسال طلب الإلغاء للمراجعة.',
      ));
      return true;
    } on DioException catch (error) {
      emit(currentState.copyWith(
        clearAction: true,
        message: mapDioException(error).message,
      ));
      return false;
    }
  }
}
```

## 10. التنقل إلى فلو التجديد

بعد نجاح `renew()`:

```dart
final renewal = await context.read<SubscriptionManagementCubit>().renew();
if (renewal == null || !context.mounted) return;

context.pushNamed(
  'subscriptionConfiguration',
  extra: SubscriptionFlowDraft(
    renewalRequestId: renewal.requestId,
    programId: renewal.programId,
    bundleId: renewal.bundleId,
    tier: renewal.tier,
    durationDays: renewal.durationDays,
    includeFriday: renewal.includeFriday,
    regionId: renewal.regionId,
    startDate: renewal.suggestedStartDate,
  ),
);
```

لا تنتقل مباشرة إلى MyFatoorah؛ يجب إعادة التسعير وفحص أهلية المطاعم ثم إنشاء Checkout جديد.

## 11. تنسيق التاريخ والمبلغ

```dart
final localInvoiceDate = invoice.issuedAtUtc.toLocal();

final amountText = NumberFormat.currency(
  locale: isArabic ? 'ar_KW' : 'en_KW',
  name: invoice.currencyCode,
  decimalDigits: 3,
).format(invoice.amount);
```

`startDate` و`endDate` قيم Date-only؛ لا تحولها إلى UTC ثم تعيد اليوم، حتى لا يتغير التاريخ بسبب المنطقة الزمنية. استخدم أجزاء السنة والشهر واليوم فقط.

## 12. رسم زر الإجراء

```dart
FilledButton(
  onPressed: subscription.actions.canFreeze &&
          state.actionInProgress == null
      ? () => showFreezeSheet(context, subscription)
      : null,
  child: state.actionInProgress == 'freeze'
      ? const SizedBox.square(
          dimension: 18,
          child: CircularProgressIndicator(strokeWidth: 2),
        )
      : const Text('تجميد'),
)
```

يمكن إظهار Tooltip/Caption من `freezeUnavailableReason` عندما يكون الزر معطلًا.

## 13. Cache اختياري

يمكن حفظ آخر `GET management` للعرض أثناء Offline بشرط:

- حفظ وقت آخر تحديث.
- إظهار علامة أن البيانات قديمة.
- عدم السماح بإرسال Command اعتمادًا على Cache قديمة قبل محاولة Refresh.
- عدم حفظ access token أو بيانات دفع داخل نفس Cache.

## 14. ملاحظات Retrofit وFreezed

إذا كان المشروع يستخدم Retrofit/Freezed:

- استخدم نفس المسارات والعقود في هذا الملف.
- اجعل جميع الحقول التي وصفت Nullable قابلة لـ`null`.
- أضف custom `JsonConverter` للـEnums لقبول String/number.
- لا تجعل `invoiceNumber`, `paymentId`, `imageUrl`, `restaurant`, `pendingFreeze`, `maskedCardNumber` حقولًا required non-null.
- تعامل مع `202 Accepted` كنجاح في `requestRenewal` و`requestCancellation`.

