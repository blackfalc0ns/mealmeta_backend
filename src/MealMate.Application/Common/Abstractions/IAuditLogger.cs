namespace MealMate.Application.Common.Abstractions;

public sealed record AuditEntry(
    string Action,
    string EntityType,
    Guid? EntityId = null,
    object? Before = null,
    object? After = null,
    string? Reason = null,
    string? SourceChannel = null);

/// <summary>Writes append-only system audit records.</summary>
public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
