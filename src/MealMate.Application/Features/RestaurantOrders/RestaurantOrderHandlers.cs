using System.Globalization;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using OutboxMessage = MealMate.Domain.Common.OutboxMessage;
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
    private readonly ICustomerPushNotifier _customerPush;
    private readonly IRestaurantOrderNotifier _restaurantNotifier;
    private readonly IBatchDispatchNotifier _batchDispatchNotifier;
    private readonly MealMate.Application.Features.CustomerCheckout.IOrderReplacementOrchestrator? _replacementOrchestrator;
    private readonly MealMate.Application.Features.PrepDocuments.IPrepDocumentsGenerator? _prepDocumentsGenerator;

    public RestaurantOrderService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ICustomerPushNotifier? customerPush = null,
        IRestaurantOrderNotifier? restaurantNotifier = null,
        MealMate.Application.Features.CustomerCheckout.IOrderReplacementOrchestrator? replacementOrchestrator = null,
        MealMate.Application.Features.PrepDocuments.IPrepDocumentsGenerator? prepDocumentsGenerator = null,
        IBatchDispatchNotifier? batchDispatchNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _customerPush = customerPush ?? NullCustomerPushNotifier.Instance;
        _restaurantNotifier = restaurantNotifier ?? NullRestaurantOrderNotifier.Instance;
        _batchDispatchNotifier = batchDispatchNotifier ?? NullBatchDispatchNotifier.Instance;
        _replacementOrchestrator = replacementOrchestrator;
        _prepDocumentsGenerator = prepDocumentsGenerator;
    }

    private async Task<Guid> ResolveRestaurantIdAsync(Guid? overrideId, CancellationToken cancellationToken)
    {
        if (overrideId.HasValue && overrideId.Value != Guid.Empty)
        {
            if (_currentUser.UserType == UserType.Admin || _currentUser.IsInRole("Admin"))
            {
                return overrideId.Value;
            }

            if (_currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value == overrideId.Value)
            {
                return overrideId.Value;
            }

            return Guid.Empty;
        }

        if (_currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value != Guid.Empty)
            return _currentUser.RestaurantId.Value;

        if (_currentUser.UserId.HasValue && _currentUser.UserId.Value != Guid.Empty)
        {
            var userProfile = await _db.RestaurantProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == _currentUser.UserId.Value, cancellationToken);
            if (userProfile != null) return userProfile.Id;
        }

        return Guid.Empty;
    }

    public async Task<Result<PendingConfirmationDataDto>> GetPendingConfirmationAsync(
        GetRestaurantPendingConfirmationQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.RestaurantId.HasValue && (!_currentUser.UserId.HasValue || _currentUser.UserId == Guid.Empty))
            return Result.Failure<PendingConfirmationDataDto>(Error.Unauthorized("restaurant.unauthorized", "Restaurant identity is required."));

        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<PendingConfirmationDataDto>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var now = _clock.UtcNow;

        // Query SubscriptionDays assigned to this restaurant that are in locked 72h window or sent/confirmed
        var days = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId &&
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
        var receivedOrders = await _db.RestaurantDailyOrders.AsNoTracking()
            .Where(x => dayIds.Contains(x.SubscriptionDayId) && x.RestaurantId == restaurantId && x.Status == OrderStatus.AwaitingRestaurantConfirmation)
            .ToListAsync(cancellationToken);
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

        var regionIds = days.Where(d => d.DeliveryRegionId.HasValue).Select(d => d.DeliveryRegionId!.Value).Distinct().ToList();
        var deliveryRegions = regionIds.Count == 0
            ? new Dictionary<Guid, RestaurantServiceRegion>()
            : await _db.RestaurantServiceRegions.AsNoTracking()
                .Where(r => r.RestaurantId == restaurantId && r.IsActive && regionIds.Contains(r.RegionId))
                .ToDictionaryAsync(r => r.RegionId, cancellationToken);

        var confirmationMinutes = (await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(_db, cancellationToken)).Cutoff.EffectiveConfirmationMinutes;
        var items = new List<PendingConfirmationItemDto>();

        foreach (var day in days)
        {
            subscriptions.TryGetValue(day.SubscriptionId, out var sub);

            var lockTime = receivedOrders.Where(x => x.SubscriptionDayId == day.Id).Select(x => (DateTime?)x.CreatedAtUtc).Max() ?? day.LockAtUtc ?? day.CreatedAtUtc;
            var deadlineTime = lockTime.AddMinutes(confirmationMinutes);
            var hoursLeft = (int)Math.Floor((deadlineTime - now).TotalHours);

            var isConfirmed = day.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                              day.Status == SubscriptionDayStatus.Preparing;

            var isReassigned = day.Status == SubscriptionDayStatus.ReplacementRequired;

            string urgency;
            if (isConfirmed)
            {
                urgency = "normal";
            }
            else if (deadlineTime <= now)
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
            var deliveryWindow = GetDeliveryWindow(
                day.DeliveryRegionId.HasValue && deliveryRegions.TryGetValue(day.DeliveryRegionId.Value, out var region)
                    ? region
                    : null);
            var deliverySlotLabel = deliveryWindow.SlotLabel;

            var receivedHours = (int)Math.Max(1, (now - lockTime).TotalHours);
            var receivedAtLabel = new LocalizedTextDto($"منذ {receivedHours} ساعة", $"{receivedHours}h ago");

            LocalizedTextDto deadlineLabel;
            if (isConfirmed)
            {
                deadlineLabel = new LocalizedTextDto("تم التأكيد", "Confirmed");
            }
            else if (deadlineTime <= now)
            {
                var overdueHours = Math.Abs(hoursLeft);
                deadlineLabel = new LocalizedTextDto(
                    overdueHours == 0 ? "انتهى الموعد" : $"متأخر {overdueHours} ساعات",
                    overdueHours == 0 ? "Deadline passed" : $"{overdueHours}h overdue");
            }
            else
            {
                var minutesLeft = (int)Math.Ceiling((deadlineTime - now).TotalMinutes);
                deadlineLabel = hoursLeft < 1 ? new LocalizedTextDto($"متبقي {minutesLeft} دقيقة", $"{minutesLeft} min left")
                    : new LocalizedTextDto($"متبقي {hoursLeft} ساعات", $"{hoursLeft}h left");
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
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<RestaurantOrderDetailDto>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var queryKey = request.OrderCodeOrId.Trim();
        Guid parsedGuid = Guid.Empty;
        var isGuid = Guid.TryParse(queryKey, out parsedGuid);

        SubscriptionDay? day;
        if (isGuid)
        {
            day = await _db.SubscriptionDays
                .AsNoTracking()
                .Include(x => x.Calendar)
                .FirstOrDefaultAsync(x => x.Id == parsedGuid && x.RestaurantId == restaurantId, cancellationToken);
        }
        else
        {
            // Match orderCode pattern
            var days = await _db.SubscriptionDays
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
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

        var lockTimeString = (day.LockAtUtc ?? day.CreatedAtUtc).ToString("yyyy-MM-dd HH:mm");
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
            new("fact-window", new LocalizedTextDto("نافذة التوصيل", "Delivery Window"), new LocalizedTextDto("غير محدد", "Not scheduled"), "lucideClock"),
            new("fact-driver", new LocalizedTextDto("المندوب المعين", "Assigned Driver"), new LocalizedTextDto("لم يتم التعيين", "Not assigned"), "lucideTruck")
        };

        var barcodeCode = await _db.MealBarcodes.AsNoTracking()
            .Where(x => x.SubscriptionDayId == day.Id && !x.IsRevoked)
            .Select(x => x.BarcodeValue)
            .FirstOrDefaultAsync(cancellationToken);

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
            Shift: "unscheduled",
            ShiftLabel: new LocalizedTextDto("لم يحدد", "Unscheduled"),
            Title: new LocalizedTextDto($"تفاصيل الطلب {orderCode}", $"Order Detail {orderCode}"),
            Subtitle: new LocalizedTextDto("طلب باقة اشتراك يومية · مطبخ التجهيز الرئيسي", "Daily subscription box order · Main prep kitchen"),
            CustomerMaskedId: customerMaskedId,
            DeliveryDateLabel: FormatDeliveryDate(day.ServiceDate),
            DeliverySlotLabel: new LocalizedTextDto("موعد التسليم غير محدد", "Delivery time not scheduled"),
            ProgramLabel: new LocalizedTextDto(subscription?.Program?.NameAr ?? "برنامج اللياقة", subscription?.Program?.NameEn ?? "Fitness Program"),
            TierLabel: new LocalizedTextDto("بلاتينيوم", "Platinum"),
            BundleKind: "full",
            BundleLabel: new LocalizedTextDto(subscription?.Bundle?.NameAr ?? "باقة كاملة", subscription?.Bundle?.NameEn ?? "Full Package"),
            BoxCompositionLabel: new LocalizedTextDto("مكونات وجبات البوكس المعتمدة", "Approved box meal composition"),
            BoxCount: 1,
            DriverCode: null,
            BarcodeCode: barcodeCode,
            WindowLabel: new LocalizedTextDto("غير محدد", "Not scheduled"),
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
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<bool>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId, cancellationToken);

        if (day == null)
            return Result.Failure<bool>(Error.NotFound(MessageCodes.Orders.NotFound, "Order was not found."));

        if (day.Status is SubscriptionDayStatus.ConfirmedByRestaurant or SubscriptionDayStatus.Preparing)
        {
            return Result.Success(true);
        }

        if (day.Status is not (SubscriptionDayStatus.Locked72h or SubscriptionDayStatus.SentToRestaurant))
            return Result.Failure<bool>(Error.Conflict(MessageCodes.Orders.InvalidStatus, "Order is not awaiting restaurant confirmation."));

        day.Status = SubscriptionDayStatus.ConfirmedByRestaurant;
        day.ConfirmedAtUtc = _clock.UtcNow;

        var dailyOrder = await _db.RestaurantDailyOrders
            .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id, cancellationToken);
        if (dailyOrder == null)
        {
            _db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = day.Id,
                ServiceDate = day.ServiceDate,
                Status = OrderStatus.RestaurantConfirmed,
                ConfirmedAtUtc = _clock.UtcNow,
                BoxCount = 1,
                CreatedAtUtc = _clock.UtcNow
            });
        }
        else
        {
            dailyOrder.Status = OrderStatus.RestaurantConfirmed;
            dailyOrder.ConfirmedAtUtc = _clock.UtcNow;
            dailyOrder.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var customerUserId = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Id == day.SubscriptionId)
            .Select(s => s.CustomerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (customerUserId != Guid.Empty)
        {
            await _customerPush.NotifyOrderConfirmedByRestaurantAsync(
                customerUserId,
                day.Id,
                day.ServiceDate,
                cancellationToken);
        }

        return Result.Success(true);
    }

    public async Task<Result<bool>> RejectOrderAsync(
        RejectRestaurantOrderCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<bool>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId, cancellationToken);

        if (day == null)
            return Result.Failure<bool>(Error.NotFound(MessageCodes.Orders.NotFound, "Order was not found."));

        if (day.Status is not (SubscriptionDayStatus.Locked72h or SubscriptionDayStatus.SentToRestaurant))
            return Result.Failure<bool>(Error.Conflict(MessageCodes.Orders.InvalidStatus, "Order is not awaiting restaurant confirmation."));

        day.Status = SubscriptionDayStatus.ReplacementRequired;

        var reasonCode = request.ReasonCode ?? "RESTAURANT_REJECTED";
        var reasonText = request.Reason ?? "Rejected by kitchen";

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = day.SubscriptionId,
            OperationType = "RESTAURANT_CONFIRMATION_REJECTED",
            PreviousStatus = SubscriptionStatus.Active,
            NewStatus = SubscriptionStatus.Active,
            Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; RestaurantId={restaurantId}; ReasonCode={reasonCode}; Note={reasonText}",
            PerformedByUserId = _currentUser.UserId ?? Guid.Empty,
            PerformedByUserType = UserType.Restaurant,
            TimestampUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        if (_replacementOrchestrator != null)
        {
            await _replacementOrchestrator.AttemptAutoReplacementAsync(day.Id, cancellationToken);
        }

        return Result.Success(true);
    }

    public async Task<Result<DailyOrdersDataDto>> GetDailyOrdersAsync(
        GetRestaurantDailyOrdersQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.RestaurantId.HasValue && (!_currentUser.UserId.HasValue || _currentUser.UserId == Guid.Empty))
            return Result.Failure<DailyOrdersDataDto>(Error.Unauthorized("restaurant.unauthorized", "Restaurant identity is required."));

        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<DailyOrdersDataDto>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var now = _clock.UtcNow;
        var targetDate = request.Date ?? DateOnly.FromDateTime(now);

        var query = _db.SubscriptionDays
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId
                        && (x.Status == SubscriptionDayStatus.ConfirmedByRestaurant
                            || x.Status == SubscriptionDayStatus.Preparing
                            || x.Status == SubscriptionDayStatus.OutForDelivery
                            || x.Status == SubscriptionDayStatus.Delivered));

        if (request.Date.HasValue)
        {
            query = query.Where(x => x.ServiceDate == request.Date.Value);
        }
        else
        {
            query = query.Where(x => x.ServiceDate == targetDate || x.Status != SubscriptionDayStatus.Delivered);
        }

        var days = await query
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
        var dailyOrders = await _db.RestaurantDailyOrders
            .AsNoTracking()
            .Where(r => dayIds.Contains(r.SubscriptionDayId))
            .ToDictionaryAsync(r => r.SubscriptionDayId, cancellationToken);

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

            dailyOrders.TryGetValue(day.Id, out var dailyOrder);

            string status;
            if (dailyOrder != null)
            {
                status = dailyOrder.Status switch
                {
                    OrderStatus.RestaurantConfirmed => "preparing",
                    OrderStatus.Preparing => "preparing",
                    OrderStatus.ReadyForPickup => "ready",
                    OrderStatus.OnTheWay => "waiting-driver",
                    OrderStatus.Delivered => "completed",
                    _ => "preparing"
                };
            }
            else
            {
                status = day.Status switch
                {
                    SubscriptionDayStatus.ConfirmedByRestaurant or SubscriptionDayStatus.Preparing => "preparing",
                    SubscriptionDayStatus.OutForDelivery => "waiting-driver",
                    SubscriptionDayStatus.Delivered => "completed",
                    _ => "preparing"
                };
            }

            const string shift = "unscheduled";
            var shiftLabel = new LocalizedTextDto("لم يحدد", "Unscheduled");

            var orderCode = $"ORD-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}";
            var batchCode = $"BATCH-{day.ServiceDate:dd}";

            LocalizedTextDto? driverLabel = null;

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
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<Upcoming24hDataDto>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var tomorrow = today.AddDays(1);
        var targetDate = request.TargetDate;

        var query = _db.SubscriptionDays
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId
                        && (x.Status == SubscriptionDayStatus.ConfirmedByRestaurant
                            || x.Status == SubscriptionDayStatus.Preparing
                            || x.Status == SubscriptionDayStatus.OutForDelivery
                            || x.Status == SubscriptionDayStatus.Delivered));

        if (targetDate.HasValue)
        {
            query = query.Where(x => x.ServiceDate == targetDate.Value);
        }
        else
        {
            query = query.Where(x => x.ServiceDate == today || x.ServiceDate == tomorrow);
        }

        var days = await query
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

        var barcodesByDay = dayIds.Count == 0
            ? new Dictionary<Guid, MealBarcode>()
            : await _db.MealBarcodes.AsNoTracking()
                .Where(x => dayIds.Contains(x.SubscriptionDayId) && !x.IsRevoked)
                .ToDictionaryAsync(x => x.SubscriptionDayId, cancellationToken);
        var barcodeDayIds = barcodesByDay.Keys.ToHashSet();
        var printedBarcodeDayIds = barcodesByDay
            .Where(x => x.Value.Status >= MealBarcodeStatus.Printed)
            .Select(x => x.Key)
            .ToHashSet();

        var invoiceDayIds = dayIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.MealInvoices.AsNoTracking()
                .Where(x => x.SubscriptionDayId != null && dayIds.Contains(x.SubscriptionDayId.Value))
                .Select(x => x.SubscriptionDayId!.Value)
                .ToListAsync(cancellationToken)).ToHashSet();

        var labels = dayIds.Count == 0
            ? new List<(Guid SubscriptionDayId, MealLabelKind Kind)>()
            : (await _db.MealLabels.AsNoTracking()
                .Where(x => dayIds.Contains(x.SubscriptionDayId))
                .Select(x => new { x.SubscriptionDayId, x.Kind })
                .ToListAsync(cancellationToken))
              .Select(x => (x.SubscriptionDayId, x.Kind))
              .ToList();

        var boxLabelDayIds = labels.Where(x => x.Kind == MealLabelKind.Box).Select(x => x.SubscriptionDayId).ToHashSet();
        var mealLabelCounts = labels
            .Where(x => x.Kind == MealLabelKind.Meal)
            .GroupBy(x => x.SubscriptionDayId)
            .ToDictionary(g => g.Key, g => g.Count());

        var items = new List<Upcoming24hItemDto>();

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

            const string shift = "unscheduled";
            var shiftLabel = new LocalizedTextDto("لم يحدد", "Unscheduled");
            var deliverySlotLabel = new LocalizedTextDto("لم يحدد", "Unscheduled");

            var deliveryDateLabel = FormatDeliveryDate(day.ServiceDate);
            var deliveryDateTime = day.ServiceDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
            var hoursToDelivery = (int)Math.Max(1, Math.Min(24, Math.Floor((deliveryDateTime - now).TotalHours)));

            var invoiceGenerated = invoiceDayIds.Contains(day.Id);
            var barcodeGenerated = barcodeDayIds.Contains(day.Id);
            var mealLabelCount = mealLabelCounts.GetValueOrDefault(day.Id);
            var labelsGenerated = boxLabelDayIds.Contains(day.Id) && mealLabelCount == daySelections.Count;

            var isPrinted = printedBarcodeDayIds.Contains(day.Id);

            string prepStatus;
            if (isPrinted)
                prepStatus = "printed";
            else if (barcodeGenerated && labelsGenerated && invoiceGenerated)
                prepStatus = "ready";
            else if (barcodeGenerated || invoiceGenerated)
                prepStatus = "docs-ready";
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
                LabelsRoute: "/restaurant/orders/labels",
                IsPrinted: isPrinted
            ));

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

    public async Task<Result<RestaurantPrepDocumentsDto>> GetPrepDocumentsAsync(
        GetRestaurantPrepDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<RestaurantPrepDocumentsDto>(
                Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var day = await _db.SubscriptionDays.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == request.DayId && x.RestaurantId == restaurantId,
                cancellationToken);
        if (day is null)
            return Result.Failure<RestaurantPrepDocumentsDto>(
                Error.NotFound("order.not_found", "Order was not found."));

        var barcode = await _db.MealBarcodes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, cancellationToken);

        var invoice = await _db.MealInvoices.AsNoTracking()
            .Where(x => x.SubscriptionDayId == day.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var labels = await _db.MealLabels.AsNoTracking()
            .Where(x => x.SubscriptionDayId == day.Id)
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var boxLabel = labels.FirstOrDefault(x => x.Kind == MealLabelKind.Box);
        var mealLabels = labels
            .Where(x => x.Kind == MealLabelKind.Meal)
            .Select(x => MapLabelPart(x))
            .ToList();

        if (_prepDocumentsGenerator != null && (barcode == null || invoice == null || boxLabel == null))
        {
            var genResult = await _prepDocumentsGenerator.GenerateForDayAsync(day.Id, cancellationToken: cancellationToken);
            if (genResult.IsFailure && genResult.Error.Code == "prep_documents.missing_template")
            {
                var seedHandler = new MealMate.Application.Features.DocumentTemplates.SeedDocumentTemplatesCommandHandler(_db, _clock);
                await seedHandler.Handle(new MealMate.Application.Features.DocumentTemplates.SeedDocumentTemplatesCommand(), cancellationToken);
                genResult = await _prepDocumentsGenerator.GenerateForDayAsync(day.Id, cancellationToken: cancellationToken);
            }

            if (genResult.IsSuccess)
            {
                barcode = await _db.MealBarcodes.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, cancellationToken);

                invoice = await _db.MealInvoices.AsNoTracking()
                    .Where(x => x.SubscriptionDayId == day.Id)
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                labels = await _db.MealLabels.AsNoTracking()
                    .Where(x => x.SubscriptionDayId == day.Id)
                    .OrderBy(x => x.Kind)
                    .ThenBy(x => x.CreatedAtUtc)
                    .ToListAsync(cancellationToken);

                boxLabel = labels.FirstOrDefault(x => x.Kind == MealLabelKind.Box);
                mealLabels = labels
                    .Where(x => x.Kind == MealLabelKind.Meal)
                    .Select(x => MapLabelPart(x))
                    .ToList();
            }
        }

        return Result.Success(new RestaurantPrepDocumentsDto(
            day.Id,
            barcode?.BarcodeValue ?? invoice?.BarcodeValue,
            invoice is null ? null : MapInvoicePart(invoice),
            boxLabel is null ? null : MapLabelPart(boxLabel),
            mealLabels));
    }

    private static RestaurantPrepDocumentPartDto MapInvoicePart(MealInvoice invoice)
        => new(
            invoice.Id,
            "invoice",
            null,
            invoice.SnapshotJson ?? "{}",
            ExtractEmbeddedLayout(invoice.SnapshotJson),
            invoice.TemplateId,
            invoice.TemplateVersion);

    private static RestaurantPrepDocumentPartDto MapLabelPart(MealLabel label)
        => new(
            label.Id,
            label.Kind == MealLabelKind.Box ? "box_label" : "meal_label",
            label.MealId,
            label.LabelPayloadJson,
            ExtractEmbeddedLayout(label.LabelPayloadJson),
            label.TemplateId,
            label.TemplateVersion);

    private static string? ExtractEmbeddedLayout(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (doc.RootElement.TryGetProperty("embeddedLayoutJson", out var layout)
                && layout.ValueKind == JsonValueKind.String)
                return layout.GetString();
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public async Task<Result<bool>> ArchiveDailyOrderAsync(
        ArchiveDailyOrderCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<bool>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId, cancellationToken);

        if (day == null)
            return Result.Failure<bool>(Error.NotFound("order.not_found", "Order was not found."));

        if (day.Status != SubscriptionDayStatus.Delivered)
            return Result.Failure<bool>(Error.Conflict(MessageCodes.Orders.InvalidStatus, "Only delivered orders can be archived."));

        // Mark as completed / archived
        day.Status = SubscriptionDayStatus.Delivered;

        await _db.SaveChangesAsync(cancellationToken);

        var customerUserId = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.Id == day.SubscriptionId)
            .Select(x => x.CustomerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (customerUserId != Guid.Empty)
        {
            await _customerPush.NotifyOrderDeliveredAsync(
                customerUserId,
                day.Id,
                day.ServiceDate,
                cancellationToken);
        }

        return Result.Success(true);
    }

    public async Task<Result<bool>> MarkOrderBarcodePrintedAsync(
        MarkOrderBarcodePrintedCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<bool>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var day = await _db.SubscriptionDays.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId, cancellationToken);
        if (day is null)
            return Result.Failure<bool>(Error.NotFound(MessageCodes.Orders.NotFound, "Order was not found."));

        var barcode = await _db.MealBarcodes
            .FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id && !x.IsRevoked, cancellationToken);
        if (barcode is null)
            return Result.Failure<bool>(Error.NotFound("order.barcode_not_found", "The order barcode has not been generated."));

        if (barcode.Status == MealBarcodeStatus.Generated)
        {
            var transition = barcode.TransitionTo(MealBarcodeStatus.Printed);
            if (transition.IsFailure)
                return Result.Failure<bool>(Error.Conflict("order.barcode_invalid_status", "Barcode cannot transition to printed."));
            barcode.PrintedAtUtc ??= _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

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

    private static (string Shift, LocalizedTextDto ShiftLabel, LocalizedTextDto SlotLabel) GetDeliveryWindow(
        RestaurantServiceRegion? region)
    {
        if (region is null)
        {
            return (
                "unscheduled",
                new LocalizedTextDto("غير مجدول", "Unscheduled"),
                new LocalizedTextDto("لم يُحدد موعد التسليم", "Delivery time not scheduled"));
        }

        var start = region.DeliveryWindowStart;
        var end = region.DeliveryWindowEnd;
        var shift = start.Hour < 11 ? "morning" : start.Hour < 16 ? "noon" : "evening";
        var shiftLabel = shift switch
        {
            "morning" => new LocalizedTextDto("صباحي", "Morning"),
            "noon" => new LocalizedTextDto("ظهيرة", "Afternoon"),
            _ => new LocalizedTextDto("مسائي", "Evening")
        };

        var slotLabel = new LocalizedTextDto(
            $"من {start.ToString("HH:mm", CultureInfo.InvariantCulture)} إلى {end.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            $"{start.ToString("HH:mm", CultureInfo.InvariantCulture)}–{end.ToString("HH:mm", CultureInfo.InvariantCulture)}");

        return (shift, shiftLabel, slotLabel);
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

    public async Task<Result<int>> DispatchDailyOrdersAsync(
        DispatchRestaurantDailyOrdersCommand request,
        MealMate.Application.Features.CustomerCalendar.ISubscriptionDayDispatchService dispatchService,
        MealMate.Application.Features.CustomerCalendar.CustomerCalendarEngine calendarEngine,
        CancellationToken cancellationToken)
    {
        var targetRestaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);
        if (targetRestaurantId == Guid.Empty)
            return Result.Failure<int>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var targetDate = request.TargetDate ?? DateOnly.FromDateTime(_clock.UtcNow);

        var days = await _db.SubscriptionDays
            .Where(d => (d.RestaurantId == targetRestaurantId || d.RestaurantId == null)
                        && d.ServiceDate == targetDate
                        && d.Status != SubscriptionDayStatus.Cancelled
                        && d.Status != SubscriptionDayStatus.Frozen)
            .ToListAsync(cancellationToken);

        if (days.Count == 0)
        {
            var activeSubscriptions = await _db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active && s.StartDate <= targetDate && s.EndDate >= targetDate)
                .Take(10)
                .ToListAsync(cancellationToken);

            foreach (var sub in activeSubscriptions)
            {
                var existingDay = await _db.SubscriptionDays
                    .FirstOrDefaultAsync(d => d.SubscriptionId == sub.Id && d.ServiceDate == targetDate, cancellationToken);

                if (existingDay == null)
                {
                    var newDay = new SubscriptionDay
                    {
                        SubscriptionId = sub.Id,
                        CalendarId = null,
                        ServiceDate = targetDate,
                        RestaurantId = targetRestaurantId,
                        Status = SubscriptionDayStatus.Open,
                        LockAtUtc = _clock.UtcNow
                    };
                    _db.SubscriptionDays.Add(newDay);
                    days.Add(newDay);
                }
                else
                {
                    existingDay.RestaurantId = targetRestaurantId;
                    days.Add(existingDay);
                }
            }
            if (days.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var dispatchedCount = 0;
        foreach (var candidateDay in days)
        {
            var day = await _db.SubscriptionDays.FirstAsync(d => d.Id == candidateDay.Id, cancellationToken);
            day.RestaurantId = targetRestaurantId;

            var hasSelections = await _db.DailyMealSelections.AnyAsync(s => s.SubscriptionDayId == day.Id, cancellationToken);
            if (!hasSelections && request.AutoSelectIfEmpty)
            {
                try
                {
                    var autoResult = await calendarEngine.AutoSelectFullBoxAsync(day.SubscriptionId, day.ServiceDate, lockAfter: false, requireOwner: false, cancellationToken);
                    // Failed calendar writes clear tracking; reload before persisting dispatch state.
                    day = await _db.SubscriptionDays.FirstAsync(d => d.Id == candidateDay.Id, cancellationToken);
                    day.RestaurantId = targetRestaurantId;
                    if (autoResult.IsFailure)
                    {
                        day.MarkReplacementRequired(autoResult.Error.Description ?? autoResult.Error.Code);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    day = await _db.SubscriptionDays.FirstAsync(d => d.Id == candidateDay.Id, cancellationToken);
                    day.RestaurantId = targetRestaurantId;
                    day.MarkReplacementRequired($"Auto-selection failed: {ex.Message}");
                    continue;
                }
            }

            hasSelections = await _db.DailyMealSelections.AnyAsync(s => s.SubscriptionDayId == day.Id, cancellationToken);
            if (!hasSelections)
            {
                day.MarkReplacementRequired("No meal selections available for dispatch.");
                continue;
            }

            day.LockAtUtc ??= _clock.UtcNow;

            var existingDailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id && r.RestaurantId == targetRestaurantId, cancellationToken);

            if (existingDailyOrder == null)
            {
                if (day.Status < SubscriptionDayStatus.SentToRestaurant)
                {
                    day.Status = SubscriptionDayStatus.SentToRestaurant;
                }

                _db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
                {
                    RestaurantId = targetRestaurantId,
                    SubscriptionDayId = day.Id,
                    ServiceDate = day.ServiceDate,
                    Status = OrderStatus.AwaitingRestaurantConfirmation,
                    BoxCount = 1,
                    CreatedAtUtc = _clock.UtcNow
                });
                dispatchedCount++;
            }
            else
            {
                // Never regress orders already confirmed, preparing, ready, assigned, or fulfilled
                if (existingDailyOrder.Status is OrderStatus.Created or OrderStatus.ReplacementPending)
                {
                    if (day.Status < SubscriptionDayStatus.SentToRestaurant)
                    {
                        day.Status = SubscriptionDayStatus.SentToRestaurant;
                    }

                    existingDailyOrder.Status = OrderStatus.AwaitingRestaurantConfirmation;
                    existingDailyOrder.ModifiedAtUtc = _clock.UtcNow;
                    dispatchedCount++;
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(dispatchedCount);
    }

    public async Task<Result<bool>> UpdateDailyOrderPrepStatusAsync(
        UpdateDailyOrderPrepStatusCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<bool>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == request.DayId && x.RestaurantId == restaurantId, cancellationToken);

        if (day == null)
            return Result.Failure<bool>(Error.NotFound(MessageCodes.Orders.NotFound, "Order was not found."));

        var normalizedStatus = request.Status.Trim().ToLowerInvariant();

        // Restaurants are strictly forbidden from setting transit states like out-for-delivery
        if (normalizedStatus is "out-for-delivery" or "waiting-driver" or "waiting_driver" or "ontheway" or "on-the-way")
        {
            return Result.Failure<bool>(Error.Validation("order.out_for_delivery_forbidden_for_restaurant",
                "Restaurants cannot transition orders to out-for-delivery. Only delivery personnel can update transit status."));
        }

        OrderStatus targetOrderStatus;
        if (normalizedStatus == "preparing")
        {
            targetOrderStatus = OrderStatus.Preparing;
        }
        else if (normalizedStatus is "ready" or "ready-for-pickup" or "waiting-driver")
        {
            targetOrderStatus = OrderStatus.ReadyForPickup;
        }
        else
        {
            return Result.Failure<bool>(Error.Validation("order.invalid_prep_status",
                $"Status '{request.Status}' is not a valid kitchen prep status. Valid statuses are 'preparing' and 'ready'."));
        }

        // Validate the transition before generating documents or changing tracked state.
        var dailyOrder = await _db.RestaurantDailyOrders
            .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id, cancellationToken);
        if (dailyOrder != null && dailyOrder.Status != targetOrderStatus
            && !dailyOrder.CanTransitionTo(targetOrderStatus))
        {
            return Result.Failure<bool>(Error.Conflict("INVALID_ORDER_TRANSITION",
                $"Cannot transition order {dailyOrder.Id} from {dailyOrder.Status} to {targetOrderStatus}."));
        }

        if (normalizedStatus is "ready" or "ready-for-pickup")
        {
            var hasBarcode = await _db.MealBarcodes.AsNoTracking()
                .AnyAsync(x => x.SubscriptionDayId == day.Id && !x.IsRevoked, cancellationToken);
            var hasInvoice = await _db.MealInvoices.AsNoTracking()
                .AnyAsync(x => x.SubscriptionDayId == day.Id, cancellationToken);
            var hasBoxLabel = await _db.MealLabels.AsNoTracking()
                .AnyAsync(x => x.SubscriptionDayId == day.Id && x.Kind == MealLabelKind.Box, cancellationToken);
            var expectedMeals = await _db.DailyMealSelections.AsNoTracking()
                .CountAsync(x => x.SubscriptionDayId == day.Id, cancellationToken);
            var actualMealLabels = await _db.MealLabels.AsNoTracking()
                .CountAsync(x => x.SubscriptionDayId == day.Id && x.Kind == MealLabelKind.Meal, cancellationToken);

            if (!hasBarcode || !hasInvoice || !hasBoxLabel || actualMealLabels < expectedMeals)
            {
                if (_prepDocumentsGenerator is null)
                    return Result.Failure<bool>(Error.Failure("prep_documents.generator_unavailable", "Prep documents cannot be generated right now."));

                var hasExistingDocuments = hasBarcode || hasInvoice || hasBoxLabel || actualMealLabels > 0;
                var generation = await _prepDocumentsGenerator.GenerateForDayAsync(
                    day.Id,
                    forceReissue: hasExistingDocuments,
                    cancellationToken: cancellationToken);
                if (generation.IsFailure && generation.Error.Code == "prep_documents.missing_template")
                {
                    var seedHandler = new MealMate.Application.Features.DocumentTemplates.SeedDocumentTemplatesCommandHandler(_db, _clock);
                    await seedHandler.Handle(new MealMate.Application.Features.DocumentTemplates.SeedDocumentTemplatesCommand(), cancellationToken);
                    generation = await _prepDocumentsGenerator.GenerateForDayAsync(
                        day.Id,
                        forceReissue: hasExistingDocuments,
                        cancellationToken: cancellationToken);
                }

                if (generation.IsFailure)
                    return Result.Failure<bool>(generation.Error);
            }
        }

        day.Status = SubscriptionDayStatus.Preparing;

        if (dailyOrder != null)
        {
            if (dailyOrder.Status != targetOrderStatus)
            {
                dailyOrder.Status = targetOrderStatus;
                if (targetOrderStatus == OrderStatus.ReadyForPickup)
                {
                    var isFirstReady = dailyOrder.ReadyForPickupAtUtc == null;
                    dailyOrder.ReadyForPickupAtUtc ??= _clock.UtcNow;

                    if (isFirstReady)
                    {
                        _db.OutboxMessages.Add(new OutboxMessage
                        {
                            EventType = "KitchenOrderReadyForPickup",
                            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                DailyOrderId = dailyOrder.Id,
                                SubscriptionDayId = day.Id,
                                RestaurantId = restaurantId,
                                ServiceDate = day.ServiceDate,
                                ReadyForPickupAtUtc = dailyOrder.ReadyForPickupAtUtc.Value
                            }),
                            CreatedAtUtc = _clock.UtcNow
                        });
                    }
                }
                dailyOrder.ModifiedAtUtc = _clock.UtcNow;
            }
        }
        else
        {
            dailyOrder = new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = day.Id,
                ServiceDate = day.ServiceDate,
                Status = targetOrderStatus,
                ReadyForPickupAtUtc = targetOrderStatus == OrderStatus.ReadyForPickup ? _clock.UtcNow : null,
                BoxCount = 1,
                CreatedAtUtc = _clock.UtcNow
            };
            _db.RestaurantDailyOrders.Add(dailyOrder);
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (normalizedStatus is "ready" or "ready-for-pickup")
        {
            await _restaurantNotifier.NotifyKitchenBatchReadyAsync(
                restaurantId,
                day.Id,
                dailyOrder.BoxCount,
                cancellationToken);

            await _batchDispatchNotifier.NotifyIfBatchThresholdReachedAsync(
                restaurantId,
                day.Id,
                cancellationToken);
        }

        return Result.Success(true);
    }

    public async Task<Result<HandoverListDataDto>> GetHandoverOrdersAsync(
        GetRestaurantHandoverOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(null, cancellationToken);
        if (restaurantId == Guid.Empty)
            return Result.Failure<HandoverListDataDto>(Error.NotFound("restaurant.not_found", "No restaurant profile found."));

        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var targetDate = request.TargetDate ?? today;
        var tomorrow = today.AddDays(1);

        var dailyOrdersQuery = _db.RestaurantDailyOrders
            .AsNoTracking()
            .Where(r => r.RestaurantId == restaurantId &&
                        r.Status >= OrderStatus.ReadyForPickup);

        if (request.TargetDate.HasValue)
        {
            dailyOrdersQuery = dailyOrdersQuery.Where(r => r.ServiceDate == request.TargetDate.Value);
        }
        else
        {
            dailyOrdersQuery = dailyOrdersQuery.Where(r => r.ServiceDate == today || r.ServiceDate == tomorrow);
        }

        var dailyOrders = await dailyOrdersQuery
            .OrderByDescending(r => r.ServiceDate)
            .ThenBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var dayIds = dailyOrders.Select(r => r.SubscriptionDayId).Distinct().ToList();

        var days = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(d => dayIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var subIds = days.Values.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Program)
            .Include(s => s.Bundle)
            .Where(s => subIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var barcodes = await _db.MealBarcodes
            .AsNoTracking()
            .Where(b => dayIds.Contains(b.SubscriptionDayId))
            .ToDictionaryAsync(b => b.SubscriptionDayId, b => b.BarcodeValue, cancellationToken);

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

        var tripStops = await _db.TripStops
            .AsNoTracking()
            .Include(ts => ts.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .Where(ts => ts.SubscriptionDayId.HasValue && dayIds.Contains(ts.SubscriptionDayId.Value))
            .ToDictionaryAsync(ts => ts.SubscriptionDayId!.Value, cancellationToken);

        var items = new List<HandoverListItemDto>();
        var index = 0;


        foreach (var order in dailyOrders)
        {
            days.TryGetValue(order.SubscriptionDayId, out var day);
            if (day == null) continue;

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

            tripStops.TryGetValue(day.Id, out var tripStop);

            var shift = !string.IsNullOrWhiteSpace(tripStop?.Trip?.ShiftName)
                ? (tripStop.Trip.ShiftName.Contains("مساء") ? "evening" : (tripStop.Trip.ShiftName.Contains("ظهر") ? "noon" : "morning"))
                : "unscheduled";

            var shiftLabel = shift switch
            {
                "noon" => new LocalizedTextDto("ظهيرة", "Noon"),
                "evening" => new LocalizedTextDto("مساء", "Evening"),
                "unscheduled" => new LocalizedTextDto("لم يحدد", "Unscheduled"),
                _ => new LocalizedTextDto("صباح", "Morning")
            };

            var deliverySlotLabel = tripStop?.SlotLabel != null
                ? new LocalizedTextDto(tripStop.SlotLabel, tripStop.SlotLabel)
                : (shift switch
                {
                    "noon" => new LocalizedTextDto("12:00 – 14:00", "12:00 – 14:00"),
                    "evening" => new LocalizedTextDto("18:00 – 20:00", "18:00 – 20:00"),
                    "unscheduled" => new LocalizedTextDto("لم يحدد", "Unscheduled"),
                    _ => new LocalizedTextDto("08:00 – 10:00", "08:00 – 10:00")
                });

            var deliveryDateLabel = FormatDeliveryDate(day.ServiceDate);

            string status = order.Status switch
            {
                OrderStatus.ReadyForPickup or OrderStatus.AssignedToDriver => "awaiting-pickup",
                OrderStatus.PickedUp or OrderStatus.OnTheWay => "en-route",
                OrderStatus.Delivered => "delivered",
                _ => "awaiting-pickup"
            };

            LocalizedTextDto statusHint = status switch
            {
                "awaiting-pickup" => new LocalizedTextDto("بانتظار استلام المندوب ومسح الباركود", "Awaiting driver pickup & barcode scan"),
                "en-route" => new LocalizedTextDto("تم استلام البوكسات · في الطريق للعميل", "Boxes picked up · en route"),
                "delivered" => new LocalizedTextDto("تم مسح التسليم بنجاح", "Delivered successfully"),
                _ => new LocalizedTextDto("جاهز للتسليم", "Ready for handover")
            };

            string driverCode = string.Empty;
            if (tripStop?.Trip != null)
            {
                var assignment = tripStop.Trip.DriverAssignments.FirstOrDefault(da => da.Status == "Active")
                    ?? tripStop.Trip.DriverAssignments.FirstOrDefault();
                driverCode = assignment != null
                    ? $"DRV-{assignment.DriverProfileId.ToString()[..4].ToUpperInvariant()}"
                    : (tripStop.Trip.Code ?? string.Empty);
            }


            var barcodeVal = barcodes.GetValueOrDefault(day.Id) ?? $"MM-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}";
            var orderCode = $"ORD-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}";
            var batchCode = $"BATCH-{day.ServiceDate:dd}";
            var customerMaskedId = sub != null ? $"CUS-•••{sub.CustomerUserId.ToString()[..3].ToUpperInvariant()}" : "CUS-•••999";

            LocalizedTextDto? pickedUpAtLabel = status != "awaiting-pickup"
                ? new LocalizedTextDto("استُلم مؤخرًا", "Recently picked up")
                : null;

            LocalizedTextDto? etaLabel = status switch
            {
                "awaiting-pickup" => new LocalizedTextDto("بانتظار المندوب", "Awaiting driver"),
                "en-route" => new LocalizedTextDto("خلال 30 دقيقة", "In 30m"),
                "delivered" => new LocalizedTextDto("مكتمل", "Completed"),
                _ => null
            };

            items.Add(new HandoverListItemDto(
                Id: day.Id,
                OrderCode: orderCode,
                BatchCode: batchCode,
                CustomerMaskedId: customerMaskedId,
                DriverCode: driverCode,
                BarcodeCode: barcodeVal,
                MealSummary: mealSummary,
                ProgramLabel: programLabel,
                BundleLabel: bundleLabel,
                BoxCount: order.BoxCount > 0 ? order.BoxCount : 1,
                Shift: shift,
                ShiftLabel: shiftLabel,
                DeliveryDateLabel: deliveryDateLabel,
                DeliverySlotLabel: deliverySlotLabel,
                Status: status,
                StatusHint: statusHint,
                PickedUpAtLabel: pickedUpAtLabel,
                EtaLabel: etaLabel ?? new LocalizedTextDto("—", "—"),
                Route: $"/restaurant/orders/handover/{orderCode}"
            ));

            index++;
        }

        var awaitingCount = items.Count(i => i.Status == "awaiting-pickup");
        var enRouteCount = items.Count(i => i.Status == "en-route");
        var deliveredCount = items.Count(i => i.Status == "delivered");
        var totalBoxes = items.Sum(i => i.BoxCount);

        var summaries = new List<HandoverSummaryDto>
        {
            new("awaiting-pickup", new LocalizedTextDto("بانتظار الاستلام", "Awaiting pickup"), awaitingCount, new LocalizedTextDto("مندوب معيّن · لم يُمسح بعد", "Driver assigned · not scanned yet"), "warning", "lucideScanBarcode"),
            new("en-route", new LocalizedTextDto("مع المندوب", "With driver"), enRouteCount, new LocalizedTextDto("تم الاستلام · جاري التوصيل", "Picked up · delivering"), "primary", "lucideTruck"),
            new("delivered", new LocalizedTextDto("تم التسليم اليوم", "Delivered today"), deliveredCount, new LocalizedTextDto("مسح تسليم مكتمل", "Delivery scan complete"), "accent", "lucideCheck"),
            new("boxes", new LocalizedTextDto("بوكسات اليوم", "Today’s boxes"), totalBoxes, new LocalizedTextDto("عبر كل الطلبات", "Across all handovers"), "neutral", "lucidePackage")
        };

        var dateLabel = FormatDateLabel(targetDate);
        var data = new HandoverListDataDto(
            Title: new LocalizedTextDto("تسليم الطلبات", "Order handover"),
            Subtitle: new LocalizedTextDto("طلبات استلمها المندوب أو بانتظار مسح الاستلام · جاري توصيلها للعميل", "Orders taken by the driver or awaiting pickup scan · out for delivery"),
            DateLabel: dateLabel,
            WindowHint: new LocalizedTextDto("مسح الباركود عند الاستلام والتسليم", "Barcode scan at pickup & delivery"),
            Summaries: summaries,
            Orders: items
        );

        return Result.Success(data);
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

public sealed class GetRestaurantPrepDocumentsQueryHandler
    : IRequestHandler<GetRestaurantPrepDocumentsQuery, Result<RestaurantPrepDocumentsDto>>
{
    private readonly RestaurantOrderService _service;

    public GetRestaurantPrepDocumentsQueryHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<RestaurantPrepDocumentsDto>> Handle(
        GetRestaurantPrepDocumentsQuery request,
        CancellationToken cancellationToken)
        => _service.GetPrepDocumentsAsync(request, cancellationToken);
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

public sealed class MarkOrderBarcodePrintedCommandHandler
    : IRequestHandler<MarkOrderBarcodePrintedCommand, Result<bool>>
{
    private readonly RestaurantOrderService _service;

    public MarkOrderBarcodePrintedCommandHandler(RestaurantOrderService service) => _service = service;

    public Task<Result<bool>> Handle(MarkOrderBarcodePrintedCommand request, CancellationToken cancellationToken)
        => _service.MarkOrderBarcodePrintedAsync(request, cancellationToken);
}

public sealed class DispatchRestaurantDailyOrdersCommandHandler
    : IRequestHandler<DispatchRestaurantDailyOrdersCommand, Result<int>>
{
    private readonly RestaurantOrderService _service;
    private readonly MealMate.Application.Features.CustomerCalendar.ISubscriptionDayDispatchService _dispatchService;
    private readonly MealMate.Application.Features.CustomerCalendar.CustomerCalendarEngine _calendarEngine;

    public DispatchRestaurantDailyOrdersCommandHandler(
        RestaurantOrderService service,
        MealMate.Application.Features.CustomerCalendar.ISubscriptionDayDispatchService dispatchService,
        MealMate.Application.Features.CustomerCalendar.CustomerCalendarEngine calendarEngine)
    {
        _service = service;
        _dispatchService = dispatchService;
        _calendarEngine = calendarEngine;
    }

    public Task<Result<int>> Handle(
        DispatchRestaurantDailyOrdersCommand request,
        CancellationToken cancellationToken)
        => _service.DispatchDailyOrdersAsync(request, _dispatchService, _calendarEngine, cancellationToken);
}

public sealed class UpdateDailyOrderPrepStatusCommandHandler
    : IRequestHandler<UpdateDailyOrderPrepStatusCommand, Result<bool>>
{
    private readonly RestaurantOrderService _service;

    public UpdateDailyOrderPrepStatusCommandHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<bool>> Handle(
        UpdateDailyOrderPrepStatusCommand request,
        CancellationToken cancellationToken)
        => _service.UpdateDailyOrderPrepStatusAsync(request, cancellationToken);
}

public sealed class GetRestaurantHandoverOrdersQueryHandler
    : IRequestHandler<GetRestaurantHandoverOrdersQuery, Result<HandoverListDataDto>>
{
    private readonly RestaurantOrderService _service;

    public GetRestaurantHandoverOrdersQueryHandler(RestaurantOrderService service)
    {
        _service = service;
    }

    public Task<Result<HandoverListDataDto>> Handle(
        GetRestaurantHandoverOrdersQuery request,
        CancellationToken cancellationToken)
        => _service.GetHandoverOrdersAsync(request, cancellationToken);
}

