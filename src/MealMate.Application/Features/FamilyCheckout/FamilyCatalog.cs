namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyLookupDto(string Code, string NameAr, string NameEn, bool ForDependents);

public static class FamilyCatalog
{
    public static IReadOnlyList<FamilyLookupDto> Relationships() =>
    [
        new("self", "أنت", "You", ForDependents: false),
        new("spouse", "زوج / زوجة", "Spouse", ForDependents: true),
        new("father", "أب", "Father", ForDependents: true),
        new("mother", "أم", "Mother", ForDependents: true),
        new("child", "ابن / ابنة", "Child", ForDependents: true),
        new("sibling", "أخ / أخت", "Sibling", ForDependents: true),
        new("friend", "صديق / صديقة", "Friend", ForDependents: true),
        new("parent", "والد / والدة", "Parent", ForDependents: true),
        new("other", "أخرى", "Other", ForDependents: true),
    ];

    public static IReadOnlyList<FamilyLookupDto> DependentRelationships()
        => Relationships().Where(x => x.ForDependents).ToList();
}
