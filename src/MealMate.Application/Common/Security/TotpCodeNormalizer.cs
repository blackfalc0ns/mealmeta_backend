namespace MealMate.Application.Common.Security;

public static class TotpCodeNormalizer
{
    public static string Normalize(string code) =>
        code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
}
