using MealMate.Application.Common;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class FoodCodeNormalizerTests
{
    [Fact]
    public void Split_Accepts_Legacy_Separators_And_Normalizes_Once()
    {
        var values = FoodCodeNormalizer.Split(" milk; gluten|TREE nuts,Milk ");
        Assert.Equal(["GLUTEN", "MILK", "TREE_NUTS"], values);
        Assert.Equal("GLUTEN,MILK,TREE_NUTS", FoodCodeNormalizer.Join(values));
    }
}
