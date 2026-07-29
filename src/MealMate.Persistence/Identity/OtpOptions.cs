using System.ComponentModel.DataAnnotations;

namespace MealMate.Persistence.Identity;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    [RegularExpression(@"^\d{6}$")]
    public string? FixedCode { get; set; }

    [RegularExpression(@"^\d{6}$")]
    public string? WhatsAppFixedCode { get; set; }
}
