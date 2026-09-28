using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.PrepDocuments;

public sealed class PrepDocumentsGenerator : IPrepDocumentsGenerator
{
    public const string MealMateLogoConfigKey = "Branding:MealMateLogoUrl";
    public const string DefaultMealMateLogoUrl = "/assets/branding/mealmate-logo.png";

    private static readonly SubscriptionDayStatus[] EligibleStatuses =
    [
        SubscriptionDayStatus.ConfirmedByRestaurant,
        SubscriptionDayStatus.Preparing,
        SubscriptionDayStatus.OutForDelivery,
        SubscriptionDayStatus.Delivered,
    ];

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PrepDocumentsGenerator> _logger;

    public PrepDocumentsGenerator(
        IAppDbContext db,
        IDateTimeProvider clock,
        IConfiguration configuration,
        ILogger<PrepDocumentsGenerator> logger)
        => (_db, _clock, _configuration, _logger) = (db, clock, configuration, logger);

    public async Task<Result> GenerateForDayAsync(
        Guid subscriptionDayId,
        bool forceReissue = false,
        bool rotateBarcode = false,
        CancellationToken cancellationToken = default)
    {
        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(x => x.Id == subscriptionDayId, cancellationToken);
        if (day is null)
            return Error.NotFound("prep_documents.day_not_found", "Subscription day was not found.");

        if (!EligibleStatuses.Contains(day.Status))
            return Error.Validation(
                "prep_documents.day_not_eligible",
                $"Subscription day status {day.Status} is not eligible for prep document generation.");

        if (day.RestaurantId is null)
            return Error.Validation("prep_documents.restaurant_missing", "Subscription day has no restaurant.");

        var templates = await LoadPublishedTemplatesAsync(cancellationToken);
        if (templates.MissingKinds.Count > 0)
        {
            _logger.LogWarning(
                "Skipping prep document generation for day {SubscriptionDayId}: missing published templates for kinds {Kinds}",
                subscriptionDayId,
                string.Join(", ", templates.MissingKinds));
            return Error.Failure(
                "prep_documents.missing_template",
                $"Missing published templates: {string.Join(", ", templates.MissingKinds)}.");
        }

        var existingBarcode = await _db.MealBarcodes
            .FirstOrDefaultAsync(x => x.SubscriptionDayId == subscriptionDayId, cancellationToken);

        if (existingBarcode is not null && !forceReissue)
            return Result.Success();

        var restaurantId = day.RestaurantId!.Value;
        var restaurant = await _db.RestaurantProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == restaurantId, cancellationToken);
        if (restaurant is null)
            return Error.NotFound("prep_documents.restaurant_not_found", "Restaurant profile was not found.");

