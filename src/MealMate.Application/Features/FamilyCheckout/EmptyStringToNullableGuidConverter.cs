using System.Text.Json;
using System.Text.Json.Serialization;

namespace MealMate.Application.Features.FamilyCheckout;

/// <summary>
/// Flutter often serializes missing Guids as "". Treat empty/whitespace as null.
/// </summary>
public sealed class EmptyStringToNullableGuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
            {
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;
                if (Guid.TryParse(raw, out var guid))
                    return guid;
                throw new JsonException($"The JSON value could not be converted to a Guid. Path: deliveryAddressId.");
            }
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for Guid?.");
        }
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}
