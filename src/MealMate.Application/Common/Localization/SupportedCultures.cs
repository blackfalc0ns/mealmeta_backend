using System.Globalization;

namespace MealMate.Application.Common.Localization;

public static class SupportedCultures
{
    public const string English = "en";
    public const string Arabic = "ar";

    public static readonly CultureInfo ArabicCulture = CultureInfo.GetCultureInfo(Arabic);
    public static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo(English);

    public static readonly CultureInfo Default = CultureInfo.GetCultureInfo(English);

    public static readonly IReadOnlyList<CultureInfo> All =
    [
        CultureInfo.GetCultureInfo(English),
        CultureInfo.GetCultureInfo(Arabic),
    ];

    public static readonly string[] Names = [English, Arabic];
}
