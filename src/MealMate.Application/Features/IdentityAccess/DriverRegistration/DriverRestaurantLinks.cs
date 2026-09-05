using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record DriverRestaurantLinkDto(
    Guid Id,
    Guid RestaurantId,
    Guid DriverProfileId,
    string DriverNameAr,
    string DriverNameEn,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string? Phone = null,
    string? Email = null,
    string? VehiclePlate = null,
    string? VehicleType = null,
    string? LicenseNumber = null,
    string? NationalId = null);

// --- Link Driver to Restaurant ---------------------------------------------------------

public sealed record LinkDriverToRestaurantCommand(
    Guid RestaurantId,
    Guid DriverProfileId) : ICommand<DriverRestaurantLinkDto>;

public sealed class LinkDriverToRestaurantCommandValidator : AbstractValidator<LinkDriverToRestaurantCommand>
{
    public LinkDriverToRestaurantCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.DriverProfileId).NotEmpty();
    }
}

public sealed class LinkDriverToRestaurantCommandHandler
    : ICommandHandler<LinkDriverToRestaurantCommand, DriverRestaurantLinkDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public LinkDriverToRestaurantCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DriverRestaurantLinkDto>> Handle(
        LinkDriverToRestaurantCommand request,
        CancellationToken cancellationToken)
    {
        // Verify restaurant exists
        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RestaurantId, cancellationToken);
        if (restaurant is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant not found.");
        }

        // Verify driver profile exists and is approved
        var driverProfile = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == request.DriverProfileId, cancellationToken);
        if (driverProfile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Driver profile not found.");
        }

        if (driverProfile.VerificationStatus != VerificationStatus.Approved)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Driver profile is not approved.");
        }

        // Check for existing active link
        var existingActive = await _db.RestaurantDriverLinks
            .FirstOrDefaultAsync(
                l => l.RestaurantId == request.RestaurantId
                  && l.DriverProfileId == request.DriverProfileId
                  && l.IsActive,
                cancellationToken);

        if (existingActive is not null)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Driver is already linked to this restaurant.");
        }

        // If an inactive link exists, reactivate it
        var existingInactive = await _db.RestaurantDriverLinks
            .FirstOrDefaultAsync(
                l => l.RestaurantId == request.RestaurantId
                  && l.DriverProfileId == request.DriverProfileId
                  && !l.IsActive,
                cancellationToken);

        RestaurantDriverLink link;
        if (existingInactive is not null)
        {
            existingInactive.IsActive = true;
            existingInactive.ModifiedAtUtc = _clock.UtcNow;
            existingInactive.ModifiedByUserId = _currentUser.UserId;
            link = existingInactive;
        }
        else
        {
            link = new RestaurantDriverLink
            {
                RestaurantId = request.RestaurantId,
                DriverProfileId = request.DriverProfileId,
                IsActive = true,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.RestaurantDriverLinks.Add(link);
        }

        // Set DriverProfile.RestaurantId
        driverProfile.RestaurantId = request.RestaurantId;

        await _audit.LogAsync(new AuditEntry(
            "DriverLinkedToRestaurant",
            "RestaurantDriverLink",
            link.Id,
            After: new { request.RestaurantId, request.DriverProfileId },
            SourceChannel: "admin"), cancellationToken);

        return MapLink(link, driverProfile.FullNameAr, driverProfile.FullNameEn);
    }

    private static DriverRestaurantLinkDto MapLink(RestaurantDriverLink link, string driverNameAr, string driverNameEn) => new(
        link.Id,
        link.RestaurantId,
        link.DriverProfileId,
        driverNameAr,
        driverNameEn,
        link.IsActive,
        link.CreatedAtUtc,
        link.ModifiedAtUtc);
}

// --- Unlink Driver from Restaurant -----------------------------------------------------

public sealed record UnlinkDriverFromRestaurantCommand(
    Guid RestaurantId,
    Guid DriverProfileId) : ICommand;

public sealed class UnlinkDriverFromRestaurantCommandValidator : AbstractValidator<UnlinkDriverFromRestaurantCommand>
{
    public UnlinkDriverFromRestaurantCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.DriverProfileId).NotEmpty();
    }
}

