using FluentValidation;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

public sealed record CustomerCancellationQuoteDto(
    Guid SubscriptionId,
    int TotalServiceDays,
    int DeliveredDays,
    int LockedKitchenDays,
    int RefundableDays,
    decimal DailyBoxPrice,
    decimal RefundAmount,
    string CurrencyCode,
    DateOnly EffectiveCancellationDate,
    string PolicyMessageAr,
    decimal CancellationFeeAmount = 0m,
    decimal NetRefundAmount = 0m,
    bool RefundToCashWallet = true,
    string? PolicyMessageEn = null,
    string? MessageCode = null,
    Guid? PayerCustomerUserId = null,
    Guid? BatchId = null,
    IReadOnlyList<FamilyRefundBeneficiaryDto>? Beneficiaries = null);

public sealed record CustomerCancellationResultDto(
    Guid RequestId,
    Guid SubscriptionId,
    string Status,
    string StatusNameAr,
    int TotalServiceDays,
    int DeliveredDays,
    int LockedKitchenDays,
    int RefundableDays,
    decimal DailyBoxPrice,
    decimal RefundAmount,
    string CurrencyCode,
    DateOnly? EffectiveCancellationDate,
    string MessageAr,
    DateTime TimestampUtc,
    decimal CancellationFeeAmount = 0m,
    decimal NetRefundAmount = 0m,
    bool RefundedToWallet = false,
    string? MessageEn = null,
    string? MessageCode = null,
    Guid? PayerCustomerUserId = null,
    Guid? SettlementId = null,
    Guid? BatchId = null,
    IReadOnlyList<FamilyRefundBeneficiaryDto>? Beneficiaries = null);

public sealed record FamilyRefundBeneficiaryDto(Guid PayerCustomerUserId, decimal ConfirmedPaidAmount,
    decimal RefundAmount, string CurrencyCode, Guid? WalletTransactionId = null);

public sealed record CustomerCancellationRequest(string? Reason, DateOnly? RequestedEffectiveDate = null);

public sealed record CustomerCancellationRequestDto(
    Guid RequestId,
    Guid SubscriptionId,
    string Status,
    string? Reason,
    DateTime RequestedAtUtc);

public sealed record AdminCancelSubscriptionRequest(string? Reason, bool ForceImmediate = false);

public sealed record RequestSubscriptionCancellationCommand(
    Guid SubscriptionId,
    Guid CallerUserId,
    UserType CallerUserType,
    string? Reason = null) : ICommand<CustomerCancellationResultDto>;

public sealed record GetSubscriptionCancellationQuoteQuery(
    Guid SubscriptionId,
    Guid CallerUserId,
    UserType CallerUserType) : IQuery<CustomerCancellationQuoteDto>;

public sealed record ProcessAdminSubscriptionCancellationCommand(
    Guid SubscriptionId,
    Guid AdminUserId,
    string? Reason = null,
    bool ForceImmediate = false) : ICommand<CustomerCancellationResultDto>;

public sealed record CancelUncompletedSubscriptionCommand(
    Guid SubscriptionId,
    Guid CallerUserId) : ICommand;

// FluentValidation Validators
public sealed class RequestSubscriptionCancellationCommandValidator : AbstractValidator<RequestSubscriptionCancellationCommand>
{
    public RequestSubscriptionCancellationCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class GetSubscriptionCancellationQuoteQueryValidator : AbstractValidator<GetSubscriptionCancellationQuoteQuery>
{
    public GetSubscriptionCancellationQuoteQueryValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

public sealed class ProcessAdminSubscriptionCancellationCommandValidator : AbstractValidator<ProcessAdminSubscriptionCancellationCommand>
{
    public ProcessAdminSubscriptionCancellationCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class CancelUncompletedSubscriptionCommandValidator : AbstractValidator<CancelUncompletedSubscriptionCommand>
{
    public CancelUncompletedSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}
