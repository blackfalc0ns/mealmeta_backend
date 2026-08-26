using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MealMate.Application.Common.Localization;

namespace MealMate.Api.Localization;

public static class MealMateJsonSerializer
{
    public static void Configure(JsonSerializerOptions options)
    {
        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
        }

        if (!options.Converters.OfType<LocalizedTextJsonConverter>().Any())
        {
            options.Converters.Add(new LocalizedTextJsonConverter());
        }

        var applied = false;
        foreach (var resolver in options.TypeInfoResolverChain)
        {
            if (resolver is DefaultJsonTypeInfoResolver defaultResolver)
            {
                if (!defaultResolver.Modifiers.Contains(BilingualJsonModifier.Modifier))
                {
                    defaultResolver.Modifiers.Add(BilingualJsonModifier.Modifier);
                }

                applied = true;
            }
        }

        if (!applied)
        {
            options.TypeInfoResolverChain.Insert(
                0,
                new DefaultJsonTypeInfoResolver
                {
                    Modifiers = { BilingualJsonModifier.Modifier },
                });
        }
    }
}
