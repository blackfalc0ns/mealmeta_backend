namespace MealMate.Application.Features.CustomerPush;

public static class CustomerPushEventKeys
{
    public const string SubscriptionPaymentConfirmed = "subscription.payment_confirmed";
    public const string OrderLocked72h = "order.locked_72h";
    public const string DeliveryPrep24h = "delivery.prep_24h";
    public const string OrderDelivered = "delivery.delivered";
}

public static class CustomerPushTemplates
{
    public sealed record PushCopy(string Title, string Body, string DeepLink);

    public static PushCopy SubscriptionPaymentConfirmed(bool isRtl)
        => isRtl
            ? new("تم تأكيد اشتراكك", "تم استلام الدفع بنجاح. يمكنك الآن متابعة اختيار وجباتك.", "mealmate://subscriptions")
            : new("Subscription confirmed", "Your payment was received successfully. You can now choose your meals.", "mealmate://subscriptions");

    public static PushCopy OrderLocked72h(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("تم تأكيد طلبك", $"طلب يوم {serviceDate:yyyy-MM-dd} أُرسل للمطعم ولا يمكن تعديله الآن.", "mealmate://calendar")
            : new("Order locked", $"Your order for {serviceDate:yyyy-MM-dd} was sent to the restaurant and can no longer be edited.", "mealmate://calendar");

    public static PushCopy DeliveryPrep24h(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("التوصيل غداً", $"طلبك ليوم {serviceDate:yyyy-MM-dd} في مرحلة التحضير النهائي.", "mealmate://calendar")
            : new("Delivery tomorrow", $"Your order for {serviceDate:yyyy-MM-dd} is in final preparation.", "mealmate://calendar");

    public static PushCopy OrderDelivered(DateOnly serviceDate, bool isRtl)
        => isRtl
            ? new("تم التسليم", $"تم توصيل طلبك ليوم {serviceDate:yyyy-MM-dd}. نتمنى لك وجبة ممتعة!", "mealmate://orders")
            : new("Delivered", $"Your order for {serviceDate:yyyy-MM-dd} was delivered. Enjoy your meal!", "mealmate://orders");
}
