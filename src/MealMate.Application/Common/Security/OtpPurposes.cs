namespace MealMate.Application.Common.Security;

internal static class OtpPurposes
{
    internal const string VerifyEmail = "VerifyEmail";
    internal const string VerifyPhone = "VerifyPhone";
    internal const string PasswordReset = "PasswordReset";
    internal const string AdminInvite = "AdminInvite";

    private static readonly HashSet<string> AnonymousSendAllowlist =
        new(StringComparer.OrdinalIgnoreCase) { VerifyEmail, VerifyPhone, PasswordReset };

    internal static bool IsAllowedForAnonymousSend(string purpose) =>
        AnonymousSendAllowlist.Contains(purpose);
}
