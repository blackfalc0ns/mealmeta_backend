using MealMate.Domain.ComplaintsSupport;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed record CustomerSupportTypeOptionDto(string Value, string LabelAr, string LabelEn);

public sealed record CustomerSupportOptionsDto(
    IReadOnlyList<CustomerSupportTypeOptionDto> Issues,
    IReadOnlyList<CustomerSupportTypeOptionDto> Suggestions);

/// <summary>Stable customer support choices exposed by the customer API.</summary>
public static class CustomerSupportCatalog
{
    public const string IssuesRequestType = "issues";
    public const string SuggestionsRequestType = "suggestions";
    public const string IssueCategoryPrefix = "customer.issue.";
    public const string SuggestionCategoryPrefix = "customer.suggestion.";

    public static readonly IReadOnlyList<CustomerSupportTypeOptionDto> Issues =
    [
        new("FoodQuality", "جودة الطعام", "Food quality"),
        new("MissingItems", "أصناف ناقصة", "Missing items"),
        new("WrongItems", "أصناف غير صحيحة", "Wrong items"),
        new("DamagedPackaging", "تغليف تالف", "Damaged packaging"),
        new("DeliveryDelay", "تأخير التوصيل", "Delivery delay"),
        new("NotDelivered", "لم يتم التوصيل", "Not delivered"),
        new("Payment", "الدفع", "Payment"),
        new("Subscription", "الاشتراك", "Subscription"),
        new("Account", "الحساب", "Account"),
        new("Other", "مشكلة أخرى", "Other issue"),
    ];

    public static readonly IReadOnlyList<CustomerSupportTypeOptionDto> Suggestions =
    [
        new("Meals", "الوجبات", "Meals"),
        new("Delivery", "التوصيل", "Delivery"),
        new("AppExperience", "تجربة التطبيق", "App experience"),
        new("Service", "الخدمة", "Service"),
        new("Other", "اقتراح آخر", "Other suggestion"),
    ];

    public static CustomerSupportOptionsDto GetOptions() => new(Issues, Suggestions);

    public static bool TryNormalizeRequestType(string? requestType, out string normalized, out SupportCaseKind kind)
    {
        if (string.Equals(requestType, IssuesRequestType, StringComparison.OrdinalIgnoreCase))
        {
            normalized = IssuesRequestType;
            kind = SupportCaseKind.Complaint;
            return true;
        }

        if (string.Equals(requestType, SuggestionsRequestType, StringComparison.OrdinalIgnoreCase))
        {
            normalized = SuggestionsRequestType;
            kind = SupportCaseKind.GeneralRequest;
            return true;
        }

        normalized = string.Empty;
        kind = default;
        return false;
    }

    public static bool TryNormalizeTypeCode(string requestType, string? typeCode, out string normalized, out string category)
    {
        var options = requestType == SuggestionsRequestType ? Suggestions : Issues;
        var match = options.FirstOrDefault(option => string.Equals(option.Value, typeCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            normalized = string.Empty;
            category = string.Empty;
            return false;
        }

        normalized = match.Value;
        category = CategoryFor(requestType, normalized);
        return true;
    }

    public static string CategoryFor(string requestType, string typeCode)
        => (requestType == SuggestionsRequestType ? SuggestionCategoryPrefix : IssueCategoryPrefix) + typeCode;

    /// <summary>Validates and canonicalizes the reserved customer taxonomy namespaces.</summary>
    public static bool TryNormalizeReservedCategory(SupportCaseKind kind, string? category, out bool isReserved, out string? normalized)
    {
        var value = category?.Trim() ?? string.Empty;
        string? requestType = null;
        string? typeCode = null;
        if (value.StartsWith(IssueCategoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            isReserved = true;
            requestType = IssuesRequestType;
            typeCode = value[IssueCategoryPrefix.Length..];
        }
        else if (value.StartsWith(SuggestionCategoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            isReserved = true;
            requestType = SuggestionsRequestType;
            typeCode = value[SuggestionCategoryPrefix.Length..];
        }
        else
        {
            isReserved = false;
            normalized = null;
            return true;
        }

        var expectedKind = requestType == IssuesRequestType ? SupportCaseKind.Complaint : SupportCaseKind.GeneralRequest;
        if (kind != expectedKind || !TryNormalizeTypeCode(requestType, typeCode, out var canonicalCode, out var canonicalCategory))
        {
            normalized = null;
            return false;
        }

        normalized = canonicalCategory;
        return true;
    }

    public static bool IsSuggestionCategory(string category)
        => category.StartsWith(SuggestionCategoryPrefix, StringComparison.OrdinalIgnoreCase);
}
