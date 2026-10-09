using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Errors;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Queries;

/// <summary>
/// Call-screen display context for one v2 call: driver identity, role, delivery
/// address and the linked order data. Scoped to the call's two participants and
/// deliberately excludes any phone number — the customer phone is only ever
/// released by the delivery-contact reveal flow after qualified attempts.
/// </summary>
public sealed record VoiceCallV2CallDisplayQuery(Guid CallId, Guid ActorUserId) : IQuery<VoiceCallDisplayDto>;

public sealed record VoiceCallDisplayDto(
    Guid CallId,
    Guid TripStopId,
    string DriverName,
    string? DriverImageUrl,
    string DriverRole,
    string? VehicleType,
    string? PlateNumber,
    string DeliveryAddress,
    string DeliveryZone,
    string OrderCode,
    string MealSummary,
    string SlotLabel,
    int BoxCount,
    string StopStatus);

public sealed class VoiceCallV2CallDisplayQueryHandler
    : IQueryHandler<VoiceCallV2CallDisplayQuery, VoiceCallDisplayDto>
{
    /// <summary>Profile images are served through the uploads content route; clients prefix their baseUrl.</summary>
    public const string DriverImagePath = "/api/v1/uploads/content?key=";

    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public VoiceCallV2CallDisplayQueryHandler(IAppDbContext db, ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<VoiceCallDisplayDto>> Handle(
        VoiceCallV2CallDisplayQuery request,
        CancellationToken cancellationToken)
    {
        var call = await _db.CallSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (call is null || call.CallerUserId != request.ActorUserId && call.ReceiverUserId != request.ActorUserId)
        {
            // Same generic deny as every other participant read: no existence leak.
            return Result.Failure<VoiceCallDisplayDto>(VoiceCallErrors.TripStopNotFound());
        }

        var stop = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
            .SingleOrDefaultAsync(s => s.Id == call.TripStopId, cancellationToken);

        if (stop is null)
        {
            return Result.Failure<VoiceCallDisplayDto>(VoiceCallErrors.TripStopNotFound());
        }

        var assignment = await _db.DriverAssignments
            .AsNoTracking()
            .Where(da => da.TripId == stop.TripId && da.Status == "Active")
            .OrderByDescending(da => da.AssignedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        DriverProfileDto? driver = null;
        if (assignment is not null)
        {
            var profile = await _db.DriverProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(dp => dp.Id == assignment.DriverProfileId, cancellationToken);

            if (profile is not null)
            {
                driver = new DriverProfileDto(
                    Name: _messages.Localize(profile.FullNameAr, profile.FullNameEn),
                    ImageUrl: string.IsNullOrWhiteSpace(profile.ProfileImageStorageKey)
                        ? null
                        : DriverImagePath + Uri.EscapeDataString(profile.ProfileImageStorageKey),
                    VehicleType: profile.VehicleType,
                    PlateNumber: profile.VehiclePlate);
            }
        }

        var zone = _messages.Localize(stop.ZoneNameAr, stop.ZoneNameEn);
        var address = string.IsNullOrWhiteSpace(stop.FullAddress) ? zone : stop.FullAddress;

        return Result.Success(new VoiceCallDisplayDto(
            CallId: call.Id,
            TripStopId: stop.Id,
            DriverName: driver?.Name ?? string.Empty,
            DriverImageUrl: driver?.ImageUrl,
            DriverRole: _messages.GetMessage(
                MessageCodes.VoiceCalls.CallScreenDriverRole, "Delivery Driver"),
            VehicleType: driver?.VehicleType,
            PlateNumber: driver?.PlateNumber,
            DeliveryAddress: address,
            DeliveryZone: zone,
            OrderCode: stop.OrderCode ?? string.Empty,
            MealSummary: _messages.Localize(stop.MealSummaryAr, stop.MealSummaryEn),
            SlotLabel: stop.SlotLabel ?? string.Empty,
            BoxCount: stop.BoxCount,
            StopStatus: stop.Status.ToString()));
    }

    private sealed record DriverProfileDto(
        string Name,
        string? ImageUrl,
        string? VehicleType,
        string? PlateNumber);
}
