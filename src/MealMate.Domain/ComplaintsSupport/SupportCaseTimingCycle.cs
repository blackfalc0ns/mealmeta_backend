using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class SupportCaseTimingCycle : BaseEntity
{
    private SupportCaseTimingCycle() { }

    public Guid SupportCaseId { get; private set; }
    public int CycleNumber { get; private set; }
    public Guid? PolicyId { get; private set; }
    public int? PolicyVersion { get; private set; }
    public Guid? MatchedRuleId { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FirstResponseDeadlineUtc { get; private set; }
    public DateTime? ResolutionDeadlineUtc { get; private set; }
    public DateTime? EscalationDeadlineUtc { get; private set; }
    public DateTime? LastWorkerAttemptAtUtc { get; private set; }
    public DateTime? FirstResponseAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public bool IsFirstResponseBreached { get; private set; }
    public bool IsResolutionBreached { get; private set; }
    public bool IsEscalated { get; private set; }
    public DateTime? EscalatedAtUtc { get; private set; }
    public string? EscalationReason { get; private set; }
    public bool IsPolicyUnconfigured { get; private set; }
    public bool IsUnrouted { get; private set; }
    public string? Notes { get; private set; }

    public static SupportCaseTimingCycle StartConfigured(
        Guid supportCaseId,
        int cycleNumber,
        Guid policyId,
        int policyVersion,
        Guid matchedRuleId,
        DateTime startedAtUtc,
        DateTime firstResponseDeadlineUtc,
        DateTime resolutionDeadlineUtc,
        TimeSpan? escalationThreshold = null)
    {
        if (supportCaseId == Guid.Empty)
            throw new ArgumentException("Support case ID is required.", nameof(supportCaseId));
        if (cycleNumber < 1)
            throw new ArgumentException("Cycle number must be 1 or higher.", nameof(cycleNumber));
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy ID is required.", nameof(policyId));
        if (policyVersion < 1)
            throw new ArgumentException("Policy version must be 1 or higher.", nameof(policyVersion));
        if (matchedRuleId == Guid.Empty)
            throw new ArgumentException("Matched rule ID is required.", nameof(matchedRuleId));
        if (startedAtUtc.Kind != DateTimeKind.Utc || firstResponseDeadlineUtc.Kind != DateTimeKind.Utc || resolutionDeadlineUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("All timestamps must be UTC.");
        if (firstResponseDeadlineUtc < startedAtUtc || resolutionDeadlineUtc < firstResponseDeadlineUtc)
            throw new ArgumentException("Deadlines must follow chronological ordering.");
        if (escalationThreshold.HasValue && escalationThreshold.Value <= TimeSpan.Zero)
            throw new ArgumentException("Escalation threshold must be positive when configured.", nameof(escalationThreshold));

        return new SupportCaseTimingCycle
        {
            SupportCaseId = supportCaseId,
            CycleNumber = cycleNumber,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            MatchedRuleId = matchedRuleId,
            StartedAtUtc = startedAtUtc,
            FirstResponseDeadlineUtc = firstResponseDeadlineUtc,
            ResolutionDeadlineUtc = resolutionDeadlineUtc,
            EscalationDeadlineUtc = escalationThreshold.HasValue ? startedAtUtc.Add(escalationThreshold.Value) : null,
            IsPolicyUnconfigured = false,
        };
    }

    public static SupportCaseTimingCycle StartUnconfigured(
        Guid supportCaseId,
        int cycleNumber,
        DateTime startedAtUtc,
        Guid? policyId = null,
        int? policyVersion = null,
        string? reason = null)
    {
        if (supportCaseId == Guid.Empty)
            throw new ArgumentException("Support case ID is required.", nameof(supportCaseId));
        if (cycleNumber < 1)
            throw new ArgumentException("Cycle number must be 1 or higher.", nameof(cycleNumber));
        if (startedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Start timestamp must be UTC.", nameof(startedAtUtc));

        return new SupportCaseTimingCycle
        {
            SupportCaseId = supportCaseId,
            CycleNumber = cycleNumber,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            StartedAtUtc = startedAtUtc,
            IsPolicyUnconfigured = true,
            Notes = string.IsNullOrWhiteSpace(reason) ? "No active policy or matching timing rule found." : reason.Trim(),
        };
    }

    public bool RecordFirstResponse(DateTime respondedAtUtc)
    {
        if (FirstResponseAtUtc.HasValue || respondedAtUtc.Kind != DateTimeKind.Utc)
            return false;

        FirstResponseAtUtc = respondedAtUtc;
        if (FirstResponseDeadlineUtc.HasValue && respondedAtUtc > FirstResponseDeadlineUtc.Value)
        {
            IsFirstResponseBreached = true;
        }

        return true;
    }

    public bool RecordResolution(DateTime resolvedAtUtc)
    {
        if (ResolvedAtUtc.HasValue || resolvedAtUtc.Kind != DateTimeKind.Utc)
            return false;

        ResolvedAtUtc = resolvedAtUtc;
        if (ResolutionDeadlineUtc.HasValue && resolvedAtUtc > ResolutionDeadlineUtc.Value)
        {
            IsResolutionBreached = true;
        }

        return true;
    }

    public void RecordClose(DateTime closedAtUtc)
    {
        if (closedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Close timestamp must be UTC.", nameof(closedAtUtc));
        ClosedAtUtc = closedAtUtc;
    }

    public void MarkFirstResponseBreached()
    {
        IsFirstResponseBreached = true;
    }

    public void MarkResolutionBreached()
    {
        IsResolutionBreached = true;
    }

    public void Escalate(string reason, DateTime escalatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Escalation reason cannot be empty.", nameof(reason));
        if (escalatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Escalation timestamp must be UTC.", nameof(escalatedAtUtc));

        IsEscalated = true;
        EscalatedAtUtc = escalatedAtUtc;
        EscalationReason = reason.Trim();
    }

    public void SetUnrouted(bool unrouted, string? notes = null)
    {
        IsUnrouted = unrouted;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes.Trim();
        }
    }

    public void MarkWorkerAttempt(DateTime attemptedAtUtc)
    {
        if (attemptedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Worker attempt timestamp must be UTC.", nameof(attemptedAtUtc));
        LastWorkerAttemptAtUtc = attemptedAtUtc;
    }
}
