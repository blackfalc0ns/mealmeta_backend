using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Middleware;

/// <summary>Previously issued customer tokens cannot bypass mandatory family password setup.</summary>
public sealed class FamilyPasswordSetupMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser,
        IAppDbContext db, ProblemDetailsLocalizer localizer)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && currentUser.UserType == UserType.Customer && currentUser.UserId is Guid userId
            && await db.FamilyMemberAccesses.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.LinkedCustomerUserId == userId && x.RequiresPasswordSetup, context.RequestAborted))
        {
            var problem = localizer.CreateFromError(Error.Forbidden(MessageCodes.Family.PasswordSetupRequired));
            problem.Status = StatusCodes.Status403Forbidden;
            problem.Instance = context.Request.Path;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
            return;
        }
        await next(context);
    }
}
