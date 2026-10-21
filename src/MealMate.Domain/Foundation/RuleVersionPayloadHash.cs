using System.Security.Cryptography;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace MealMate.Domain.Foundation;

public static class RuleVersionPayloadHash
{
    private sealed record GovernanceFingerprint(
        Guid RuleVersionId,
        string RuleKey,
        string VersionCode,
        Guid LegalEntityId,
        int ScopeType,
        Guid? ScopeEntityId,
        string NormalizedScopeId,
        int SchemaVersion,
        string PayloadHash,
        DateTime EffectiveFromUtc,
        DateTime? EffectiveToUtc,
        string? Reason,
        Guid? PreviousVersionId,
        Guid? OwnerUserId,
        Guid? MakerUserId);

    public static string Compute(string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        var canonicalJson = Canonicalize(payloadJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }

    public static string ComputeGovernanceFingerprint(RuleVersion row) => Compute(JsonSerializer.Serialize(new GovernanceFingerprint(
        row.Id,
        row.RuleKey,
        row.VersionCode,
        row.LegalEntityId,
        (int)row.ScopeType,
        row.ScopeEntityId,
        row.NormalizedScopeId,
        row.SchemaVersion,
        Compute(row.PayloadJson),
        row.EffectiveFromUtc,
        row.EffectiveToUtc,
        row.Reason,
        row.PreviousVersionId,
        row.OwnerUserId,
        row.CreatedByUserId)));

    private static string Canonicalize(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteCanonical(document.RootElement, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().ToArray();
                if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                {
                    throw new JsonException("Duplicate JSON object properties are not supported.");
                }
                writer.WriteStartObject();
                foreach (var property in properties.OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (!element.TryGetDecimal(out var number))
                {
                    throw new JsonException("JSON numeric values must fit the supported decimal range.");
                }
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Unsupported JSON value.");
        }
    }
}
