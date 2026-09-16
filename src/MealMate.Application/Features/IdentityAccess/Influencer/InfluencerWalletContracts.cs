using FluentValidation;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

// ==========================================
// DTOs
// ==========================================

public sealed record CurrencyAmountDto(decimal Amount, string Currency = "KWD");

public sealed record InfluencerBankInfoDto(
    string AccountHolderName,
    string BankName,
    string Iban,
    string? MaskedIban = null,
    bool IsConfigured = true);

public sealed record InfluencerWithdrawalPolicyDto(
    decimal MinimumAmount,
    decimal MaximumAmount,
    string CurrencyCode,
    bool CanWithdraw,
    string? DisabledReason);

public sealed record WalletTransactionDto(
    string TransactionId,
    string TransactionType, // "Withdrawal" | "Commission" | "Reversal"
    string ReferenceNumber,
    decimal Amount,
    string Currency,
    bool IsCredit,
    string Status,
    DateTime CreatedAtUtc);

public sealed record InfluencerWalletDto(
    CurrencyAmountDto AvailableBalance,
    CurrencyAmountDto PendingBalance,
    CurrencyAmountDto LifetimeEarnings,
    CurrencyAmountDto ThisMonthEarned,
    CurrencyAmountDto ThisMonthWithdrawn,
    string CurrencyCode,
    bool HasActiveWithdrawalRequest,
    InfluencerWithdrawalPolicyDto WithdrawalPolicy,
    InfluencerBankInfoDto? BankInfo,
    IReadOnlyList<WalletTransactionDto> RecentTransactions);

public sealed record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record WithdrawalRecordDto(
    Guid WithdrawalId,
    string ReferenceNumber,
    CurrencyAmountDto Amount,
    CurrencyAmountDto TransferFee,
    CurrencyAmountDto NetAmount,
    string Status,
    string Method,
    string CurrencyCode,
    InfluencerBankInfoDto BankDetails,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    DateTime? CompletedAtUtc,
    string? RejectionReason,
    int EstimatedMinBusinessDays,
    int EstimatedMaxBusinessDays);

// ==========================================
// Queries & Commands
// ==========================================

public sealed record GetInfluencerWalletQuery() : IQuery<InfluencerWalletDto>;

public sealed record GetInfluencerWithdrawalsQuery(
    string? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResultDto<WithdrawalRecordDto>>;

public sealed record GetInfluencerWithdrawalDetailsQuery(Guid Id) : IQuery<WithdrawalRecordDto>;

public sealed record CancelInfluencerWithdrawalCommand(Guid Id) : ICommand<WithdrawalRecordDto>;

public sealed record SubmitInfluencerWithdrawalCommand(
    decimal Amount,
    string WithdrawalMethod = "BankTransfer",
    string? BankName = null,
    string? AccountHolderName = null,
    string? Iban = null,
    bool SaveAsDefaultBank = true,
    string? CorrelationId = null) : ICommand<WithdrawalRecordDto>;

public sealed class SubmitInfluencerWithdrawalCommandValidator : AbstractValidator<SubmitInfluencerWithdrawalCommand>
{
    public SubmitInfluencerWithdrawalCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(10m)
            .WithMessage(MessageCodes.Influencers.WithdrawalBelowMinimum);

        When(x => !string.IsNullOrWhiteSpace(x.Iban), () =>
        {
            RuleFor(x => x.BankName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Iban!).MinimumLength(15).MaximumLength(34);
        });
    }
}
