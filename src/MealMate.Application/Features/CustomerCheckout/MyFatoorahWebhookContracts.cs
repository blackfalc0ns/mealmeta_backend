using System.Text.Json.Serialization;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record MyFatoorahWebhookV2Payload(
    MyFatoorahWebhookV2Event? Event,
    MyFatoorahWebhookV2Data? Data);

public sealed record MyFatoorahWebhookV2Event(
    int Code,
    string Name,
    string? CountryIsoCode,
    DateTime? CreationDate,
    string? Reference);

public sealed record MyFatoorahWebhookV2Data(
    MyFatoorahWebhookV2Invoice Invoice,
    MyFatoorahWebhookV2Transaction Transaction,
    MyFatoorahWebhookV2Amount Amount);

public sealed record MyFatoorahWebhookV2Invoice(
    string Id,
    string Status,
    string? UserDefinedField,
    string? ExternalIdentifier);

public sealed record MyFatoorahWebhookV2Transaction(
    string Status,
    string PaymentId);

public sealed record MyFatoorahWebhookV2Amount(
    string BaseCurrency,
    string ValueInBaseCurrency);

public sealed record MyFatoorahWebhookExecutionResult(
    int StatusCode,
    object? Response);
