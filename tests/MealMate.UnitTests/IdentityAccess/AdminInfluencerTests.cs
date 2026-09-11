using FluentAssertions;
using MealMate.Application.Features.IdentityAccess.Influencer;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class AdminInfluencerTests
{
    [Fact]
    public void Approve_withdrawal_validator_requires_transfer_reference_number()
    {
        var validator = new ApproveInfluencerWithdrawalCommandValidator();
        var command = new ApproveInfluencerWithdrawalCommand(Guid.NewGuid(), "");

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApproveInfluencerWithdrawalCommand.TransferReferenceNumber));
    }

    [Fact]
    public void Approve_withdrawal_validator_accepts_valid_command()
    {
        var validator = new ApproveInfluencerWithdrawalCommandValidator();
        var command = new ApproveInfluencerWithdrawalCommand(
            Guid.NewGuid(),
            "TRX-NBK-2026-99123",
            DateTime.UtcNow,
            "Transfer executed via online banking");

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Reject_withdrawal_validator_requires_rejection_reason()
    {
        var validator = new RejectInfluencerWithdrawalCommandValidator();
        var command = new RejectInfluencerWithdrawalCommand(Guid.NewGuid(), "");

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RejectInfluencerWithdrawalCommand.RejectionReason));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Update_commission_rate_validator_rejects_out_of_range(decimal rate)
    {
        var validator = new UpdateInfluencerCommissionRateCommandValidator();
        var command = new UpdateInfluencerCommissionRateCommand(Guid.NewGuid(), rate, "Testing");

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_bank_info_validator_validates_iban_and_bank_name()
    {
        var validator = new UpdateInfluencerBankInfoCommandValidator();
        var command = new UpdateInfluencerBankInfoCommand(Guid.NewGuid(), "", "", "123");

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterOrEqualTo(3);
    }
}
