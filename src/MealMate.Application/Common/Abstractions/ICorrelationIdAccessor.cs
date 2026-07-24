namespace MealMate.Application.Common.Abstractions;

/// <summary>Correlation id for the current request (from header or generated).</summary>
public interface ICorrelationIdAccessor
{
    string CorrelationId { get; }
}
