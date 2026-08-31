using System.Text.Json;

namespace MealMate.Application.Features.MenuCycles;

public static class MenuCyclePendingSequence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<Guid> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Guid>();
        return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
    }

    public static string Serialize(IReadOnlyList<Guid> ids) =>
        JsonSerializer.Serialize(ids, JsonOptions);

    public static IReadOnlyList<Guid> SeedFromActive(IReadOnlyList<Guid> activeOrderedIds) =>
        activeOrderedIds.ToList();

    public static IReadOnlyList<Guid> Insert(IReadOnlyList<Guid> pending, Guid newDayId, int targetDayNumber)
    {
        if (targetDayNumber < 1 || targetDayNumber > pending.Count + 1)
            throw new ArgumentOutOfRangeException(nameof(targetDayNumber));
        var list = pending.ToList();
        list.Insert(targetDayNumber - 1, newDayId);
        return list;
    }

    public static IReadOnlyList<Guid> Swap(IReadOnlyList<Guid> pending, Guid dayId, int targetDayNumber)
    {
        if (targetDayNumber < 1 || targetDayNumber > pending.Count)
            throw new ArgumentOutOfRangeException(nameof(targetDayNumber));
        var list = pending.ToList();
        var from = list.IndexOf(dayId);
        if (from < 0) throw new InvalidOperationException("day_not_in_pending");
        var to = targetDayNumber - 1;
        if (from == to) return list;
        (list[from], list[to]) = (list[to], list[from]);
        return list;
    }

    /// <summary>True when previous working day was the last slot of a loop (slot N).</summary>
    public static bool ShouldActivate(int previousWorkingIndex, int activeCount)
    {
        if (activeCount <= 0 || previousWorkingIndex < 0) return false;
        return (previousWorkingIndex % activeCount) == (activeCount - 1);
    }

    public static IReadOnlyList<Guid> ResolveOrderForWorkingIndex(
        IReadOnlyList<Guid> activeOrderedIds,
        IReadOnlyList<Guid> pendingOrderedIds,
        int workingIndex,
        int activeCount)
    {
        if (pendingOrderedIds.Count == 0 || activeCount <= 0) return activeOrderedIds;
        if (ShouldActivate(workingIndex - 1, activeCount))
            return pendingOrderedIds;
        return activeOrderedIds;
    }

    /// <summary>
    /// Picks day for a working index while pending is still unapplied.
    /// Before loop boundary: active modulo. From the first day of the next loop onward: pending modulo from 0.
    /// </summary>
    public static Guid? PickDayIdForWorkingIndex(
        IReadOnlyList<Guid> activeOrderedIds,
        IReadOnlyList<Guid> pendingOrderedIds,
        int workingIndex)
    {
        var activeCount = activeOrderedIds.Count;
        if (activeCount == 0 && pendingOrderedIds.Count == 0) return null;
        if (pendingOrderedIds.Count == 0 || activeCount <= 0)
            return activeOrderedIds[workingIndex % activeOrderedIds.Count];

        var activationWorkingIndex = activeCount; // first day after completing indices 0..N-1
        if (workingIndex < activationWorkingIndex)
            return activeOrderedIds[workingIndex % activeCount];

        var pendingIndex = workingIndex - activationWorkingIndex;
        return pendingOrderedIds[pendingIndex % pendingOrderedIds.Count];
    }
}
