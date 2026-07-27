namespace MealMate.Application.Common.Security;

public static class OtpPurposes
{
    public const string VerifyEmail = "VerifyEmail";
    public const string VerifyPhone = "VerifyPhone";
    public const string PasswordReset = "PasswordReset";
    public const string AdminInvite = "AdminInvite";

    private static readonly Dictionary<string, string> CanonicalByPurpose =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [VerifyEmail] = VerifyEmail,
            [VerifyPhone] = VerifyPhone,
            [PasswordReset] = PasswordReset,
            [AdminInvite] = AdminInvite,
        };

    private static readonly HashSet<string> AnonymousSendAllowlist =
        new(StringComparer.OrdinalIgnoreCase) { VerifyEmail, VerifyPhone, PasswordReset };

    public static string Normalize(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return purpose;
        }

        return CanonicalByPurpose.TryGetValue(purpose.Trim(), out var canonical)
            ? canonical
            : purpose.Trim();
    }

    public static bool IsAllowedForAnonymousSend(string purpose) =>
        AnonymousSendAllowlist.Contains(purpose);
}