        var mealSelections = await _db.DailyMealSelections.AsNoTracking()
            .Where(x => x.SubscriptionDayId == subscriptionDayId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var mealIds = mealSelections.Select(x => x.MealId).Distinct().ToList();
        var meals = mealIds.Count == 0
            ? new Dictionary<Guid, Meal>()
            : await _db.Meals.AsNoTracking()
                .Where(x => mealIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var mealPayloads = await BuildMealPayloadsAsync(mealSelections, meals, cancellationToken);
        var incompleteNutrition = mealPayloads.Any(IsNutritionIncomplete);

        var mealMateLogoUrl = ResolveMealMateLogoUrl();
        var restaurantLogoUrl = await ResolveRestaurantLogoUrlAsync(restaurantId, cancellationToken);
        var shortAddress = await ResolveShortAddressAsync(day, cancellationToken);

        var now = _clock.UtcNow;
        var barcodeValue = existingBarcode?.BarcodeValue ?? BuildBarcodeValue(day);
        if (existingBarcode is not null && rotateBarcode)
            barcodeValue = await RotateBarcodeValueAsync(day, existingBarcode.BarcodeValue, cancellationToken);

        if (forceReissue)
            await RemoveExistingDocumentsAsync(subscriptionDayId, cancellationToken);

        var dailyOrder = await _db.RestaurantDailyOrders
            .FirstOrDefaultAsync(x => x.SubscriptionDayId == subscriptionDayId && x.RestaurantId == restaurantId, cancellationToken);

        if (existingBarcode is null)
        {
            _db.MealBarcodes.Add(new MealBarcode
            {
                SubscriptionDayId = subscriptionDayId,
                OrderId = dailyOrder?.Id,
                BarcodeValue = barcodeValue,
                Status = MealBarcodeStatus.Generated,
                GeneratedAtUtc = now,
                CreatedAtUtc = now,
            });
        }
        else
        {
            if (dailyOrder is not null && existingBarcode.OrderId is null)
                existingBarcode.OrderId = dailyOrder.Id;

            if (rotateBarcode && !string.Equals(existingBarcode.BarcodeValue, barcodeValue, StringComparison.Ordinal))
            {
                existingBarcode.BarcodeValue = barcodeValue;
                existingBarcode.GeneratedAtUtc = now;
            }
        }

        var invoiceSnapshot = BuildSnapshot(
            mealMateLogoUrl,
            restaurantLogoUrl,
            restaurant.TradeNameAr,
            restaurant.TradeNameEn,
            day,
            barcodeValue,
            shortAddress,
            incompleteNutrition,
            templates.Invoice!.LayoutJson,
            mealPayloads);

        var boxLabelSnapshot = BuildSnapshot(
            mealMateLogoUrl,
            restaurantLogoUrl,
            restaurant.TradeNameAr,
            restaurant.TradeNameEn,
            day,
            barcodeValue,
            shortAddress: null,
            incompleteNutrition,
            templates.BoxLabel!.LayoutJson,
            mealPayloads);

        _db.MealInvoices.Add(new MealInvoice
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = subscriptionDayId,
            ServiceDate = day.ServiceDate,
            InvoiceNumber = BuildInvoiceNumber(day),
            TotalBoxes = 1,
            SnapshotJson = PrepDocumentPayloadSerializer.Serialize(invoiceSnapshot),
            TemplateId = templates.Invoice.Id,
            TemplateVersion = templates.Invoice.Version,
            BarcodeValue = barcodeValue,
            CreatedAtUtc = now,
        });

        _db.MealLabels.Add(new MealLabel
        {
            SubscriptionDayId = subscriptionDayId,
            Kind = MealLabelKind.Box,
            LabelPayloadJson = PrepDocumentPayloadSerializer.Serialize(boxLabelSnapshot),
            GeneratedAtUtc = now,
            TemplateId = templates.BoxLabel!.Id,
            TemplateVersion = templates.BoxLabel.Version,
            CreatedAtUtc = now,
        });

        var mealLabelTemplate = templates.MealLabel!;
        foreach (var mealPayload in mealPayloads)
        {
            var mealLabelSnapshot = BuildSnapshot(
                mealMateLogoUrl,
                restaurantLogoUrl,
                restaurant.TradeNameAr,
                restaurant.TradeNameEn,
                day,
                barcodeValue,
                shortAddress: null,
                incompleteNutrition,
                mealLabelTemplate.LayoutJson,
                [mealPayload]);

            _db.MealLabels.Add(new MealLabel
            {
                SubscriptionDayId = subscriptionDayId,
                Kind = MealLabelKind.Meal,
                MealId = mealPayload.MealId,
                LabelPayloadJson = PrepDocumentPayloadSerializer.Serialize(mealLabelSnapshot),
                GeneratedAtUtc = now,
                TemplateId = mealLabelTemplate.Id,
                TemplateVersion = mealLabelTemplate.Version,
                CreatedAtUtc = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<PublishedTemplates> LoadPublishedTemplatesAsync(CancellationToken cancellationToken)
    {
        var published = await _db.DocumentTemplates.AsNoTracking()
            .Where(x => x.Status == DocumentTemplateStatus.Published)
            .ToListAsync(cancellationToken);

        var invoice = published.FirstOrDefault(x => x.Kind == DocumentTemplateKind.Invoice);
        var boxLabel = published.FirstOrDefault(x => x.Kind == DocumentTemplateKind.BoxLabel);
        var mealLabel = published.FirstOrDefault(x => x.Kind == DocumentTemplateKind.MealLabel);

        var missing = new List<DocumentTemplateKind>();
        if (invoice is null) missing.Add(DocumentTemplateKind.Invoice);
        if (boxLabel is null) missing.Add(DocumentTemplateKind.BoxLabel);
        if (mealLabel is null) missing.Add(DocumentTemplateKind.MealLabel);

        return new PublishedTemplates(invoice, boxLabel, mealLabel, missing);
    }

    private string ResolveMealMateLogoUrl()
    {
        var configured = _configuration[MealMateLogoConfigKey];
        return string.IsNullOrWhiteSpace(configured) ? DefaultMealMateLogoUrl : configured.Trim();
    }

    private async Task<string?> ResolveRestaurantLogoUrlAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var storagePath = await _db.Documents.AsNoTracking()
            .Where(x => x.OwnerType == DocumentOwnerType.Restaurant
                && x.OwnerId == restaurantId
                && x.DocumentType == DocumentType.ProfileImage)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        return CatalogImageStorage.ToContentUrl(storagePath);
    }

    private async Task<string> ResolveShortAddressAsync(SubscriptionDay day, CancellationToken cancellationToken)
    {
        if (day.DeliveryAddressId is { } addressId)
        {
            var address = await _db.CustomerAddresses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == addressId, cancellationToken);
            if (address is not null)
            {
                var parts = new[] { address.Governorate, address.Area, address.Block }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .ToList();
                if (parts.Count > 0)
                    return string.Join(", ", parts);
            }
        }

        return string.Empty;
    }

    private async Task<List<PrepDocumentMealPayload>> BuildMealPayloadsAsync(
        IReadOnlyList<DailyMealSelection> selections,
        IReadOnlyDictionary<Guid, Meal> meals,
        CancellationToken cancellationToken)
    {
        var allergenRows = await _db.AllergenDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Code, x.NameAr, x.NameEn })
            .ToListAsync(cancellationToken);
        var allergenMap = allergenRows.ToDictionary(
            x => x.Code,
            x => (x.NameAr, x.NameEn),
            StringComparer.OrdinalIgnoreCase);

        var payloads = new List<PrepDocumentMealPayload>();
        foreach (var selection in selections)
        {
            if (!meals.TryGetValue(selection.MealId, out var meal))
                continue;

            var presentCodes = FoodCodeNormalizer.Split(meal.PresentAllergenCodes);
            var mayContainCodes = FoodCodeNormalizer.Split(meal.MayContainAllergenCodes);

            payloads.Add(new PrepDocumentMealPayload(
                meal.Id,
                meal.NameAr,
                meal.NameEn,
                meal.Slot,
                selection.SortOrder,
                meal.Calories,
                meal.ProteinG,
                meal.CarbsG,
                meal.FatG,
                presentCodes,
                mayContainCodes,
                MapAllergens(presentCodes, allergenMap),
                MapAllergens(mayContainCodes, allergenMap)));
        }

        return payloads;
    }

