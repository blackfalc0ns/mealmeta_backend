using FluentValidation;
using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed record AdminInfluencerAttributionDto(
    Guid SubscriptionId,
    string? CustomerName,
    string? CustomerPhone,
    decimal SubscriptionAmount,
    decimal CommissionRate,
    decimal CommissionAmount,
    string Status,
    DateTime SubscribedAtUtc,
    DateTime? FirstDeliveryAtUtc);

public sealed record AdminInfluencerWithdrawalSummaryDto(
    Guid Id,
    string ReferenceNumber,
    decimal Amount,
    string Currency,
    string Status,
    string? BankName,
    string? Iban,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    string? TransferReferenceNumber);

public sealed record AdminInfluencerDetailDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? Email,
    string? Phone,
    string? InfluencerCode,
    Guid? LinkedCustomerUserId,
    string? LinkedCustomerName,
    string? LinkedCustomerPhone,
    string Status,
    bool IsActive,
    decimal DefaultCommissionRate,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    string? BankName,
    string? AccountHolderName,
    string? Iban,
    InfluencerSocialLinksDto SocialLinks,
    decimal AvailableBalance,
    decimal PendingBalance,
    decimal LifetimeEarnings,
    decimal TotalWithdrawn,
    int ReferralsCount,
    int PaidSubscriptionsCount,
    decimal ConversionRate,
    IReadOnlyList<AdminInfluencerAttributionDto> Attributions,
    IReadOnlyList<AdminInfluencerWithdrawalSummaryDto> Withdrawals);

public sealed record GetAdminInfluencerDetailQuery(Guid Id) : IQuery<AdminInfluencerDetailDto>;

public sealed record UpdateInfluencerStatusCommand(Guid Id, string Status, string Reason) : ICommand<InfluencerDto>;

public sealed class UpdateInfluencerStatusCommandValidator : AbstractValidator<UpdateInfluencerStatusCommand>
{
    public UpdateInfluencerStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed record UpdateInfluencerCommissionRateCommand(Guid Id, decimal NewRatePercent, string Reason) : ICommand<InfluencerDto>;

public sealed class UpdateInfluencerCommissionRateCommandValidator : AbstractValidator<UpdateInfluencerCommissionRateCommand>
{
    public UpdateInfluencerCommissionRateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NewRatePercent).InclusiveBetween(0m, 100m);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed record UpdateInfluencerBankInfoCommand(Guid Id, string BankName, string AccountHolderName, string Iban) : ICommand<InfluencerDto>;

public sealed class UpdateInfluencerBankInfoCommandValidator : AbstractValidator<UpdateInfluencerBankInfoCommand>
{
    public UpdateInfluencerBankInfoCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Iban).NotEmpty().MinimumLength(15).MaximumLength(40);
    }
}
