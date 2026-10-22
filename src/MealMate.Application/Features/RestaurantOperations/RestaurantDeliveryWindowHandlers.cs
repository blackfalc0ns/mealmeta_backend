using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Catalog;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOperations;

public sealed record RestaurantDeliveryWindowDto(TimeOnly? DeliveryWindowStart, TimeOnly? DeliveryWindowEnd, bool HasConflictingWindows, int? PreparationLeadTimeMinutes = null, bool HasConflictingPreparationTimes = false);
public sealed record RestaurantDeliveryWindowSaveResult(bool RequiresReview);
public sealed record GetRestaurantDeliveryWindowQuery(Guid? RestaurantId = null) : IQuery<RestaurantDeliveryWindowDto>;
public sealed record UpdateRestaurantDeliveryWindowCommand(TimeOnly DeliveryWindowStart, TimeOnly DeliveryWindowEnd, Guid? RestaurantId = null, int? PreparationLeadTimeMinutes = null) : ICommand<RestaurantDeliveryWindowSaveResult>;
public sealed class GetRestaurantDeliveryWindowHandler(RestaurantDeliveryWindowService service) : IQueryHandler<GetRestaurantDeliveryWindowQuery, RestaurantDeliveryWindowDto>
{
    public Task<Result<RestaurantDeliveryWindowDto>> Handle(GetRestaurantDeliveryWindowQuery request, CancellationToken cancellationToken) => service.GetAsync(request.RestaurantId, cancellationToken);
}
public sealed class UpdateRestaurantDeliveryWindowHandler(RestaurantDeliveryWindowService service) : ICommandHandler<UpdateRestaurantDeliveryWindowCommand, RestaurantDeliveryWindowSaveResult>
{
    public Task<Result<RestaurantDeliveryWindowSaveResult>> Handle(UpdateRestaurantDeliveryWindowCommand request, CancellationToken cancellationToken) => service.SaveAsync(request, cancellationToken);
}

public sealed class RestaurantDeliveryWindowService(IAppDbContext db, ICurrentUser user, IDateTimeProvider clock, KuwaitTenantResolver tenantResolver)
{
    private async Task<Result<RestaurantProfile>> ResolveAsync(Guid? restaurantId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Error.Unauthorized("restaurant.authentication_required");
        var scope = RestaurantOperationsScope.ResolveRestaurantId(user, restaurantId);
        if (scope.IsFailure) return Result.Failure<RestaurantProfile>(scope.Error);
        var tenant = await tenantResolver.ResolveAsync(allowSuperAdminFallback: restaurantId.HasValue, ct);
        if (tenant.IsFailure) return Result.Failure<RestaurantProfile>(tenant.Error);
        var profile = await db.RestaurantProfiles.FirstOrDefaultAsync(x => x.Id == scope.Value, ct);
        if (profile is null) return Error.NotFound("restaurant.not_found");
        if (profile.CountryId != tenant.Value.CountryId) return Error.Forbidden("catalog.restaurant_tenant_mismatch");
        return profile;
    }

    public async Task<Result<RestaurantDeliveryWindowDto>> GetAsync(Guid? restaurantId, CancellationToken ct)
    {
        var scope = await ResolveAsync(restaurantId, ct);
        if (scope.IsFailure) return Result.Failure<RestaurantDeliveryWindowDto>(scope.Error);
        var profile = scope.Value;
        var regions = await db.RestaurantServiceRegions.AsNoTracking().Where(x => x.RestaurantId == profile.Id).ToListAsync(ct);
        var active = regions.Where(x => x.IsActive).ToList();
        var preparationTimes = (active.Count > 0 ? active : regions).Select(x => x.PreparationLeadTimeMinutes).Distinct().ToList();
        var preparation = profile.PreparationLeadTimeMinutes ?? (preparationTimes.Count == 1 && preparationTimes[0] >= 0 ? preparationTimes[0] : null);
        var preparationConflict = !profile.PreparationLeadTimeMinutes.HasValue && (preparationTimes.Count > 1 || preparationTimes.Any(x => x < 0));
        if (profile.DeliveryWindowStart.HasValue && profile.DeliveryWindowEnd.HasValue)
            return new RestaurantDeliveryWindowDto(profile.DeliveryWindowStart, profile.DeliveryWindowEnd, false, preparation, preparationConflict);
        var pairs = (active.Count > 0 ? active : regions).Select(x => (x.DeliveryWindowStart, x.DeliveryWindowEnd)).Distinct().ToList();
        return pairs.Count == 1 && pairs[0].DeliveryWindowStart < pairs[0].DeliveryWindowEnd
            ? new RestaurantDeliveryWindowDto(pairs[0].DeliveryWindowStart, pairs[0].DeliveryWindowEnd, false, preparation, preparationConflict)
            : new RestaurantDeliveryWindowDto(null, null, pairs.Count > 0, preparation, preparationConflict);
    }

