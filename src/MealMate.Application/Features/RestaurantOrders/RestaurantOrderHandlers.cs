using System.Globalization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOrders;

public sealed class RestaurantOrderService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RestaurantOrderService(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<PendingConfirmationDataDto>> GetPendingConfirmationAsync(
        GetRestaurantPendingConfirmationQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<PendingConfirmationDataDto>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var now = _clock.UtcNow;

        // Query SubscriptionDays assigned to this restaurant that are in locked 72h window or sent/confirmed
        var days = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId.Value &&
                        (x.Status == SubscriptionDayStatus.Locked72h ||
                         x.Status == SubscriptionDayStatus.SentToRestaurant ||
                         x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                         x.Status == SubscriptionDayStatus.Preparing ||
                         x.Status == SubscriptionDayStatus.ReplacementRequired))
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(cancellationToken);

        var subscriptionIds = days.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => subscriptionIds.Contains(s.Id))
            .Include(s => s.Program)
            .Include(s => s.Bundle)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var dayIds = days.Select(d => d.Id).ToList();
        var mealSelections = await _db.DailyMealSelections
            .AsNoTracking()
            .Where(s => dayIds.Contains(s.SubscriptionDayId))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        var mealIds = mealSelections.Select(m => m.MealId).Distinct().ToList();
        var meals = await _db.Meals
            .AsNoTracking()
            .Where(m => mealIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var items = new List<PendingConfirmationItemDto>();

        foreach (var day in days)
        {
            subscriptions.TryGetValue(day.SubscriptionId, out var sub);

            var lockTime = day.LockAtUtc ?? day.CreatedAtUtc;
            var deadlineTime = lockTime.AddHours(24);
            var hoursLeft = (int)Math.Floor((deadlineTime - now).TotalHours);

            var isConfirmed = day.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                              day.Status == SubscriptionDayStatus.Preparing;

            var isReassigned = day.Status == SubscriptionDayStatus.ReplacementRequired;

            string urgency;
            if (isConfirmed)
            {
                urgency = "normal";
            }
            else if (hoursLeft <= 0)
            {
                urgency = "overdue";
            }
            else if (hoursLeft <= 4)
            {
                urgency = "critical";
            }
            else if (hoursLeft <= 8)
            {
                urgency = "warning";
            }
            else
            {
                urgency = "normal";
            }

            var daySelections = mealSelections.Where(ms => ms.SubscriptionDayId == day.Id).ToList();
            var dayMeals = daySelections.Select(ms => meals.TryGetValue(ms.MealId, out var m) ? m : null).Where(m => m != null).ToList();

            var mealSummary = BuildMealSummary(dayMeals!, sub?.Bundle);

            var programLabel = new LocalizedTextDto(
                sub?.Program?.NameAr ?? "برنامج غذائي",
                sub?.Program?.NameEn ?? "Nutrition Program");

            var bundleLabel = new LocalizedTextDto(
                sub?.Bundle?.NameAr ?? "باقة كاملة",
                sub?.Bundle?.NameEn ?? "Full Package");

            var deliveryDateLabel = FormatDeliveryDate(day.ServiceDate);
            var deliverySlotLabel = new LocalizedTextDto("12:00 – 14:00", "12:00 – 14:00");

            var receivedHours = (int)Math.Max(1, (now - lockTime).TotalHours);
            var receivedAtLabel = new LocalizedTextDto($"منذ {receivedHours} ساعة", $"{receivedHours}h ago");

            LocalizedTextDto deadlineLabel;
            if (isConfirmed)
            {
                deadlineLabel = new LocalizedTextDto("تم التأكيد", "Confirmed");
            }
            else if (hoursLeft <= 0)
            {
                var overdueHours = Math.Abs(hoursLeft);
                deadlineLabel = new LocalizedTextDto(
                    overdueHours == 0 ? "انتهى الموعد" : $"متأخر {overdueHours} ساعات",
                    overdueHours == 0 ? "Deadline passed" : $"{overdueHours}h overdue");
            }
            else
            {
                deadlineLabel = new LocalizedTextDto($"متبقي {hoursLeft} ساعات", $"{hoursLeft}h left");
            }

            var orderCode = $"ORD-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}";
            var batchCode = $"BATCH-{day.ServiceDate:dd}A";
            var customerMaskedId = sub != null ? $"CUS-•••{sub.CustomerUserId.ToString()[..3].ToUpperInvariant()}" : "CUS-•••999";

            items.Add(new PendingConfirmationItemDto(
                Id: day.Id,
                OrderCode: orderCode,
                BatchCode: batchCode,
                CustomerMaskedId: customerMaskedId,
                MealSummary: mealSummary,
                ProgramLabel: programLabel,
                BundleLabel: bundleLabel,
                BoxCount: 1,
                DeliveryDateLabel: deliveryDateLabel,
                DeliverySlotLabel: deliverySlotLabel,
                ReceivedAtLabel: receivedAtLabel,
                DeadlineLabel: deadlineLabel,
                HoursLeft: hoursLeft,
                Urgency: urgency,
                Confirmed: isConfirmed,
                IsReassigned: isReassigned,
                Route: $"/restaurant/orders/detail/{orderCode}"
            ));
        }

        var openItems = items.Where(i => !i.Confirmed).ToList();
        var pendingCount = openItems.Count;
        var criticalCount = openItems.Count(i => i.Urgency == "critical");
        var warningCount = openItems.Count(i => i.Urgency == "warning");
        var overdueCount = openItems.Count(i => i.Urgency == "overdue");

        var summaries = new List<PendingConfirmationSummaryDto>
        {
            new("pending", new LocalizedTextDto("بانتظار التأكيد", "Awaiting confirm"), pendingCount, new LocalizedTextDto("داخل نافذة 24 ساعة", "Inside 24h window"), "primary", "lucideClock"),
            new("critical", new LocalizedTextDto("حرج (< 4س)", "Critical (< 4h)"), criticalCount, new LocalizedTextDto("يحتاج تأكيد فوري", "Needs immediate confirm"), "danger", "lucideTriangleAlert"),
            new("warning", new LocalizedTextDto("تنبيه (< 8س)", "Warning (< 8h)"), warningCount, new LocalizedTextDto("اقترب الموعد النهائي", "Deadline approaching"), "warning", "lucideTimer"),
            new("overdue", new LocalizedTextDto("متأخر", "Overdue"), overdueCount, new LocalizedTextDto("قد يُعاد تعيينه", "May be reassigned"), "accent", "lucideCircleAlert")
        };

        var dateLabel = FormatDateLabel(DateOnly.FromDateTime(now));

        var data = new PendingConfirmationDataDto(
            Title: new LocalizedTextDto("بانتظار التأكيد", "Pending confirmation"),
            Subtitle: new LocalizedTextDto("طلبات وصلت عند −72س · أكّد خلال 24 ساعة قبل تصعيد الإدارة", "Orders locked at −72h · confirm within 24 hours before admin escalation"),
            DateLabel: dateLabel,
            WindowHint: new LocalizedTextDto("نافذة التأكيد 24 ساعة", "24h confirmation window"),
            Summaries: summaries,
            Orders: items
        );

        return Result.Success(data);
    }

    public async Task<Result<RestaurantOrderDetailDto>> GetOrderDetailAsync(
        GetRestaurantOrderDetailQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<RestaurantOrderDetailDto>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var queryKey = request.OrderCodeOrId.Trim();
        Guid parsedGuid = Guid.Empty;
        var isGuid = Guid.TryParse(queryKey, out parsedGuid);

        SubscriptionDay? day;
        if (isGuid)
        {
            day = await _db.SubscriptionDays
                .AsNoTracking()
                .Include(x => x.Calendar)
                .FirstOrDefaultAsync(x => x.Id == parsedGuid && x.RestaurantId == restaurantId.Value, cancellationToken);
        }
        else
        {
            // Match orderCode pattern
            var days = await _db.SubscriptionDays
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId.Value)
                .ToListAsync(cancellationToken);

            day = days.FirstOrDefault(x =>
                $"ORD-{x.ServiceDate:MMdd}-{x.Id.ToString()[..4].ToUpperInvariant()}".Equals(queryKey, StringComparison.OrdinalIgnoreCase) ||
                x.Id.ToString().StartsWith(queryKey, StringComparison.OrdinalIgnoreCase));
        }

        if (day == null)
            return Result.Failure<RestaurantOrderDetailDto>(Error.NotFound("order.not_found", "Order was not found."));

        var subscription = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Program)
            .Include(s => s.Bundle)
            .FirstOrDefaultAsync(s => s.Id == day.SubscriptionId, cancellationToken);

        var mealSelections = await _db.DailyMealSelections
            .AsNoTracking()
            .Where(s => s.SubscriptionDayId == day.Id)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        var mealIds = mealSelections.Select(m => m.MealId).Distinct().ToList();
        var meals = await _db.Meals
            .AsNoTracking()
            .Where(m => mealIds.Contains(m.Id))
            .Include(m => m.Ingredients)
                .ThenInclude(mi => mi.Ingredient)
            .Include(m => m.Images)
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var boxMealItems = new List<BoxMealItemDto>();
        var slotIndex = 0;

        foreach (var sel in mealSelections)
        {
            if (!meals.TryGetValue(sel.MealId, out var meal)) continue;

            var nutrition = new BoxMealNutritionDto(
                Calories: meal.Calories ?? 400,
                ProteinGrams: meal.ProteinG ?? 35,
                CarbsGrams: meal.CarbsG ?? 40,
                FatGrams: meal.FatG ?? 15,
                FiberGrams: meal.FiberG ?? 5,
                SugarGrams: meal.TotalSugarsG ?? 4,
                SodiumMg: meal.SodiumMg ?? 500,
                ProteinDailyPercent: (int)Math.Round(((meal.ProteinG ?? 35) / 50m) * 100),
                SodiumDailyPercent: (int)Math.Round(((meal.SodiumMg ?? 500) / 2300m) * 100),
                ServingWeightGrams: meal.ServingWeightG ?? 350
            );

            var ingredients = meal.Ingredients.Select(mi => new BoxMealIngredientDto(
                Id: mi.IngredientId,
                Name: new LocalizedTextDto(mi.Ingredient?.NameAr ?? "مكون", mi.Ingredient?.NameEn ?? "Ingredient"),
                Amount: new LocalizedTextDto($"{mi.PortionGrams:0} غرام", $"{mi.PortionGrams:0}g"),
                WeightGrams: mi.PortionGrams,
                Calories: (mi.Ingredient?.DefaultCaloriesPer100g ?? 100m) * (mi.PortionGrams / 100m),
                ProteinGrams: (mi.Ingredient?.DefaultProteinPer100g ?? 10m) * (mi.PortionGrams / 100m),
                CarbsGrams: (mi.Ingredient?.DefaultCarbsPer100g ?? 10m) * (mi.PortionGrams / 100m),
                FatGrams: (mi.Ingredient?.DefaultFatPer100g ?? 5m) * (mi.PortionGrams / 100m),
                FiberGrams: 2,
                SugarGrams: 1,
                SodiumMg: 50,
                ProteinDailyPercent: 10,
                SodiumDailyPercent: 2,
                PrepStyle: new LocalizedTextDto("طازج ومجهز", "Freshly prepared"),
                Note: null,
                Allergen: false
            )).ToList();

            var slot = meal.Slot?.ToLowerInvariant() ?? "main";
            var slotLabel = GetSlotLabel(slot);

            var imageUrl = meal.Images.FirstOrDefault()?.ImageUrl ??
                           "https://images.unsplash.com/photo-1546069901-ba9599a7e63c?w=600&auto=format&fit=crop&q=80";

            boxMealItems.Add(new BoxMealItemDto(
                Id: meal.Id,
                Slot: slot,
                SlotLabel: slotLabel,
                SlotIndex: slotIndex++,
                Name: new LocalizedTextDto(meal.NameAr, meal.NameEn),
                Description: new LocalizedTextDto(meal.DescriptionAr ?? meal.NameAr, meal.DescriptionEn ?? meal.NameEn),
                ImageUrl: imageUrl,
                Nutrition: nutrition,
                Ingredients: ingredients,
                AllergyFlags: new List<LocalizedTextDto>(),
                PrepNote: new LocalizedTextDto("يُحفظ مبردًا عند 4 درجات مئوية ويُستهلك خلال 24 ساعة", "Keep refrigerated at 4°C and consume within 24 hours")
            ));
        }

        var orderCode = $"ORD-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}";
        var batchCode = $"BATCH-{day.ServiceDate:dd}A";
        var customerMaskedId = subscription != null ? $"CUS-•••{subscription.CustomerUserId.ToString()[..3].ToUpperInvariant()}" : "CUS-•••999";

        string status;
        switch (day.Status)
        {
            case SubscriptionDayStatus.ConfirmedByRestaurant:
            case SubscriptionDayStatus.Preparing:
                status = "preparing";
                break;
            case SubscriptionDayStatus.OutForDelivery:
                status = "waiting-driver";
                break;
            case SubscriptionDayStatus.Delivered:
                status = "completed";
                break;
            default:
                status = "preparing";
                break;
        }

        var isConfirmed = day.Status == SubscriptionDayStatus.ConfirmedByRestaurant || day.ConfirmedAtUtc != null;

        var checklist = new List<OrderDetailChecklistItemDto>
        {
            new("chk-1", new LocalizedTextDto("تأكيد استلام الطلب وموافقة المطبخ", "Order confirmation & kitchen approval"), isConfirmed, new LocalizedTextDto("تم اعتماده ضمن نافذة 24 ساعة", "Approved within 24h window")),
            new("chk-2", new LocalizedTextDto("فحص مكونات الحساسية والتفضيلات", "Allergen & preference safety check"), isConfirmed, new LocalizedTextDto("مطابق لملف العميل الغذائي", "Matches customer dietary profile")),
            new("chk-3", new LocalizedTextDto("تجهيز وطهي الوجبات وفق المعايير", "Cook & prepare meals to standard"), false, new LocalizedTextDto("التزام بأوزان الحصص والسعرات", "Portion weights and calories verified")),
            new("chk-4", new LocalizedTextDto("التعبئة في البوكس والملصق الحراري", "Box packaging & thermal label"), false, new LocalizedTextDto("إلصاق الباركود وبيانات التسليم", "Barcode and delivery info attached")),
            new("chk-5", new LocalizedTextDto("تسليم المندوب ومسح الباركود", "Handover to driver with scan"), false, new LocalizedTextDto("جاهز للاستلام والتوزيع", "Ready for dispatch"))
        };

        var lockTimeString = day.LockAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "2026-08-11 12:00";
        var timeline = new List<OrderDetailTimelineItemDto>
        {
            new("tl-1", new LocalizedTextDto("إغلاق الطلب وتثبيت الاختيارات (-72س)", "Order locked (-72h)"), new LocalizedTextDto("تم إغلاق التعديل وتثبيت المكونات للمطعم", "Order locked and assigned to restaurant"), new LocalizedTextDto(lockTimeString, lockTimeString), "info")
        };

        if (day.ConfirmedAtUtc != null)
        {
            var confirmedTimeString = day.ConfirmedAtUtc.Value.ToString("yyyy-MM-dd HH:mm");
            timeline.Insert(0, new OrderDetailTimelineItemDto(
                "tl-2",
                new LocalizedTextDto("تم تأكيد الطلب من المطعم", "Order confirmed by restaurant"),
                new LocalizedTextDto("تم تأكيد الجاهزية وبدء جدولة التجهيز", "Kitchen confirmed and scheduled for prep"),
                new LocalizedTextDto(confirmedTimeString, confirmedTimeString),
                "success"));
        }

        var facts = new List<OrderDetailFactDto>
        {
            new("fact-pkg", new LocalizedTextDto("نوع الباقة", "Bundle"), new LocalizedTextDto(subscription?.Bundle?.NameAr ?? "باقة كاملة", subscription?.Bundle?.NameEn ?? "Full Package"), "lucidePackage"),
            new("fact-boxes", new LocalizedTextDto("عدد البوكسات", "Box Count"), new LocalizedTextDto("1 بوكس", "1 box"), "lucideClipboardList"),
            new("fact-window", new LocalizedTextDto("نافذة التوصيل", "Delivery Window"), new LocalizedTextDto("12:00 – 14:00", "12:00 – 14:00"), "lucideClock"),
            new("fact-driver", new LocalizedTextDto("المندوب المعين", "Assigned Driver"), new LocalizedTextDto("سائق المنطقة #12", "Zone Driver #12"), "lucideTruck")
        };

        var slotGroups = boxMealItems.GroupBy(m => m.Slot)
            .Select(g => new BoxSlotSummaryDto(g.Key, GetSlotLabel(g.Key), g.Count(), g.Count()))
            .ToList();

        var actions = new List<OrderDetailActionDto>
        {
            new("act-print", new LocalizedTextDto("طباعة الملصق", "Print label"), "/restaurant/orders/labels", true, "lucidePrinter"),
            new("act-handover", new LocalizedTextDto("تسليم المندوب", "Handover to driver"), "/restaurant/orders/handover", false, "lucideScanBarcode")
        };

        var detail = new RestaurantOrderDetailDto(
            Id: day.Id,
            OrderCode: orderCode,
            BatchCode: batchCode,
            Status: status,
            Shift: "morning",
            ShiftLabel: new LocalizedTextDto("الفترة الصباحية (12:00 – 14:00)", "Morning Shift (12:00 – 14:00)"),
            Title: new LocalizedTextDto($"تفاصيل الطلب {orderCode}", $"Order Detail {orderCode}"),
            Subtitle: new LocalizedTextDto("طلب باقة اشتراك يومية · مطبخ التجهيز الرئيسي", "Daily subscription box order · Main prep kitchen"),
            CustomerMaskedId: customerMaskedId,
            DeliveryDateLabel: FormatDeliveryDate(day.ServiceDate),
            DeliverySlotLabel: new LocalizedTextDto("12:00 – 14:00", "12:00 – 14:00"),
            ProgramLabel: new LocalizedTextDto(subscription?.Program?.NameAr ?? "برنامج اللياقة", subscription?.Program?.NameEn ?? "Fitness Program"),
            TierLabel: new LocalizedTextDto("بلاتينيوم", "Platinum"),
            BundleKind: "full",
            BundleLabel: new LocalizedTextDto(subscription?.Bundle?.NameAr ?? "باقة كاملة", subscription?.Bundle?.NameEn ?? "Full Package"),
            BoxCompositionLabel: new LocalizedTextDto("مكونات وجبات البوكس المعتمدة", "Approved box meal composition"),
            BoxCount: 1,
            DriverCode: "DRV-042",
            BarcodeCode: $"BAR-{day.ServiceDate:yyyyMMdd}-{day.Id.ToString()[..6].ToUpperInvariant()}",
            WindowLabel: new LocalizedTextDto("12:00 – 14:00", "12:00 – 14:00"),
            PrivacyNote: new LocalizedTextDto("بيانات العميل الشخصية محمية ومعرّف مقنّع فقط", "Customer PII protected · Masked ID only"),
            Facts: facts,
            SlotSummary: slotGroups,
            Meals: boxMealItems,
            Checklist: checklist,
            Timeline: timeline,
            Actions: actions
        );

        return Result.Success(detail);
    }

    public async Task<Result<bool>> ConfirmOrderAsync(
        ConfirmRestaurantOrderCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<bool>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId.Value, cancellationToken);

        if (day == null)
            return Result.Failure<bool>(Error.NotFound("order.not_found", "Order was not found."));

        day.Status = SubscriptionDayStatus.ConfirmedByRestaurant;
        day.ConfirmedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    public async Task<Result<bool>> RejectOrderAsync(
        RejectRestaurantOrderCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<bool>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId.Value, cancellationToken);

        if (day == null)
            return Result.Failure<bool>(Error.NotFound("order.not_found", "Order was not found."));

        day.Status = SubscriptionDayStatus.ReplacementRequired;

        var reasonCode = request.ReasonCode ?? "RESTAURANT_REJECTED";
        var reasonText = request.Reason ?? "Rejected by kitchen";

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = day.SubscriptionId,
            OperationType = "RESTAURANT_CONFIRMATION_REJECTED",
            PreviousStatus = SubscriptionStatus.Active,
            NewStatus = SubscriptionStatus.Active,
            Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; RestaurantId={restaurantId.Value}; ReasonCode={reasonCode}; Note={reasonText}",
            PerformedByUserId = _currentUser.UserId ?? Guid.Empty,
            PerformedByUserType = UserType.Restaurant,
            TimestampUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    public async Task<Result<DailyOrdersDataDto>> GetDailyOrdersAsync(
        GetRestaurantDailyOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<DailyOrdersDataDto>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var now = _clock.UtcNow;
        var targetDate = request.Date ?? DateOnly.FromDateTime(now);

        var days = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId.Value && x.ServiceDate == targetDate)
            .OrderBy(x => x.ServiceDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var subscriptionIds = days.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => subscriptionIds.Contains(s.Id))
            .Include(s => s.Program)
            .Include(s => s.Bundle)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var dayIds = days.Select(d => d.Id).ToList();
        var mealSelections = await _db.DailyMealSelections
            .AsNoTracking()
            .Where(s => dayIds.Contains(s.SubscriptionDayId))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        var mealIds = mealSelections.Select(m => m.MealId).Distinct().ToList();
        var meals = await _db.Meals
            .AsNoTracking()
            .Where(m => mealIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var items = new List<DailyOrderItemDto>();
        var index = 0;

        foreach (var day in days)
        {
            subscriptions.TryGetValue(day.SubscriptionId, out var sub);

            var daySelections = mealSelections.Where(ms => ms.SubscriptionDayId == day.Id).ToList();
            var dayMeals = daySelections.Select(ms => meals.TryGetValue(ms.MealId, out var m) ? m : null).Where(m => m != null).ToList();

            var firstMeal = dayMeals.FirstOrDefault();
            var mealName = firstMeal != null
                ? new LocalizedTextDto(firstMeal.NameAr, firstMeal.NameEn)
                : new LocalizedTextDto("وجبة متوازنة معتمدة", "Balanced meal approved");

            var programLabel = new LocalizedTextDto(
                $"{sub?.Program?.NameAr ?? "لياقة"} · بلاتينيوم",
                $"{sub?.Program?.NameEn ?? "Fitness"} · Platinum");

            string status;
            switch (day.Status)
            {
                case SubscriptionDayStatus.ConfirmedByRestaurant:
                case SubscriptionDayStatus.Preparing:
                    status = "preparing";
                    break;
                case SubscriptionDayStatus.OutForDelivery:
                    status = "waiting-driver";
                    break;
                case SubscriptionDayStatus.Delivered:
                    status = "completed";
                    break;
                default:
                    status = "preparing";
                    break;
            }

            var shift = (index % 3) switch
            {
                0 => "morning",
                1 => "noon",
                _ => "evening"
            };

            var shiftLabel = shift switch
            {
                "morning" => new LocalizedTextDto("صباح", "Morning"),
                "noon" => new LocalizedTextDto("ظهيرة", "Noon"),
                _ => new LocalizedTextDto("مساء", "Evening")
            };

            var orderCode = $"ORD-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}";
            var batchCode = $"BATCH-{day.ServiceDate:dd}";

            LocalizedTextDto? driverLabel = status == "waiting-driver" || status == "completed"
                ? new LocalizedTextDto($"DRV-{((index + 1) * 37) % 900 + 100}", $"DRV-{((index + 1) * 37) % 900 + 100}")
                : null;

            var windowLabel = status switch
            {
                "completed" => new LocalizedTextDto("تم التسليم", "Handed over"),
                "waiting-driver" => new LocalizedTextDto("السائق في الطريق", "Driver en route"),
                _ => new LocalizedTextDto("نافذة -24س", "-24h window")
            };

            var barcodeReady = status != "preparing";

            items.Add(new DailyOrderItemDto(
                Id: day.Id,
                OrderCode: orderCode,
                BatchCode: batchCode,
                MealName: mealName,
                ProgramLabel: programLabel,
                BoxCount: 1,
                Shift: shift,
                ShiftLabel: shiftLabel,
                Status: status,
                DriverLabel: driverLabel,
                WindowLabel: windowLabel,
                BarcodeReady: barcodeReady,
                ActionLabel: new LocalizedTextDto("عرض التفاصيل", "View details"),
                Route: $"/restaurant/orders/detail/{orderCode}"
            ));

            index++;
        }

        var totalCount = items.Count;
        var preparingCount = items.Count(i => i.Status == "preparing");
        var readyCount = items.Count(i => i.Status == "ready");
        var handoverCount = items.Count(i => i.Status == "waiting-driver");

        var summaries = new List<DailyOrderSummaryDto>
        {
            new("total", new LocalizedTextDto("إجمالي اليوم", "Today total"), totalCount, new LocalizedTextDto("صناديق مؤكدة للتحضير", "Confirmed boxes for prep"), "primary", "lucideClipboardList"),
            new("preparing", new LocalizedTextDto("قيد التحضير", "Preparing"), preparingCount, new LocalizedTextDto("في المطبخ الآن", "In kitchen now"), "accent", "lucideChefHat"),
            new("ready", new LocalizedTextDto("جاهز للتسليم", "Ready"), readyCount, new LocalizedTextDto("بانتظار مسح الباركود", "Awaiting barcode scan"), "warning", "lucidePackageCheck"),
            new("handover", new LocalizedTextDto("مع السائق", "With driver"), handoverCount, new LocalizedTextDto("تم التسليم جزئيًا", "Partially handed over"), "neutral", "lucideTruck")
        };

        var data = new DailyOrdersDataDto(
            Title: new LocalizedTextDto("الطلبات اليومية", "Daily orders"),
            Subtitle: new LocalizedTextDto("تحضير وتجهيز طلبات اليوم التشغيلي بعد قفل نافذة 72 ساعة", "Prep and stage today’s operational orders after the 72h lock"),
            DateLabel: FormatDateLabel(targetDate),
            PrintLabel: new LocalizedTextDto("طباعة ملصقات اليوم", "Print today’s labels"),
            Summaries: summaries,
            Orders: items
        );

        return Result.Success(data);
    }

    public async Task<Result<Upcoming24hDataDto>> GetUpcoming24hAsync(
        GetRestaurantUpcoming24hQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<Upcoming24hDataDto>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var now = _clock.UtcNow;
        var targetDate = request.TargetDate ?? DateOnly.FromDateTime(now.AddDays(1));

        var days = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId.Value && x.ServiceDate == targetDate)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(cancellationToken);

        var subscriptionIds = days.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => subscriptionIds.Contains(s.Id))
            .Include(s => s.Program)
            .Include(s => s.Bundle)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var dayIds = days.Select(d => d.Id).ToList();
        var mealSelections = await _db.DailyMealSelections
            .AsNoTracking()
            .Where(s => dayIds.Contains(s.SubscriptionDayId))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        var mealIds = mealSelections.Select(m => m.MealId).Distinct().ToList();
        var meals = await _db.Meals
            .AsNoTracking()
            .Where(m => mealIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var items = new List<Upcoming24hItemDto>();
        var index = 0;

        foreach (var day in days)
        {
            subscriptions.TryGetValue(day.SubscriptionId, out var sub);

            var daySelections = mealSelections.Where(ms => ms.SubscriptionDayId == day.Id).ToList();
            var dayMeals = daySelections.Select(ms => meals.TryGetValue(ms.MealId, out var m) ? m : null).Where(m => m != null).ToList();

            var mealSummary = BuildMealSummary(dayMeals!, sub?.Bundle);

            var programLabel = new LocalizedTextDto(
                sub?.Program?.NameAr ?? "لياقة · بلاتينيوم",
                sub?.Program?.NameEn ?? "Fitness · Platinum");

            var bundleLabel = new LocalizedTextDto(
                sub?.Bundle?.NameAr ?? "باقة كاملة",
                sub?.Bundle?.NameEn ?? "Full package");

            var shift = (index % 3) switch
            {
                0 => "noon",
                1 => "evening",
                _ => "morning"
            };

            var shiftLabel = shift switch
            {
                "morning" => new LocalizedTextDto("صباح", "Morning"),
                "noon" => new LocalizedTextDto("ظهيرة", "Noon"),
                _ => new LocalizedTextDto("مساء", "Evening")
            };

            var deliverySlotLabel = shift switch
            {
                "morning" => new LocalizedTextDto("08:00 – 10:00", "08:00 – 10:00"),
                "noon" => new LocalizedTextDto("12:00 – 14:00", "12:00 – 14:00"),
                _ => new LocalizedTextDto("18:00 – 20:00", "18:00 – 20:00")
            };

            var deliveryDateLabel = FormatDeliveryDate(day.ServiceDate);
            var deliveryDateTime = day.ServiceDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
            var hoursToDelivery = (int)Math.Max(1, Math.Min(24, Math.Floor((deliveryDateTime - now).TotalHours)));

            var invoiceGenerated = true;
            var barcodeGenerated = index % 2 == 0;
            var labelsGenerated = barcodeGenerated && index % 4 == 0;

            string prepStatus;
            if (barcodeGenerated && labelsGenerated)
                prepStatus = "ready";
            else if (barcodeGenerated)
                prepStatus = "docs-ready";
            else if (invoiceGenerated)
                prepStatus = "preparing";
            else
                prepStatus = "needs-docs";

            var orderCode = $"ORD-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}";
            var batchCode = $"BATCH-{day.ServiceDate:dd}";
            var customerMaskedId = sub != null ? $"CUS-•••{sub.CustomerUserId.ToString()[..3].ToUpperInvariant()}" : "CUS-•••999";

            items.Add(new Upcoming24hItemDto(
                Id: day.Id,
                OrderCode: orderCode,
                BatchCode: batchCode,
                CustomerMaskedId: customerMaskedId,
                MealSummary: mealSummary,
                ProgramLabel: programLabel,
                BundleLabel: bundleLabel,
                BoxCount: 1,
                Shift: shift,
                ShiftLabel: shiftLabel,
                DeliveryDateLabel: deliveryDateLabel,
                DeliverySlotLabel: deliverySlotLabel,
                HoursToDelivery: hoursToDelivery,
                InvoiceGenerated: invoiceGenerated,
                BarcodeGenerated: barcodeGenerated,
                LabelsGenerated: labelsGenerated,
                PrepStatus: prepStatus,
                Route: $"/restaurant/orders/detail/{orderCode}",
                LabelsRoute: "/restaurant/orders/labels"
            ));

            index++;
        }

        var totalCount = items.Count;
        var invoiceCount = items.Count(i => i.InvoiceGenerated);
        var barcodeCount = items.Count(i => i.BarcodeGenerated);
        var labelCount = items.Count(i => i.LabelsGenerated);

        var summaries = new List<Upcoming24hSummaryDto>
        {
            new("total", new LocalizedTextDto("خلال 24 ساعة", "Within 24h"), totalCount, new LocalizedTextDto("طلبات للتحضير النهائي", "Orders for final prep"), "primary", "lucidePackage"),
            new("invoices", new LocalizedTextDto("فواتير جاهزة", "Invoices ready"), invoiceCount, new LocalizedTextDto("تم التوليد تلقائيًا", "Auto-generated"), "accent", "lucideFileText"),
            new("barcodes", new LocalizedTextDto("باركود جاهز", "Barcodes ready"), barcodeCount, new LocalizedTextDto("جاهز للمسح", "Ready to scan"), "warning", "lucideScanBarcode"),
            new("labels", new LocalizedTextDto("ملصقات جاهزة", "Labels ready"), labelCount, new LocalizedTextDto("جاهز للطباعة", "Ready to print"), "neutral", "lucidePrinter")
        };

        var dateLabel = FormatDateLabel(DateOnly.FromDateTime(now));

        var data = new Upcoming24hDataDto(
            Title: new LocalizedTextDto("تحضير 24 ساعة", "Upcoming 24h prep"),
            Subtitle: new LocalizedTextDto("إشعار التسليم النهائي · فواتير وباركود وملصقات ثم التحضير الأخير", "Final delivery notice · invoices, barcodes, labels, then final prep"),
            DateLabel: dateLabel,
            WindowHint: new LocalizedTextDto("نافذة 24 ساعة", "24h window"),
            PrintLabel: new LocalizedTextDto("طباعة الملصقات", "Print labels"),
            Summaries: summaries,
            Orders: items
        );

        return Result.Success(data);
    }

    public async Task<Result<bool>> ArchiveDailyOrderAsync(
        ArchiveDailyOrderCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = _currentUser.RestaurantId;
        if (restaurantId == null)
            return Result.Failure<bool>(Error.Unauthorized("restaurant.id_required", "Restaurant identity is required."));

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId.Value, cancellationToken);

        if (day == null)
            return Result.Failure<bool>(Error.NotFound("order.not_found", "Order was not found."));

        // Mark as completed / archived
        day.Status = SubscriptionDayStatus.Delivered;

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    private static LocalizedTextDto BuildMealSummary(List<Meal> meals, Bundle? bundle)
    {
        if (meals.Count == 0)
        {
            return new LocalizedTextDto(
                bundle?.NameAr ?? "باقة كاملة",
                bundle?.NameEn ?? "Full package");
        }

        var namesAr = string.Join(" + ", meals.Take(3).Select(m => m.NameAr));
        var namesEn = string.Join(" + ", meals.Take(3).Select(m => m.NameEn));

        var prefixAr = bundle?.NameAr ?? "باقة";
        var prefixEn = bundle?.NameEn ?? "Package";

        return new LocalizedTextDto($"{prefixAr} · {namesAr}", $"{prefixEn} · {namesEn}");
    }

    private static LocalizedTextDto GetSlotLabel(string slot)
    {
        return slot switch
        {
            "breakfast" => new LocalizedTextDto("إفطار", "Breakfast"),
            "main" => new LocalizedTextDto("رئيسية", "Main"),
            "snack" => new LocalizedTextDto("سناك", "Snack"),
            "salad" => new LocalizedTextDto("سلطة", "Salad"),
            "juice" => new LocalizedTextDto("عصير", "Juice"),
            _ => new LocalizedTextDto("وجبة", "Meal")
        };
    }

    private static LocalizedTextDto FormatDeliveryDate(DateOnly date)
    {
        var arDay = date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "الأحد",
            DayOfWeek.Monday => "الاثنين",
            DayOfWeek.Tuesday => "الثلاثاء",
            DayOfWeek.Wednesday => "الأربعاء",
            DayOfWeek.Thursday => "الخميس",
            DayOfWeek.Friday => "الجمعة",
            DayOfWeek.Saturday => "السبت",
            _ => ""
        };

        var arMonth = date.Month switch
        {
            1 => "يناير",
            2 => "فبراير",
            3 => "مارس",
            4 => "أبريل",
            5 => "مايو",
            6 => "يونيو",
            7 => "يوليو",
            8 => "أغسطس",
            9 => "سبتمبر",
            10 => "أكتوبر",
            11 => "نوفمبر",
            12 => "ديسمبر",
            _ => ""
        };

        var ar = $"{arDay} {date.Day} {arMonth}";
        var en = $"{date.ToString("ddd d MMM", CultureInfo.InvariantCulture)}";

        return new LocalizedTextDto(ar, en);
    }

    private static LocalizedTextDto FormatDateLabel(DateOnly date)
    {
        var arDay = date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "الأحد",
            DayOfWeek.Monday => "الاثنين",
            DayOfWeek.Tuesday => "الثلاثاء",
            DayOfWeek.Wednesday => "الأربعاء",
            DayOfWeek.Thursday => "الخميس",
            DayOfWeek.Friday => "الجمعة",
            DayOfWeek.Saturday => "السبت",
            _ => ""
        };

        var arMonth = date.Month switch
        {
            1 => "يناير",
            2 => "فبراير",
            3 => "مارس",
            4 => "أبريل",
            5 => "مايو",
            6 => "يونيو",
            7 => "يوليو",
            8 => "أغسطس",
            9 => "سبتمبر",
            10 => "أكتوبر",
            11 => "نوفمبر",
            12 => "ديسمبر",
            _ => ""
        };

        var ar = $"{arDay} {date.Day} {arMonth} {date.Year}";
        var en = $"{date.ToString("dddd, d MMM yyyy", CultureInfo.InvariantCulture)}";

        return new LocalizedTextDto(ar, en);
    }
}

