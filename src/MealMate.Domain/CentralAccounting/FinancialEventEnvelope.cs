namespace MealMate.Domain.CentralAccounting;

public sealed record FinancialEventEnvelope(
    Guid EventId,
    Guid LegalEntityId,
    string SourceType,
    Guid SourceId,
    Guid? ServiceDayId,
    string EffectType,
    int SchemaVersion,
    string IdempotencyKey,
    DateTimeOffset OccurredAtUtc,
    DateOnly BusinessDate,
    Guid RuleVersionId,
    string PayloadHash);
