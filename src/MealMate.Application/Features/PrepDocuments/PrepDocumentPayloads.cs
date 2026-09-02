using System.Text.Json;
using System.Text.Json.Serialization;

namespace MealMate.Application.Features.PrepDocuments;

public sealed record PrepDocumentAllergenPayload(
    string Code,
    string NameAr,
    string NameEn);

public sealed record PrepDocumentMealPayload(
    Guid MealId,
    string NameAr,
    string NameEn,
    string? Slot,
    int SortOrder,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG,
    IReadOnlyList<string> PresentAllergenCodes,
    IReadOnlyList<string> MayContainAllergenCodes,
    IReadOnlyList<PrepDocumentAllergenPayload> PresentAllergens,
    IReadOnlyList<PrepDocumentAllergenPayload> MayContainAllergens);

public sealed record PrepDocumentSnapshotPayload(
    string MealMateLogoUrl,
    string? RestaurantLogoUrl,
    string RestaurantNameAr,
    string RestaurantNameEn,
    DateOnly ServiceDate,
    Guid SubscriptionDayId,
    string BarcodeValue,
    string? ShortAddress,
    bool IncompleteNutrition,
    string EmbeddedLayoutJson,
    IReadOnlyList<PrepDocumentMealPayload> Meals);

internal static class PrepDocumentPayloadSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(PrepDocumentSnapshotPayload payload)
        => JsonSerializer.Serialize(payload, Options);
}
