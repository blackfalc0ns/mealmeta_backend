using System.Net;
using System.Text.RegularExpressions;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.AdminDashboard.Email;

public sealed record RenderedEmail(string Subject, string Html, string Text);

public static partial class EmailTemplateCatalog
{
    public const string VerificationOtp = "verification_otp";
    public const string PasswordReset = "password_reset";
    public const string AdminInvite = "admin_invite";
    public const string RestaurantSubmitted = "restaurant_submitted";
    public const string RestaurantApproved = "restaurant_approved";
    public const string RestaurantChangesRequested = "restaurant_changes_requested";
    public const string RestaurantRejected = "restaurant_rejected";

    public const string DeliveryManagerInvite = "delivery_manager_invite";

    public static readonly IReadOnlyList<string> Keys =
    [
        VerificationOtp, PasswordReset, AdminInvite, DeliveryManagerInvite, RestaurantSubmitted,
        RestaurantApproved, RestaurantChangesRequested, RestaurantRejected,
    ];

    public static ResendEmailTemplates CreateDefaults() => new()
    {
        VerificationOtp = Template(
            "رمز التحقق الخاص بك في MealMate: {{otp_code}}",
            "Your MealMate verification code: {{otp_code}}",
            "تأكيد هويتك",
            "Verify your identity",
            "استخدم الرمز التالي لإكمال التحقق. لا تشاركه مع أي شخص.",
            "Use the code below to complete verification. Never share it with anyone.",
            CodeBlock("{{otp_code}}") + Note("تنتهي صلاحية الرمز خلال {{expires_minutes}} دقائق.", "{{expires_minutes}} minutes until this code expires.")),
        PasswordReset = Template(
            "إعادة تعيين كلمة مرور MealMate",
            "Reset your MealMate password",
            "طلب إعادة تعيين كلمة المرور",
            "Password reset requested",
            "وصلنا طلب لتغيير كلمة مرور حسابك. استخدم الرمز التالي إذا كنت أنت صاحب الطلب.",
            "We received a request to change your password. Use this code only if you made the request.",
            CodeBlock("{{otp_code}}") + Note("تنتهي صلاحية الرمز خلال {{expires_minutes}} دقائق. تجاهل الرسالة إن لم تطلب ذلك.", "The code expires in {{expires_minutes}} minutes. Ignore this email if you did not request it.")),
        AdminInvite = Template(
            "دعوة للانضمام إلى إدارة MealMate",
            "You are invited to MealMate Admin",
            "مرحبًا {{recipient_name}}",
            "Welcome, {{recipient_name}}",
            "تمت دعوتك للانضمام إلى لوحة إدارة MealMate. فعّل حسابك من الزر التالي.",
            "You have been invited to the MealMate administration portal. Activate your account below.",
            Button("قبول الدعوة", "Accept invitation", "{{dashboard_url}}") + Note("رمز الدعوة: {{otp_code}}", "Invitation code: {{otp_code}}")),
        DeliveryManagerInvite = Template(
            "دعوة للانضمام إلى MealMate كمسؤول توصيل",
            "Invitation to join MealMate as Delivery Manager",
            "مرحبًا {{recipient_name}}",
            "Welcome, {{recipient_name}}",
            "تم إنشاء حساب مسؤول توصيل جديد لك من قِبل المطعم. يمكنك تسجيل الدخول إلى تطبيق مسؤول التوصيل باستخدام بيانات الدخول التالية:",
            "A new delivery manager account has been created for you by the restaurant. You can log in to the dispatch mobile app using the following credentials:",
            CodeBlock("اسم المستخدم / الإيميل: {{email}}") + Note("كلمة المرور المؤقتة: {{password}}", "Temporary password: {{password}}")),
        RestaurantSubmitted = Template(
            "تم استلام طلب تسجيل {{restaurant_name}}",
            "Registration received for {{restaurant_name}}",
            "طلبك قيد المراجعة",
            "Your application is under review",
            "استلمنا بيانات ومستندات {{restaurant_name}} بنجاح. سيقوم فريقنا بمراجعتها وإبلاغك فور تحديث الحالة.",
            "We received the details and documents for {{restaurant_name}}. Our team will review them and notify you when the status changes.",
            Button("متابعة حالة الطلب", "Track application", "{{dashboard_url}}")),
        RestaurantApproved = Template(
            "تم اعتماد مطعم {{restaurant_name}}",
            "{{restaurant_name}} has been approved",
            "تهانينا، تم الاعتماد",
            "Congratulations, you are approved",
            "اكتملت مراجعة {{restaurant_name}} وتم تفعيل حساب المطعم. يمكنك الآن متابعة الإعداد من لوحة التحكم.",
            "The review of {{restaurant_name}} is complete and the restaurant account is active. You can continue setup from the dashboard.",
            Button("فتح لوحة المطعم", "Open restaurant dashboard", "{{dashboard_url}}")),
        RestaurantChangesRequested = Template(
            "مطلوب تحديث بيانات {{restaurant_name}}",
            "Updates required for {{restaurant_name}}",
            "نحتاج بعض التعديلات",
            "A few updates are required",
            "راجع فريقنا الطلب وحدد البيانات أو المستندات التالية المطلوب تحديثها:",
            "Our review team needs the following information or documents updated:",
            Highlight("{{changes_required}}") + Button("تحديث الطلب", "Update application", "{{review_url}}")),
        RestaurantRejected = Template(
            "تحديث مهم بشأن طلب {{restaurant_name}}",
            "Important update about {{restaurant_name}}",
            "تعذر اعتماد الطلب",
            "Application could not be approved",
            "بعد مراجعة الطلب، تعذر اعتماده في الوقت الحالي للسبب التالي:",
            "After reviewing the application, we could not approve it at this time for the following reason:",
            Highlight("{{rejection_reason}}") + Button("مراجعة الطلب", "Review application", "{{review_url}}")),
    };

