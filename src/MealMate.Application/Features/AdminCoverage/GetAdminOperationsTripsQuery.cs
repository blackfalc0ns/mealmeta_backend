using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminCoverage;

public sealed record GetAdminOperationsTripsQuery(
    string CountryCode,
    DateOnly ServiceDate,
    Guid? GovernorateId,
    Guid? RestaurantId,
    string? Search,
    string? TripStatus,
    int Page = 1,
    int PageSize = 25) : IQuery<OperationsTripPageDto>;

public sealed class GetAdminOperationsTripsQueryHandler : IQueryHandler<GetAdminOperationsTripsQuery, OperationsTripPageDto>
{
    private readonly IAppDbContext _db;
    private readonly AdminCoverageAccess _access;
    private readonly IDateTimeProvider _clock;

    public GetAdminOperationsTripsQueryHandler(IAppDbContext db, AdminCoverageAccess access, IDateTimeProvider clock)
        => (_db, _access, _clock) = (db, access, clock);

    public async Task<Result<OperationsTripPageDto>> Handle(GetAdminOperationsTripsQuery request, CancellationToken cancellationToken)
    {
        if (request.ServiceDate == default) return Error.Validation("admin_coverage.service_date_required");
        if (request.Page < 1 || request.PageSize is < 1 or > 100) return Error.Validation("admin_coverage.trip_pagination_invalid");
        TripStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(request.TripStatus))
        {
            if (!Enum.TryParse<TripStatus>(request.TripStatus, true, out var parsed) || !Enum.IsDefined(parsed))
                return Error.Validation("admin_coverage.trip_status_invalid");
            statusFilter = parsed;
        }

        var accessResult = await _access.ResolveAsync(request.CountryCode, cancellationToken, PermissionCodes.AdminCoverageOperationsView);
        if (accessResult.IsFailure) return accessResult.Error;
        var access = accessResult.Value;
        var regionById = access.Regions.ToDictionary(region => region.Id);
        if (request.GovernorateId is Guid governorateId)
        {
            var selectable = access.GetAuthorizedRegionIds();
            var governorate = access.Regions.FirstOrDefault(region => region.Id == governorateId && region.Type == RegionType.Governorate);
            if (governorate is null || !selectable.Contains(governorateId) || !governorate.IsActive)
                return Error.Forbidden("admin_coverage.governorate_not_allowed");
        }

        var authorizedTrips = await AdminCoverageTripScope.LoadAsync(_db, access, request.ServiceDate, null, request.RestaurantId, null, cancellationToken);
        IEnumerable<AdminCoverageTripRecord> filtered = authorizedTrips;
        if (request.GovernorateId is Guid selectedGovernorate)
            filtered = filtered.Where(trip => AdminCoverageTripScope.AllStopsBelongToGovernorate(trip, selectedGovernorate, regionById));
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(trip => trip.Trip.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                || trip.RestaurantNameAr.Contains(search, StringComparison.OrdinalIgnoreCase)
                || trip.RestaurantNameEn.Contains(search, StringComparison.OrdinalIgnoreCase)
                || trip.Stops.Any(stop => stop.OrderCode.Contains(search, StringComparison.OrdinalIgnoreCase)));
        var scoped = filtered.ToArray();
        var inProgressCount = scoped.Count(trip => trip.Trip.Status == TripStatus.InProgress);
        var scheduledCount = scoped.Count(trip => trip.Trip.Status is TripStatus.Planned or TripStatus.Assigned);
        var statusFiltered = statusFilter is TripStatus status ? scoped.Where(trip => trip.Trip.Status == status) : scoped;
        var sorted = statusFiltered.OrderByDescending(trip => trip.Trip.PlannedDate)
            .ThenBy(trip => trip.Trip.Code, StringComparer.OrdinalIgnoreCase).ThenBy(trip => trip.Trip.Id).ToArray();
        var totalCount = sorted.Length;
        var items = sorted.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).Select(ToTripDto).ToArray();
        return new OperationsTripPageDto(_clock.UtcNow, items, request.Page, request.PageSize, totalCount, inProgressCount, scheduledCount);
    }

    private static OperationsTripDto ToTripDto(AdminCoverageTripRecord record)
        => new(record.Trip.Id, record.Trip.Code, record.Trip.RestaurantId, record.Trip.PlannedDate,
            record.Trip.Status.ToString(), record.DriverProfileId, record.Stops.Count,
            record.Stops.Count(stop => stop.Status == TripStopStatus.Delivered),
            record.Stops.Count(stop => stop.Status == TripStopStatus.Failed));
}
