using MealMate.Application.Features.CustomerCheckout;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class MealPreferenceFilterTests
{
    [Fact]
    public void Same_ingredient_name_blocks_meals_across_restaurants()
    {
        var chickenA = new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "دجاج", "Chicken", []);
        var chickenB = new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "دجاج", "Chicken", []);
        var prefs = MealPreferenceFilter.CreateState(
            [new MealPreferenceFilter.SavedChoice($"INGREDIENT:{chickenA.Id}", true, "دجاج", "Chicken")],
            [chickenA],
            []);

        Assert.Equal(MealPreferenceFilter.IngredientPreferenceConflict,
            MealPreferenceFilter.BlockReason([], [], [chickenB], [], prefs));
        Assert.Null(MealPreferenceFilter.BlockReason([], [],
            [new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "أرز", "Rice", [])], [], prefs));
    }

    [Fact]
    public void Allergy_on_ingredient_blocks_meal()
    {
        var milk = new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "حليب", "Milk", ["MILK"]);
        var prefs = MealPreferenceFilter.CreateState(
            [new MealPreferenceFilter.SavedChoice("MILK", false, "الحليب", "Milk")],
            [],
            []);

        Assert.Equal(MealPreferenceFilter.AllergyConflict, MealPreferenceFilter.BlockReason([], [], [milk], [], prefs));
        Assert.Null(MealPreferenceFilter.BlockReason([], ["MILK"], [], [], MealPreferenceFilter.Empty));
    }

    [Fact]
    public void Dietary_tags_derived_from_all_ingredients_satisfy_and()
    {
        var first = new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "لحم", "Beef", []);
        var second = new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "زيت", "Oil", []);
        var tags = MealPreferenceFilter.DeriveDietaryTags(null, [], [first, second],
            [(first.Id, "KETO"), (second.Id, "KETO")]);
        var prefs = MealPreferenceFilter.CreateState([], [], ["KETO"]);

        Assert.Contains("KETO", tags);
        Assert.Null(MealPreferenceFilter.BlockReason([], [], [first, second], tags, prefs));
    }

    [Fact]
    public void Dietary_and_fails_when_one_ingredient_missing_tag()
    {
        var first = new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "لحم", "Beef", []);
        var second = new MealPreferenceFilter.NamedIngredient(Guid.NewGuid(), "خبز", "Bread", []);
        var tags = MealPreferenceFilter.DeriveDietaryTags(null, [], [first, second], [(first.Id, "KETO")]);
        var prefs = MealPreferenceFilter.CreateState([], [], ["KETO"]);

        Assert.DoesNotContain("KETO", tags);
        Assert.Equal(MealPreferenceFilter.DietaryPreferenceConflict,
            MealPreferenceFilter.BlockReason([], [], [first, second], tags, prefs));
    }

    [Fact]
    public void Snapshot_meal_reappears_when_allergy_is_cleared()
    {
        var meal = new MealSnapshotItem(
            Guid.NewGuid(), "وجبة", "Meal", null, null, "lunch", 1, "/m.png", 500, 40, 40, 20,
            [], [], [],
            [new MealSnapshotIngredient(Guid.NewGuid(), "حليب", "Milk", "KW", null, true, true, ["MILK"])]);
        var blocked = MealPreferenceFilter.CreateState(
            [new MealPreferenceFilter.SavedChoice("MILK", false, "الحليب", "Milk")], [], []);

        Assert.Equal(MealPreferenceFilter.AllergyConflict, MealPreferenceFilter.BlockReason(meal, blocked));
        Assert.Null(MealPreferenceFilter.BlockReason(meal, MealPreferenceFilter.Empty));
        Assert.DoesNotContain(new[] { meal }, x => MealPreferenceFilter.BlockReason(x, blocked) is null);
        Assert.Single(new[] { meal }, x => MealPreferenceFilter.BlockReason(x, MealPreferenceFilter.Empty) is null);
    }
}