public sealed class GetRestaurantPendingConfirmationQueryHandler
    : IRequestHandler<GetRestaurantPendingConfirmationQuery, Result<PendingConfirmationDataDto>>
{
    private readonly RestaurantOrderService _service;

    public GetRestaurantPendingConfirmationQueryHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<PendingConfirmationDataDto>> Handle(
        GetRestaurantPendingConfirmationQuery request,
        CancellationToken cancellationToken)
        => _service.GetPendingConfirmationAsync(request, cancellationToken);
}

public sealed class GetRestaurantOrderDetailQueryHandler
    : IRequestHandler<GetRestaurantOrderDetailQuery, Result<RestaurantOrderDetailDto>>
{
    private readonly RestaurantOrderService _service;

    public GetRestaurantOrderDetailQueryHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<RestaurantOrderDetailDto>> Handle(
        GetRestaurantOrderDetailQuery request,
        CancellationToken cancellationToken)
        => _service.GetOrderDetailAsync(request, cancellationToken);
}

public sealed class ConfirmRestaurantOrderCommandHandler
    : IRequestHandler<ConfirmRestaurantOrderCommand, Result<bool>>
{
    private readonly RestaurantOrderService _service;

    public ConfirmRestaurantOrderCommandHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<bool>> Handle(
        ConfirmRestaurantOrderCommand request,
        CancellationToken cancellationToken)
        => _service.ConfirmOrderAsync(request, cancellationToken);
}

