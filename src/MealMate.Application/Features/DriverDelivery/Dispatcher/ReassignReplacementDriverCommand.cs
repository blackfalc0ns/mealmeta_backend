using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record ReassignReplacementDriverCommand(Guid IssueId, Guid ReplacementDriverId, string? Notes = null) : ICommand<ReassignReplacementDriverResponseDto>;

/// <summary>Compatibility endpoint that opens a persisted handover; custody changes only at receipt completion.</summary>
public sealed class ReassignReplacementDriverCommandHandler : ICommandHandler<ReassignReplacementDriverCommand, ReassignReplacementDriverResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IAuditLogger _audit;
    private readonly ICurrentUser? _user;
    private readonly IDateTimeProvider? _clock;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public ReassignReplacementDriverCommandHandler(IAppDbContext db, ILocalizedMessageProvider messages, IAuditLogger audit,
        ICurrentUser? currentUser = null, IDateTimeProvider? clock = null,
        IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null)
        => (_db, _messages, _audit, _user, _clock, _transactions, _locks) = (db, messages, audit, currentUser, clock, transactions, locks);

    public async Task<Result<ReassignReplacementDriverResponseDto>> Handle(ReassignReplacementDriverCommand request, CancellationToken ct)
    {
        if (_user is null || !_user.IsAuthenticated || !_user.UserId.HasValue || _user.UserType is not (MealMate.Domain.Enums.UserType.DeliveryManager or MealMate.Domain.Enums.UserType.Admin))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", _messages.GetMessage("dispatcher_support.forbidden", "Dispatcher access is required."));
        if (_clock is null || _transactions is null || _locks is null)
            return Error.Conflict("HANDOVER_INFRASTRUCTURE_UNAVAILABLE", _messages.GetMessage("driver_support_handover.infrastructure_unavailable", "Handover transactions and locks are unavailable."));
        if (request.ReplacementDriverId == Guid.Empty) return Error.Validation("INVALID_REPLACEMENT_DRIVER", _messages.GetMessage("driver_support_handover.invalid_replacement_driver", "Invalid replacement driver id."));
        var notes = request.Notes?.Trim();
        if (notes?.Length > 500) return Error.Validation("NOTES_TOO_LONG", _messages.GetMessage("dispatcher_support.notes_too_long", "Notes must not exceed 500 characters."));
        var stableKeyMaterial = $"{_user.UserId.Value:N}|{request.IssueId:N}|{request.ReplacementDriverId:N}|{notes}";
        var key = $"legacy-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableKeyMaterial)))}";
        var handovers = new DriverSupportHandoverHandlers(_db, _user, _messages, _clock, _transactions, _locks);
        var created = await handovers.Handle(new CreateDriverSupportHandoverCommand(request.IssueId, request.ReplacementDriverId, key, notes), ct);
        if (created.IsFailure) return created.Error;
        await _audit.LogAsync(new AuditEntry("DriverSupportHandoverRequested", nameof(DriverSupportHandover), created.Value.HandoverId,
            Before: new { Status = "NoHandover" }, After: new { IssueId = created.Value.IssueId, created.Value.OriginalDriverId, created.Value.ReplacementDriverId, created.Value.Status },
            Reason: notes, SourceChannel: "DispatcherDashboard"), ct);
        var profile = await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == created.Value.ReplacementDriverId, ct);
        if (profile is null) return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("driver.not_found", "Replacement driver was not found."));
        var isAr = _messages.IsArabic();
        var driver = new SupportProblemDriverDto(profile.Id, NonBlank(profile.LicenseNumber), NonBlank(isAr ? profile.FullNameAr : profile.FullNameEn),
            profile.ProfileImageStorageKey is null ? null : MealMate.Application.Common.Storage.CatalogImageStorage.ToContentUrl(profile.ProfileImageStorageKey),
            "Available", _messages.GetMessage("driver.available", isAr ? "متاح" : "Available"), "#10B981", null,
            Vehicle(profile), new DriverVehicleDto(NonBlank(profile.VehicleType), NonBlank(profile.VehicleModel), NonBlank(profile.VehicleColor), NonBlank(profile.VehiclePlate)));
        return Result.Success(new ReassignReplacementDriverResponseDto(true, request.IssueId, created.Value.OriginalDriverId,
            created.Value.Status, driver, created.Value.AssignedAtUtc,
            _messages.GetMessage("driver_support_handover.assigned", isAr ? "تم إنشاء نقل العهدة؛ لن تنتقل ملكية الصناديق حتى تسجيل استلامها." : "Handover created; box custody transfers only after receipt is recorded."),
            profile.Id, NonBlank(profile.LicenseNumber), NonBlank(isAr ? profile.FullNameAr : profile.FullNameEn), created.Value.HandoverId, created.Value.Status));
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string? Vehicle(DriverProfile driver)
    {
        var text = string.Join(" • ", new[] { NonBlank(driver.VehicleModel), NonBlank(driver.VehicleType), NonBlank(driver.VehicleColor) }.Where(x => x is not null));
        return text.Length == 0 ? null : text;
    }
}
