using MealMate.Application.Common.Localization;

namespace MealMate.Application.Common.Exceptions;

/// <summary>
/// Aggregated FluentValidation failures. Messages are resolved at the API boundary
/// from <see cref="FieldErrors"/> codes using the request culture.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException()
        : base(MessageCodes.Common.ValidationFailed)
        => FieldErrors = new Dictionary<string, IReadOnlyList<ValidationFailureDetail>>();

    public ValidationException(IReadOnlyDictionary<string, IReadOnlyList<ValidationFailureDetail>> fieldErrors)
        : base(MessageCodes.Common.ValidationFailed)
        => FieldErrors = fieldErrors;

    public IReadOnlyDictionary<string, IReadOnlyList<ValidationFailureDetail>> FieldErrors { get; }
}
