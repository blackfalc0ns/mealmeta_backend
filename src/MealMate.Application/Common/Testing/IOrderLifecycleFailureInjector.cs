namespace MealMate.Application.Common.Testing;

public interface IOrderLifecycleFailureInjector
{
    Task ExecuteAsync(string boundary, CancellationToken cancellationToken = default);
}

public sealed class NullOrderLifecycleFailureInjector : IOrderLifecycleFailureInjector
{
    public static readonly NullOrderLifecycleFailureInjector Instance = new();

    public Task ExecuteAsync(string boundary, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
