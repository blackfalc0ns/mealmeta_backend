namespace MealMate.Application.Common.Security;

/// <summary>Custom claim types embedded in the access token.</summary>
public static class AppClaimTypes
{
    public const string UserId = "sub";
    public const string UserType = "mm_user_type";
    public const string RestaurantId = "mm_restaurant_id";
    public const string Permission = "mm_permission";
}
