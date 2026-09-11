using FluentValidation;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed record AdminWithdrawalItemDto(
    Guid Id,
    string ReferenceNumber,
    Guid InfluencerProfileId,
    string InfluencerName,
    string? InfluencerCode,
    string? InfluencerPhone,
    decimal Amount,
    string Currency,
    string Status,
    string? BankName,
    string? AccountHolderName,
    string? Iban,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewedByAdminName,
    string? TransferReferenceNumber,
    string? RejectionReason,
    string? Notes);

public sealed record ListAdminWithdrawalsQuery(
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AdminWithdrawalItemDto>>;

public sealed record ApproveInfluencerWithdrawalCommand(
    Guid WithdrawalId,
    string TransferReferenceNumber,
    DateTime? TransferDateUtc = null,
    string? Notes = null) : ICommand<AdminWithdrawalItemDto>;

public sealed class ApproveInfluencerWithdrawalCommandValidator : AbstractValidator<ApproveInfluencerWithdrawalCommand>
{
    public ApproveInfluencerWithdrawalCommandValidator()
    {
        RuleFor(x => x.WithdrawalId).NotEmpty();
        RuleFor(x => x.TransferReferenceNumber).NotEmpty().MaximumLength(100);
    }
}

public sealed record RejectInfluencerWithdrawalCommand(
    Guid WithdrawalId,
    string RejectionReason) : ICommand<AdminWithdrawalItemDto>;

public sealed class RejectInfluencerWithdrawalCommandValidator : AbstractValidator<RejectInfluencerWithdrawalCommand>
{
    public RejectInfluencerWithdrawalCommandValidator()
    {
        RuleFor(x => x.WithdrawalId).NotEmpty();
        RuleFor(x => x.RejectionReason).NotEmpty().MaximumLength(500);
    }
}
