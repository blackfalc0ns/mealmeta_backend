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
        public const string RequiredField = "validation.required_field";
        public const string EmailInvalid = "validation.email.invalid";
        public const string PasswordMinimumLength = "validation.password.minimum_length_8";
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

    public static class Auth
    {
        public const string InvalidCredentials = "auth.invalid_credentials";
        public const string AccountSuspended = "auth.account_suspended";
        public const string AccountClosed = "auth.account_closed";
        public const string DuplicateEmail = "auth.duplicate_email";
        public const string DuplicatePhone = "auth.duplicate_phone";
        public const string InvalidRefreshToken = "auth.invalid_refresh_token";
        public const string OtpInvalid = "auth.otp_invalid";
        public const string OtpExpired = "auth.otp_expired";
        public const string OtpTooManyAttempts = "auth.otp_too_many_attempts";
        public const string PasswordTooWeak = "auth.password_too_weak";
        public const string Forbidden = "auth.forbidden";
        public const string SelfPrivilegeEscalation = "auth.self_privilege_escalation";
        public const string UserNotFound = "auth.user_not_found";
        public const string RoleNotFound = "auth.role_not_found";
        public const string InvalidState = "auth.invalid_state";
        public const string DocumentsIncomplete = "auth.documents_incomplete";
        public const string DuplicateCommercialRegistration = "auth.duplicate_commercial_registration";
        public const string DuplicateInfluencerCode = "auth.duplicate_influencer_code";
        public const string InvitationInvalid = "auth.invitation_invalid";
        public const string AccountLocked = "auth.account_locked";
        public const string OtpSendLimitExceeded = "auth.otp_send_limit_exceeded";
        public const string TotpInvalid = "auth.totp_invalid";
        public const string TotpChallengeInvalid = "auth.totp_challenge_invalid";
        public const string TotpChallengeExpired = "auth.totp_challenge_expired";
        public const string TotpChallengeTooManyAttempts = "auth.totp_challenge_too_many_attempts";
        public const string TotpSetupNotAllowed = "auth.totp_setup_not_allowed";
        public const string TotpRequired = "auth.totp_required";
        public const string TotpEnrollmentFailed = "auth.totp_enrollment_failed";
    }
}
