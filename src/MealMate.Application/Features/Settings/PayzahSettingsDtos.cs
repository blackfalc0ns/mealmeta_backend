using MealMate.Application.Common.Messaging;
namespace MealMate.Application.Features.Settings;
public sealed record PayzahSettingsDto(string GatewayName, bool IsSandbox, string BaseUrl, string MaskedPrivateKey,
    string DefaultCurrency, string CountryCode, DateTime? LastUpdatedAtUtc, bool HasPrivateKey, string CallbackUrl, bool IsReady);
public sealed record GetPayzahSettingsQuery() : IQuery<PayzahSettingsDto>;
public sealed record UpdatePayzahSettingsCommand(bool IsSandbox, string? RawPrivateKey) : ICommand<PayzahSettingsDto>;
public sealed record TestPayzahConnectionCommand(string? RawPrivateKey, bool IsSandbox) : ICommand<bool>;
