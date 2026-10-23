using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class SupportTimingPolicy : BaseEntity
{
    private readonly List<SupportTimingRule> _rules = new();

    private SupportTimingPolicy() { }

    public int Version { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime PublishedAtUtc { get; private set; }
    public Guid PublishedByUserId { get; private set; }
    public string PublicationReason { get; private set; } = default!;
    public string IdempotencyKey { get; private set; } = default!;
    public IReadOnlyCollection<SupportTimingRule> Rules => _rules.AsReadOnly();

    public static SupportTimingPolicy Create(
        int version,
        string name,
        string? description,
        Guid publishedByUserId,
        string publicationReason,
        string idempotencyKey,
        DateTime publishedAtUtc)
    {
        if (version < 1)
            throw new ArgumentException("Policy version must be 1 or higher.", nameof(version));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Policy name must contain between 1 and 200 characters.", nameof(name));
        if (description is { Length: > 1000 })
            throw new ArgumentException("Description cannot exceed 1000 characters.", nameof(description));
        if (publishedByUserId == Guid.Empty)
            throw new ArgumentException("PublishedBy user ID is required.", nameof(publishedByUserId));
        if (string.IsNullOrWhiteSpace(publicationReason) || publicationReason.Trim().Length > 1000)
            throw new ArgumentException("Publication reason must contain between 1 and 1000 characters.", nameof(publicationReason));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 128)
            throw new ArgumentException("Idempotency key must contain between 1 and 128 characters.", nameof(idempotencyKey));
        if (publishedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Published timestamp must be UTC.", nameof(publishedAtUtc));

        return new SupportTimingPolicy
        {
            Version = version,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsActive = true,
            PublishedByUserId = publishedByUserId,
            PublicationReason = publicationReason.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
            PublishedAtUtc = publishedAtUtc,
        };
    }

    public SupportTimingRule AddRule(
        SupportCaseChannel? channel,
        SupportCasePriority? priority,
        string? category,
        TimeSpan firstResponseDeadline,
        TimeSpan resolutionDeadline,
        TimeSpan escalationThreshold,
        bool requiresImmediateAck,
        int evaluationOrder)
    {
        var rule = SupportTimingRule.Create(
            Id,
            channel,
            priority,
            category,
            firstResponseDeadline,
            resolutionDeadline,
            escalationThreshold,
            requiresImmediateAck,
            evaluationOrder);

        _rules.Add(rule);
        return rule;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public SupportTimingRule? FindMatchingRule(SupportCaseChannel channel, SupportCasePriority? priority, string? category)
    {
        return _rules
            .Where(r => r.Matches(channel, priority, category))
            .OrderBy(r => r.EvaluationOrder)
            .FirstOrDefault();
    }
}
