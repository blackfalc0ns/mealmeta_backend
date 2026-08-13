using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.Settings;

public sealed record MyFatoorahSettingsDto(
    string GatewayName,
    bool IsSandbox,
    string BaseUrl,
    string MaskedApiToken,
    string MaskedWebhookSecret,
    bool IsKnetEnabled,
    bool IsVisaMasterEnabled,
    bool IsApplePayEnabled,
    string DefaultCurrency,
    string CountryCode,
    DateTime? LastUpdatedAtUtc,
    bool HasApiToken,
    bool HasWebhookSecret,
    string WebhookUrl,
    string WebhookVersion,
    bool IsReady
);

public sealed record GetMyFatoorahSettingsQuery() : IQuery<MyFatoorahSettingsDto>;

public sealed record UpdateMyFatoorahSettingsCommand(
    bool IsSandbox,
    string BaseUrl,
    string? RawApiToken,
    string? RawWebhookSecret,
    bool IsKnetEnabled,
    bool IsVisaMasterEnabled,
    bool IsApplePayEnabled,
    string DefaultCurrency,
    string CountryCode
) : ICommand<MyFatoorahSettingsDto>;

public sealed record TestMyFatoorahConnectionCommand(
    string? RawApiToken,
    bool IsSandbox
) : ICommand<bool>;
