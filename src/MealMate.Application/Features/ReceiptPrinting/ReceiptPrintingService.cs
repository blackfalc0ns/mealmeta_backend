using System.Text.Json;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.ReceiptPrinting;

public sealed class ReceiptPrintingService : IReceiptPrintingService
{
    private const string MealMateLogoConfigKey = "Branding:MealMateLogoUrl";
    private const string DefaultMealMateLogoUrl = "/assets/branding/mealmate-logo.png";
    private const string PublicReceiptBaseUrlConfigKey = "Branding:PublicReceiptBaseUrl";
    private const string DefaultPublicReceiptBaseUrl = "https://app.mealmate.kw/receipt/";

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly IUserAccountService _userAccounts;
    private readonly ILogger<ReceiptPrintingService> _logger;

    public ReceiptPrintingService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IConfiguration configuration,
        IUserAccountService userAccounts,
        ILogger<ReceiptPrintingService> logger)
    {
        _db = db;
        _clock = clock;
        _configuration = configuration;
        _userAccounts = userAccounts;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReceiptTemplateDto>> GetAllTemplatesForAdminAsync(CancellationToken cancellationToken = default)
    {
        var templates = await _db.ReceiptTemplates
            .AsNoTracking()
            .OrderByDescending(x => x.IsGlobalDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return templates.Select(MapToDto).ToList();
    }

    public async Task<ReceiptTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _db.ReceiptTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return template is null ? null : MapToDto(template);
    }

    public async Task<Result<ReceiptTemplateDto>> UpdateConfigurationAsync(
        Guid id,
        string configurationJson,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.ReceiptTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("receipt_template.not_found", "Receipt template not found.");

        if (string.IsNullOrWhiteSpace(configurationJson))
            configurationJson = "{}";

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
        }
        catch (JsonException ex)
        {
            return Error.Validation("receipt_template.invalid_json", $"Invalid JSON configuration: {ex.Message}");
        }

        template.ConfigurationJson = configurationJson;
        template.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(template);
    }

    public async Task<Result<ReceiptTemplateDto>> PublishTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _db.ReceiptTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("receipt_template.not_found", "Receipt template not found.");

        template.IsPublished = true;
        template.Version++;
        template.PublishedAtUtc = _clock.UtcNow;
        template.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(template);
    }

    public async Task<Result<ReceiptTemplateDto>> UpdateVisibilityAsync(
        Guid id,
        bool isEnabled,
        bool isRestaurantVisible,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.ReceiptTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("receipt_template.not_found", "Receipt template not found.");

        if (template.IsGlobalDefault && !isEnabled)
            return Error.Validation("receipt_template.cannot_disable_default", "Cannot disable the global default template.");

        template.IsEnabled = isEnabled;
        template.IsRestaurantVisible = isRestaurantVisible;
        template.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(template);
    }

    public async Task<Result> SetGlobalDefaultTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var target = await _db.ReceiptTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (target is null)
            return Error.NotFound("receipt_template.not_found", "Receipt template not found.");

        if (!target.IsEnabled || !target.IsPublished)
            return Error.Validation("receipt_template.must_be_active_and_published", "Global default template must be active and published.");

        var all = await _db.ReceiptTemplates.ToListAsync(cancellationToken);
        foreach (var t in all)
        {
            t.IsGlobalDefault = (t.Id == id);
            t.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<RestaurantReceiptSettingsDto> GetRestaurantSettingsAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var settings = await _db.RestaurantReceiptSettings
            .AsNoTracking()
            .Include(x => x.SelectedTemplate)
            .Include(x => x.AdminAssignedTemplate)
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);

        var effective = await ResolveEffectiveTemplateEntityAsync(restaurantId, settings, cancellationToken);

        return new RestaurantReceiptSettingsDto(
            restaurantId,
            settings?.SelectedTemplateId,
            settings?.SelectedTemplate?.Key,
            settings?.AdminAssignedTemplateId,
            settings?.AdminAssignedTemplate?.Key,
            settings?.AllowRestaurantSelection ?? true,
            effective.Id,
            effective.Key);
    }

    public async Task<Result<RestaurantReceiptSettingsDto>> AdminUpdateRestaurantSettingsAsync(
        Guid restaurantId,
        Guid? adminAssignedTemplateId,
        bool allowRestaurantSelection,
        CancellationToken cancellationToken = default)
    {
        if (adminAssignedTemplateId.HasValue)
        {
            var template = await _db.ReceiptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == adminAssignedTemplateId.Value, cancellationToken);
            if (template is null || !template.IsEnabled || !template.IsPublished)
                return Error.Validation("receipt_template.invalid_assignment", "Assigned template must be enabled and published.");
        }

        var settings = await _db.RestaurantReceiptSettings.FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);
        if (settings is null)
        {
            settings = new RestaurantReceiptSettings
            {
                RestaurantId = restaurantId,
                AdminAssignedTemplateId = adminAssignedTemplateId,
                AllowRestaurantSelection = allowRestaurantSelection,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.RestaurantReceiptSettings.Add(settings);
        }
        else
        {
            settings.AdminAssignedTemplateId = adminAssignedTemplateId;
            settings.AllowRestaurantSelection = allowRestaurantSelection;
            settings.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetRestaurantSettingsAsync(restaurantId, cancellationToken);
    }

    public async Task<Result<RestaurantReceiptSettingsDto>> RestaurantSelectTemplateAsync(
        Guid restaurantId,
        Guid selectedTemplateId,
        CancellationToken cancellationToken = default)
    {
        var settings = await _db.RestaurantReceiptSettings.FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);
        if (settings is not null && !settings.AllowRestaurantSelection)
            return Error.Forbidden("receipt_settings.selection_locked", "Template selection is managed by system administration for this restaurant.");

        var template = await _db.ReceiptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == selectedTemplateId, cancellationToken);
        if (template is null || !template.IsEnabled || !template.IsPublished || !template.IsRestaurantVisible)
            return Error.Validation("receipt_template.not_available", "The selected template is not available for restaurant selection.");

        if (settings is null)
        {
            settings = new RestaurantReceiptSettings
            {
                RestaurantId = restaurantId,
                SelectedTemplateId = selectedTemplateId,
                AllowRestaurantSelection = true,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.RestaurantReceiptSettings.Add(settings);
        }
        else
        {
            settings.SelectedTemplateId = selectedTemplateId;
            settings.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetRestaurantSettingsAsync(restaurantId, cancellationToken);
    }

    public async Task<IReadOnlyList<ReceiptTemplateDto>> GetAllowedTemplatesForRestaurantAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        var templates = await _db.ReceiptTemplates
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.IsPublished && x.IsRestaurantVisible)
            .OrderByDescending(x => x.IsGlobalDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return templates.Select(MapToDto).ToList();
    }

    public async Task<Result<OperationalReceiptDto>> GetOperationalReceiptAsync(
        Guid orderId,
        Guid? requestingRestaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var dailyOrder = await _db.RestaurantDailyOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId || x.OrderId == orderId, cancellationToken);

        if (dailyOrder is null)
            return Error.NotFound("receipt.order_not_found", "Order not found.");

        if (requestingRestaurantId.HasValue && dailyOrder.RestaurantId != requestingRestaurantId.Value)
            return Error.Forbidden("receipt.unauthorized_restaurant", "You are not authorized to access receipts for this order.");

        var restaurantId = dailyOrder.RestaurantId;
        var effectiveTemplate = await ResolveEffectiveTemplateAsync(restaurantId, cancellationToken);

        return await BuildOperationalReceiptPayloadAsync(dailyOrder, effectiveTemplate, cancellationToken);
    }

    public async Task<Result<OperationalReceiptDto>> GetPreviewOperationalReceiptAsync(
        Guid? templateId = null,
        Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        ReceiptTemplate? template = null;
        if (templateId.HasValue)
        {
            template = await _db.ReceiptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId.Value, cancellationToken);
        }

        if (template is null)
        {
            template = await _db.ReceiptTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsGlobalDefault && x.IsEnabled, cancellationToken)
                ?? await _db.ReceiptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.IsEnabled, cancellationToken);
        }

        if (template is null)
            return Error.NotFound("receipt.no_template_available", "No receipt template available for preview.");

        var query = _db.RestaurantDailyOrders.AsNoTracking();
        if (restaurantId.HasValue)
            query = query.Where(x => x.RestaurantId == restaurantId.Value);

        var sampleOrder = await query
            .OrderByDescending(x => x.ServiceDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (sampleOrder is null)
            return Error.NotFound("receipt.no_orders_found", "No real orders exist in the database to preview.");

        return await BuildOperationalReceiptPayloadAsync(sampleOrder, template, cancellationToken);
    }

    public async Task<Result> LogPrintAsync(
        Guid orderId,
        Guid restaurantId,
        Guid templateId,
        int templateVersion,
        Guid printedByUserId,
        string? clientIp,
        string? printStatus,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var log = new ReceiptPrintLog
        {
            OrderId = orderId,
            RestaurantId = restaurantId,
            TemplateId = templateId,
            TemplateVersion = templateVersion,
            PrintedByUserId = printedByUserId,
            PrintedAtUtc = _clock.UtcNow,
            PrintStatus = string.IsNullOrWhiteSpace(printStatus) ? "Success" : printStatus.Trim(),
            ClientIp = clientIp,
            Notes = notes,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.ReceiptPrintLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<ReceiptTemplate> ResolveEffectiveTemplateAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var settings = await _db.RestaurantReceiptSettings
            .AsNoTracking()
            .Include(x => x.AdminAssignedTemplate)
            .Include(x => x.SelectedTemplate)
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);

        return await ResolveEffectiveTemplateEntityAsync(restaurantId, settings, cancellationToken);
    }

    private async Task<ReceiptTemplate> ResolveEffectiveTemplateEntityAsync(
        Guid restaurantId,
        RestaurantReceiptSettings? settings,
        CancellationToken cancellationToken)
    {
        if (settings?.AdminAssignedTemplate is { IsEnabled: true, IsPublished: true } adminTemplate)
            return adminTemplate;

        if (settings is { AllowRestaurantSelection: true } &&
            settings.SelectedTemplate is { IsEnabled: true, IsPublished: true, IsRestaurantVisible: true } selectedTemplate)
            return selectedTemplate;

        var globalDefault = await _db.ReceiptTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsGlobalDefault && x.IsEnabled && x.IsPublished, cancellationToken);

        if (globalDefault is not null)
            return globalDefault;

        var fallback = await _db.ReceiptTemplates
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.IsPublished)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (fallback is not null)
            return fallback;

        throw new InvalidOperationException("No published receipt template is available in the system.");
    }

    private async Task<Result<OperationalReceiptDto>> BuildOperationalReceiptPayloadAsync(
        RestaurantDailyOrder dailyOrder,
        ReceiptTemplate template,
        CancellationToken cancellationToken)
    {
        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dailyOrder.RestaurantId, cancellationToken);

        if (restaurant is null)
            return Error.NotFound("receipt.restaurant_not_found", "Restaurant profile not found.");

        var day = await _db.SubscriptionDays
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dailyOrder.SubscriptionDayId, cancellationToken);

        var (customerNameAr, customerNameEn, areaAr, areaEn, addressLineAr, addressLineEn) =
            await ResolveCustomerAndAddressAsync(day, cancellationToken);

        var mealSelections = await _db.DailyMealSelections
            .AsNoTracking()
            .Where(x => x.SubscriptionDayId == dailyOrder.SubscriptionDayId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var mealIds = mealSelections.Select(x => x.MealId).Distinct().ToList();
        var mealsDict = mealIds.Count == 0
            ? new Dictionary<Guid, Meal>()
            : await _db.Meals
                .AsNoTracking()
                .Include(x => x.Images)
                .Where(x => mealIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var mealItems = BuildMealItems(mealSelections, mealsDict);

        var orderCode = $"ORD-{dailyOrder.ServiceDate:yyyyMMdd}-{dailyOrder.Id.ToString("N")[..4].ToUpperInvariant()}";
        var barcodeText = $"MM-{orderCode}";

        var existingBarcode = await _db.MealBarcodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SubscriptionDayId == dailyOrder.SubscriptionDayId, cancellationToken);
        if (existingBarcode is not null && !string.IsNullOrWhiteSpace(existingBarcode.BarcodeValue))
        {
            barcodeText = existingBarcode.BarcodeValue;
        }

        var baseUrl = _configuration[PublicReceiptBaseUrlConfigKey] ?? DefaultPublicReceiptBaseUrl;
        var qrPayload = $"{baseUrl.TrimEnd('/')}/{dailyOrder.Id:D}";

        var mealMateLogoUrl = _configuration[MealMateLogoConfigKey] ?? DefaultMealMateLogoUrl;
        var restaurantLogoUrl = await ResolveRestaurantLogoUrlAsync(dailyOrder.RestaurantId, cancellationToken);
        var restaurantInitials = GenerateInitials(restaurant.TradeNameEn, restaurant.TradeNameAr);

        var appBrand = new ReceiptBrandDto(
            "MealMate",
            "MealMate",
            mealMateLogoUrl,
            "MM");

        var restaurantBrand = new ReceiptBrandDto(
            restaurant.TradeNameAr,
            restaurant.TradeNameEn,
            restaurantLogoUrl,
            restaurantInitials);

        var customerDto = new ReceiptCustomerDto(
            customerNameAr,
            customerNameEn,
            areaAr,
            areaEn,
            addressLineAr,
            addressLineEn);

        var deliveryDto = new ReceiptDeliveryDto(
            dailyOrder.ServiceDate,
            "11:00 - 13:00",
            dailyOrder.ScheduleMode?.ToString() ?? "Standard",
            $"BATCH-{dailyOrder.ServiceDate:MMdd}",
            dailyOrder.Status.ToString());

        return new OperationalReceiptDto(
            dailyOrder.Id,
            orderCode,
            barcodeText,
            qrPayload,
            mealItems.Count,
            template.Key,
            template.ConfigurationJson,
            appBrand,
            restaurantBrand,
            customerDto,
            deliveryDto,
            mealItems);
    }

    private static IReadOnlyList<ReceiptMealItemDto> BuildMealItems(
        IReadOnlyList<DailyMealSelection> selections,
        IReadOnlyDictionary<Guid, Meal> mealsDict)
    {
        var result = new List<ReceiptMealItemDto>();
        var itemIndex = 1;

        foreach (var sel in selections)
        {
            if (!mealsDict.TryGetValue(sel.MealId, out var meal))
                continue;

            var primaryImage = meal.Images.FirstOrDefault(x => x.IsPrimary)?.ImageUrl
                ?? meal.Images.FirstOrDefault()?.ImageUrl;
            var imageUrl = !string.IsNullOrWhiteSpace(primaryImage)
                ? (primaryImage.StartsWith("http", StringComparison.OrdinalIgnoreCase) || primaryImage.StartsWith('/')
                    ? primaryImage
                    : CatalogImageStorage.ToContentUrl(primaryImage))
                : null;

            var calories = meal.Calories ?? 0m;
            var protein = meal.ProteinG ?? 0m;
            var carbs = meal.CarbsG ?? 0m;
            var fat = meal.FatG ?? 0m;

            var allergens = FoodCodeNormalizer.Split(meal.PresentAllergenCodes);

            result.Add(new ReceiptMealItemDto(
                meal.Id,
                itemIndex++,
                meal.NameAr,
                meal.NameEn,
                imageUrl,
                1,
                new ReceiptMealNutritionDto(calories, protein, carbs, fat),
                allergens,
                meal.RestaurantNotes));
        }

        return result;
    }

    private async Task<(string NameAr, string NameEn, string AreaAr, string AreaEn, string AddressLineAr, string AddressLineEn)>
        ResolveCustomerAndAddressAsync(SubscriptionDay? day, CancellationToken cancellationToken)
    {
        var nameAr = "عميل MealMate";
        var nameEn = "MealMate Customer";
        var areaAr = "الكويت";
        var areaEn = "Kuwait";
        var addressLineAr = "";
        var addressLineEn = "";

        if (day?.DeliveryAddressId is { } addrId)
        {
            var address = await _db.CustomerAddresses
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == addrId, cancellationToken);

            if (address is not null)
            {
                areaAr = string.IsNullOrWhiteSpace(address.Area) ? address.Governorate ?? "الكويت" : $"{address.Area} - قطعة {address.Block}";
                areaEn = string.IsNullOrWhiteSpace(address.Area) ? address.Governorate ?? "Kuwait" : $"{address.Area} - Block {address.Block}";

                var streetAr = string.IsNullOrWhiteSpace(address.Street) ? "" : $"شارع {address.Street}";
                var buildingAr = string.IsNullOrWhiteSpace(address.Building) ? "" : $"، مبنى {address.Building}";
                var floorAr = string.IsNullOrWhiteSpace(address.Floor) ? "" : $"، دور {address.Floor}";
                var unitAr = string.IsNullOrWhiteSpace(address.Unit) ? "" : $"، شقة {address.Unit}";
                addressLineAr = string.IsNullOrWhiteSpace(address.AddressLine)
                    ? $"{streetAr}{buildingAr}{floorAr}{unitAr}".Trim('،', ' ')
                    : address.AddressLine;

                var streetEn = string.IsNullOrWhiteSpace(address.Street) ? "" : $"{address.Street} St";
                var buildingEn = string.IsNullOrWhiteSpace(address.Building) ? "" : $", Bldg {address.Building}";
                var floorEn = string.IsNullOrWhiteSpace(address.Floor) ? "" : $", Floor {address.Floor}";
                var unitEn = string.IsNullOrWhiteSpace(address.Unit) ? "" : $", Unit {address.Unit}";
                addressLineEn = string.IsNullOrWhiteSpace(address.AddressLine)
                    ? $"{streetEn}{buildingEn}{floorEn}{unitEn}".Trim(',', ' ')
                    : address.AddressLine;

                var user = await _userAccounts.FindByIdAsync(address.UserId, cancellationToken);
                if (user is not null && !string.IsNullOrWhiteSpace(user.FullName))
                {
                    nameAr = user.FullName;
                    nameEn = user.FullName;
                }
            }
        }

        return (nameAr, nameEn, areaAr, areaEn, addressLineAr, addressLineEn);
    }

    private async Task<string?> ResolveRestaurantLogoUrlAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var storagePath = await _db.Documents
            .AsNoTracking()
            .Where(x => x.OwnerType == DocumentOwnerType.Restaurant
                && x.OwnerId == restaurantId
                && x.DocumentType == DocumentType.ProfileImage)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        return CatalogImageStorage.ToContentUrl(storagePath);
    }

    public static string GenerateInitials(string? nameEn, string? nameAr)
    {
        if (!string.IsNullOrWhiteSpace(nameEn))
        {
            var parts = nameEn.Split(new[] { ' ', '-', '_', '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Length > 0 && char.IsLetter(p[0]))
                .ToList();

            if (parts.Count >= 2)
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            if (parts.Count == 1 && parts[0].Length >= 2)
                return parts[0][..2].ToUpperInvariant();
            if (parts.Count == 1 && parts[0].Length == 1)
                return parts[0].ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(nameAr))
        {
            var parts = nameAr.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Length > 0 && char.IsLetter(p[0]))
                .Select(p => p.StartsWith("ال") && p.Length > 2 ? p[2..] : p)
                .ToList();

            if (parts.Count >= 2)
                return $"{parts[0][0]}{parts[1][0]}";
            if (parts.Count == 1 && parts[0].Length >= 2)
                return parts[0][..2];
            if (parts.Count == 1)
                return parts[0];
        }

        return "MM";
    }

    private static ReceiptTemplateDto MapToDto(ReceiptTemplate t) =>
        new(
            t.Id,
            t.Key,
            t.NameAr,
            t.NameEn,
            t.DescriptionAr,
            t.DescriptionEn,
            t.IsEnabled,
            t.IsPublished,
            t.IsRestaurantVisible,
            t.IsGlobalDefault,
            t.Version,
            t.ConfigurationJson,
            t.PublishedAtUtc);
}
