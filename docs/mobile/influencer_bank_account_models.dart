// ============================================================================
// MealMate Mobile App - Influencer & Bank Account Dart Models
// Path: lib/features/influencer/models/influencer_models.dart
// ============================================================================

import 'package:flutter/foundation.dart';

@immutable
class CurrencyAmountDto {
  final double amount;
  final String currency;

  const CurrencyAmountDto({
    required this.amount,
    this.currency = 'KWD',
  });

  factory CurrencyAmountDto.fromJson(Map<String, dynamic> json) {
    return CurrencyAmountDto(
      amount: (json['amount'] as num?)?.toDouble() ?? 0.0,
      currency: json['currency'] as String? ?? 'KWD',
    );
  }

  Map<String, dynamic> toJson() => {
        'amount': amount,
        'currency': currency,
      };
}

@immutable
class BankCatalogItemDto {
  final String id;
  final String code;
  final String nameAr;
  final String nameEn;
  final bool isActive;

  const BankCatalogItemDto({
    required this.id,
    required this.code,
    required this.nameAr,
    required this.nameEn,
    required this.isActive,
  });

  factory BankCatalogItemDto.fromJson(Map<String, dynamic> json) {
    return BankCatalogItemDto(
      id: json['id'] as String? ?? '',
      code: json['code'] as String? ?? '',
      nameAr: json['nameAr'] as String? ?? '',
      nameEn: json['nameEn'] as String? ?? '',
      isActive: json['isActive'] as bool? ?? true,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'code': code,
        'nameAr': nameAr,
        'nameEn': nameEn,
        'isActive': isActive,
      };
}

@immutable
class InfluencerBankInfoDto {
  final String accountHolderName;
  final String bankName;
  final String? iban;
  final String? maskedIban;
  final bool isConfigured;

  const InfluencerBankInfoDto({
    required this.accountHolderName,
    required this.bankName,
    this.iban,
    this.maskedIban,
    required this.isConfigured,
  });

