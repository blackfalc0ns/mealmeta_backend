using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MediatR;

namespace MealMate.Application.Features.RestaurantOrders;

public sealed record PendingConfirmationSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value,
    LocalizedTextDto Hint,
    string Tone,
    string Icon);

public sealed record PendingConfirmationItemDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    string CustomerMaskedId,
    LocalizedTextDto MealSummary,
    LocalizedTextDto ProgramLabel,
    LocalizedTextDto BundleLabel,
    int BoxCount,
    LocalizedTextDto DeliveryDateLabel,
    LocalizedTextDto DeliverySlotLabel,
    LocalizedTextDto ReceivedAtLabel,
    LocalizedTextDto DeadlineLabel,
    int HoursLeft,
    string Urgency,
    bool Confirmed,
    bool IsReassigned,
    string Route);

public sealed record PendingConfirmationDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto WindowHint,
    IReadOnlyList<PendingConfirmationSummaryDto> Summaries,
    IReadOnlyList<PendingConfirmationItemDto> Orders);

public sealed record BoxMealNutritionDto(
    decimal Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal FiberGrams,
    decimal SugarGrams,
    decimal SodiumMg,
    int ProteinDailyPercent,
    int SodiumDailyPercent,
    decimal ServingWeightGrams);

public sealed record BoxMealIngredientDto(
    Guid Id,
    LocalizedTextDto Name,
    LocalizedTextDto Amount,
    decimal WeightGrams,
    decimal Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal FiberGrams,
    decimal SugarGrams,
    decimal SodiumMg,
    int ProteinDailyPercent,
    int SodiumDailyPercent,
    LocalizedTextDto PrepStyle,
    LocalizedTextDto? Note,
    bool Allergen);

public sealed record BoxMealItemDto(
    Guid Id,
    string Slot,
    LocalizedTextDto SlotLabel,
    int SlotIndex,
    LocalizedTextDto Name,
    LocalizedTextDto Description,
    string ImageUrl,
    BoxMealNutritionDto Nutrition,
    IReadOnlyList<BoxMealIngredientDto> Ingredients,
    IReadOnlyList<LocalizedTextDto> AllergyFlags,
    LocalizedTextDto PrepNote);

public sealed record BoxSlotSummaryDto(
    string Slot,
    LocalizedTextDto Label,
    int Expected,
    int Filled);

public sealed record OrderDetailFactDto(
    string Id,
    LocalizedTextDto Label,
    LocalizedTextDto Value,
    string Icon);

public sealed record OrderDetailChecklistItemDto(
    string Id,
    LocalizedTextDto Label,
    bool Done,
    LocalizedTextDto Hint);

public sealed record OrderDetailTimelineItemDto(
    string Id,
    LocalizedTextDto Title,
    LocalizedTextDto Detail,
    LocalizedTextDto Time,
    string Tone);

public sealed record OrderDetailActionDto(
    string Id,
    LocalizedTextDto Label,
    string Route,
    bool Primary,
    string Icon);

public sealed record RestaurantOrderDetailDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    string Status,
    string Shift,
    LocalizedTextDto ShiftLabel,
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    string CustomerMaskedId,
    LocalizedTextDto DeliveryDateLabel,
    LocalizedTextDto DeliverySlotLabel,
    LocalizedTextDto ProgramLabel,
    LocalizedTextDto TierLabel,
    string BundleKind,
    LocalizedTextDto BundleLabel,
    LocalizedTextDto BoxCompositionLabel,
    int BoxCount,
    string? DriverCode,
    string? BarcodeCode,
    LocalizedTextDto WindowLabel,
    LocalizedTextDto PrivacyNote,
    IReadOnlyList<OrderDetailFactDto> Facts,
    IReadOnlyList<BoxSlotSummaryDto> SlotSummary,
    IReadOnlyList<BoxMealItemDto> Meals,
    IReadOnlyList<OrderDetailChecklistItemDto> Checklist,
    IReadOnlyList<OrderDetailTimelineItemDto> Timeline,
    IReadOnlyList<OrderDetailActionDto> Actions);

