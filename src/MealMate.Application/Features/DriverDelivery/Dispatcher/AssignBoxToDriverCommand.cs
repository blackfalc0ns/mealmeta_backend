using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record AssignBoxToDriverCommand(
    Guid BoxId,
    Guid DriverId,
    string? Notes = null) : ICommand<AssignBoxResponse>;

public sealed class AssignBoxToDriverCommandHandler : ICommandHandler<AssignBoxToDriverCommand, AssignBoxResponse>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public AssignBoxToDriverCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<AssignBoxResponse>> Handle(
        AssignBoxToDriverCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DriverId, cancellationToken);

        var driverName = driver?.FullNameAr ?? driver?.FullNameEn ?? "السائق المعين";

        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        var now = DateTime.UtcNow;
        var boxCode = stop?.OrderCode ?? $"#BX-{Math.Abs(request.BoxId.GetHashCode()) % 9000 + 1000}";

        if (stop is not null)
        {
            stop.Trip.Status = TripStatus.Assigned;

            var existingAssignment = await _db.DriverAssignments
                .FirstOrDefaultAsync(da => da.TripId == stop.TripId && da.DriverProfileId == request.DriverId, cancellationToken);

            if (existingAssignment is null)
            {
                var assignment = new DriverAssignment
                {
                    TripId = stop.TripId,
                    DriverProfileId = request.DriverId,
                    AssignedAtUtc = now,
                    Status = "Active"
                };
                _db.DriverAssignments.Add(assignment);
            }
            else
            {
                existingAssignment.Status = "Active";
                existingAssignment.AssignedAtUtc = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        var message = _messages.GetMessage(MessageCodes.DispatcherOrders.BoxAssignedSuccess, "تم إسناد البوكس للسائق بنجاح");

        return Result.Success(new AssignBoxResponse(
            BoxId: request.BoxId,
            BoxCode: boxCode,
            DriverId: request.DriverId,
            DriverName: driverName,
            AssignedAtUtc: now,
            Message: message));
    }
}