    private static IReadOnlyList<PrepDocumentAllergenPayload> MapAllergens(
        IReadOnlyList<string> codes,
        IReadOnlyDictionary<string, (string NameAr, string NameEn)> allergenMap)
    {
        if (codes.Count == 0)
            return Array.Empty<PrepDocumentAllergenPayload>();

        return codes
            .Select(code =>
            {
                if (allergenMap.TryGetValue(code, out var names))
                    return new PrepDocumentAllergenPayload(code, names.NameAr, names.NameEn);

                return new PrepDocumentAllergenPayload(code, code, code);
            })
            .ToList();
    }

    private static bool IsNutritionIncomplete(PrepDocumentMealPayload meal)
        => meal.Calories is null || meal.ProteinG is null || meal.CarbsG is null || meal.FatG is null;

    private static PrepDocumentSnapshotPayload BuildSnapshot(
        string mealMateLogoUrl,
        string? restaurantLogoUrl,
        string restaurantNameAr,
        string restaurantNameEn,
        SubscriptionDay day,
        string barcodeValue,
        string? shortAddress,
        bool incompleteNutrition,
        string embeddedLayoutJson,
        IReadOnlyList<PrepDocumentMealPayload> meals)
        => new(
            mealMateLogoUrl,
            restaurantLogoUrl,
            restaurantNameAr,
            restaurantNameEn,
            day.ServiceDate,
            day.Id,
            barcodeValue,
            shortAddress,
            incompleteNutrition,
            string.IsNullOrWhiteSpace(embeddedLayoutJson) ? "[]" : embeddedLayoutJson.Trim(),
            meals);

    private static string BuildBarcodeValue(SubscriptionDay day)
    {
        var raw = $"MM-{day.ServiceDate:yyyyMMdd}-{day.Id:N}";
        return raw.Length <= 100 ? raw : raw[..100];
    }

    private async Task<string> RotateBarcodeValueAsync(
        SubscriptionDay day,
        string currentValue,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var candidate = BuildRotatedBarcodeValue(day, attempt);
            var exists = await _db.MealBarcodes.AsNoTracking()
                .AnyAsync(x => x.BarcodeValue == candidate && x.BarcodeValue != currentValue, cancellationToken);
            if (!exists)
                return candidate;
        }

        return BuildRotatedBarcodeValue(day, Guid.NewGuid());
    }

    private static string BuildRotatedBarcodeValue(SubscriptionDay day, int attempt)
    {
        var raw = $"MM-{day.ServiceDate:yyyyMMdd}-{day.Id:N}-{attempt}";
        return raw.Length <= 100 ? raw : raw[..100];
    }

    private static string BuildRotatedBarcodeValue(SubscriptionDay day, Guid suffix)
    {
        var raw = $"MM-{day.ServiceDate:yyyyMMdd}-{suffix:N}";
        return raw.Length <= 100 ? raw : raw[..100];
    }

    private static string BuildInvoiceNumber(SubscriptionDay day)
    {
        var raw = $"INV-{day.ServiceDate:yyyyMMdd}-{day.Id:N}";
        return raw.Length <= 100 ? raw : raw[..100];
    }

    private async Task RemoveExistingDocumentsAsync(Guid subscriptionDayId, CancellationToken cancellationToken)
    {
        var invoices = await _db.MealInvoices
            .Where(x => x.SubscriptionDayId == subscriptionDayId)
            .ToListAsync(cancellationToken);
        if (invoices.Count > 0)
            _db.MealInvoices.RemoveRange(invoices);

        var labels = await _db.MealLabels
            .Where(x => x.SubscriptionDayId == subscriptionDayId)
            .ToListAsync(cancellationToken);
        if (labels.Count > 0)
            _db.MealLabels.RemoveRange(labels);
    }

    private sealed record PublishedTemplates(
        DocumentTemplate? Invoice,
        DocumentTemplate? BoxLabel,
        DocumentTemplate? MealLabel,
        IReadOnlyList<DocumentTemplateKind> MissingKinds);
}