public sealed record ConfirmRestaurantOrderRequest(string? Note = null);
public sealed record RejectRestaurantOrderRequest(string? ReasonCode = null, string? Reason = null);
public sealed record DispatchOrdersRequest(Guid? RestaurantId = null, DateOnly? TargetDate = null, bool AutoSelectIfEmpty = true);
public sealed record UpdateDailyOrderPrepStatusRequest(string Status);

// --- Daily Orders DTOs & Queries ---
public sealed record DailyOrderSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value,
    LocalizedTextDto Hint,
    string Tone,
    string Icon);

public sealed record DailyOrderItemDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    LocalizedTextDto MealName,
    LocalizedTextDto ProgramLabel,
    int BoxCount,
    string Shift,
    LocalizedTextDto ShiftLabel,
    string Status,
    LocalizedTextDto? DriverLabel,
    LocalizedTextDto WindowLabel,
    bool BarcodeReady,
    LocalizedTextDto ActionLabel,
    string Route);

public sealed record DailyOrdersDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto PrintLabel,
    IReadOnlyList<DailyOrderSummaryDto> Summaries,
    IReadOnlyList<DailyOrderItemDto> Orders);

// --- Upcoming 24h DTOs & Queries ---
public sealed record Upcoming24hSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value,
    LocalizedTextDto Hint,
    string Tone,
    string Icon);

public sealed record Upcoming24hItemDto(
    Guid Id,
    string OrderCode,
    string BatchCode,
    string CustomerMaskedId,
    LocalizedTextDto MealSummary,
    LocalizedTextDto ProgramLabel,
    LocalizedTextDto BundleLabel,
    int BoxCount,
    string Shift,
    LocalizedTextDto ShiftLabel,
    LocalizedTextDto DeliveryDateLabel,
    LocalizedTextDto DeliverySlotLabel,
    int HoursToDelivery,
    bool InvoiceGenerated,
    bool BarcodeGenerated,
    bool LabelsGenerated,
    string PrepStatus,
    string Route,
    string LabelsRoute);

public sealed record Upcoming24hDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto WindowHint,
    LocalizedTextDto PrintLabel,
    IReadOnlyList<Upcoming24hSummaryDto> Summaries,
    IReadOnlyList<Upcoming24hItemDto> Orders);

public sealed record RestaurantPrepDocumentPartDto(
    Guid Id,
    string Kind,
    Guid? MealId,
    string SnapshotJson,
    string? LayoutJson,
    Guid? TemplateId,
    int? TemplateVersion);

public sealed record RestaurantPrepDocumentsDto(
    Guid SubscriptionDayId,
    string? BarcodeValue,
    RestaurantPrepDocumentPartDto? Invoice,
    RestaurantPrepDocumentPartDto? BoxLabel,
    IReadOnlyList<RestaurantPrepDocumentPartDto> MealLabels);

public sealed record GetRestaurantPendingConfirmationQuery(string? Filter = null, string? Search = null) : IRequest<Result<PendingConfirmationDataDto>>;
public sealed record GetRestaurantOrderDetailQuery(string OrderCodeOrId) : IRequest<Result<RestaurantOrderDetailDto>>;
public sealed record ConfirmRestaurantOrderCommand(Guid DayId, string? Note = null) : IRequest<Result<bool>>;
public sealed record RejectRestaurantOrderCommand(Guid DayId, string? Reason = null, string? ReasonCode = null) : IRequest<Result<bool>>;
public sealed record GetRestaurantDailyOrdersQuery(DateOnly? Date = null, string? Shift = null, string? Status = null, string? Search = null) : IRequest<Result<DailyOrdersDataDto>>;
public sealed record GetRestaurantUpcoming24hQuery(DateOnly? TargetDate = null, string? Shift = null, string? PrepStatus = null, string? Search = null) : IRequest<Result<Upcoming24hDataDto>>;
public sealed record GetRestaurantPrepDocumentsQuery(Guid DayId) : IRequest<Result<RestaurantPrepDocumentsDto>>;
public sealed record ArchiveDailyOrderCommand(Guid DayId) : IRequest<Result<bool>>;
public sealed record DispatchRestaurantDailyOrdersCommand(Guid? RestaurantId = null, DateOnly? TargetDate = null, bool AutoSelectIfEmpty = true) : IRequest<Result<int>>;
public sealed record UpdateDailyOrderPrepStatusCommand(Guid DayId, string Status) : IRequest<Result<bool>>;

