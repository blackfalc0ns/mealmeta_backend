namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record SubscriberCapState(
    int? MaxSubscribers,
    int ReservedSlots,
    int CommittedSlots);

public sealed record SubscriberCapOperationResult(
    bool IsSuccess,
    SubscriberCapState NewState,
    string? FailureReason = null)
{
    public static SubscriberCapOperationResult Success(SubscriberCapState newState) =>
        new(true, newState);

    public static SubscriberCapOperationResult Failure(SubscriberCapState currentState, string reason) =>
        new(false, currentState, reason);
}

public interface ISubscriberCapManager
{
    bool CanReserve(SubscriberCapState state, int slotsToReserve = 1);
    SubscriberCapOperationResult TryReserve(SubscriberCapState state, int slotsToReserve = 1);
    SubscriberCapState Release(SubscriberCapState state, int slotsToRelease = 1);
    SubscriberCapOperationResult TryCommit(SubscriberCapState state, int slotsToCommit = 1);
}

public sealed class SubscriberCapManager : ISubscriberCapManager
{
    public bool CanReserve(SubscriberCapState state, int slotsToReserve = 1)
    {
        if (slotsToReserve <= 0) return true;
        if (!state.MaxSubscribers.HasValue) return true;

        return (state.ReservedSlots + state.CommittedSlots + slotsToReserve) <= state.MaxSubscribers.Value;
    }

    public SubscriberCapOperationResult TryReserve(SubscriberCapState state, int slotsToReserve = 1)
    {
        if (slotsToReserve <= 0)
        {
            return SubscriberCapOperationResult.Success(state);
        }

        if (!CanReserve(state, slotsToReserve))
        {
            return SubscriberCapOperationResult.Failure(
                state,
                $"Subscriber cap reached ({state.MaxSubscribers}). Cannot reserve {slotsToReserve} slot(s). Currently reserved: {state.ReservedSlots}, committed: {state.CommittedSlots}.");
        }

        var newState = state with { ReservedSlots = state.ReservedSlots + slotsToReserve };
        return SubscriberCapOperationResult.Success(newState);
    }

    public SubscriberCapState Release(SubscriberCapState state, int slotsToRelease = 1)
    {
        if (slotsToRelease <= 0) return state;

        var newReserved = Math.Max(0, state.ReservedSlots - slotsToRelease);
        return state with { ReservedSlots = newReserved };
    }

    public SubscriberCapOperationResult TryCommit(SubscriberCapState state, int slotsToCommit = 1)
    {
        if (slotsToCommit <= 0)
        {
            return SubscriberCapOperationResult.Success(state);
        }

        if (state.ReservedSlots < slotsToCommit)
        {
            return SubscriberCapOperationResult.Failure(
                state,
                $"Cannot commit {slotsToCommit} slot(s); insufficient reserved slots available ({state.ReservedSlots}).");
        }

        var newState = state with
        {
            ReservedSlots = state.ReservedSlots - slotsToCommit,
            CommittedSlots = state.CommittedSlots + slotsToCommit
        };

        return SubscriberCapOperationResult.Success(newState);
    }
}
