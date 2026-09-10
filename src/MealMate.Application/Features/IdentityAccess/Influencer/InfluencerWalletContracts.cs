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
    string Iban);

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
    bool HasActiveWithdrawalRequest,
    decimal MinimumWithdrawalAmount,
    IReadOnlyList<WalletTransactionDto> RecentTransactions);

public sealed record WithdrawalRecordDto(
    Guid WithdrawalId,
    string ReferenceNumber,
    CurrencyAmountDto Amount,
    CurrencyAmountDto TransferFee,
    CurrencyAmountDto NetAmount,
    string Status,
    string Method,
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

public sealed record GetInfluencerWithdrawalsQuery(string? Status) : IQuery<IReadOnlyList<WithdrawalRecordDto>>;

public sealed record GetInfluencerWithdrawalDetailsQuery(Guid Id) : IQuery<WithdrawalRecordDto>;

public sealed record SubmitInfluencerWithdrawalCommand(
    decimal Amount,
    string WithdrawalMethod,
    string BankName,
    string AccountHolderName,
    string Iban,
    bool SaveAsDefaultBank) : ICommand<WithdrawalRecordDto>;

public sealed class SubmitInfluencerWithdrawalCommandValidator : AbstractValidator<SubmitInfluencerWithdrawalCommand>
{
    public SubmitInfluencerWithdrawalCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(10m)
            .WithMessage(MessageCodes.Influencers.WithdrawalBelowMinimum);

        RuleFor(x => x.BankName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Iban).NotEmpty().MaximumLength(50);
    }
}