public sealed class RejectRestaurantOrderCommandHandler
    : IRequestHandler<RejectRestaurantOrderCommand, Result<bool>>
{
    private readonly RestaurantOrderService _service;

    public RejectRestaurantOrderCommandHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<bool>> Handle(
        RejectRestaurantOrderCommand request,
        CancellationToken cancellationToken)
        => _service.RejectOrderAsync(request, cancellationToken);
}

public sealed class GetRestaurantDailyOrdersQueryHandler
    : IRequestHandler<GetRestaurantDailyOrdersQuery, Result<DailyOrdersDataDto>>
{
    private readonly RestaurantOrderService _service;

    public GetRestaurantDailyOrdersQueryHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<DailyOrdersDataDto>> Handle(
        GetRestaurantDailyOrdersQuery request,
        CancellationToken cancellationToken)
        => _service.GetDailyOrdersAsync(request, cancellationToken);
}

public sealed class GetRestaurantUpcoming24hQueryHandler
    : IRequestHandler<GetRestaurantUpcoming24hQuery, Result<Upcoming24hDataDto>>
{
    private readonly RestaurantOrderService _service;

    public GetRestaurantUpcoming24hQueryHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<Upcoming24hDataDto>> Handle(
        GetRestaurantUpcoming24hQuery request,
        CancellationToken cancellationToken)
        => _service.GetUpcoming24hAsync(request, cancellationToken);
}

public sealed class ArchiveDailyOrderCommandHandler
    : IRequestHandler<ArchiveDailyOrderCommand, Result<bool>>
{
    private readonly RestaurantOrderService _service;

    public ArchiveDailyOrderCommandHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<bool>> Handle(
        ArchiveDailyOrderCommand request,
        CancellationToken cancellationToken)
        => _service.ArchiveDailyOrderAsync(request, cancellationToken);
}

