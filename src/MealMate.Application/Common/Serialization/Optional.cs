using System.Text.Json;
using System.Text.Json.Serialization;

namespace MealMate.Application.Common.Serialization;

/// <summary>
/// Distinguishes JSON omit (unspecified) from JSON null (clear) and present values.
/// </summary>
public readonly struct Optional<T>
{
    private readonly bool _isSpecified;
    private readonly T? _value;

    public Optional(T? value)
    {
        _isSpecified = true;
        _value = value;
    }

    public bool IsSpecified => _isSpecified;

    public T? Value => _value;

    public static Optional<T> None => default;

    public static Optional<T> Of(T? value) => new(value);
}

public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Optional<T>.Of(default);

        var value = JsonSerializer.Deserialize<T>(ref reader, options);
        return Optional<T>.Of(value);
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (!value.IsSpecified)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
