namespace MealMate.Application.Common.Security;

/// <summary>System role names seeded into ASP.NET Identity Roles.</summary>
public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string CountryAdmin = "CountryAdmin";
    public const string OperationsAdmin = "OperationsAdmin";
    public const string FinanceAdmin = "FinanceAdmin";
    public const string SupportAdmin = "SupportAdmin";
    public const string RestaurantOwner = "RestaurantOwner";
    public const string RestaurantStaff = "RestaurantStaff";
    public const string Driver = "Driver";
    public const string Customer = "Customer";
    public const string Influencer = "Influencer";

    public static IReadOnlyList<string> All { get; } =
    [
        SuperAdmin,
        CountryAdmin,
        OperationsAdmin,
        FinanceAdmin,
        SupportAdmin,
        RestaurantOwner,
        RestaurantStaff,
        Driver,
        Customer,
        Influencer,
    ];

    public static IReadOnlyList<string> AdminRoles { get; } =
    [
        SuperAdmin,
        CountryAdmin,
        OperationsAdmin,
        FinanceAdmin,
        SupportAdmin,
    ];

    public static bool IsSuperAdmin(string roleName) =>
        string.Equals(roleName, SuperAdmin, StringComparison.OrdinalIgnoreCase);

    public static bool IsAdminRole(string roleName) =>
        AdminRoles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));
}
