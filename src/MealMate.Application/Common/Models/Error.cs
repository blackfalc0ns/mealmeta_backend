namespace MealMate.Application.Common.Models;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
}

/// <summary>
/// A machine-readable error with a stable <see cref="Code"/>. The API translates
/// the code to a localized message; <see cref="Description"/> is an optional
/// English fallback for logs and missing resources.
/// </summary>
public sealed record Error(
    string Code,
    string? Description = null,
    ErrorType Type = ErrorType.Failure,
    object?[]? Arguments = null,
    IReadOnlyDictionary<string, object?>? Metadata = null)
{
    public static readonly Error None = new(string.Empty, null, ErrorType.Failure);

    public static Error Validation(string code, string? description = null)
        => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string? description = null)
        => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string? description = null)
        => new(code, description, ErrorType.Conflict);

    public static Error ConflictWithMetadata(string code, IReadOnlyDictionary<string, object?> metadata)
        => new(code, null, ErrorType.Conflict, null, metadata);

    public static Error Unauthorized(string code, string? description = null)
        => new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string? description = null)
        => new(code, description, ErrorType.Forbidden);

    public static Error Failure(string code, string? description = null)
        => new(code, description, ErrorType.Failure);

    public static Error ValidationWithArguments(string code, params object?[] arguments)
        => new(code, null, ErrorType.Validation, arguments);

    public static Error ValidationWithArguments(string code, string description, params object?[] arguments)
        => new(code, description, ErrorType.Validation, arguments);

    public static Error NotFoundWithArguments(string code, params object?[] arguments)
        => new(code, null, ErrorType.NotFound, arguments);

    public static Error FailureWithArguments(string code, params object?[] arguments)
        => new(code, null, ErrorType.Failure, arguments);
}
