using FluentValidation;
using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

// ==========================================
// DTOs
// ==========================================

public sealed record InfluencerSocialLinksDto(
    string? Tiktok,
    string? Instagram,
    string? Whatsapp,
    string? Telegram,
    string? Youtube,
    string? Snapchat);

public sealed record InfluencerSocialLinksResponseDto(
    InfluencerSocialLinksDto SocialLinks,
    bool HasCompletedSocialOnboarding);

public sealed record InfluencerPartnerProfileDto(
    Guid InfluencerProfileId,
    string DisplayName,
    string InfluencerCode,
    decimal CommissionRate,
    bool IsActive,
    string Status,
    string? AvatarUrl,
    bool HasCompletedSocialOnboarding,
    string PartnerTermsUrl,
    InfluencerBankInfoDto? BankInfo,
    InfluencerSocialLinksDto SocialLinks);

public sealed record QuickStatsDto(
    int LinkClicks,
    int AffiliatedOrders,
    decimal ConversionRate);

public sealed record InfluencerLinksDto(
    string InfluencerCode,
    string ReferralLink,
    string QrCodeData,
    QuickStatsDto QuickStats);

public sealed record BankCatalogItemDto(
    string Id,
    string Code,
    string NameAr,
    string NameEn,
    bool IsActive);

// ==========================================
// Queries & Commands
// ==========================================

public sealed record GetInfluencerMeQuery() : IQuery<InfluencerPartnerProfileDto>;

public sealed record GetInfluencerLinksQuery() : IQuery<InfluencerLinksDto>;

public sealed record GetInfluencerBanksQuery() : IQuery<IReadOnlyList<BankCatalogItemDto>>;

public sealed record GetInfluencerBankAccountQuery() : IQuery<InfluencerBankInfoDto>;

public sealed record UpdateInfluencerBankAccountCommand(
    string BankName,
    string AccountHolderName,
    string Iban) : ICommand<InfluencerBankInfoDto>;

public sealed class UpdateInfluencerBankAccountCommandValidator : AbstractValidator<UpdateInfluencerBankAccountCommand>
{
    public UpdateInfluencerBankAccountCommandValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Iban)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(34)
            .Matches("^[a-zA-Z0-9 ]+$")
            .WithMessage("Invalid IBAN format");
    }
}

public sealed record DeleteInfluencerBankAccountCommand() : ICommand;

public sealed record UpdateInfluencerSocialLinksCommand(
    string? Tiktok,
    string? Instagram,
    string? Whatsapp,
    string? Telegram,
    string? Youtube,
    string? Snapchat) : ICommand<InfluencerSocialLinksResponseDto>;
