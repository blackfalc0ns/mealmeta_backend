namespace MealMate.Application.Common.Localization;

/// <summary>
/// Stable message codes used across validators, use cases, and domain rules.
/// Resource keys in Messages.resx must match these values exactly.
/// </summary>
public static class MessageCodes
{
    public static class Titles
    {
        public const string ValidationFailed = "errors.validation.title";
        public const string DomainViolation = "errors.domain.title";
        public const string Unauthorized = "errors.unauthorized.title";
        public const string ServerError = "errors.server.title";
    }

    public static class Common
    {
        public const string ServerError = "errors.common.server_error";
        public const string Unauthorized = "errors.common.unauthorized";
        public const string ValidationFailed = "errors.validation.failed";
        public const string Unknown = "errors.common.unknown";
    }

    public static class Validation
    {
        public const string Unknown = "validation.unknown";
        public const string Required = "validation.required";
        public const string EmailInvalid = "validation.email.invalid";
        public const string MaxLength = "validation.max_length";
        public const string MinLength = "validation.min_length";
        public const string GreaterThan = "validation.greater_than";
        public const string LessThan = "validation.less_than";
        public const string InclusiveBetween = "validation.inclusive_between";
    }

    public static class Domain
    {
        public const string Generic = "domain.generic";
        public const string MoneyCurrencyRequired = "domain.money.currency_required";
        public const string MoneyCurrencyMismatch = "domain.money.currency_mismatch";
        public const string PercentageNegative = "domain.percentage.negative";
    }
}
