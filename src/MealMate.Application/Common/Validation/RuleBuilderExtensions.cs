using FluentValidation;

namespace MealMate.Application.Common.Validation;

/// <summary>
/// FluentValidation helpers. Rules should use message codes; the API translates
/// them using Accept-Language (en / ar).
/// </summary>
public static class RuleBuilderExtensions
{
    /// <summary>
    /// Sets a stable error code used for localization. The raw message is also set
    /// to the code so logs remain readable before translation.
    /// </summary>
    public static IRuleBuilderOptions<T, TProperty> WithMessageCode<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule,
        string code)
        => rule.WithErrorCode(code).WithMessage(code);
}
