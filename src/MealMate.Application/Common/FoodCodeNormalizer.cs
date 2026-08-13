namespace MealMate.Application.Common;

public static class FoodCodeNormalizer
{
    private static readonly char[] Separators = [',', ';', '|'];

    public static IReadOnlyList<string> Split(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    public static string Join(IEnumerable<string>? values) => values is null
        ? string.Empty
        : string.Join(',', values.SelectMany(Split).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal));

    public static string Normalize(string value) => value.Trim().Replace(' ', '_').ToUpperInvariant();
}
