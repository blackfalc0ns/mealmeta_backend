using MealMate.Api.Localization;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Common;

/// <summary>
/// Base for all API controllers. Converts application <see cref="Result"/> failures
/// into localized ProblemDetails using the request culture.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;
    private ProblemDetailsLocalizer? _localizer;
    private ILocalizedMessageProvider? _messages;

    protected ISender Sender =>
        _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected ILocalizedMessageProvider Messages =>
        _messages ??= HttpContext.RequestServices.GetRequiredService<ILocalizedMessageProvider>();

    private ProblemDetailsLocalizer? Localizer =>
        _localizer ??= HttpContext?.RequestServices?.GetService<ProblemDetailsLocalizer>();

    protected IActionResult HandleResult(Result result) =>
        result is null
            ? ProblemFromError(Error.Failure("common.null_result", "Operation returned a null result."))
            : result.IsSuccess ? NoContent() : ProblemFromError(result.Error);

    protected IActionResult HandleResult<T>(Result<T> result) =>
        result is null
            ? ProblemFromError(Error.Failure("common.null_result", "Operation returned a null result."))
            : result.IsSuccess ? Ok(result.Value) : ProblemFromError(result.Error);

    protected IActionResult HandleCreated<T>(Result<T> result, string location) =>
        result is null
            ? ProblemFromError(Error.Failure("common.null_result", "Operation returned a null result."))
            : result.IsSuccess ? Created(location, result.Value) : ProblemFromError(result.Error);

    protected IActionResult ProblemFromError(Error error)
    {
        var problem = Localizer?.CreateFromError(error) ?? new ProblemDetails
        {
            Title = "Error",
            Detail = error.Description,
            Extensions = { ["code"] = error.Code }
        };
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Gone => StatusCodes.Status410Gone,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        problem.Status = statusCode;
        problem.Instance = HttpContext?.Request?.Path.Value ?? string.Empty;

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
