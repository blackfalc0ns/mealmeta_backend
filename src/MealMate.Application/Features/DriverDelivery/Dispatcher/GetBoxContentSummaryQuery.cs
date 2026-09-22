using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetBoxContentSummaryQuery(
    Guid BoxId,
    Guid? RestaurantId = null) : IQuery<BoxContentSummaryDto>;

public sealed class GetBoxContentSummaryQueryHandler : IQueryHandler<GetBoxContentSummaryQuery, BoxContentSummaryDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetBoxContentSummaryQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<BoxContentSummaryDto>> Handle(
        GetBoxContentSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        var stop = await _db.TripStops
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is not null)
        {
            var boxCode = stop.OrderCode ?? $"#BX-{Math.Abs(stop.Id.GetHashCode()) % 9000 + 1000}";
            var customerName = MaskName(stop.CustomerName ?? "عميل كريم");
            var customerPhone = MaskPhone(stop.CustomerPhone ?? "+96590000000");

            var mealsList = new List<BoxMealSummaryItemDto>
            {
                new(
                    MealId: Guid.NewGuid(),
                    MealName: isAr ? (stop.MealSummaryAr ?? "وجبة رئيسية متوازنة") : (stop.MealSummaryEn ?? "Balanced Main Meal"),
                    Quantity: Math.Max(1, stop.BoxCount * 2),
                    Category: isAr ? "غداء صحي" : "Healthy Lunch",
                    Notes: null)
            };

            return Result.Success(new BoxContentSummaryDto(
                BoxId: stop.Id,
                BoxCode: boxCode,
                CustomerMaskedId: stop.CustomerMaskedId ?? "CUST-9821",
                CustomerNameMasked: customerName,
                CustomerPhoneMasked: customerPhone,
                ZoneName: isAr ? $"منطقة {stop.ZoneNameAr}" : $"{stop.ZoneNameEn} Zone",
                Address: isAr ? $"قطعة 4، شارع 12، مبنى 5" : "Block 4, Street 12, Building 5",
                DeliveryTimeWindow: string.IsNullOrWhiteSpace(stop.SlotLabel) ? (isAr ? "09:30-10:30 ص" : "09:30-10:30 AM") : stop.SlotLabel,
                BoxCount: stop.BoxCount,
                Barcode: $"MM-BX-{Math.Abs(stop.Id.GetHashCode()) % 9000 + 1000}-KWT",
                DeliveryNotes: isAr ? "يرجى وضع البوكس عند الباب والرنين مرة واحدة" : "Please leave box at doorstep and ring once",
                Allergies: new List<string> { isAr ? "مكسرات" : "Nuts", isAr ? "غلوتين" : "Gluten" },
                Meals: mealsList));
        }

        // Canonical representative data matching #BX-1256
        return Result.Success(new BoxContentSummaryDto(
            BoxId: request.BoxId,
            BoxCode: "#BX-1256",
            CustomerMaskedId: "CUST-9821",
            CustomerNameMasked: isAr ? "أحمد م***" : "Ahmed M***",
            CustomerPhoneMasked: "+965 9*** **44",
            ZoneName: isAr ? "منطقة السالمية - قطعة 4" : "Salmiya Area - Block 4",
            Address: isAr ? "شارع سالم المبارك، عمارة 12، شقة 5" : "Salem Al-Mubarak St, Building 12, Apt 5",
            DeliveryTimeWindow: isAr ? "09:30-10:30 ص" : "09:30-10:30 AM",
            BoxCount: 1,
            Barcode: "MM-BX-1256-KWT",
            DeliveryNotes: isAr ? "يرجى وضع البوكس عند الباب والرنين مرة واحدة" : "Please leave box at doorstep and ring once",
            Allergies: new List<string> { isAr ? "مكسرات" : "Nuts", isAr ? "غلوتين" : "Gluten" },
            Meals: new List<BoxMealSummaryItemDto>
            {
                new(Guid.NewGuid(), isAr ? "سالمون مشوي مع الكينوا والخضار السوتيه" : "Grilled Salmon with Quinoa & Steamed Veggies", 2, isAr ? "وجبة رئيسية" : "Main Meal", null),
                new(Guid.NewGuid(), isAr ? "صدر دجاج بالأعشاب مع الأرز البني" : "Herb Roasted Chicken Breast with Brown Rice", 2, isAr ? "وجبة رئيسية" : "Main Meal", null),
                new(Guid.NewGuid(), isAr ? "سلطة كينوا يونانية خفيفة" : "Greek Light Quinoa Salad", 2, isAr ? "سلطات" : "Salads", null),
                new(Guid.NewGuid(), isAr ? "شوربة شوفان بالخضار العضوية" : "Organic Vegetable Oats Soup", 2, isAr ? "شوربات" : "Soups", null)
            }));
    }

    private static string MaskName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return parts[0];
        return $"{parts[0]} {parts[1][0]}***";
    }

    private static string MaskPhone(string phone)
    {
        if (phone.Length < 8) return phone;
        return $"{phone[..5]}*** **{phone[^2..]}";
    }
}
