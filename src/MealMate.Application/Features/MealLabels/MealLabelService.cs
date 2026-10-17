using System.Text.Json;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.MealLabels;

public sealed class MealLabelService : IMealLabelService
{
    private const string MealMateLogoConfigKey = "Branding:MealMateLogoUrl";
    private const string DefaultMealMateLogoUrl = "/assets/images/logo/mealmate-logo.webp";

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MealLabelService> _logger;

    public MealLabelService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IConfiguration configuration,
        ILogger<MealLabelService> logger)
    {
        _db = db;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MealBoxLabelTemplateDto>> GetAllTemplatesForAdminAsync(CancellationToken cancellationToken = default)
    {
        var templates = await _db.MealBoxLabelTemplates
            .AsNoTracking()
            .OrderByDescending(x => x.IsGlobalDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return templates.Select(MapToDto).ToList();
    }

    public async Task<MealBoxLabelTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _db.MealBoxLabelTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return template is null ? null : MapToDto(template);
    }

    public async Task<Result<MealBoxLabelTemplateDto>> UpdateConfigurationAsync(
        Guid id,
        string configurationJson,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.MealBoxLabelTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("meal_label_template.not_found", "Meal box label template not found.");

        if (string.IsNullOrWhiteSpace(configurationJson))
            configurationJson = "{}";

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
        }
        catch (JsonException ex)
        {
            return Error.Validation("meal_label_template.invalid_json", $"Invalid JSON configuration: {ex.Message}");
        }

        template.ConfigurationJson = configurationJson;
        template.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(template);
    }

    public async Task<Result<MealBoxLabelTemplateDto>> PublishTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _db.MealBoxLabelTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("meal_label_template.not_found", "Meal box label template not found.");

        template.IsPublished = true;
        template.Version++;
        template.PublishedAtUtc = _clock.UtcNow;
        template.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(template);
    }

    public async Task<Result<MealBoxLabelTemplateDto>> UpdateVisibilityAsync(
        Guid id,
        bool isEnabled,
        bool isRestaurantVisible,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.MealBoxLabelTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("meal_label_template.not_found", "Meal box label template not found.");

        if (template.IsGlobalDefault && !isEnabled)
            return Error.Validation("meal_label_template.cannot_disable_default", "Cannot disable the global default meal box label template.");

        template.IsEnabled = isEnabled;
        template.IsRestaurantVisible = isRestaurantVisible;
        template.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(template);
    }

    public async Task<Result> SetGlobalDefaultTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var target = await _db.MealBoxLabelTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (target is null)
            return Error.NotFound("meal_label_template.not_found", "Meal box label template not found.");

        if (!target.IsEnabled || !target.IsPublished)
            return Error.Validation("meal_label_template.must_be_active_and_published", "Global default meal box label template must be active and published.");

        var all = await _db.MealBoxLabelTemplates.ToListAsync(cancellationToken);
        foreach (var t in all)
        {
            t.IsGlobalDefault = (t.Id == id);
            t.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<RestaurantMealLabelSettingsDto> GetRestaurantSettingsAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var settings = await _db.RestaurantMealLabelSettings
            .AsNoTracking()
            .Include(x => x.SelectedTemplate)
            .Include(x => x.AdminAssignedTemplate)
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);

        var effective = await ResolveEffectiveTemplateEntityAsync(restaurantId, settings, cancellationToken);

        return new RestaurantMealLabelSettingsDto(
            restaurantId,
            settings?.SelectedTemplateId,
            settings?.SelectedTemplate?.Key,
            settings?.AdminAssignedTemplateId,
            settings?.AdminAssignedTemplate?.Key,
            settings?.AllowRestaurantSelection ?? true,
            settings?.ConfiguredWidthMm ?? effective.DefaultWidthMm,
            settings?.ConfiguredHeightMm ?? effective.DefaultHeightMm,
            settings?.Orientation ?? effective.DefaultOrientation,
            settings?.ShowPackagingDate ?? true,
            settings?.ShowOrderReference ?? true,
            settings?.ShowQuantity ?? true,
            effective.Id,
            effective.Key);
    }

    public async Task<Result<RestaurantMealLabelSettingsDto>> AdminUpdateRestaurantSettingsAsync(
        Guid restaurantId,
        AdminUpdateRestaurantMealLabelSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AdminAssignedTemplateId.HasValue)
        {
            var template = await _db.MealBoxLabelTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AdminAssignedTemplateId.Value, cancellationToken);
            if (template is null || !template.IsEnabled || !template.IsPublished)
                return Error.Validation("meal_label_template.invalid_assignment", "Assigned template must be enabled and published.");
        }

        var settings = await _db.RestaurantMealLabelSettings.FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);
        if (settings is null)
        {
            settings = new RestaurantMealLabelSettings
            {
                RestaurantId = restaurantId,
                AdminAssignedTemplateId = request.AdminAssignedTemplateId,
                AllowRestaurantSelection = request.AllowRestaurantSelection,
                ConfiguredWidthMm = request.ConfiguredWidthMm ?? 70,
                ConfiguredHeightMm = request.ConfiguredHeightMm ?? 100,
                Orientation = request.Orientation ?? "portrait",
                ShowPackagingDate = request.ShowPackagingDate ?? true,
                ShowOrderReference = request.ShowOrderReference ?? true,
                ShowQuantity = request.ShowQuantity ?? true,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.RestaurantMealLabelSettings.Add(settings);
        }
        else
        {
            settings.AdminAssignedTemplateId = request.AdminAssignedTemplateId;
            settings.AllowRestaurantSelection = request.AllowRestaurantSelection;
            if (request.ConfiguredWidthMm.HasValue) settings.ConfiguredWidthMm = request.ConfiguredWidthMm.Value;
            if (request.ConfiguredHeightMm.HasValue) settings.ConfiguredHeightMm = request.ConfiguredHeightMm.Value;
            if (!string.IsNullOrWhiteSpace(request.Orientation)) settings.Orientation = request.Orientation;
            if (request.ShowPackagingDate.HasValue) settings.ShowPackagingDate = request.ShowPackagingDate.Value;
            if (request.ShowOrderReference.HasValue) settings.ShowOrderReference = request.ShowOrderReference.Value;
            if (request.ShowQuantity.HasValue) settings.ShowQuantity = request.ShowQuantity.Value;
            settings.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetRestaurantSettingsAsync(restaurantId, cancellationToken);
    }

    public async Task<Result<RestaurantMealLabelSettingsDto>> RestaurantSelectTemplateAsync(
        Guid restaurantId,
        RestaurantSelectMealLabelTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = await _db.RestaurantMealLabelSettings.FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);
        if (settings is not null && !settings.AllowRestaurantSelection)
            return Error.Forbidden("meal_label_settings.selection_locked", "Meal box label template selection is restricted by system administration for this restaurant.");

        var template = await _db.MealBoxLabelTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.SelectedTemplateId, cancellationToken);
        if (template is null || !template.IsEnabled || !template.IsPublished || !template.IsRestaurantVisible)
            return Error.Validation("meal_label_template.not_available", "The selected template is not available for restaurant selection.");

        if (settings is null)
        {
            settings = new RestaurantMealLabelSettings
            {
                RestaurantId = restaurantId,
                SelectedTemplateId = request.SelectedTemplateId,
                AllowRestaurantSelection = true,
                ConfiguredWidthMm = request.ConfiguredWidthMm ?? template.DefaultWidthMm,
                ConfiguredHeightMm = request.ConfiguredHeightMm ?? template.DefaultHeightMm,
                Orientation = request.Orientation ?? template.DefaultOrientation,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.RestaurantMealLabelSettings.Add(settings);
        }
        else
        {
            settings.SelectedTemplateId = request.SelectedTemplateId;
            if (request.ConfiguredWidthMm.HasValue) settings.ConfiguredWidthMm = request.ConfiguredWidthMm.Value;
            if (request.ConfiguredHeightMm.HasValue) settings.ConfiguredHeightMm = request.ConfiguredHeightMm.Value;
            if (!string.IsNullOrWhiteSpace(request.Orientation)) settings.Orientation = request.Orientation;
            settings.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetRestaurantSettingsAsync(restaurantId, cancellationToken);
    }

    public async Task<IReadOnlyList<MealBoxLabelTemplateDto>> GetAllowedTemplatesForRestaurantAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        var templates = await _db.MealBoxLabelTemplates
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.IsPublished && x.IsRestaurantVisible)
            .OrderByDescending(x => x.IsGlobalDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return templates.Select(MapToDto).ToList();
    }

    public async Task<Result<MealBoxLabelDataDto>> GetPreviewMealLabelAsync(
        Guid? templateId = null,
        Guid? restaurantId = null,
        Guid? mealId = null,
        CancellationToken cancellationToken = default)
    {
        MealBoxLabelTemplate? template = null;
        if (templateId.HasValue)
        {
            template = await _db.MealBoxLabelTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId.Value, cancellationToken);
        }

        if (template is null)
        {
            template = await _db.MealBoxLabelTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsGlobalDefault && x.IsEnabled, cancellationToken)
                ?? await _db.MealBoxLabelTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.IsEnabled, cancellationToken);
        }

        if (template is null)
            return Error.NotFound("meal_label.no_template_available", "No meal box label template available.");

        // Resolve Restaurant Profile
        RestaurantProfile? restaurant = null;
        if (restaurantId.HasValue)
        {
            restaurant = await _db.RestaurantProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == restaurantId.Value, cancellationToken);
        }
        restaurant ??= await _db.RestaurantProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var restTradeAr = restaurant?.TradeNameAr ?? "مطبخ الصحة والعافية";
        var restTradeEn = restaurant?.TradeNameEn ?? "Health & Wellness Kitchen";
        var restLogo = restaurant is not null ? await ResolveRestaurantLogoUrlAsync(restaurant.Id, cancellationToken) : null;
        var restInitials = GenerateInitials(restTradeEn, restTradeAr);

        var restaurantBrand = new MealLabelBrandDto(
            restTradeAr,
            restTradeEn,
            restLogo,
            restInitials);

        var appLogo = _configuration[MealMateLogoConfigKey] ?? DefaultMealMateLogoUrl;
        var appBrand = new MealLabelBrandDto(
            "MealMate",
            "MealMate",
            appLogo,
            "MM");

        // Resolve Meal
        Meal? meal = null;
        if (mealId.HasValue)
        {
            meal = await _db.Meals.AsNoTracking()
                .Include(x => x.Images)
                .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient)
                .Include(x => x.Allergens)
                .FirstOrDefaultAsync(x => x.Id == mealId.Value, cancellationToken);
        }

        if (meal is null && restaurant is not null)
        {
            meal = await _db.Meals.AsNoTracking()
                .Include(x => x.Images)
                .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient)
                .Include(x => x.Allergens)
                .FirstOrDefaultAsync(x => x.RestaurantId == restaurant.Id, cancellationToken);
        }

        MealBoxLabelItemDto mealDto;
        if (meal is not null)
        {
            var mealImage = meal.Images?.OrderByDescending(x => x.IsPrimary).Select(x => x.ImageUrl).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(mealImage))
            {
                mealImage = meal.NameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) || meal.NameAr.Contains("تونة")
                    ? "assets/images/receipts/fresh-tuna-salad.jpg"
                    : "assets/images/receipts/grilled-chicken-bowl.jpg";
            }

            var ingAr = meal.Ingredients?.Count > 0
                ? string.Join("، ", meal.Ingredients.Select(i => i.Ingredient != null ? i.Ingredient.NameAr : "مكون صحي" ?? "مكون صحي").Take(6))
                : (meal.NameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase)
                    ? "تونة طازجة، كينوا، خس، خيار، طماطم، زيت زيتون بكر"
                    : "دجاج مشوي، أرز بني، بروكلي، زبادي خفيف");

            var ingEn = meal.Ingredients?.Count > 0
                ? string.Join(", ", meal.Ingredients.Select(i => i.Ingredient != null ? i.Ingredient.NameEn : "Healthy ingredient" ?? "Healthy ingredient").Take(6))
                : (meal.NameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase)
                    ? "Fresh tuna, quinoa, lettuce, cucumber, cherry tomatoes, olive oil"
                    : "Grilled chicken, brown rice, broccoli, light yogurt sauce");

            var allAr = meal.Allergens?.Count > 0
                ? string.Join("، ", meal.Allergens.Select(a => a.AllergenCode).Take(3))
                : (meal.NameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "أسماك" : "حليب، سمسم");

            var allEn = meal.Allergens?.Count > 0
                ? string.Join(", ", meal.Allergens.Select(a => a.AllergenCode).Take(3))
                : (meal.NameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "Fish" : "Milk, sesame");

            var prepAr = !string.IsNullOrWhiteSpace(meal.RestaurantNotes)
                ? meal.RestaurantNotes
                : (meal.NameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "بدون بصل، ليمون إضافي" : "بدون فلفل حار، الصوص منفصل");

            var prepEn = !string.IsNullOrWhiteSpace(meal.RestaurantNotes)
                ? meal.RestaurantNotes
                : (meal.NameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "No onion, extra lemon" : "No chili, sauce on the side");

            mealDto = new MealBoxLabelItemDto(
                meal.Id,
                meal.NameAr,
                meal.NameEn,
                mealImage,
                new MealLabelNutritionDto(
                    meal.Calories ?? 540,
                    meal.ProteinG ?? 44,
                    meal.CarbsG ?? 48,
                    meal.FatG ?? 18),
                ingAr,
                ingEn,
                allAr,
                allEn,
                prepAr,
                prepEn,
                _clock.UtcNow.ToString("yyyy-MM-dd"),
                "ORD-SAMPLE-01",
                1);
        }
        else
        {
            // Default sample matching authoritative reference
            var isTuna = template.Key == "organic-nature" || template.Key == "fresh-modern";
            mealDto = new MealBoxLabelItemDto(
                Guid.NewGuid(),
                isTuna ? "سلطة التونة الطازجة بالكينوا" : "بول الدجاج المشوي مع الأرز البني",
                isTuna ? "Fresh Tuna Quinoa Salad" : "Grilled Chicken Bowl with Brown Rice",
                isTuna ? "assets/images/receipts/fresh-tuna-salad.jpg" : "assets/images/receipts/grilled-chicken-bowl.jpg",
                new MealLabelNutritionDto(
                    isTuna ? 430 : 540,
                    isTuna ? 36 : 44,
                    isTuna ? 34 : 48,
                    isTuna ? 16 : 18),
                isTuna ? "تونة طازجة، كينوا، خس، خيار، طماطم، زيت زيتون بكر" : "دجاج مشوي، أرز بني، بروكلي، زبادي خفيف",
                isTuna ? "Fresh tuna, quinoa, lettuce, cucumber, cherry tomatoes, olive oil" : "Grilled chicken, brown rice, broccoli, light yogurt sauce",
                isTuna ? "أسماك" : "حليب، سمسم",
                isTuna ? "Fish" : "Milk, sesame",
                isTuna ? "بدون بصل، ليمون إضافي" : "بدون فلفل حار، الصوص منفصل",
                isTuna ? "No onion, extra lemon" : "No chili, sauce on the side",
                _clock.UtcNow.ToString("yyyy-MM-dd"),
                "ORD-20261016-101",
                1);
        }

        var dimensions = new MealLabelDimensionsDto(
            template.DefaultWidthMm,
            template.DefaultHeightMm,
            template.DefaultOrientation);

        return new MealBoxLabelDataDto(
            template.Key,
            dimensions,
            restaurantBrand,
            appBrand,
            mealDto);
    }

    public async Task<Result<IReadOnlyList<MealBoxLabelDataDto>>> GetOrderMealLabelsAsync(
        Guid orderId,
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        var dailyOrder = await _db.RestaurantDailyOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.Id == orderId || x.OrderId == orderId) && x.RestaurantId == restaurantId, cancellationToken);

        if (dailyOrder is null)
            return Error.NotFound("meal_label.order_not_found", "Order not found or unauthorized for this restaurant.");

        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == restaurantId, cancellationToken);

        var restTradeAr = restaurant?.TradeNameAr ?? "مطبخ الصحة والعافية";
        var restTradeEn = restaurant?.TradeNameEn ?? "Health & Wellness Kitchen";
        var restLogo = await ResolveRestaurantLogoUrlAsync(restaurantId, cancellationToken);
        var restInitials = GenerateInitials(restTradeEn, restTradeAr);

        var restaurantBrand = new MealLabelBrandDto(
            restTradeAr,
            restTradeEn,
            restLogo,
            restInitials);

        var appLogo = _configuration[MealMateLogoConfigKey] ?? DefaultMealMateLogoUrl;
        var appBrand = new MealLabelBrandDto(
            "MealMate",
            "MealMate",
            appLogo,
            "MM");

        var effectiveTemplate = await ResolveEffectiveTemplateAsync(restaurantId, cancellationToken);
        var settings = await _db.RestaurantMealLabelSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);

        var dimensions = new MealLabelDimensionsDto(
            settings?.ConfiguredWidthMm ?? effectiveTemplate.DefaultWidthMm,
            settings?.ConfiguredHeightMm ?? effectiveTemplate.DefaultHeightMm,
            settings?.Orientation ?? effectiveTemplate.DefaultOrientation);

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
                .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient)
                .Include(x => x.Allergens)
                .Where(x => mealIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var orderRef = $"ORD-{dailyOrder.ServiceDate:yyyyMMdd}-{dailyOrder.Id.ToString("N")[..4].ToUpperInvariant()}";
        var packagingDate = dailyOrder.ServiceDate.ToString("yyyy-MM-dd");

        var labels = new List<MealBoxLabelDataDto>();
        foreach (var selection in mealSelections)
        {
            mealsDict.TryGetValue(selection.MealId, out var meal);

            var nameAr = meal?.NameAr ?? "وجبة صحية طازجة";
            var nameEn = meal?.NameEn ?? "Fresh Healthy Meal";
            var image = meal?.Images?.OrderByDescending(x => x.IsPrimary).Select(x => x.ImageUrl).FirstOrDefault()
                ?? (nameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) || nameAr.Contains("تونة")
                    ? "assets/images/receipts/fresh-tuna-salad.jpg"
                    : "assets/images/receipts/grilled-chicken-bowl.jpg");

            var ingAr = meal?.Ingredients?.Count > 0
                ? string.Join("، ", meal.Ingredients.Select(i => i.Ingredient != null ? i.Ingredient.NameAr : "مكون صحي" ?? "مكون صحي").Take(6))
                : (nameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase)
                    ? "تونة طازجة، كينوا، خس، خيار، طماطم، زيت زيتون بكر"
                    : "دجاج مشوي، أرز بني، بروكلي، زبادي خفيف");

            var ingEn = meal?.Ingredients?.Count > 0
                ? string.Join(", ", meal.Ingredients.Select(i => i.Ingredient != null ? i.Ingredient.NameEn : "Healthy ingredient" ?? "Healthy ingredient").Take(6))
                : (nameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase)
                    ? "Fresh tuna, quinoa, lettuce, cucumber, cherry tomatoes, olive oil"
                    : "Grilled chicken, brown rice, broccoli, light yogurt sauce");

            var allAr = meal?.Allergens?.Count > 0
                ? string.Join("، ", meal.Allergens.Select(a => a.AllergenCode).Take(3))
                : (nameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "أسماك" : "حليب، سمسم");

            var allEn = meal?.Allergens?.Count > 0
                ? string.Join(", ", meal.Allergens.Select(a => a.AllergenCode).Take(3))
                : (nameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "Fish" : "Milk, sesame");

            var prepAr = !string.IsNullOrWhiteSpace(meal?.RestaurantNotes)
                ? meal.RestaurantNotes
                : (nameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "بدون بصل، ليمون إضافي" : "بدون فلفل حار، الصوص منفصل");

            var prepEn = !string.IsNullOrWhiteSpace(meal?.RestaurantNotes)
                ? meal.RestaurantNotes
                : (nameEn.Contains("Tuna", StringComparison.OrdinalIgnoreCase) ? "No onion, extra lemon" : "No chili, sauce on the side");

            var itemDto = new MealBoxLabelItemDto(
                selection.MealId,
                nameAr,
                nameEn,
                image,
                new MealLabelNutritionDto(
                    meal?.Calories ?? 540,
                    meal?.ProteinG ?? 44,
                    meal?.CarbsG ?? 48,
                    meal?.FatG ?? 18),
                ingAr,
                ingEn,
                allAr,
                allEn,
                prepAr,
                prepEn,
                settings?.ShowPackagingDate ?? true ? packagingDate : null,
                settings?.ShowOrderReference ?? true ? orderRef : null,
                settings?.ShowQuantity ?? true ? 1 : 1);

            labels.Add(new MealBoxLabelDataDto(
                effectiveTemplate.Key,
                dimensions,
                restaurantBrand,
                appBrand,
                itemDto));
        }

        return labels;
    }

    public async Task<Result> LogPrintAsync(
        Guid restaurantId,
        Guid? orderId,
        Guid? mealId,
        Guid templateId,
        int templateVersion,
        Guid userId,
        string? clientIp,
        string printStatus,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var log = new MealLabelPrintLog
        {
            RestaurantId = restaurantId,
            OrderId = orderId,
            MealId = mealId,
            TemplateId = templateId,
            TemplateVersion = templateVersion,
            PrintedByUserId = userId,
            PrintedAtUtc = _clock.UtcNow,
            ClientIp = clientIp,
            PrintStatus = string.IsNullOrWhiteSpace(printStatus) ? "Success" : printStatus.Trim(),
            Notes = notes,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.MealLabelPrintLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<MealBoxLabelTemplate> ResolveEffectiveTemplateAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var settings = await _db.RestaurantMealLabelSettings
            .AsNoTracking()
            .Include(x => x.AdminAssignedTemplate)
            .Include(x => x.SelectedTemplate)
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);

        return await ResolveEffectiveTemplateEntityAsync(restaurantId, settings, cancellationToken);
    }

    private async Task<MealBoxLabelTemplate> ResolveEffectiveTemplateEntityAsync(
        Guid restaurantId,
        RestaurantMealLabelSettings? settings,
        CancellationToken cancellationToken)
    {
        if (settings?.AdminAssignedTemplate is { IsEnabled: true, IsPublished: true } adminTemplate)
            return adminTemplate;

        if (settings is { AllowRestaurantSelection: true } &&
            settings.SelectedTemplate is { IsEnabled: true, IsPublished: true, IsRestaurantVisible: true } selectedTemplate)
            return selectedTemplate;

        var globalDefault = await _db.MealBoxLabelTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsGlobalDefault && x.IsEnabled && x.IsPublished, cancellationToken);

        if (globalDefault is not null)
            return globalDefault;

        var fallback = await _db.MealBoxLabelTemplates
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.IsPublished)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (fallback is not null)
            return fallback;

        throw new InvalidOperationException("No published meal box label template is available in the system.");
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

        return "HW";
    }

    private static MealBoxLabelTemplateDto MapToDto(MealBoxLabelTemplate t) =>
        new(
            t.Id,
            t.Key,
            t.NameAr,
            t.NameEn,
            t.DescriptionAr,
            t.DescriptionEn,
            t.DefaultWidthMm,
            t.DefaultHeightMm,
            t.DefaultOrientation,
            t.IsEnabled,
            t.IsPublished,
            t.IsRestaurantVisible,
            t.IsGlobalDefault,
            t.Version,
            t.ConfigurationJson,
            t.PublishedAtUtc);
}

