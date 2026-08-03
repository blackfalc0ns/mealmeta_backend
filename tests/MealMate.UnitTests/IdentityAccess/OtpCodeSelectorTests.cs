using FluentAssertions;
using MealMate.Persistence.Identity;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class OtpCodeSelectorTests
{
    private const string DevCode = "123456";
    private const string DeterministicCode = "987654";

    private static OtpOptions WhatsAppOnly() => new() { WhatsAppFixedCode = DevCode };

    [Theory]
    [InlineData("WhatsApp")]
    [InlineData("whatsapp")]
    [InlineData("WHATSAPP")]
    [InlineData(" WhatsApp ")]
    [InlineData("Phone")]
    [InlineData("phone")]
    [InlineData(" PHONE ")]
    public void WhatsApp_or_Phone_channel_returns_WhatsAppFixedCode(string channel)
    {
        var code = OtpService.SelectCode(channel, WhatsAppOnly());

        code.Should().Be(DevCode);
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("email")]
    [InlineData("SMS")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_WhatsApp_channel_does_not_use_WhatsAppFixedCode(string? channel)
    {
        var code = OtpService.SelectCode(channel, WhatsAppOnly(), () => DeterministicCode);

        code.Should().Be(DeterministicCode);
    }

    [Fact]
    public void Email_generates_random_six_digit_code_when_only_WhatsAppFixedCode_set()
    {
        var code = OtpService.SelectCode("Email", WhatsAppOnly(), () => DeterministicCode);

        code.Should().Be(DeterministicCode);
    }

    [Fact]
    public void Legacy_FixedCode_fallback_works_when_WhatsAppFixedCode_absent()
    {
        var options = new OtpOptions { FixedCode = "654321" };

        var code = OtpService.SelectCode("Email", options);

        code.Should().Be("654321");
    }

    [Fact]
    public void No_fixed_codes_configured_generates_random_code()
    {
        var options = new OtpOptions();

        var code = OtpService.SelectCode("WhatsApp", options, () => DeterministicCode);

        code.Should().Be(DeterministicCode);
    }
}