    public static Result<RenderedEmail> Render(
        string key,
        string language,
        IReadOnlyDictionary<string, string> variables,
        ResendEmailTemplates? templates = null)
    {
        var template = ResolveTemplate(templates ?? CreateDefaults(), key);
        if (template is null)
            return Error.Validation("email.template.invalid", "Unknown email template.");

        var ar = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var en = language.Equals("en", StringComparison.OrdinalIgnoreCase);
        if (!ar && !en)
            return Error.Validation("email.template.language_invalid", "Language must be ar or en.");

        var subject = ar ? template.SubjectAr : template.SubjectEn;
        var body = ar ? template.HtmlBodyAr : template.HtmlBodyEn;
        foreach (Match match in PlaceholderRegex().Matches(subject + body))
        {
            var name = match.Groups[1].Value;
            if (!variables.ContainsKey(name))
                return Error.Validation("email.template.variable_missing", $"Missing template variable: {name}.");
        }

        foreach (var variable in variables)
        {
            var token = "{{" + variable.Key + "}}";
            subject = subject.Replace(token, variable.Value, StringComparison.Ordinal);
            body = body.Replace(token, WebUtility.HtmlEncode(variable.Value), StringComparison.Ordinal);
        }

        var html = Layout(ar, subject, body, variables.GetValueOrDefault("support_email", "support@mealmate.app"));
        var text = WebUtility.HtmlDecode(HtmlTagRegex().Replace(body, " "))
            .Replace("  ", " ", StringComparison.Ordinal).Trim();
        return new RenderedEmail(subject, html, text);
    }

    public static IReadOnlyDictionary<string, string> SampleVariables() =>
        new Dictionary<string, string>
        {
            ["recipient_name"] = "محمد أحمد / Mohamed Ahmed",
            ["otp_code"] = "482731",
            ["expires_minutes"] = "10",
            ["email"] = "delivery@restaurant.com",
            ["password"] = "TmpPass123!",
            ["restaurant_name"] = "مطعم مذاق / Mazaq Restaurant",
            ["dashboard_url"] = "https://restaurant.mealmate.app",
            ["review_url"] = "https://restaurant.mealmate.app/restaurant/under-review",
            ["rejection_reason"] = "يرجى رفع نسخة واضحة وسارية من الرخصة التجارية.",
            ["changes_required"] = "تحديث تاريخ انتهاء الرخصة وإعادة رفع الصورة كاملة.",
            ["support_email"] = "support@mealmate.app",
            ["current_year"] = DateTime.UtcNow.Year.ToString(),
        };

