using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Opaque, authenticated token that carries pending restaurant-registration
/// credentials between the <c>start</c> and <c>verify</c> stages without
/// persisting them to the database.
/// </summary>
public sealed record PendingRegistration(
    string Email,
    string Phone,
    string Password,
    string FullName);

public sealed record CreatedRegistrationToken(
    string Token,
    DateTime ExpiresAtUtc);

public interface IRestaurantAccountRegistrationTokenService
{
    CreatedRegistrationToken CreateToken(PendingRegistration payload);

    Result<PendingRegistration> ResolveToken(string token);
}
