namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>One bilingual display text for a customer delivery status.</summary>
public sealed record CustomerDeliveryStatusText(string Ar, string En);

/// <summary>
/// The single bilingual catalog for customer delivery status display texts. Status
/// codes and resolver decision rules stay independent from wording; every consumer
/// reads the text from here so translations live in one place and never inside
/// status codes or events.
/// </summary>
public static class CustomerDeliveryStatusTexts
{
    public static readonly CustomerDeliveryStatusText NotStarted = new("لم يبدأ التجهيز", "Preparation not started");
    public static readonly CustomerDeliveryStatusText Preparing = new("جارٍ التجهيز", "Preparing");
    public static readonly CustomerDeliveryStatusText ReadyForPickup = new("تم التجهيز", "Ready for pickup");
    public static readonly CustomerDeliveryStatusText OutForDelivery = new("خرج للتوصيل", "Out for delivery");
    public static readonly CustomerDeliveryStatusText Delivered = new("تم التسليم", "Delivered");
    public static readonly CustomerDeliveryStatusText Failed = new("تعذر التسليم", "Delivery failed");
    public static readonly CustomerDeliveryStatusText ReturnedToKitchen = new("أُعيد للمطعم", "Returned to restaurant");
    public static readonly CustomerDeliveryStatusText Frozen = new("اليوم مجمّد", "Day frozen");
    public static readonly CustomerDeliveryStatusText Cancelled = new("ملغى", "Cancelled");
    public static readonly CustomerDeliveryStatusText TrackingUnavailable = new("حالة التتبع غير واضحة", "Tracking unavailable");

    /// <summary>Text for a known status code; unknown codes never leak the code as copy.</summary>
    public static CustomerDeliveryStatusText For(string statusCode) => statusCode switch
    {
        CustomerDeliveryStatusCodes.NotStarted => NotStarted,
        CustomerDeliveryStatusCodes.Preparing => Preparing,
        CustomerDeliveryStatusCodes.ReadyForPickup => ReadyForPickup,
        CustomerDeliveryStatusCodes.OutForDelivery => OutForDelivery,
        CustomerDeliveryStatusCodes.Delivered => Delivered,
        CustomerDeliveryStatusCodes.Failed => Failed,
        CustomerDeliveryStatusCodes.ReturnedToKitchen => ReturnedToKitchen,
        CustomerDeliveryStatusCodes.Frozen => Frozen,
        CustomerDeliveryStatusCodes.Cancelled => Cancelled,
        _ => TrackingUnavailable,
    };
}