    public static BilingualTemplate? ResolveTemplate(ResendEmailTemplates t, string key) => key switch
    {
        VerificationOtp => t.VerificationOtp,
        PasswordReset => t.PasswordReset,
        AdminInvite => t.AdminInvite,
        DeliveryManagerInvite => t.DeliveryManagerInvite,
        RestaurantSubmitted => t.RestaurantSubmitted,
        RestaurantApproved => t.RestaurantApproved,
        RestaurantChangesRequested => t.RestaurantChangesRequested,
        RestaurantRejected => t.RestaurantRejected,
        _ => null,
    };

    private static BilingualTemplate Template(string subjectAr, string subjectEn, string titleAr, string titleEn, string introAr, string introEn, string content) => new()
    {
        SubjectAr = subjectAr,
        SubjectEn = subjectEn,
        HtmlBodyAr = $"<h1>{titleAr}</h1><p>مرحبًا {{{{recipient_name}}}}،</p><p>{introAr}</p>{ArabicPart(content)}",
        HtmlBodyEn = $"<h1>{titleEn}</h1><p>Hello {{{{recipient_name}}}},</p><p>{introEn}</p>{EnglishPart(content)}",
    };

    private static string Layout(bool ar, string preview, string body, string support) => $"""
        <!doctype html><html lang="{(ar ? "ar" : "en")}" dir="{(ar ? "rtl" : "ltr")}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width">
        <title>{WebUtility.HtmlEncode(preview)}</title></head>
        <body style="margin:0;background:#f3f7f5;font-family:Arial,'Segoe UI',sans-serif;color:#102a24">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f7f5;padding:24px 12px"><tr><td align="center">
        <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="max-width:600px;width:100%;background:#fff;border:1px solid #dbe8e3;border-radius:18px;overflow:hidden">
        <tr><td style="background:#087a5b;padding:24px 32px;color:#fff;font-size:24px;font-weight:700">MealMate</td></tr>
        <tr><td style="padding:34px 32px;font-size:16px;line-height:1.8;text-align:{(ar ? "right" : "left")}">{body}</td></tr>
        <tr><td style="padding:20px 32px;background:#f8fbfa;color:#60756f;font-size:12px;line-height:1.7;text-align:center">
        {(ar ? "هذه رسالة آلية آمنة. للمساعدة:" : "This is a secure automated message. Need help?")} 
        <a href="mailto:{WebUtility.HtmlEncode(support)}" style="color:#087a5b">{WebUtility.HtmlEncode(support)}</a><br>© {DateTime.UtcNow.Year} MealMate
        </td></tr></table></td></tr></table></body></html>
        """;

    private static string CodeBlock(string code) => $"<div data-ar=\"{code}\" data-en=\"{code}\"></div>";
    private static string Note(string ar, string en) => $"<div data-ar=\"{ar}\" data-en=\"{en}\"></div>";
    private static string Button(string ar, string en, string url) => $"<a data-ar=\"{ar}\" data-en=\"{en}\" href=\"{url}\"></a>";
    private static string Highlight(string value) => $"<blockquote data-ar=\"{value}\" data-en=\"{value}\"></blockquote>";
    private static string ArabicPart(string value) => ConvertParts(value, true);
    private static string EnglishPart(string value) => ConvertParts(value, false);

    private static string ConvertParts(string value, bool ar)
    {
        value = Regex.Replace(value, "<div data-ar=\"([^\"]*)\" data-en=\"([^\"]*)\"></div>", m =>
            $"<div style=\"margin:22px 0;padding:16px;background:#eefaf6;border-radius:12px;text-align:center;font-weight:700;font-size:20px\">{m.Groups[ar ? 1 : 2].Value}</div>");
        value = Regex.Replace(value, "<a data-ar=\"([^\"]*)\" data-en=\"([^\"]*)\" href=\"([^\"]*)\"></a>", m =>
            $"<p style=\"margin:26px 0;text-align:center\"><a href=\"{m.Groups[3].Value}\" style=\"display:inline-block;background:#087a5b;color:#fff;text-decoration:none;padding:13px 24px;border-radius:10px;font-weight:700\">{m.Groups[ar ? 1 : 2].Value}</a></p>");
        return Regex.Replace(value, "<blockquote data-ar=\"([^\"]*)\" data-en=\"([^\"]*)\"></blockquote>", m =>
            $"<div style=\"margin:20px 0;padding:16px;background:#fff7ed;border:1px solid #fed7aa;border-radius:10px\">{m.Groups[ar ? 1 : 2].Value}</div>");
    }

    [GeneratedRegex(@"\{\{([a-z0-9_]+)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
