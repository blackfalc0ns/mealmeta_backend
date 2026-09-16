// ============================================================================
// MealMate Mobile App - Influencer & Bank Account Dio API Service
// Path: lib/features/influencer/services/influencer_api_service.dart
// ============================================================================

import 'package:dio/dio.dart';
import 'influencer_bank_account_models.dart';

class InfluencerApiService {
  final Dio _dio;

  InfluencerApiService(this._dio);

  static const String _basePath = '/api/v1/influencer-partner';

  /// 1. جلب قائمة البنوك المعتمدة
  Future<List<BankCatalogItemDto>> getBanks() async {
    final response = await _dio.get('$_basePath/banks');
    final list = response.data as List<dynamic>;
    return list.map((e) => BankCatalogItemDto.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// 2. جلب الملف الشخصي الكامل للمؤثر
  Future<InfluencerPartnerProfileDto> getProfile() async {
    final response = await _dio.get('$_basePath/me');
    return InfluencerPartnerProfileDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// 3. تحديث قنوات التواصل الاجتماعي للمؤثر
  Future<InfluencerSocialLinksResponseDto> updateSocialLinks(InfluencerSocialLinksDto links) async {
    final response = await _dio.put(
      '$_basePath/me/social-links',
      data: links.toJson(),
    );
    return InfluencerSocialLinksResponseDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// 4. جلب بيانات لوحة التحكم الرئيسية
  Future<Map<String, dynamic>> getHomeDashboard() async {
    final response = await _dio.get('$_basePath/me/home');
    return response.data as Map<String, dynamic>;
  }

  /// 5. جلب روابط الإحالة وكود الـ QR
  Future<Map<String, dynamic>> getLinks() async {
    final response = await _dio.get('$_basePath/me/links');
    return response.data as Map<String, dynamic>;
  }

  /// 6. جلب الأنشطة الأخيرة مع التصفح (Pagination)
  Future<PagedResultDto<Map<String, dynamic>>> getActivities({int page = 1, int pageSize = 20}) async {
    final response = await _dio.get(
      '$_basePath/me/activities',
      queryParameters: {'page': page, 'pageSize': pageSize},
    );
    return PagedResultDto.fromJson(response.data as Map<String, dynamic>, (json) => json);
  }

  /// 7. جلب تحليلات الأداء مع تصفية الفترة (7d, 30d, 90d, all)
  Future<Map<String, dynamic>> getAnalytics({String period = '30d'}) async {
    final response = await _dio.get(
      '$_basePath/me/analytics',
      queryParameters: {'period': period},
    );
    return response.data as Map<String, dynamic>;
  }

  /// 8. جلب بيانات المحفظة والأرصدة وسياسة السحب
  Future<InfluencerWalletDto> getWallet() async {
    final response = await _dio.get('$_basePath/me/wallet');
    return InfluencerWalletDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// 9. جلب بيانات الحساب البنكي لاستلام الأرباح
  Future<InfluencerBankInfoDto> getBankAccount() async {
    final response = await _dio.get('$_basePath/me/bank-account');
    return InfluencerBankInfoDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// 10. إضافة أو تحديث بيانات الحساب البنكي
  Future<InfluencerBankInfoDto> updateBankAccount(UpdateInfluencerBankAccountRequest request) async {
    final response = await _dio.put(
      '$_basePath/me/bank-account',
      data: request.toJson(),
    );
    return InfluencerBankInfoDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// 11. حذف الحساب البنكي المسجل
  Future<void> deleteBankAccount() async {
    await _dio.delete('$_basePath/me/bank-account');
  }

  /// 12. تقديم طلب سحب أرباح (Smart Payout)
  Future<InfluencerWithdrawalDto> submitWithdrawal(SubmitWithdrawalRequest request) async {
    final response = await _dio.post(
      '$_basePath/me/withdrawals',
      data: request.toJson(),
    );
    return InfluencerWithdrawalDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// 13. جلب سجل طلبات السحب مع التصفح والفلترة
  Future<PagedResultDto<InfluencerWithdrawalDto>> getWithdrawals({
    String? status,
    int page = 1,
    int pageSize = 20,
  }) async {
    final response = await _dio.get(
      '$_basePath/me/withdrawals',
      queryParameters: {
        if (status != null && status != 'All') 'status': status,
        'page': page,
        'pageSize': pageSize,
      },
    );
    return PagedResultDto.fromJson(
      response.data as Map<String, dynamic>,
      (json) => InfluencerWithdrawalDto.fromJson(json),
    );
  }

  /// 14. جلب تفاصيل طلب سحب محدد
  Future<InfluencerWithdrawalDto> getWithdrawalDetails(String withdrawalId) async {
    final response = await _dio.get('$_basePath/me/withdrawals/$withdrawalId');
    return InfluencerWithdrawalDto.fromJson(response.data as Map<String, dynamic>);
  }

  /// 15. إلغاء طلب سحب معلق (Cancel Pending Withdrawal)
  Future<InfluencerWithdrawalDto> cancelWithdrawal(String withdrawalId) async {
    final response = await _dio.post('$_basePath/me/withdrawals/$withdrawalId/cancel');
    return InfluencerWithdrawalDto.fromJson(response.data as Map<String, dynamic>);
  }
}