    public async Task<Result<RestaurantDeliveryWindowSaveResult>> SaveAsync(UpdateRestaurantDeliveryWindowCommand request, CancellationToken ct)
    {
        var scope = await ResolveAsync(request.RestaurantId, ct);
        if (scope.IsFailure) return Result.Failure<RestaurantDeliveryWindowSaveResult>(scope.Error);
        if (request.DeliveryWindowStart >= request.DeliveryWindowEnd)
            return Error.Validation("service_region.invalid_delivery_window");
        if (request.PreparationLeadTimeMinutes < 0)
            return Error.Validation("service_region.invalid_preparation_time");
        var profile = scope.Value;
        var openCoverage = await db.RestaurantSettingsChangeRequests.Where(x =>
            x.RestaurantId == profile.Id && x.Category == RestaurantChangeCategory.Coverage &&
            (x.Status == VerificationStatus.Draft || x.Status == VerificationStatus.Submitted ||
             x.Status == VerificationStatus.UnderReview || x.Status == VerificationStatus.NeedsChanges)).ToListAsync(ct);
        foreach (var pending in openCoverage)
        {
            if (!IsTimingOnly(pending.PayloadJson)) continue;
            // Close obsolete timing proposals without representing a human approval or touching coverage proposals.
            var events = string.IsNullOrWhiteSpace(pending.DecisionEventsJson)
                ? new List<RestaurantChangeDecisionEvent>()
                : JsonSerializer.Deserialize<List<RestaurantChangeDecisionEvent>>(pending.DecisionEventsJson) ?? [];
            events.Add(new("SupersededByDirectTimingSave", null, null, clock.UtcNow, pending.BaseVersion, pending.PayloadVersion));
            pending.DecisionEventsJson = JsonSerializer.Serialize(events);
            pending.Status = VerificationStatus.Rejected;
            pending.ReviewedAtUtc = clock.UtcNow;
            pending.ReviewedByUserId = null;
            pending.ModifiedAtUtc = clock.UtcNow;
        }
        await ApplyAsync(db, profile, request.DeliveryWindowStart, request.DeliveryWindowEnd, clock.UtcNow, ct, request.PreparationLeadTimeMinutes);
        await db.SaveChangesAsync(ct);
        return new RestaurantDeliveryWindowSaveResult(false);
    }

    private static bool IsTimingOnly(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("DeliveryWindowOnly", out var flag) && flag.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    public static async Task ApplyAsync(IAppDbContext db, RestaurantProfile profile, TimeOnly start, TimeOnly end, DateTime now, CancellationToken ct, int? preparationLeadTimeMinutes = null)
    {
        profile.DeliveryWindowStart = start;
        profile.DeliveryWindowEnd = end;
        profile.PreparationLeadTimeMinutes = preparationLeadTimeMinutes;
        profile.ModifiedAtUtc = now;
        var regions = await db.RestaurantServiceRegions.Where(x => x.RestaurantId == profile.Id).ToListAsync(ct);
        foreach (var region in regions)
        {
            region.DeliveryWindowStart = start;
            region.DeliveryWindowEnd = end;
            region.PreparationLeadTimeMinutes = preparationLeadTimeMinutes;
            region.ModifiedAtUtc = now;
        }
    }
}
