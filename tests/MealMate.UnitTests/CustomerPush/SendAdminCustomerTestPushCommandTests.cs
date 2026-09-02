using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerPush;
using MealMate.Domain.Enums;
using NSubstitute;

namespace MealMate.UnitTests.CustomerPush;

public sealed class SendAdminCustomerTestPushCommandTests
{
    [Fact]
    public async Task Returns_not_found_when_user_does_not_exist()
    {
        var users = Substitute.For<IUserAccountService>();
        var testPush = Substitute.For<ICustomerPushTestService>();
        users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((UserAccountDto?)null);

        var handler = new SendAdminCustomerTestPushCommandHandler(users, testPush);
        var userId = Guid.NewGuid();

        var result = await handler.Handle(
            new SendAdminCustomerTestPushCommand(userId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.UserNotFound);
        await testPush.DidNotReceive().SendTestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_non_customer_users()
    {
        var users = Substitute.For<IUserAccountService>();
        var testPush = Substitute.For<ICustomerPushTestService>();
        var userId = Guid.NewGuid();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Account(userId, UserType.Restaurant));

        var handler = new SendAdminCustomerTestPushCommandHandler(users, testPush);

        var result = await handler.Handle(
            new SendAdminCustomerTestPushCommand(userId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("push.not_a_customer");
        await testPush.DidNotReceive().SendTestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sends_test_push_for_a_customer()
    {
        var users = Substitute.For<IUserAccountService>();
        var testPush = Substitute.For<ICustomerPushTestService>();
        var userId = Guid.NewGuid();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Account(userId, UserType.Customer));

        var expected = new CustomerPushTestResultDto(true, 1, 1, 0, []);
        testPush.SendTestAsync(userId, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new SendAdminCustomerTestPushCommandHandler(users, testPush);

        var result = await handler.Handle(
            new SendAdminCustomerTestPushCommand(userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
        await testPush.Received(1).SendTestAsync(userId, Arg.Any<CancellationToken>());
    }

    private static UserAccountDto Account(Guid id, UserType userType)
        => new(
            id,
            "user@test.com",
            "+96500000000",
            "Test User",
            userType,
            AccountStatus.Active,
            null,
            true,
            true,
            DateTime.UtcNow,
            DateTime.UtcNow);
}