  factory InfluencerBankInfoDto.fromJson(Map<String, dynamic> json) {
    return InfluencerBankInfoDto(
      accountHolderName: json['accountHolderName'] as String? ?? '',
      bankName: json['bankName'] as String? ?? '',
      iban: json['iban'] as String?,
      maskedIban: json['maskedIban'] as String?,
      isConfigured: json['isConfigured'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
        'accountHolderName': accountHolderName,
        'bankName': bankName,
        if (iban != null) 'iban': iban,
        if (maskedIban != null) 'maskedIban': maskedIban,
        'isConfigured': isConfigured,
      };
}

@immutable
class UpdateInfluencerBankAccountRequest {
  final String bankName;
  final String accountHolderName;
  final String iban;

  const UpdateInfluencerBankAccountRequest({
    required this.bankName,
    required this.accountHolderName,
    required this.iban,
  });

  Map<String, dynamic> toJson() => {
        'bankName': bankName.trim(),
        'accountHolderName': accountHolderName.trim(),
        'iban': iban.replaceAll(' ', '').replaceAll('-', '').toUpperCase(),
      };
}

@immutable
class InfluencerSocialLinksDto {
  final String? tiktok;
  final String? instagram;
  final String? whatsapp;
  final String? telegram;
  final String? youtube;
  final String? snapchat;

  const InfluencerSocialLinksDto({
    this.tiktok,
    this.instagram,
    this.whatsapp,
    this.telegram,
    this.youtube,
    this.snapchat,
  });

  factory InfluencerSocialLinksDto.fromJson(Map<String, dynamic> json) {
    return InfluencerSocialLinksDto(
      tiktok: json['tiktok'] as String?,
      instagram: json['instagram'] as String?,
      whatsapp: json['whatsapp'] as String?,
      telegram: json['telegram'] as String?,
      youtube: json['youtube'] as String?,
      snapchat: json['snapchat'] as String?,
    );
  }

  Map<String, dynamic> toJson() => {
        'tiktok': tiktok,
        'instagram': instagram,
        'whatsapp': whatsapp,
        'telegram': telegram,
        'youtube': youtube,
        'snapchat': snapchat,
      };
}

@immutable
class InfluencerSocialLinksResponseDto {
  final InfluencerSocialLinksDto socialLinks;
  final bool hasCompletedSocialOnboarding;

  const InfluencerSocialLinksResponseDto({
    required this.socialLinks,
    required this.hasCompletedSocialOnboarding,
  });

  factory InfluencerSocialLinksResponseDto.fromJson(Map<String, dynamic> json) {
    return InfluencerSocialLinksResponseDto(
      socialLinks: json['socialLinks'] != null
          ? InfluencerSocialLinksDto.fromJson(json['socialLinks'] as Map<String, dynamic>)
          : const InfluencerSocialLinksDto(),
      hasCompletedSocialOnboarding: json['hasCompletedSocialOnboarding'] as bool? ?? false,
    );
  }
}

@immutable
class InfluencerPartnerProfileDto {
  final String influencerProfileId;
  final String displayName;
  final String influencerCode;
  final double commissionRate;
  final bool isActive;
  final String status;
  final String? avatarUrl;
  final bool hasCompletedSocialOnboarding;
  final String partnerTermsUrl;
  final InfluencerBankInfoDto? bankInfo;
  final InfluencerSocialLinksDto socialLinks;

  const InfluencerPartnerProfileDto({
    required this.influencerProfileId,
    required this.displayName,
    required this.influencerCode,
    required this.commissionRate,
    required this.isActive,
    required this.status,
    this.avatarUrl,
    required this.hasCompletedSocialOnboarding,
    required this.partnerTermsUrl,
    this.bankInfo,
    required this.socialLinks,
  });

  factory InfluencerPartnerProfileDto.fromJson(Map<String, dynamic> json) {
    return InfluencerPartnerProfileDto(
      influencerProfileId: json['influencerProfileId'] as String? ?? '',
      displayName: json['displayName'] as String? ?? '',
      influencerCode: json['influencerCode'] as String? ?? '',
      commissionRate: (json['commissionRate'] as num?)?.toDouble() ?? 0.0,
      isActive: json['isActive'] as bool? ?? false,
      status: json['status'] as String? ?? 'Pending',
      avatarUrl: json['avatarUrl'] as String?,
      hasCompletedSocialOnboarding: json['hasCompletedSocialOnboarding'] as bool? ?? false,
      partnerTermsUrl: json['partnerTermsUrl'] as String? ?? '',
      bankInfo: json['bankInfo'] != null
          ? InfluencerBankInfoDto.fromJson(json['bankInfo'] as Map<String, dynamic>)
          : null,
      socialLinks: json['socialLinks'] != null
          ? InfluencerSocialLinksDto.fromJson(json['socialLinks'] as Map<String, dynamic>)
          : const InfluencerSocialLinksDto(),
    );
  }
}

@immutable
class InfluencerWithdrawalPolicyDto {
  final double minimumAmount;
  final double maximumAmount;
  final String currencyCode;
  final bool canWithdraw;
  final String? disabledReason;

  const InfluencerWithdrawalPolicyDto({
    required this.minimumAmount,
    required this.maximumAmount,
    this.currencyCode = 'KWD',
    required this.canWithdraw,
    this.disabledReason,
  });

  factory InfluencerWithdrawalPolicyDto.fromJson(Map<String, dynamic> json) {
    return InfluencerWithdrawalPolicyDto(
      minimumAmount: (json['minimumAmount'] as num?)?.toDouble() ?? 10.0,
      maximumAmount: (json['maximumAmount'] as num?)?.toDouble() ?? 0.0,
      currencyCode: json['currencyCode'] as String? ?? 'KWD',
      canWithdraw: json['canWithdraw'] as bool? ?? false,
      disabledReason: json['disabledReason'] as String?,
    );
  }
}

@immutable
class InfluencerWalletDto {
  final CurrencyAmountDto availableBalance;
  final CurrencyAmountDto pendingBalance;
  final CurrencyAmountDto lifetimeEarnings;
  final CurrencyAmountDto thisMonthEarned;
  final CurrencyAmountDto thisMonthWithdrawn;
  final String currencyCode;
  final bool hasActiveWithdrawalRequest;
  final InfluencerWithdrawalPolicyDto withdrawalPolicy;
  final InfluencerBankInfoDto? bankInfo;
  final List<dynamic> recentTransactions;

  const InfluencerWalletDto({
    required this.availableBalance,
    required this.pendingBalance,
    required this.lifetimeEarnings,
    required this.thisMonthEarned,
    required this.thisMonthWithdrawn,
    this.currencyCode = 'KWD',
    required this.hasActiveWithdrawalRequest,
    required this.withdrawalPolicy,
    this.bankInfo,
    this.recentTransactions = const [],
  });

  factory InfluencerWalletDto.fromJson(Map<String, dynamic> json) {
    return InfluencerWalletDto(
      availableBalance: json['availableBalance'] != null
          ? CurrencyAmountDto.fromJson(json['availableBalance'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      pendingBalance: json['pendingBalance'] != null
          ? CurrencyAmountDto.fromJson(json['pendingBalance'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      lifetimeEarnings: json['lifetimeEarnings'] != null
          ? CurrencyAmountDto.fromJson(json['lifetimeEarnings'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      thisMonthEarned: json['thisMonthEarned'] != null
          ? CurrencyAmountDto.fromJson(json['thisMonthEarned'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      thisMonthWithdrawn: json['thisMonthWithdrawn'] != null
          ? CurrencyAmountDto.fromJson(json['thisMonthWithdrawn'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      currencyCode: json['currencyCode'] as String? ?? 'KWD',
      hasActiveWithdrawalRequest: json['hasActiveWithdrawalRequest'] as bool? ?? false,
      withdrawalPolicy: json['withdrawalPolicy'] != null
          ? InfluencerWithdrawalPolicyDto.fromJson(json['withdrawalPolicy'] as Map<String, dynamic>)
          : const InfluencerWithdrawalPolicyDto(minimumAmount: 10.0, maximumAmount: 0.0, canWithdraw: false),
      bankInfo: json['bankInfo'] != null
          ? InfluencerBankInfoDto.fromJson(json['bankInfo'] as Map<String, dynamic>)
          : null,
      recentTransactions: json['recentTransactions'] as List<dynamic>? ?? const [],
    );
  }
}

@immutable
class PagedResultDto<T> {
  final List<T> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final int totalPages;
  final bool hasPreviousPage;
  final bool hasNextPage;

  const PagedResultDto({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.totalPages,
    required this.hasPreviousPage,
    required this.hasNextPage,
  });

  factory PagedResultDto.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) fromJsonT,
  ) {
    final list = json['items'] as List<dynamic>? ?? [];
    return PagedResultDto(
      items: list.map((e) => fromJsonT(e as Map<String, dynamic>)).toList(),
      page: json['page'] as int? ?? 1,
      pageSize: json['pageSize'] as int? ?? 20,
      totalCount: json['totalCount'] as int? ?? 0,
      totalPages: json['totalPages'] as int? ?? 0,
      hasPreviousPage: json['hasPreviousPage'] as bool? ?? false,
      hasNextPage: json['hasNextPage'] as bool? ?? false,
    );
  }
}

@immutable
class SubmitWithdrawalRequest {
  final double amount;
  final String withdrawalMethod;
  final String? bankName;
  final String? accountHolderName;
  final String? iban;
  final bool saveAsDefaultBank;
  final String? correlationId;

  const SubmitWithdrawalRequest({
    required this.amount,
    this.withdrawalMethod = 'BankTransfer',
    this.bankName,
    this.accountHolderName,
    this.iban,
    this.saveAsDefaultBank = true,
    this.correlationId,
  });

  Map<String, dynamic> toJson() => {
        'amount': amount,
        'withdrawalMethod': withdrawalMethod,
        if (bankName != null) 'bankName': bankName,
        if (accountHolderName != null) 'accountHolderName': accountHolderName,
        if (iban != null) 'iban': iban!.replaceAll(' ', '').replaceAll('-', '').toUpperCase(),
        'saveAsDefaultBank': saveAsDefaultBank,
        if (correlationId != null) 'correlationId': correlationId,
      };
}

@immutable
class InfluencerWithdrawalDto {
  final String id;
  final String referenceNumber;
  final CurrencyAmountDto amount;
  final CurrencyAmountDto transferFee;
  final CurrencyAmountDto netAmount;
  final String status;
  final String method;
  final String currencyCode;
  final InfluencerBankInfoDto bankDetails;
  final DateTime requestedAtUtc;
  final DateTime? reviewedAtUtc;
  final DateTime? completedAtUtc;
  final String? rejectionReason;

  const InfluencerWithdrawalDto({
    required this.id,
    required this.referenceNumber,
    required this.amount,
    required this.transferFee,
    required this.netAmount,
    required this.status,
    required this.method,
    this.currencyCode = 'KWD',
    required this.bankDetails,
    required this.requestedAtUtc,
    this.reviewedAtUtc,
    this.completedAtUtc,
    this.rejectionReason,
  });

  factory InfluencerWithdrawalDto.fromJson(Map<String, dynamic> json) {
    return InfluencerWithdrawalDto(
      id: json['withdrawalId'] as String? ?? json['id'] as String? ?? '',
      referenceNumber: json['referenceNumber'] as String? ?? '',
      amount: json['amount'] != null
          ? CurrencyAmountDto.fromJson(json['amount'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      transferFee: json['transferFee'] != null
          ? CurrencyAmountDto.fromJson(json['transferFee'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      netAmount: json['netAmount'] != null
          ? CurrencyAmountDto.fromJson(json['netAmount'] as Map<String, dynamic>)
          : const CurrencyAmountDto(amount: 0.0),
      status: json['status'] as String? ?? 'Pending',
      method: json['method'] as String? ?? 'BankTransfer',
      currencyCode: json['currencyCode'] as String? ?? 'KWD',
      bankDetails: json['bankDetails'] != null
          ? InfluencerBankInfoDto.fromJson(json['bankDetails'] as Map<String, dynamic>)
          : const InfluencerBankInfoDto(accountHolderName: '', bankName: '', isConfigured: false),
      requestedAtUtc: DateTime.tryParse(json['requestedAtUtc'] as String? ?? '') ?? DateTime.now(),
      reviewedAtUtc: json['reviewedAtUtc'] != null ? DateTime.tryParse(json['reviewedAtUtc'] as String) : null,
      completedAtUtc: json['completedAtUtc'] != null ? DateTime.tryParse(json['completedAtUtc'] as String) : null,
      rejectionReason: json['rejectionReason'] as String?,
    );
  }
}
