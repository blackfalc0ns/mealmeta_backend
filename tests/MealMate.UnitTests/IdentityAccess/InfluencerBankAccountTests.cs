using FluentAssertions;
using MealMate.Application.Features.IdentityAccess.Influencer;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class InfluencerBankAccountTests
{
    [Fact]
    public void Update_bank_account_validator_accepts_valid_details()
    {
        var validator = new UpdateInfluencerBankAccountCommandValidator();
        var command = new UpdateInfluencerBankAccountCommand(
            BankName: "National Bank of Kuwait",
            AccountHolderName: "Fahad Al-Mutawa",
            Iban: "KW82 NBOK 0000 1234 5678 9012");

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Fahad Al-Mutawa", "KW82NBOK0000123456789012")]
    [InlineData("NBK", "", "KW82NBOK0000123456789012")]
    [InlineData("NBK", "Fahad Al-Mutawa", "")]
    public void Update_bank_account_validator_rejects_empty_fields(
        string bankName,
        string accountHolderName,
        string iban)
    {
        var validator = new UpdateInfluencerBankAccountCommandValidator();
        var command = new UpdateInfluencerBankAccountCommand(
            BankName: bankName,
            AccountHolderName: accountHolderName,
            Iban: iban);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("KW1234")] // Too short (< 15)
    [InlineData("KW82NBOK00001234567890123456789012345")] // Too long (> 34)
    [InlineData("KW82#NBOK@0000!1234")] // Invalid characters
    public void Update_bank_account_validator_rejects_malformed_iban(string iban)
    {
        var validator = new UpdateInfluencerBankAccountCommandValidator();
        var command = new UpdateInfluencerBankAccountCommand(
            BankName: "National Bank of Kuwait",
            AccountHolderName: "Fahad Al-Mutawa",
            Iban: iban);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateInfluencerBankAccountCommand.Iban));
    }

    [Theory]
    [InlineData("KW82NBOK0000123456789012", "KW82 •••• •••• 9012")]
    [InlineData("KW1234567890123", "KW12 •••• •••• 0123")]
    public void MaskIban_masks_middle_characters_properly(string rawIban, string expectedMasked)
    {
        var masked = InfluencerProfileService.MaskIban(rawIban);

        masked.Should().Be(expectedMasked);
    }

    [Fact]
    public async Task GetBanks_returns_popular_kuwait_banks_catalog()
    {
        var service = new InfluencerProfileService(null!);
        var result = await service.GetBanksAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        result.Value.Should().Contain(b => b.Code == "NBK" && b.NameEn == "National Bank of Kuwait");
        result.Value.Should().Contain(b => b.Code == "BOUBYAN" && b.NameEn == "Boubyan Bank");
        result.Value.Should().Contain(b => b.Code == "KFH" && b.NameEn == "Kuwait Finance House");
    }

    [Fact]
    public void Withdrawal_policy_dto_initializes_correctly()
    {
        var policy = new InfluencerWithdrawalPolicyDto(
            MinimumAmount: 10.0m,
            MaximumAmount: 150.0m,
            CurrencyCode: "KWD",
            CanWithdraw: true,
            DisabledReason: null);

        policy.CanWithdraw.Should().BeTrue();
        policy.MinimumAmount.Should().Be(10.0m);
        policy.MaximumAmount.Should().Be(150.0m);
        policy.CurrencyCode.Should().Be("KWD");
        policy.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void PagedResultDto_calculates_page_navigation_flags_properly()
    {
        var paged = new PagedResultDto<string>(
            Items: new List<string> { "item1", "item2" },
            Page: 2,
            PageSize: 10,
            TotalCount: 35,
            TotalPages: 4,
            HasPreviousPage: true,
            HasNextPage: true);

        paged.HasPreviousPage.Should().BeTrue();
        paged.HasNextPage.Should().BeTrue();
        paged.TotalPages.Should().Be(4);
    }
}
