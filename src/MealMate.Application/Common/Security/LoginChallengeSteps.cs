namespace MealMate.Application.Common.Security;

public static class LoginChallengeSteps
{
    public const string EnrollTotp = "enroll_totp";
    public const string VerifyTotp = "verify_totp";

    public static bool IsValid(string? step) =>
        string.Equals(step, EnrollTotp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(step, VerifyTotp, StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalize(string? step, out string normalized)
    {
        if (string.Equals(step, EnrollTotp, StringComparison.OrdinalIgnoreCase))
        {
            normalized = EnrollTotp;
            return true;
        }

        if (string.Equals(step, VerifyTotp, StringComparison.OrdinalIgnoreCase))
        {
            normalized = VerifyTotp;
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
