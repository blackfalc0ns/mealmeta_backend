namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyLookupDto(string Code, string NameAr, string NameEn);

public static class FamilyCatalog
{
    public static IReadOnlyList<FamilyLookupDto> Relationships() =>
    [
        new("self", "رب الأسرة", "Primary"),
        new("spouse", "زوج / زوجة", "Spouse"),
        new("child", "ابن / ابنة", "Child"),
        new("parent", "والد / والدة", "Parent"),
        new("sibling", "أخ / أخت", "Sibling"),
        new("other", "أخرى", "Other"),
    ];

    public static IReadOnlyList<FamilyLookupDto> Goals() =>
    [
        new("balanced", "متوازن", "Balanced"),
        new("weight_loss", "خسارة وزن", "Weight loss"),
        new("muscle", "بناء عضلات", "Muscle building"),
        new("keto", "كيتو", "Keto"),
    ];

    public static IReadOnlyList<FamilyLookupDto> ActivityLevels() =>
    [
        new("sedentary", "قليل النشاط", "Sedentary"),
        new("moderate", "متوسط", "Moderate"),
        new("active", "نشط", "Active"),
    ];
}
