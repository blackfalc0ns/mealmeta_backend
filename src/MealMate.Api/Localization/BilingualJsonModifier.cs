using System.Text.Json.Serialization.Metadata;
using MealMate.Application.Common.Localization;

namespace MealMate.Api.Localization;

/// <summary>
/// For every string property pair <c>fooAr</c>/<c>fooEn</c>, JSON emits only <c>foo</c>
/// in the request language unless <see cref="LocalizedJsonScope.IncludeSourceFields"/>.
/// </summary>
public static class BilingualJsonModifier
{
    public static readonly Action<JsonTypeInfo> Modifier = AddLocalizedProperties;

    public static void AddLocalizedProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var properties = typeInfo.Properties;
        var byName = new Dictionary<string, JsonPropertyInfo>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!byName.ContainsKey(property.Name))
            {
                byName[property.Name] = property;
            }
        }

        var added = new List<JsonPropertyInfo>();
        foreach (var arProperty in properties)
        {
            if (!IsStringProperty(arProperty) || !arProperty.Name.EndsWith("Ar", StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = arProperty.Name[..^2];
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            var enName = prefix + "En";
            if (!byName.TryGetValue(enName, out var enProperty) || !IsStringProperty(enProperty))
            {
                continue;
            }

            if (arProperty.Get is null || enProperty.Get is null)
            {
                continue;
            }

            WrapShouldSerialize(arProperty, includeSource: true);
            WrapShouldSerialize(enProperty, includeSource: true);

            var arGetter = arProperty.Get;
            var enGetter = enProperty.Get;
            if (byName.TryGetValue(prefix, out var existingPrefix))
            {
                var previousGet = existingPrefix.Get;
                existingPrefix.Get = obj => LocalizedJsonScope.IncludeSourceFields
                    ? previousGet?.Invoke(obj)
                    : LocalizedText.Pick(arGetter(obj) as string, enGetter(obj) as string);
                continue;
            }

            var localized = typeInfo.CreateJsonPropertyInfo(typeof(string), prefix);
            localized.Get = obj => LocalizedText.Pick(arGetter(obj) as string, enGetter(obj) as string);
            localized.ShouldSerialize = (_, _) => !LocalizedJsonScope.IncludeSourceFields;
            added.Add(localized);
            byName[prefix] = localized;
        }

        foreach (var property in added)
        {
            properties.Add(property);
        }
    }

    private static void WrapShouldSerialize(JsonPropertyInfo property, bool includeSource)
    {
        var previous = property.ShouldSerialize;
        property.ShouldSerialize = (obj, value) =>
        {
            if (LocalizedJsonScope.IncludeSourceFields != includeSource)
            {
                return false;
            }

            return previous?.Invoke(obj, value) ?? true;
        };
    }

    private static bool IsStringProperty(JsonPropertyInfo property)
        => property.PropertyType == typeof(string);
}
