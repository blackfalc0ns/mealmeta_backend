using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public static class SubscriptionLocalization
{
    public static string StatusNameAr(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "بانتظار الدفع",
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => "تم الدفع",
        SubscriptionStatus.Scheduled => "مجدول",
        SubscriptionStatus.Active => "نشط",
        SubscriptionStatus.Frozen => "مجمد",
        SubscriptionStatus.Completed => "مكتمل",
        SubscriptionStatus.Cancelled => "ملغي",
        SubscriptionStatus.CancellationPending => "إلغاء قيد المتابعة",
        SubscriptionStatus.Expired => "منتهي",
        SubscriptionStatus.PaymentFailed => "فشل الدفع",
        _ => "مسودة",
    };

    public static string StatusNameEn(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "Pending Payment",
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => "Paid",
        SubscriptionStatus.Scheduled => "Scheduled",
        SubscriptionStatus.Active => "Active",
        SubscriptionStatus.Frozen => "Frozen",
        SubscriptionStatus.Completed => "Completed",
        SubscriptionStatus.Cancelled => "Cancelled",
        SubscriptionStatus.CancellationPending => "Cancellation Pending",
        SubscriptionStatus.Expired => "Expired",
        SubscriptionStatus.PaymentFailed => "Payment Failed",
        _ => "Draft",
    };

    public static string FamilyStatusNameAr(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "اشتراك عائلي بانتظار الدفع",
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => "اشتراك عائلي",
        SubscriptionStatus.Scheduled => "اشتراك عائلي مجدول",
        SubscriptionStatus.Active => "اشتراك عائلي",
        SubscriptionStatus.Frozen => "اشتراك عائلي مجمد",
        _ => StatusNameAr(status),
    };

    public static string FamilyStatusNameEn(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "Family Subscription Pending Payment",
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => "Family Subscription",
        SubscriptionStatus.Scheduled => "Family Subscription Scheduled",
        SubscriptionStatus.Active => "Family Subscription",
        SubscriptionStatus.Frozen => "Family Subscription Frozen",
        _ => StatusNameEn(status),
    };

    public static string InvoiceStatusNameAr(string status) => status switch
    {
        "PAID" => "تم الدفع",
        "PAID_PENDING_FULFILLMENT" => "تم الدفع (قيد التفعيل)",
        _ => status,
    };

    public static string InvoiceStatusNameEn(string status) => status switch
    {
        "PAID" => "Paid",
        "PAID_PENDING_FULFILLMENT" => "Paid, fulfillment pending",
        _ => status,
    };

    public static string VerificationStatusNameAr(VerificationStatus status) => status switch
    {
        VerificationStatus.Approved => "معتمد ومطبق",
        VerificationStatus.Submitted => "قيد المراجعة",
        VerificationStatus.Rejected => "مرفوض",
        _ => status.ToString(),
    };

    public static string FreezeNoticeAr(int startDelayDays) =>
        $"عند تجميد الاشتراك: يتم تفعيل التجميد تلقائياً وتمديد تاريخ انتهاء اشتراكك فوراً بدون انتظار موافقة الإدارة بعدد أيام التجميد. طلبات مهلة المطبخ ({startDelayDays * 24} ساعة القادمة) مؤكدة وسيتم تسليمها، ويتوقف توصيل الوجبات مؤقتاً خلال فترة التجميد ويُستأنف تلقائياً بعدها. في حال كان لديك أيام تم اختيار وجباتها مسبقاً ضمن فترة التجميد، سيتم إلغاء تلك الاختيارات وإضافة أيام تعويضية بديلة في نهاية الاشتراك.";

    public static string FreezeNoticeEn(int startDelayDays) =>
        $"When freezing: Subscription end date will be extended automatically without waiting for admin approval. Orders within the kitchen lead window ({startDelayDays * 24}h) are confirmed and delivered, and deliveries pause during freeze and resume automatically. If you had already selected meals for days within the freeze period, those selections will be cleared and replaced with new days at the end.";

    public static string FreezeSummaryAr(int frozenDaysCount, DateOnly startDate, DateOnly endDate, int compensationDaysCount, DateOnly newEndDate) =>
        $"تم تجميد {frozenDaysCount} يوم خدمة للفترة من {startDate:yyyy-MM-dd} إلى {endDate:yyyy-MM-dd}، وتم تعويضها بإضافة {compensationDaysCount} يوم بديل في نهاية الاشتراك حتى {newEndDate:yyyy-MM-dd}.";

    public static string FreezeSummaryEn(int frozenDaysCount, DateOnly startDate, DateOnly endDate, int compensationDaysCount, DateOnly newEndDate) =>
        $"{frozenDaysCount} service days frozen from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}, compensated with {compensationDaysCount} replacement days at subscription end until {newEndDate:yyyy-MM-dd}.";

    public static string EffectiveUpgradeNoticeAr(DateOnly effectiveFromDate) =>
        $"يبدأ تفعيل التصنيف الجديد على جدول وجباتك اعتباراً من {effectiveFromDate:yyyy-MM-dd} (بعد مهلة الـ 72 ساعة المقررة لتجهيز الطلبات الحالية).";

    public static string EffectiveUpgradeNoticeEn(DateOnly effectiveFromDate) =>
        $"The new tier will take effect on your meal schedule starting from {effectiveFromDate:yyyy-MM-dd} (after the 72-hour operational preparation cutoff).";
}
