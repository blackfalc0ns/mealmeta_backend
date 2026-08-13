using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RestaurantOwnershipAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (currentUser.UserType == UserType.Admin) { await next(); return; }
        if (currentUser.UserId is not { } userId || !context.RouteData.Values.TryGetValue("restaurantId", out var routeValue) || !Guid.TryParse(routeValue?.ToString(), out var restaurantId))
        { context.Result = new ForbidResult(); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<IAppDbContext>();
        if (!await db.RestaurantProfiles.AsNoTracking().AnyAsync(x => x.Id == restaurantId && x.UserId == userId, context.HttpContext.RequestAborted))
        { context.Result = new ForbidResult(); return; }
        if (context.ActionArguments.TryGetValue("id", out var idValue) && idValue is Guid resourceId)
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            var ownsResource = path.Contains("/ingredients", StringComparison.OrdinalIgnoreCase)
                ? await db.Ingredients.AsNoTracking().AnyAsync(x => x.Id == resourceId && x.RestaurantId == restaurantId, context.HttpContext.RequestAborted)
                : !path.Contains("/meals", StringComparison.OrdinalIgnoreCase)
                  || await db.Meals.AsNoTracking().AnyAsync(x => x.Id == resourceId && x.RestaurantId == restaurantId, context.HttpContext.RequestAborted);
            if (!ownsResource) { context.Result = new ForbidResult(); return; }
        }
        await next();
    }
}
