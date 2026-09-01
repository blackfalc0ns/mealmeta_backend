using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.Extensions.Hosting;

namespace MealMate.Application.Features.CustomerPush;

public sealed record SendCustomerTestPushCommand() : ICommand<CustomerPushTestResultDto>;

public sealed class SendCustomerTestPushCommandHandler
    : ICommandHandler<SendCustomerTestPushCommand, CustomerPushTestResultDto>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerPushTestService _testPush;
    private readonly IHostEnvironment _environment;

    public SendCustomerTestPushCommandHandler(
        ICurrentUser currentUser,
        ICustomerPushTestService testPush,
        IHostEnvironment environment)
    {
        _currentUser = currentUser;
        _testPush = testPush;
        _environment = environment;
    }

    public async Task<Result<CustomerPushTestResultDto>> Handle(
        SendCustomerTestPushCommand request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return Error.Forbidden(
                "push.test_not_allowed",
                "Test push is available in Development only.");
        }

        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");
        }

        var result = await _testPush.SendTestAsync(userId, cancellationToken);
        return result;
    }
}
