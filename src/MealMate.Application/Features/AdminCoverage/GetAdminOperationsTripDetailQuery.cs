using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminCoverage;

public sealed record GetAdminOperationsTripDetailQuery(string CountryCode, Guid TripId) : IQuery<OperationsTripDetailDto>;

public sealed class GetAdminOperationsTripDetailQueryHandler : IQueryHandler<GetAdminOperationsTripDetailQuery, OperationsTripDetailDto>
{
    private const int MaxTrackPoints = 2000;
    private readonly IAppDbContext _db;
    private readonly AdminCoverageAccess _access;

    public GetAdminOperationsTripDetailQueryHandler(IAppDbContext db, AdminCoverageAccess access)
        => (_db, _access) = (db, access);

    public async Task<Result<OperationsTripDetailDto>> Handle(GetAdminOperationsTripDetailQuery request, CancellationToken cancellationToken)
    {
        if (request.TripId == Guid.Empty) return Error.NotFound("admin_coverage.trip_not_found");
        var accessResult = await _access.ResolveAsync(request.CountryCode, cancellationToken, PermissionCodes.AdminCoverageOperationsView);
        if (accessResult.IsFailure) return accessResult.Error;
        var trips = await AdminCoverageTripScope.LoadAsync(_db, accessResult.Value, null, request.TripId, null, null, cancellationToken);
        var record = trips.SingleOrDefault();
        if (record is null) return Error.NotFound("admin_coverage.trip_not_found");

        IReadOnlyList<RecordedMapPointDto> track = [];
        var truncated = false;
        var sourceCount = 0;
        var samplingStrategy = "complete";
        if (record.AssignedDriverProfileIds.Count > 0)
        {
            var canonicalPoints = _db.DeliveryTrackingPoints.AsNoTracking()
                .Where(point => point.TripId == record.Trip.Id && record.AssignedDriverProfileIds.Contains(point.DriverProfileId)
                    && point.Latitude >= -90m && point.Latitude <= 90m && point.Longitude >= -180m && point.Longitude <= 180m);
            sourceCount = await canonicalPoints.CountAsync(cancellationToken);
            truncated = sourceCount > MaxTrackPoints;
            if (truncated)
            {
                const int halfWindow = MaxTrackPoints / 2;
                var head = await canonicalPoints.OrderBy(point => point.RecordedAtUtc).ThenBy(point => point.Id).Take(halfWindow)
                    .Select(point => new RecordedMapPointDto(point.Latitude, point.Longitude, point.RecordedAtUtc, point.DriverProfileId, 0)).ToListAsync(cancellationToken);
                var tail = await canonicalPoints.OrderByDescending(point => point.RecordedAtUtc).ThenByDescending(point => point.Id).Take(halfWindow)
                    .Select(point => new RecordedMapPointDto(point.Latitude, point.Longitude, point.RecordedAtUtc, point.DriverProfileId, 1)).ToListAsync(cancellationToken);
                track = head.Concat(tail).OrderBy(point => point.RecordedAtUtc).ThenBy(point => point.SegmentIndex).ToArray();
                samplingStrategy = "head-tail";
            }
            else
            {
                track = await canonicalPoints.OrderBy(point => point.RecordedAtUtc).ThenBy(point => point.Id)
                    .Select(point => new RecordedMapPointDto(point.Latitude, point.Longitude, point.RecordedAtUtc, point.DriverProfileId, 0)).ToListAsync(cancellationToken);
            }
        }

        var stops = record.Stops.OrderBy(stop => stop.SequenceNumber).ThenBy(stop => stop.Id)
            .Select(stop => new OperationsTripStopDto(stop.Id, stop.SequenceNumber, stop.Status.ToString(), ValidPoint(stop.Latitude, stop.Longitude), stop.OrderCode))
            .ToArray();
        return new OperationsTripDetailDto(record.Trip.Id, record.Trip.Code, record.Trip.RestaurantId, record.Trip.PlannedDate,
            record.Trip.Status.ToString(), record.DriverProfileId, stops.Length,
            record.Stops.Count(stop => stop.Status == MealMate.Domain.Enums.TripStopStatus.Delivered),
            record.Stops.Count(stop => stop.Status == MealMate.Domain.Enums.TripStopStatus.Failed),
            stops, track, truncated, sourceCount, samplingStrategy);
    }

    private static MapPointDto? ValidPoint(decimal? latitude, decimal? longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m
            ? new MapPointDto(latitude.Value, longitude.Value)
            : null;
}