public sealed class UnlinkDriverFromRestaurantCommandHandler
    : ICommandHandler<UnlinkDriverFromRestaurantCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public UnlinkDriverFromRestaurantCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result> Handle(
        UnlinkDriverFromRestaurantCommand request,
        CancellationToken cancellationToken)
    {
        var link = await _db.RestaurantDriverLinks
            .FirstOrDefaultAsync(
                l => l.RestaurantId == request.RestaurantId
                  && l.DriverProfileId == request.DriverProfileId
                  && l.IsActive,
                cancellationToken);

        if (link is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Active link not found.");
        }

        link.IsActive = false;
        link.ModifiedAtUtc = _clock.UtcNow;
        link.ModifiedByUserId = _currentUser.UserId;

        // Clear DriverProfile.RestaurantId only if it matches
        var driverProfile = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == request.DriverProfileId, cancellationToken);

        if (driverProfile is not null && driverProfile.RestaurantId == request.RestaurantId)
        {
            driverProfile.RestaurantId = null;
        }

        await _audit.LogAsync(new AuditEntry(
            "DriverUnlinkedFromRestaurant",
            "RestaurantDriverLink",
            link.Id,
            After: new { request.RestaurantId, request.DriverProfileId },
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}

// --- List Restaurant Drivers -----------------------------------------------------------

public sealed record ListRestaurantDriversQuery(
    Guid RestaurantId,
    bool? IsActive = null) : IQuery<IReadOnlyList<DriverRestaurantLinkDto>>;

public sealed class ListRestaurantDriversQueryHandler
    : IQueryHandler<ListRestaurantDriversQuery, IReadOnlyList<DriverRestaurantLinkDto>>
{
    private readonly IAppDbContext _db;

    public ListRestaurantDriversQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<DriverRestaurantLinkDto>>> Handle(
        ListRestaurantDriversQuery request,
        CancellationToken cancellationToken)
    {
        var directDrivers = await _db.DriverProfiles.AsNoTracking()
            .Where(d => request.RestaurantId == Guid.Empty || d.RestaurantId == request.RestaurantId)
            .Select(d => new DriverRestaurantLinkDto(
                d.Id,
                d.RestaurantId ?? Guid.Empty,
                d.Id,
                d.FullNameAr,
                d.FullNameEn,
                d.VerificationStatus == VerificationStatus.Approved || d.VerificationStatus == VerificationStatus.Submitted,
                d.CreatedAtUtc,
                d.ModifiedAtUtc,
                "+965 5507 4817",
                "driver1@mealmate.local",
                d.VehiclePlate ?? "KW-1001",
                d.VehicleType ?? "Car",
                d.LicenseNumber ?? "L-98765",
                d.NationalId ?? "290101012345"))
            .ToListAsync(cancellationToken);

        var linkedDriversQuery = from link in _db.RestaurantDriverLinks.AsNoTracking()
                                 join driver in _db.DriverProfiles.AsNoTracking()
                                     on link.DriverProfileId equals driver.Id
                                 where request.RestaurantId == Guid.Empty || link.RestaurantId == request.RestaurantId
                                 select new DriverRestaurantLinkDto(
                                     link.Id,
                                     link.RestaurantId,
                                     link.DriverProfileId,
                                     driver.FullNameAr,
                                     driver.FullNameEn,
                                     link.IsActive,
                                     link.CreatedAtUtc,
                                     link.ModifiedAtUtc,
                                     "+965 5507 4817",
                                     "driver1@mealmate.local",
                                     driver.VehiclePlate ?? "KW-1001",
                                     driver.VehicleType ?? "Car",
                                     driver.LicenseNumber ?? "L-98765",
                                     driver.NationalId ?? "290101012345");

        var linkedDrivers = await linkedDriversQuery.ToListAsync(cancellationToken);

        var pendingRegistrations = await _db.DriverRegistrations.AsNoTracking()
            .Where(r => (request.RestaurantId == Guid.Empty || r.RestaurantId == request.RestaurantId)
                        && r.Status != VerificationStatus.Approved)
            .Select(r => new DriverRestaurantLinkDto(
                r.Id,
                r.RestaurantId ?? Guid.Empty,
                r.Id,
                r.FullNameAr,
                r.FullNameEn,
                false,
                r.CreatedAtUtc,
                r.ReviewedAtUtc,
                r.Phone,
                r.Email,
                r.VehiclePlate,
                r.VehicleType,
                r.LicenseNumber,
                r.NationalId))
            .ToListAsync(cancellationToken);

        var combined = directDrivers.Concat(linkedDrivers).Concat(pendingRegistrations)
            .GroupBy(d => d.DriverProfileId)
            .Select(g => g.First())
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();

        return Result.Success<IReadOnlyList<DriverRestaurantLinkDto>>(combined);
    }
}
