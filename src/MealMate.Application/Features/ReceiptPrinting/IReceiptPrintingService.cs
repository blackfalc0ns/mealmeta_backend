using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ReceiptPrinting;

public interface IReceiptPrintingService
{
    Task<IReadOnlyList<ReceiptTemplateDto>> GetAllTemplatesForAdminAsync(CancellationToken cancellationToken = default);

    Task<ReceiptTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<ReceiptTemplateDto>> UpdateConfigurationAsync(Guid id, string configurationJson, CancellationToken cancellationToken = default);

    Task<Result<ReceiptTemplateDto>> PublishTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<ReceiptTemplateDto>> UpdateVisibilityAsync(Guid id, bool isEnabled, bool isRestaurantVisible, CancellationToken cancellationToken = default);

    Task<Result> SetGlobalDefaultTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RestaurantReceiptSettingsDto> GetRestaurantSettingsAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task<Result<RestaurantReceiptSettingsDto>> AdminUpdateRestaurantSettingsAsync(
        Guid restaurantId,
        Guid? adminAssignedTemplateId,
        bool allowRestaurantSelection,
        CancellationToken cancellationToken = default);

    Task<Result<RestaurantReceiptSettingsDto>> RestaurantSelectTemplateAsync(
        Guid restaurantId,
        Guid selectedTemplateId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReceiptTemplateDto>> GetAllowedTemplatesForRestaurantAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default);

    Task<Result<OperationalReceiptDto>> GetOperationalReceiptAsync(
        Guid orderId,
        Guid? requestingRestaurantId = null,
        CancellationToken cancellationToken = default);

    Task<Result<OperationalReceiptDto>> GetPreviewOperationalReceiptAsync(
        Guid? templateId = null,
        Guid? restaurantId = null,
        CancellationToken cancellationToken = default);

    Task<Result> LogPrintAsync(
        Guid orderId,
        Guid restaurantId,
        Guid templateId,
        int templateVersion,
        Guid printedByUserId,
        string? clientIp,
        string? printStatus,
        string? notes,
        CancellationToken cancellationToken = default);
}
