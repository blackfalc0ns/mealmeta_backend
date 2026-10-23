using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class SupportTimingRule : BaseEntity
{
    private SupportTimingRule() { }

    public Guid PolicyId { get; private set; }
    public SupportCaseChannel? Channel { get; private set; }
    public SupportCasePriority? Priority { get; private set; }
    public string? Category { get; private set; }
    public TimeSpan FirstResponseDeadline { get; private set; }
    public TimeSpan ResolutionDeadline { get; private set; }
    public TimeSpan EscalationThreshold { get; private set; }
    public bool RequiresImmediateAck { get; private set; }
    public int EvaluationOrder { get; private set; }

    public static SupportTimingRule Create(
        Guid policyId,
        SupportCaseChannel? channel,
        SupportCasePriority? priority,
        string? category,
        TimeSpan firstResponseDeadline,
        TimeSpan resolutionDeadline,
        TimeSpan escalationThreshold,
        bool requiresImmediateAck,
        int evaluationOrder)
    {
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy ID must be specified.", nameof(policyId));
        if (firstResponseDeadline <= TimeSpan.Zero)
            throw new ArgumentException("First response deadline must be positive.", nameof(firstResponseDeadline));
        if (resolutionDeadline <= TimeSpan.Zero)
            throw new ArgumentException("Resolution deadline must be positive.", nameof(resolutionDeadline));
        if (firstResponseDeadline > resolutionDeadline)
            throw new ArgumentException("First response deadline cannot exceed resolution deadline.", nameof(firstResponseDeadline));
        if (escalationThreshold <= TimeSpan.Zero)
            throw new ArgumentException("Escalation threshold must be positive.", nameof(escalationThreshold));
        if (category is { Length: > 100 })
            throw new ArgumentException("Category cannot exceed 100 characters.", nameof(category));
        if (evaluationOrder < 0)
            throw new ArgumentException("Evaluation order cannot be negative.", nameof(evaluationOrder));

        return new SupportTimingRule
        {
            PolicyId = policyId,
            Channel = channel,
            Priority = priority,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            FirstResponseDeadline = firstResponseDeadline,
            ResolutionDeadline = resolutionDeadline,
            EscalationThreshold = escalationThreshold,
            RequiresImmediateAck = requiresImmediateAck,
            EvaluationOrder = evaluationOrder,
        };
    }

    public bool Matches(SupportCaseChannel channel, SupportCasePriority? priority, string? category)
    {
        if (Channel.HasValue && Channel.Value != channel)
            return false;
        if (Priority.HasValue && Priority.Value != priority)
            return false;
        if (!string.IsNullOrWhiteSpace(Category) && !string.Equals(Category, category, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
