using FluentValidation;
using FluentValidation.Results;
using MealMate.Application.Common.Exceptions;
using MealMate.Application.Common.Localization;
using MediatR;
using AppValidationException = MealMate.Application.Common.Exceptions.ValidationException;

namespace MealMate.Application.Common.Behaviors;

/// <summary>
/// Runs FluentValidation and raises <see cref="ValidationException"/> with stable
/// message codes (not hard-coded English text).
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next();
        }

        var fieldErrors = failures
            .GroupBy(f => string.IsNullOrWhiteSpace(f.PropertyName) ? string.Empty : f.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ValidationFailureDetail>)g
                    .Select(ToFailureDetail)
                    .ToList());

        throw new AppValidationException(fieldErrors);
    }

    private static ValidationFailureDetail ToFailureDetail(ValidationFailure failure)
    {
        var code = string.IsNullOrWhiteSpace(failure.ErrorCode)
            ? MessageCodes.Validation.Unknown
            : failure.ErrorCode;

        return new ValidationFailureDetail(code, ExtractArguments(failure));
    }

    private static object?[] ExtractArguments(ValidationFailure failure)
    {
        if (failure.FormattedMessagePlaceholderValues is null ||
            failure.FormattedMessagePlaceholderValues.Count == 0)
        {
            return [];
        }

        // Prefer common FluentValidation placeholders in a stable order.
        var orderedKeys = new[]
        {
            "PropertyName",
            "PropertyValue",
            "MinLength",
            "MaxLength",
            "MinValue",
            "MaxValue",
            "From",
            "To",
            "ComparisonValue",
        };

        var values = new List<object?>();
        foreach (var key in orderedKeys)
        {
            if (failure.FormattedMessagePlaceholderValues.TryGetValue(key, out var value))
            {
                values.Add(value);
            }
        }

        if (values.Count > 0)
        {
            return values.ToArray();
        }

        return failure.FormattedMessagePlaceholderValues.Values.ToArray();
    }
}
