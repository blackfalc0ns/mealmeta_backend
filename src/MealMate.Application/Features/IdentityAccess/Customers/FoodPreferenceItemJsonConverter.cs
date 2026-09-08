using System.Text.Json;
using System.Text.Json.Serialization;

namespace MealMate.Application.Features.IdentityAccess.Customers;

/// <summary>
/// Flutter sends allergy items as objects <c>{ code }</c> or as bare strings <c>"MILK"</c>.
/// </summary>
public sealed class FoodPreferenceItemJsonConverter : JsonConverter<FoodPreferenceItemDto>
{
    public override FoodPreferenceItemDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var code = reader.GetString();
            return new FoodPreferenceItemDto(code ?? "", null, null, null);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var code = ReadString(root, "code") ?? ReadString(root, "Code") ?? "";
            var nameAr = ReadString(root, "nameAr") ?? ReadString(root, "NameAr");
            var nameEn = ReadString(root, "nameEn") ?? ReadString(root, "NameEn");
            var severity = ReadString(root, "severity") ?? ReadString(root, "Severity");
            return new FoodPreferenceItemDto(code, nameAr, nameEn, severity);
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for food preference item.");
    }

    public override void Write(Utf8JsonWriter writer, FoodPreferenceItemDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("code", value.Code);
        if (value.NameAr is not null) writer.WriteString("nameAr", value.NameAr);
        if (value.NameEn is not null) writer.WriteString("nameEn", value.NameEn);
        if (value.Severity is not null) writer.WriteString("severity", value.Severity);
        writer.WriteEndObject();
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
