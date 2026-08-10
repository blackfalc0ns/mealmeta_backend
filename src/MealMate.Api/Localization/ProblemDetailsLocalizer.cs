using System.Globalization;
using MealMate.Application.Common.Exceptions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Localization;

/// <summary>
/// Builds localized ProblemDetails from application errors and exceptions.
/// </summary>
public sealed class ProblemDetailsLocalizer
{
    private readonly ILocalizedMessageProvider _messages;

    public ProblemDetailsLocalizer(ILocalizedMessageProvider messages) => _messages = messages;

    public string Translate(Error error)
    {
        var args = error.Arguments is { Length: > 0 }
            ? Array.ConvertAll(error.Arguments, x => x ?? string.Empty)
            : Array.Empty<object>();

        return args.Length == 0
            ? _messages.GetMessage(error.Code)
            : _messages.GetMessage(error.Code, arguments: args);
    }

    public string Translate(ValidationFailureDetail detail)
    {
        var args = detail.Arguments is { Length: > 0 } a
            ? Array.ConvertAll(a, x => x ?? string.Empty)
            : Array.Empty<object>();

        return args.Length == 0
            ? _messages.GetMessage(detail.Code)
            : _messages.GetMessage(detail.Code, arguments: args);
    }

    public ValidationProblemDetails CreateValidationProblem(ValidationException exception)
    {
        var errors = exception.FieldErrors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(Translate).Distinct().ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Title = _messages.GetMessage(MessageCodes.Titles.ValidationFailed),
            Detail = _messages.GetMessage(MessageCodes.Common.ValidationFailed),
        };

        problem.Extensions["code"] = MessageCodes.Common.ValidationFailed;
        return problem;
    }

    public ProblemDetails CreateDomainProblem(DomainException exception)
    {
        var code = exception.Code ?? MessageCodes.Domain.Generic;
        return new ProblemDetails
        {
            Title = _messages.GetMessage(MessageCodes.Titles.DomainViolation),
            Detail = _messages.GetMessage(code),
            Extensions = { ["code"] = code },
        };
    }

    public ProblemDetails CreateUnauthorizedProblem() =>
        new()
        {
            Title = _messages.GetMessage(MessageCodes.Titles.Unauthorized),
            Detail = _messages.GetMessage(MessageCodes.Common.Unauthorized),
            Extensions = { ["code"] = MessageCodes.Common.Unauthorized },
        };

    public ProblemDetails CreateServerErrorProblem() =>
        new()
        {
            Title = _messages.GetMessage(MessageCodes.Titles.ServerError),
            Detail = _messages.GetMessage(MessageCodes.Common.ServerError),
            Extensions = { ["code"] = MessageCodes.Common.ServerError },
        };

    public ProblemDetails CreateFromError(Error error)
    {
        var problem = new ProblemDetails
        {
            Title = GetTitleForErrorType(error.Type),
            Detail = Translate(error),
            Extensions = { ["code"] = error.Code },
        };

        return problem;
    }

    private string GetTitleForErrorType(ErrorType type) =>
        type switch
        {
            ErrorType.Validation => _messages.GetMessage(MessageCodes.Titles.ValidationFailed),
            ErrorType.Unauthorized => _messages.GetMessage(MessageCodes.Titles.Unauthorized),
            ErrorType.Forbidden => _messages.GetMessage(MessageCodes.Titles.Unauthorized),
            ErrorType.NotFound => _messages.GetMessage("errors.not_found.title"),
            ErrorType.Conflict => _messages.GetMessage("errors.conflict.title"),
            _ => _messages.GetMessage(MessageCodes.Titles.DomainViolation),
        };
}
