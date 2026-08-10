using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class RestaurantAccountRegistrationTests
{
    private static readonly PendingRegistration Pending =
        new("owner@restaurant.test", "0500000000", "StrongPass123", "Restaurant Owner");

    [Theory]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoNumberHere")]
    [InlineData("Short1A")]
    public void Start_validator_rejects_passwords_outside_the_published_policy(string password)
    {
        var result = new StartRestaurantRegistrationCommandValidator().Validate(
            new StartRestaurantRegistrationCommand(
                Pending.Email,
                Pending.Phone,
                password,
                Pending.FullName));

        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(StartRestaurantRegistrationCommand.Password));

        if (password.Length >= 8)
        {
            result.Errors.Should().Contain(error =>
                error.PropertyName == nameof(StartRestaurantRegistrationCommand.Password)
                && error.ErrorCode == MessageCodes.Auth.PasswordTooWeak);
        }
    }

    [Fact]
    public void Start_validator_accepts_password_matching_the_published_policy()
    {
        var result = new StartRestaurantRegistrationCommandValidator().Validate(
            new StartRestaurantRegistrationCommand(
                Pending.Email,
                Pending.Phone,
                "MealMate8",
                Pending.FullName));

        result.Errors.Should().NotContain(error =>
            error.PropertyName == nameof(StartRestaurantRegistrationCommand.Password));
    }

    [Fact]
    public async Task Start_sends_otp_without_creating_user()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var tokens = Substitute.For<IRestaurantAccountRegistrationTokenService>();
        tokens.CreateToken(Arg.Any<PendingRegistration>())
            .Returns(new CreatedRegistrationToken("protected-token", DateTime.UtcNow.AddMinutes(10)));
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new StartRestaurantRegistrationCommandHandler(users, otp, tokens);
        var result = await handler.Handle(
            new StartRestaurantRegistrationCommand(
                Pending.Email,
                Pending.Phone,
                Pending.Password,
                Pending.FullName),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RegistrationToken.Should().Be("protected-token");
        await users.DidNotReceive().CreateAsync(
            Arg.Any<CreateUserRequest>(),
            Arg.Any<CancellationToken>());
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(request =>
                request.Destination == Pending.Email
                && request.Purpose == OtpPurposes.RestaurantRegistration),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_otp_never_creates_user()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var tokens = Substitute.For<IRestaurantAccountRegistrationTokenService>();
        tokens.ResolveToken("protected-token").Returns(Result.Success(Pending));
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.OtpInvalid, "Invalid code."));

        var handler = new VerifyRestaurantRegistrationCommandHandler(users, otp, tokens);
        var result = await handler.Handle(
            new VerifyRestaurantRegistrationCommand("protected-token", "000000"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await users.DidNotReceive().CreateAsync(
            Arg.Any<CreateUserRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Valid_otp_creates_and_confirms_restaurant_owner_once()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var tokens = Substitute.For<IRestaurantAccountRegistrationTokenService>();
        var userId = Guid.NewGuid();
        tokens.ResolveToken("protected-token").Returns(Result.Success(Pending));
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(new UserAccountDto(
                userId,
                Pending.Email,
                Pending.Phone,
                Pending.FullName,
                UserType.Restaurant,
                AccountStatus.Registered,
                null,
                false,
                false,
                DateTime.UtcNow,
                null)));
        users.AddToRoleAsync(userId, AppRoles.RestaurantOwner, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new VerifyRestaurantRegistrationCommandHandler(users, otp, tokens);
        var result = await handler.Handle(
            new VerifyRestaurantRegistrationCommand("protected-token", "123456"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await users.Received(1).CreateAsync(
            Arg.Is<CreateUserRequest>(request =>
                request.Email == Pending.Email
                && request.Password == Pending.Password
                && request.UserType == UserType.Restaurant),
            Arg.Any<CancellationToken>());
        await users.Received(1).AddToRoleAsync(
            userId,
            AppRoles.RestaurantOwner,
            Arg.Any<CancellationToken>());
        await users.Received(1).SetEmailConfirmedAsync(
            userId,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_registration_token_fails_before_otp_or_user_creation()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var tokens = Substitute.For<IRestaurantAccountRegistrationTokenService>();
        tokens.ResolveToken("invalid")
            .Returns(Error.Validation(MessageCodes.Auth.InvalidState, "Invalid token."));

        var handler = new VerifyRestaurantRegistrationCommandHandler(users, otp, tokens);
        var result = await handler.Handle(
            new VerifyRestaurantRegistrationCommand("invalid", "123456"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await otp.DidNotReceive().VerifyAsync(
            Arg.Any<OtpVerifyRequest>(),
            Arg.Any<CancellationToken>());
        await users.DidNotReceive().CreateAsync(
            Arg.Any<CreateUserRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_uses_destination_from_protected_token()
    {
        var otp = Substitute.For<IOtpService>();
        var tokens = Substitute.For<IRestaurantAccountRegistrationTokenService>();
        tokens.ResolveToken("protected-token").Returns(Result.Success(Pending));
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new ResendRestaurantRegistrationOtpCommandHandler(otp, tokens);
        var result = await handler.Handle(
            new ResendRestaurantRegistrationOtpCommand("protected-token"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(request =>
                request.Destination == Pending.Email
                && request.Purpose == OtpPurposes.RestaurantRegistration),
            Arg.Any<CancellationToken>());
    }
}
