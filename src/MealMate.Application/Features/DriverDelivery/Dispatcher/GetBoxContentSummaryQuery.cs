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

        if (stop is null)
        {
            return Error.NotFound(
                MessageCodes.Orders.NotFound,
                isAr ? "لم يتم العثور على بيانات الصندوق المطلوب." : "Box content was not found.");
        }

        var boxCode = stop.OrderCode ?? string.Empty;
        var customerName = !string.IsNullOrWhiteSpace(stop.CustomerName) ? MaskName(stop.CustomerName) : string.Empty;
        var customerPhone = !string.IsNullOrWhiteSpace(stop.CustomerPhone) ? MaskPhone(stop.CustomerPhone) : string.Empty;
        var mealSummary = isAr ? stop.MealSummaryAr : stop.MealSummaryEn;

        var mealsList = !string.IsNullOrWhiteSpace(mealSummary)
            ? new List<BoxMealSummaryItemDto>
            {
                new(
                    MealId: Guid.Empty,
                    MealName: mealSummary,
                    Quantity: stop.BoxCount > 0 ? stop.BoxCount : 1,
                    Category: isAr ? "وجبة" : "Meal",
                    Notes: null)
            }
            : new List<BoxMealSummaryItemDto>();

        return Result.Success(new BoxContentSummaryDto(
            BoxId: stop.Id,
            BoxCode: boxCode,
            CustomerMaskedId: stop.CustomerMaskedId ?? string.Empty,
            CustomerNameMasked: customerName,
            CustomerPhoneMasked: customerPhone,
            ZoneName: isAr ? (stop.ZoneNameAr ?? string.Empty) : (stop.ZoneNameEn ?? string.Empty),
            Address: isAr ? (stop.ZoneNameAr ?? string.Empty) : (stop.ZoneNameEn ?? string.Empty),
            DeliveryTimeWindow: stop.SlotLabel ?? string.Empty,
            BoxCount: stop.BoxCount,
            Barcode: boxCode,
            DeliveryNotes: null,
            Allergies: new List<string>(),
            Meals: mealsList));
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
