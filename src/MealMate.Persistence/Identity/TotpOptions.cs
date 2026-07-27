namespace MealMate.Persistence.Identity;

public sealed class TotpOptions
{
    public const string SectionName = "Totp";

    public string Issuer { get; set; } = "MealMate Admin";
}
