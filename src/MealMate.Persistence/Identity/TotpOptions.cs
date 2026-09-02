using System.ComponentModel.DataAnnotations;

namespace MealMate.Persistence.Identity;

public sealed class TotpOptions
{
    public const string SectionName = "Totp";

    public string Issuer { get; set; } = "MealMate Admin";

    [RegularExpression(@"^\d{6}$")]
    public string? FixedCode { get; set; }

    public bool AllowFixedCodeOutsideDevelopment { get; set; }
}
