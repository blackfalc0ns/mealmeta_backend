using MealMate.Api.Localization;
using MealMate.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Common;

/// <summary>
/// Base for all API controllers. Converts application <see cref="Result"/> failures
/// into localized ProblemDetails using the request culture.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;
    private ProblemDetailsLocalizer? _localizer;

    protected ISender Sender =>
        _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    private ProblemDetailsLocalizer Localizer =>
        _localizer ??= HttpContext.RequestServices.GetRequiredService<ProblemDetailsLocalizer>();

    protected IActionResult HandleResult(Result result) =>
        result.IsSuccess ? NoContent() : ProblemFromError(result.Error);

    protected IActionResult HandleResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : ProblemFromError(result.Error);

    protected IActionResult HandleCreated<T>(Result<T> result, string location) =>
        result.IsSuccess ? Created(location, result.Value) : ProblemFromError(result.Error);

    private IActionResult ProblemFromError(Error error)
    {
        var problem = Localizer.CreateFromError(error);
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        problem.Status = statusCode;
        problem.Instance = HttpContext.Request.Path;

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
