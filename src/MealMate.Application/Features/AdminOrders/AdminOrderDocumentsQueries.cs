using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminOrders;

public sealed record GetAdminOrderInvoiceQuery(Guid OrderId) : IQuery<RestaurantPrepDocumentPartDto?>;
public sealed record GetAdminOrderLabelsQuery(Guid OrderId) : IQuery<IReadOnlyList<RestaurantPrepDocumentPartDto>>;
public sealed record GetAdminOrderPrepDocumentsQuery(Guid OrderId) : IQuery<RestaurantPrepDocumentsDto>;

public sealed class AdminOrderDocumentsQueryService
{
    private readonly IAppDbContext _db;
    private readonly IPrepDocumentsGenerator _generator;

    public AdminOrderDocumentsQueryService(IAppDbContext db, IPrepDocumentsGenerator generator)
    {
        _db = db;
        _generator = generator;
    }

    public async Task<Result<(Guid DayId, MealMate.Domain.RestaurantOperations.MealInvoice? Invoice, IReadOnlyList<MealMate.Domain.RestaurantOperations.MealLabel> Labels)>> LoadAsync(Guid orderId, CancellationToken ct)
    {
        var dayId = await _db.RestaurantDailyOrders.AsNoTracking()
            .Where(x => x.Id == orderId)
            .Select(x => (Guid?)x.SubscriptionDayId)
            .SingleOrDefaultAsync(ct);
        if (!dayId.HasValue)
            return Result.Failure<(Guid, MealMate.Domain.RestaurantOperations.MealInvoice?, IReadOnlyList<MealMate.Domain.RestaurantOperations.MealLabel>)>(Error.NotFound("order.not_found", "Order was not found."));

        var invoice = await _db.MealInvoices.AsNoTracking().Where(x => x.SubscriptionDayId == dayId.Value)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var labels = await _db.MealLabels.AsNoTracking().Where(x => x.SubscriptionDayId == dayId.Value)
            .OrderBy(x => x.Kind).ThenBy(x => x.CreatedAtUtc).ToListAsync(ct);

        if (invoice is null || !labels.Any(x => x.Kind == MealLabelKind.Box))
        {
            var generated = await _generator.GenerateForDayAsync(dayId.Value, cancellationToken: ct);
            if (generated.IsSuccess)
            {
                invoice = await _db.MealInvoices.AsNoTracking().Where(x => x.SubscriptionDayId == dayId.Value)
                    .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
                labels = await _db.MealLabels.AsNoTracking().Where(x => x.SubscriptionDayId == dayId.Value)
                    .OrderBy(x => x.Kind).ThenBy(x => x.CreatedAtUtc).ToListAsync(ct);
            }
        }

        return Result.Success<(Guid DayId, MealMate.Domain.RestaurantOperations.MealInvoice? Invoice, IReadOnlyList<MealMate.Domain.RestaurantOperations.MealLabel> Labels)>((dayId.Value, invoice, labels));
    }

    public static RestaurantPrepDocumentPartDto MapInvoice(MealMate.Domain.RestaurantOperations.MealInvoice invoice)
        => new(invoice.Id, "invoice", null, invoice.SnapshotJson ?? "{}", ExtractLayout(invoice.SnapshotJson), invoice.TemplateId, invoice.TemplateVersion);

    public static RestaurantPrepDocumentPartDto MapLabel(MealMate.Domain.RestaurantOperations.MealLabel label)
        => new(label.Id, label.Kind == MealLabelKind.Box ? "box_label" : "meal_label", label.MealId, label.LabelPayloadJson, ExtractLayout(label.LabelPayloadJson), label.TemplateId, label.TemplateVersion);

    private static string? ExtractLayout(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("layout", out var layout) ? layout.GetRawText() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}

public sealed class GetAdminOrderPrepDocumentsQueryHandler : IQueryHandler<GetAdminOrderPrepDocumentsQuery, RestaurantPrepDocumentsDto>
{
    private readonly AdminOrderDocumentsQueryService _service;
    public GetAdminOrderPrepDocumentsQueryHandler(AdminOrderDocumentsQueryService service) => _service = service;

    public async Task<Result<RestaurantPrepDocumentsDto>> Handle(GetAdminOrderPrepDocumentsQuery request, CancellationToken ct)
    {
        var loaded = await _service.LoadAsync(request.OrderId, ct);
        if (loaded.IsFailure) return Result.Failure<RestaurantPrepDocumentsDto>(loaded.Error);
        var box = loaded.Value.Labels.FirstOrDefault(x => x.Kind == MealLabelKind.Box);
        var mealLabels = loaded.Value.Labels.Where(x => x.Kind == MealLabelKind.Meal).Select(AdminOrderDocumentsQueryService.MapLabel).ToList();
        return Result.Success(new RestaurantPrepDocumentsDto(
            loaded.Value.DayId,
            loaded.Value.Invoice?.BarcodeValue,
            loaded.Value.Invoice is null ? null : AdminOrderDocumentsQueryService.MapInvoice(loaded.Value.Invoice),
            box is null ? null : AdminOrderDocumentsQueryService.MapLabel(box),
            mealLabels));
    }
}

public sealed class GetAdminOrderInvoiceQueryHandler : IQueryHandler<GetAdminOrderInvoiceQuery, RestaurantPrepDocumentPartDto?>
{
    private readonly AdminOrderDocumentsQueryService _service;
    public GetAdminOrderInvoiceQueryHandler(AdminOrderDocumentsQueryService service) => _service = service;

    public async Task<Result<RestaurantPrepDocumentPartDto?>> Handle(GetAdminOrderInvoiceQuery request, CancellationToken ct)
    {
        var loaded = await _service.LoadAsync(request.OrderId, ct);
        if (loaded.IsFailure) return Result.Failure<RestaurantPrepDocumentPartDto?>(loaded.Error);
        return Result.Success(loaded.Value.Invoice is null ? null : AdminOrderDocumentsQueryService.MapInvoice(loaded.Value.Invoice));
    }
}

public sealed class GetAdminOrderLabelsQueryHandler : IQueryHandler<GetAdminOrderLabelsQuery, IReadOnlyList<RestaurantPrepDocumentPartDto>>
{
    private readonly AdminOrderDocumentsQueryService _service;
    public GetAdminOrderLabelsQueryHandler(AdminOrderDocumentsQueryService service) => _service = service;

    public async Task<Result<IReadOnlyList<RestaurantPrepDocumentPartDto>>> Handle(GetAdminOrderLabelsQuery request, CancellationToken ct)
    {
        var loaded = await _service.LoadAsync(request.OrderId, ct);
        if (loaded.IsFailure) return Result.Failure<IReadOnlyList<RestaurantPrepDocumentPartDto>>(loaded.Error);
        return Result.Success((IReadOnlyList<RestaurantPrepDocumentPartDto>)loaded.Value.Labels.Select(AdminOrderDocumentsQueryService.MapLabel).ToList());
    }
}