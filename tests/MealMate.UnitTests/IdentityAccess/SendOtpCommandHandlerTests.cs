using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Auth;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class SendOtpCommandHandlerTests
{
    [Theory]
    [InlineData("VerifyEmail")]
    [InlineData("VerifyPhone")]
    [InlineData("PasswordReset")]
    [InlineData("verifyemail")]
    public async Task Anonymous_send_allows_known_purposes(string purpose)
    {
        var otp = Substitute.For<IOtpService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var currentUser = Substitute.For<ICurrentUser>();

        currentUser.UserId.Returns((Guid?)null);
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new SendOtpCommandHandler(otp, users, audit, currentUser);
        var result = await handler.Handle(
            new SendOtpCommand("a@test.com", "Email", purpose),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(r => r.Purpose.Equals(purpose, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anonymous_send_rejects_unknown_purpose()
    {
        var otp = Substitute.For<IOtpService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var currentUser = Substitute.For<ICurrentUser>();

        currentUser.UserId.Returns((Guid?)null);

        var handler = new SendOtpCommandHandler(otp, users, audit, currentUser);
        var result = await handler.Handle(
            new SendOtpCommand("a@test.com", "Email", "Login"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Unknown);
        await otp.DidNotReceive().SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authenticated_send_allows_non_allowlisted_purpose()
    {
        var otp = Substitute.For<IOtpService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var currentUser = Substitute.For<ICurrentUser>();

        currentUser.UserId.Returns(Guid.NewGuid());
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new SendOtpCommandHandler(otp, users, audit, currentUser);
        var result = await handler.Handle(
            new SendOtpCommand("a@test.com", "Email", "Login"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
