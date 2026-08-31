using MealMate.Api.Localization;
using MealMate.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ValidationException = MealMate.Application.Common.Exceptions.ValidationException;

namespace MealMate.Api.Common;

/// <summary>
/// Translates unhandled exceptions into localized RFC 7807 ProblemDetails responses.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var localizer = httpContext.RequestServices.GetRequiredService<ProblemDetailsLocalizer>();
        var (status, problem) = Map(exception, localizer);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning("Handled exception: {Message}", exception.Message);
        }

        httpContext.Response.StatusCode = status;
        problem.Status = status;
        problem.Instance = httpContext.Request.Path;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static (int Status, ProblemDetails Problem) Map(
        Exception exception,
        ProblemDetailsLocalizer localizer) =>
        exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                localizer.CreateValidationProblem(validation)),

            DomainException domain => (
                StatusCodes.Status422UnprocessableEntity,
                localizer.CreateDomainProblem(domain)),

            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                localizer.CreateUnauthorizedProblem()),

            // Client aborted / navigated away — not a server failure.
            // Use numeric 499 (nginx Client Closed Request); StatusCodes.Status499*
            // is available on recent ASP.NET Core versions but keep this portable.
            OperationCanceledException => (
                499,
                new ProblemDetails
                {
                    Title = "Request canceled",
                    Detail = "The client canceled the request.",
                    Type = "https://httpstatuses.com/499",
                }),

            _ => (
                StatusCodes.Status500InternalServerError,
                localizer.CreateServerErrorProblem()),
        };
}
