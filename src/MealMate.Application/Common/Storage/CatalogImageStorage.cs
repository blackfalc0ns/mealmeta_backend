namespace MealMate.Application.Common.Storage;

public static class CatalogImageStorage
{
    public const string ProgramPrefix = "uploads/admin/programs/";
    public const string BundlePrefix = "uploads/admin/bundles/";
    public const string TierPrefix = "uploads/admin/tiers/";
    public const string FamilyPrefix = "uploads/admin/family/";
    public const string IngredientPrefix = "uploads/restaurant/ingredients/";
    public const int MaxKeyLength = 500;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Replace('\\', '/').Trim();
        var key = ExtractStorageKey(trimmed).TrimStart('/');
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public static string? ToContentUrl(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return null;
        }

        var normalized = storageKey.Replace('\\', '/').Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out _) || normalized.StartsWith('/'))
        {
            return normalized;
        }

        return $"/api/v1/uploads/content?key={Uri.EscapeDataString(normalized.TrimStart('/'))}";
    }

    public static bool HasAllowedPrefix(string? storageKey, string prefix)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return true;
        }

        return IsUnderPrefix(storageKey, prefix);
    }

    public static bool IsUnderPrefix(string? storageKey, string prefix)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        var normalized = storageKey.Replace('\\', '/');
        var key = ExtractStorageKey(normalized).TrimStart('/');
        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.Contains("/" + prefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractStorageKey(string value)
    {
        if (!value.Contains("/api/v1/uploads/content", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex == value.Length - 1)
        {
            return value;
        }

        foreach (var part in value[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (!part[..eq].Equals("key", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        return value;
    }
}
