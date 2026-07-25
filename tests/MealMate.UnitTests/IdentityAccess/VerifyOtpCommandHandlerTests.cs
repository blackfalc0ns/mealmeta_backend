using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class VerifyOtpCommandHandlerTests
{
    [Theory]
    [InlineData("PasswordReset")]
    [InlineData("passwordreset")]
    [InlineData("AdminInvite")]
    [InlineData("admininvite")]
    public async Task VerifyOtp_rejects_password_reset_and_admin_invite_purposes(string purpose)
    {
        var otp = Substitute.For<IOtpService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new VerifyOtpCommandHandler(otp, users, audit);
        var result = await handler.Handle(
            new VerifyOtpCommand("a@test.com", purpose, "123456"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Unknown);
        await otp.DidNotReceive().VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyOtp_allows_verify_email()
    {
        var otp = Substitute.For<IOtpService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();

        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                Guid.NewGuid(), "a@test.com", null, "A", UserType.Customer,
                AccountStatus.Active, null, false, false, DateTime.UtcNow, null));

        var handler = new VerifyOtpCommandHandler(otp, users, audit);
        var result = await handler.Handle(
            new VerifyOtpCommand("a@test.com", "VerifyEmail", "123456"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await otp.Received(1).VerifyAsync(
            Arg.Is<OtpVerifyRequest>(r => r.Purpose == "VerifyEmail"),
            Arg.Any<CancellationToken>());
    }
}
