namespace MealMate.Application.Features.CustomerPush;

public static class CustomerPushEventKeys
{
    public const string SubscriptionPaymentConfirmed = "subscription.payment_confirmed";
    public const string OrderLocked72h = "order.locked_72h";
    public const string OrderConfirmedByRestaurant = "order.confirmed_by_restaurant";
    public const string DeliveryPrep24h = "delivery.prep_24h";
    public const string OrderOutForDelivery = "delivery.out_for_delivery";
    public const string OrderDelivered = "delivery.delivered";
    public const string InfluencerPromoted = "influencer.promoted";
}

public static class CustomerPushTemplates
{
    public sealed record PushCopy(string Title, string Body, string DeepLink);

    public static PushCopy SubscriptionPaymentConfirmed(bool isRtl)
        => isRtl
            ? new("أهلاً بك في MealMate", "تم تفعيل اشتراكك بنجاح. ابدأ باختيار وجباتك من التقويم.", "mealmate://calendar")
            : new("Welcome to MealMate", "Your subscription is active. Open the calendar and pick your meals.", "mealmate://calendar");

    public static PushCopy OrderLocked72h(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("طلبك قيد المعالجة", $"طلب يوم {serviceDate:yyyy-MM-dd} أُرسل للمطعم لبدء جدولته.", "mealmate://calendar")
            : new("Order locked", $"Your order for {serviceDate:yyyy-MM-dd} was sent to the restaurant and can no longer be edited.", "mealmate://calendar");

    public static PushCopy OrderConfirmedByRestaurant(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("تم تأكيد طلبك 👨‍🍳", $"المطعم أكد وجبتك ليوم {serviceDate:yyyy-MM-dd} وبدأ التجهيز.", "mealmate://orders")
            : new("Order Confirmed 👨‍🍳", $"The restaurant confirmed your meal for {serviceDate:yyyy-MM-dd}.", "mealmate://orders");

    public static PushCopy DeliveryPrep24h(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("التوصيل غداً", $"طلبك ليوم {serviceDate:yyyy-MM-dd} في مرحلة التحضير النهائي.", "mealmate://calendar")
            : new("Delivery tomorrow", $"Your order for {serviceDate:yyyy-MM-dd} is in final preparation.", "mealmate://calendar");

    public static PushCopy OrderOutForDelivery(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("طلبك في الطريق إليك 🚗", $"السائق استلم وجبتك ليوم {serviceDate:yyyy-MM-dd} وهو الآن في الطريق لتوصيلها إلى عنوانك.", "mealmate://orders")
            : new("Out for Delivery 🚗", $"The driver is on the way to deliver your meal for {serviceDate:yyyy-MM-dd}.", "mealmate://orders");

    public static PushCopy OrderDelivered(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("تم تسليم طلبك بنجاح ✅", $"تم توصيل وجبتك ليوم {serviceDate:yyyy-MM-dd}. بالهناء والشفاء!", "mealmate://orders")
            : new("Delivered ✅", $"Your meal for {serviceDate:yyyy-MM-dd} was delivered. Enjoy your meal!", "mealmate://orders");

    public static PushCopy InfluencerPromoted(string influencerCode, decimal commissionRate, bool isRtl)
        => isRtl
            ? new(
                "تهانينا! أصبحت مؤثراً شريكاً في MealMate 🎉",
                $"تمت ترقية حسابك بنجاح! كود الخصم الخاص بك هو ({influencerCode}) بنسبة عمولة {commissionRate:0.#}%. يمكنك الآن مشاركته ومتابعة أرباحك وعمولاتك من تطبيقك.",
                "mealmate://influencer-portal")
            : new(
                "Congratulations! You're now a MealMate Influencer 🎉",
                $"Your account has been promoted to Influencer partner! Your promo code is ({influencerCode}) with {commissionRate:0.#}% commission. Share it now to start earning.",
                "mealmate://influencer-portal");
}
