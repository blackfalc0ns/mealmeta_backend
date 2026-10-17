using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.MealLabels;

public interface IMealLabelService
{
    Task<IReadOnlyList<MealBoxLabelTemplateDto>> GetAllTemplatesForAdminAsync(CancellationToken cancellationToken = default);

    Task<MealBoxLabelTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<MealBoxLabelTemplateDto>> UpdateConfigurationAsync(
        Guid id,
        string configurationJson,
        CancellationToken cancellationToken = default);

    Task<Result<MealBoxLabelTemplateDto>> PublishTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<MealBoxLabelTemplateDto>> UpdateVisibilityAsync(
        Guid id,
        bool isEnabled,
        bool isRestaurantVisible,
        CancellationToken cancellationToken = default);

    Task<Result> SetGlobalDefaultTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RestaurantMealLabelSettingsDto> GetRestaurantSettingsAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task<Result<RestaurantMealLabelSettingsDto>> AdminUpdateRestaurantSettingsAsync(
        Guid restaurantId,
        AdminUpdateRestaurantMealLabelSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<RestaurantMealLabelSettingsDto>> RestaurantSelectTemplateAsync(
        Guid restaurantId,
        RestaurantSelectMealLabelTemplateRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealBoxLabelTemplateDto>> GetAllowedTemplatesForRestaurantAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default);

    Task<Result<MealBoxLabelDataDto>> GetPreviewMealLabelAsync(
        Guid? templateId = null,
        Guid? restaurantId = null,
        Guid? mealId = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<MealBoxLabelDataDto>>> GetOrderMealLabelsAsync(
        Guid orderId,
        Guid restaurantId,
        CancellationToken cancellationToken = default);

    Task<Result> LogPrintAsync(
        Guid restaurantId,
        Guid? orderId,
        Guid? mealId,
        Guid templateId,
        int templateVersion,
        Guid userId,
        string? clientIp,
        string printStatus,
        string? notes,
        CancellationToken cancellationToken = default);
}
