using Microsoft.AspNetCore.Mvc.Filters;

namespace MealMate.Api.Common;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DeprecatedRouteAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        context.HttpContext.Response.Headers["Deprecation"] = "true";
        context.HttpContext.Response.Headers.Append("Link", "</menu-plans>; rel=\"successor-version\"");
    }
}
