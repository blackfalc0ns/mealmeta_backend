using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/customers")]
[EnableRateLimiting("auth")]
public sealed class CustomerAuthController : ApiControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] CustomerRegisterRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RegisterCustomerCommand(
            body.FullName,
            Phone: CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty,
            body.Password), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CustomerLoginCommand(
            Email: null,
            Phone: CustomerPhoneInput.Normalize(body.CountryCode, body.PhoneNumber ?? body.Phone),
            body.Password,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("verify-phone")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyPhone(
        [FromBody] CustomerVerifyPhoneRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CustomerVerifyPhoneCommand(
            CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty,
            body.Code,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("resend-phone-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendPhoneOtp(
        [FromBody] CustomerResendPhoneOtpRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CustomerResendPhoneOtpCommand(
            CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] CustomerForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CustomerForgotPasswordCommand(
                CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] CustomerResetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CustomerResetPasswordCommand(
                CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty,
                body.OtpCode,
                body.NewPassword), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record CustomerRegisterRequest(
    string FullName,
    string Phone,
    string Password,
    string CountryCode);

public sealed record CustomerVerifyPhoneRequest(string Phone, string Code, string? CountryCode);
public sealed record CustomerResendPhoneOtpRequest(string Phone, string? CountryCode);
public sealed record CustomerForgotPasswordRequest(string Phone, string? CountryCode);
public sealed record CustomerResetPasswordRequest(string Phone, string OtpCode, string NewPassword, string? CountryCode);

internal static class CustomerPhoneInput
{
    public static string? Normalize(string? countryCode, string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return phone;
        var value = phone.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (value.StartsWith("+", StringComparison.Ordinal)) return value;
        var code = countryCode?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        var dialCode = code.StartsWith('+') || code.All(char.IsDigit)
            ? $"+{code.TrimStart('+')}"
            : CallingCodesController.Codes
                .FirstOrDefault(item => item.CountryCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                ?.DialCode;

        return string.IsNullOrWhiteSpace(dialCode) ? string.Empty : $"{dialCode}{value.TrimStart('0')}";
    }
}

// Kept here as a small, dependency-free public catalog for the mobile country picker.
[ApiController]
[Route("api/v1/catalog/calling-codes")]
public sealed class CallingCodesController : ControllerBase
{
    internal static readonly IReadOnlyList<CallingCodeDto> Codes =
    [
        new("AF", "أفغانستان", "Afghanistan", "+93"), new("AL", "ألبانيا", "Albania", "+355"),
        new("DZ", "الجزائر", "Algeria", "+213"), new("AR", "الأرجنتين", "Argentina", "+54"),
        new("AM", "أرمينيا", "Armenia", "+374"), new("AU", "أستراليا", "Australia", "+61"),
        new("AT", "النمسا", "Austria", "+43"), new("AZ", "أذربيجان", "Azerbaijan", "+994"),
        new("BH", "البحرين", "Bahrain", "+973"), new("BD", "بنغلاديش", "Bangladesh", "+880"),
        new("BY", "بيلاروسيا", "Belarus", "+375"), new("BE", "بلجيكا", "Belgium", "+32"),
        new("BR", "البرازيل", "Brazil", "+55"), new("BG", "بلغاريا", "Bulgaria", "+359"),
        new("CA", "كندا", "Canada", "+1"), new("CL", "تشيلي", "Chile", "+56"),
        new("CN", "الصين", "China", "+86"), new("CO", "كولومبيا", "Colombia", "+57"),
        new("HR", "كرواتيا", "Croatia", "+385"), new("CY", "قبرص", "Cyprus", "+357"),
        new("CZ", "التشيك", "Czechia", "+420"), new("DK", "الدنمارك", "Denmark", "+45"),
        new("EG", "مصر", "Egypt", "+20"), new("EE", "إستونيا", "Estonia", "+372"),
        new("ET", "إثيوبيا", "Ethiopia", "+251"), new("FI", "فنلندا", "Finland", "+358"),
        new("FR", "فرنسا", "France", "+33"), new("GE", "جورجيا", "Georgia", "+995"),
        new("DE", "ألمانيا", "Germany", "+49"), new("GH", "غانا", "Ghana", "+233"),
        new("GR", "اليونان", "Greece", "+30"), new("HK", "هونغ كونغ", "Hong Kong", "+852"),
        new("HU", "المجر", "Hungary", "+36"), new("IS", "آيسلندا", "Iceland", "+354"),
        new("IN", "الهند", "India", "+91"), new("ID", "إندونيسيا", "Indonesia", "+62"),
        new("IR", "إيران", "Iran", "+98"), new("IQ", "العراق", "Iraq", "+964"),
        new("IE", "أيرلندا", "Ireland", "+353"), new("IT", "إيطاليا", "Italy", "+39"),
        new("JP", "اليابان", "Japan", "+81"), new("JO", "الأردن", "Jordan", "+962"),
        new("KZ", "كازاخستان", "Kazakhstan", "+7"), new("KE", "كينيا", "Kenya", "+254"),
        new("KW", "الكويت", "Kuwait", "+965"), new("KG", "قيرغيزستان", "Kyrgyzstan", "+996"),
        new("LB", "لبنان", "Lebanon", "+961"), new("LY", "ليبيا", "Libya", "+218"),
        new("LU", "لوكسمبورغ", "Luxembourg", "+352"), new("MY", "ماليزيا", "Malaysia", "+60"),
        new("MV", "المالديف", "Maldives", "+960"), new("MT", "مالطا", "Malta", "+356"),
        new("MU", "موريشيوس", "Mauritius", "+230"), new("MX", "المكسيك", "Mexico", "+52"),
        new("MA", "المغرب", "Morocco", "+212"), new("NP", "نيبال", "Nepal", "+977"),
        new("NL", "هولندا", "Netherlands", "+31"), new("NZ", "نيوزيلندا", "New Zealand", "+64"),
        new("NG", "نيجيريا", "Nigeria", "+234"), new("NO", "النرويج", "Norway", "+47"),
        new("OM", "عُمان", "Oman", "+968"), new("PK", "باكستان", "Pakistan", "+92"),
        new("PS", "فلسطين", "Palestine", "+970"), new("PH", "الفلبين", "Philippines", "+63"),
        new("PL", "بولندا", "Poland", "+48"), new("PT", "البرتغال", "Portugal", "+351"),
        new("QA", "قطر", "Qatar", "+974"), new("RO", "رومانيا", "Romania", "+40"),
        new("RU", "روسيا", "Russia", "+7"), new("SA", "السعودية", "Saudi Arabia", "+966"),
        new("RS", "صربيا", "Serbia", "+381"), new("SG", "سنغافورة", "Singapore", "+65"),
        new("SK", "سلوفاكيا", "Slovakia", "+421"), new("SI", "سلوفينيا", "Slovenia", "+386"),
        new("ZA", "جنوب أفريقيا", "South Africa", "+27"), new("KR", "كوريا الجنوبية", "South Korea", "+82"),
        new("ES", "إسبانيا", "Spain", "+34"), new("LK", "سريلانكا", "Sri Lanka", "+94"),
        new("SD", "السودان", "Sudan", "+249"), new("SE", "السويد", "Sweden", "+46"),
        new("CH", "سويسرا", "Switzerland", "+41"), new("SY", "سوريا", "Syria", "+963"),
        new("TW", "تايوان", "Taiwan", "+886"), new("TZ", "تنزانيا", "Tanzania", "+255"),
        new("TH", "تايلاند", "Thailand", "+66"), new("TN", "تونس", "Tunisia", "+216"),
        new("TR", "تركيا", "Turkey", "+90"), new("UA", "أوكرانيا", "Ukraine", "+380"),
        new("AE", "الإمارات", "United Arab Emirates", "+971"), new("GB", "المملكة المتحدة", "United Kingdom", "+44"),
        new("US", "الولايات المتحدة", "United States", "+1"), new("UZ", "أوزبكستان", "Uzbekistan", "+998"),
        new("VE", "فنزويلا", "Venezuela", "+58"), new("VN", "فيتنام", "Vietnam", "+84"),
        new("YE", "اليمن", "Yemen", "+967")
    ];

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(Codes);
}

public sealed record CallingCodeDto(string CountryCode, string NameAr, string NameEn, string DialCode);

