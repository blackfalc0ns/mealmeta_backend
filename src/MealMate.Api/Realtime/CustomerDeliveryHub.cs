using System.Security.Claims;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

[Authorize(Policy = CustomerDeliveryHub.CustomerPolicy)]
public sealed class CustomerDeliveryHub : Hub
{
    public const string HubPath = "/hubs/customer-delivery";
    public const string EventCustomerDeliveryChanged = "customer-delivery-changed";
    public const string CustomerPolicy = "CustomerDeliveryCustomerOnly";

    public static string GroupForUser(Guid userId) => $"customer-delivery:user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var userType = Context.User?.FindFirstValue(AppClaimTypes.UserType)
                       ?? Context.User?.FindFirstValue(ClaimTypes.Role);
        var userIdValue = Context.User?.FindFirstValue(AppClaimTypes.UserId)
                          ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.Equals(userType, UserType.Customer.ToString(), StringComparison.Ordinal)
            || !Guid.TryParse(userIdValue, out var userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(userId));
        await base.OnConnectedAsync();
    }
}
