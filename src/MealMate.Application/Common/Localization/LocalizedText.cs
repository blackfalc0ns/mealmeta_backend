using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MealMate.Application.Common.Localization;

/// <summary>
/// Picks Arabic or English copy from the ambient request UI culture
/// (<c>Accept-Language</c> / <c>Language</c> / <c>X-Language</c>).
/// </summary>
public static class LocalizedText
{
    public static bool IsArabic(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Equals(culture.TwoLetterISOLanguageName, SupportedCultures.Arabic, StringComparison.OrdinalIgnoreCase);
    }

    public static string Pick(string? ar, string? en, CultureInfo? culture = null)
    {
        var preferred = IsArabic(culture) ? ar : en;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        var fallback = IsArabic(culture) ? en : ar;
        return fallback ?? string.Empty;
    }
}

/// <summary>
/// Request-scoped switch: dashboards that edit both languages send <c>X-Bilingual: true</c>.
/// Default is a single language in JSON matching the request culture.
/// </summary>
public static class LocalizedJsonScope
{
    public const string BilingualHeader = "X-Bilingual";

    private static readonly AsyncLocal<bool> IncludeSource = new();

    public static bool IncludeSourceFields => IncludeSource.Value;

    public static IDisposable Begin(bool includeSourceFields)
    {
        var previous = IncludeSource.Value;
        IncludeSource.Value = includeSourceFields;
        return new Reset(previous);
    }

    private sealed class Reset(bool previous) : IDisposable
    {
        public void Dispose() => IncludeSource.Value = previous;
    }
}

/// <summary>
/// Bilingual UI copy. JSON is a single string for the request language,
/// or <c>{ ar, en }</c> when <see cref="LocalizedJsonScope.IncludeSourceFields"/> is set.
/// </summary>
[JsonConverter(typeof(LocalizedTextJsonConverter))]
public sealed record LocalizedTextDto(string Ar, string En);

public sealed class LocalizedTextJsonConverter : JsonConverter<LocalizedTextDto>
{
    public override LocalizedTextDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString() ?? string.Empty;
            return new LocalizedTextDto(text, text);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Localized text must be a string or { ar, en } object.");
        }

        string ar = string.Empty;
        string en = string.Empty;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();
            var value = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? string.Empty : string.Empty;
            if (string.Equals(name, "ar", StringComparison.OrdinalIgnoreCase))
            {
                ar = value;
            }
            else if (string.Equals(name, "en", StringComparison.OrdinalIgnoreCase))
            {
                en = value;
            }
        }

        return new LocalizedTextDto(ar, en);
    }

    public override void Write(Utf8JsonWriter writer, LocalizedTextDto value, JsonSerializerOptions options)
    {
        if (LocalizedJsonScope.IncludeSourceFields)
        {
            writer.WriteStartObject();
            writer.WriteString("ar", value.Ar);
            writer.WriteString("en", value.En);
            writer.WriteEndObject();
            return;
        }

        writer.WriteStringValue(LocalizedText.Pick(value.Ar, value.En));
    }
}
