namespace MealMate.Application.Common.Exceptions;

/// <summary>
/// A single field validation failure identified by a stable message code.
/// Arguments are passed to the localized resource string ({0}, {1}, ...).
/// </summary>
public sealed record ValidationFailureDetail(string Code, object?[] Arguments);
