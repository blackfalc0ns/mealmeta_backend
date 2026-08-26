using System.Text.Json.Nodes;

namespace MealMate.Application.Common.Localization;

/// <summary>
/// Collapses bilingual JSON pairs from DTOs, anonymous projections, dictionaries,
/// and stored database snapshots into the request language.
/// Keeps the preferred <c>*Ar</c>/<c>*En</c> key for mobile clients and also sets the unsuffixed key.
/// </summary>
public static class BilingualJsonCollapser
{
    public static JsonNode? Collapse(JsonNode? node)
    {
        if (LocalizedJsonScope.IncludeSourceFields || node is null)
        {
            return node;
        }

        if (node is JsonObject root && TryReadLanguagePair(root, out var rootText))
        {
            return JsonValue.Create(rootText);
        }

        CollapseInPlace(node);
        return node;
    }

    private static void CollapseInPlace(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                CollapseObject(obj);
                return;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonObject nested && TryReadLanguagePair(nested, out var text))
                    {
                        arr[i] = JsonValue.Create(text);
                        continue;
                    }

                    CollapseInPlace(child);
                }

                return;
        }
    }

    private static void CollapseObject(JsonObject obj)
    {
        foreach (var property in obj.ToArray())
        {
            var value = property.Value;
            if (value is JsonObject nested && TryReadLanguagePair(nested, out var text))
            {
                obj[property.Key] = JsonValue.Create(text);
                continue;
            }

            CollapseInPlace(value);
        }

        CollapseSuffixedPairs(obj);
    }

    private static void CollapseSuffixedPairs(JsonObject obj)
    {
        var names = obj.Select(x => x.Key).ToList();
        var preferAr = LocalizedText.IsArabic();
        foreach (var arName in names)
        {
            if (!arName.EndsWith("Ar", StringComparison.Ordinal) || arName.Length <= 2)
            {
                continue;
            }

            if (!obj.ContainsKey(arName))
            {
                continue;
            }

            var prefix = arName[..^2];
            var enName = prefix + "En";
            if (!obj.TryGetPropertyValue(enName, out var enNode))
            {
                continue;
            }

            if (!obj.TryGetPropertyValue(arName, out var arNode))
            {
                continue;
            }

            if (!IsStringOrNull(arNode) || !IsStringOrNull(enNode))
            {
                continue;
            }

            var picked = LocalizedText.Pick(ReadString(arNode), ReadString(enNode));
            if (preferAr)
            {
                obj.Remove(enName);
                obj[arName] = picked;
            }
            else
            {
                obj.Remove(arName);
                obj[enName] = picked;
            }

            obj[prefix] = picked;
        }
    }

    private static bool TryReadLanguagePair(JsonObject obj, out string text)
    {
        text = string.Empty;
        if (obj.Count is < 1 or > 2)
        {
            return false;
        }

        string? ar = null;
        string? en = null;
        var sawLanguageKey = false;
        foreach (var property in obj)
        {
            if (property.Key.Equals("ar", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsStringOrNull(property.Value))
                {
                    return false;
                }

                ar = ReadString(property.Value);
                sawLanguageKey = true;
                continue;
            }

            if (property.Key.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsStringOrNull(property.Value))
                {
                    return false;
                }

                en = ReadString(property.Value);
                sawLanguageKey = true;
                continue;
            }

            return false;
        }

        if (!sawLanguageKey)
        {
            return false;
        }

        text = LocalizedText.Pick(ar, en);
        return true;
    }

    private static bool IsStringOrNull(JsonNode? node)
    {
        if (node is null)
        {
            return true;
        }

        return node is JsonValue value && value.TryGetValue<string>(out _);
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return null;
    }
}
