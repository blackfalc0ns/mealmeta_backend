using FluentAssertions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.IdentityAccess.Influencer;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class InfluencerPartnerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9.999)]
    public void Withdrawal_validator_rejects_amount_below_ten_kwd_with_message_code(decimal amount)
    {
        var validator = new SubmitInfluencerWithdrawalCommandValidator();
        var command = new SubmitInfluencerWithdrawalCommand(
            Amount: amount,
            WithdrawalMethod: "BankTransfer",
            BankName: "National Bank of Kuwait",
            AccountHolderName: "Ahmed Salem",
            Iban: "KW81NBOK0000123456789010",
            SaveAsDefaultBank: true);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == MessageCodes.Influencers.WithdrawalBelowMinimum);
    }

    [Theory]
    [InlineData("", "Ahmed Salem", "KW81NBOK0000123456789010")]
    [InlineData("NBK", "", "KW81NBOK0000123456789010")]
    [InlineData("NBK", "Ahmed Salem", "")]
    public void Withdrawal_validator_rejects_empty_bank_fields(string bankName, string accountHolderName, string iban)
    {
        var validator = new SubmitInfluencerWithdrawalCommandValidator();
        var command = new SubmitInfluencerWithdrawalCommand(
            Amount: 25m,
            WithdrawalMethod: "BankTransfer",
            BankName: bankName,
            AccountHolderName: accountHolderName,
            Iban: iban,
            SaveAsDefaultBank: true);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Withdrawal_validator_accepts_valid_command()
    {
        var validator = new SubmitInfluencerWithdrawalCommandValidator();
        var command = new SubmitInfluencerWithdrawalCommand(
            Amount: 50m,
            WithdrawalMethod: "BankTransfer",
            BankName: "National Bank of Kuwait",
            AccountHolderName: "Ahmed Salem",
            Iban: "KW81NBOK0000123456789010",
            SaveAsDefaultBank: true);

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
