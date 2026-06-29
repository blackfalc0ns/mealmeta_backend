namespace MealMate.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant or business rule is violated. Mapped to a 422 /
/// 400 ProblemDetails at the API boundary.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string? Code { get; }
}
