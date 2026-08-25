using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MealMate.Application.Features.CustomerCheckout;

/// <summary>
/// Frozen checkout meal snapshots are stored as jsonb. Postgres/Npgsql round-trips can
/// change whitespace/number formatting, so byte-exact SHA over the raw strings is unstable.
/// Capture still records a hash for audit; fulfillment validates parseable non-empty JSON.
/// </summary>
public static class FrozenSnapshotIntegrity
{
    public static string ComputeContentHash(string slotsJson, string mealsJson)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slotsJson + "\n" + mealsJson))).ToLowerInvariant();

    public static bool HasUsablePayload(string? slotsJson, string? mealsJson, string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash)) return false;
        if (string.IsNullOrWhiteSpace(slotsJson) || string.IsNullOrWhiteSpace(mealsJson)) return false;
        return IsNonEmptyJsonArray(slotsJson) && IsNonEmptyJsonArray(mealsJson);
    }

    private static bool IsNonEmptyJsonArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
