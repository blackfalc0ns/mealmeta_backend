using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CustomerPush;

public sealed record SendAdminCustomerTestPushCommand(Guid CustomerUserId)
    : ICommand<CustomerPushTestResultDto>;

public sealed class SendAdminCustomerTestPushCommandHandler
    : ICommandHandler<SendAdminCustomerTestPushCommand, CustomerPushTestResultDto>
{
    private readonly IUserAccountService _users;
    private readonly ICustomerPushTestService _testPush;

    public SendAdminCustomerTestPushCommandHandler(
        IUserAccountService users,
        ICustomerPushTestService testPush)
    {
        _users = users;
        _testPush = testPush;
    }

    public async Task<Result<CustomerPushTestResultDto>> Handle(
        SendAdminCustomerTestPushCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(request.CustomerUserId, cancellationToken);
        if (user is null)
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");

        if (user.UserType != UserType.Customer)
            return Error.Validation("push.not_a_customer", "Test push is only available for customer accounts.");

        return await _testPush.SendTestAsync(request.CustomerUserId, cancellationToken);
    }
}
