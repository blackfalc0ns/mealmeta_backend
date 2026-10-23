using MealMate.Domain.Enums;

namespace MealMate.Application.Common.Models;

public sealed record SupportOperatorScope(
    Guid Id,
    AccessScopeType ScopeType,
    Guid? CountryId,
    Guid? RegionId,
    Guid? RestaurantId,
    Guid? EntityId,
    string? EntityType,
    Guid? TenantId);

public sealed record SupportOperatorCandidate(
    Guid UserId,
    UserType UserType,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> PermissionCodes,
    IReadOnlyList<SupportOperatorScope> Scopes);
