using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.Advertising.Customer;

public sealed record GetEligibleAdPlacementsQuery(string PlacementKey) : IQuery<EligibleAdPlacementsDto>;

public sealed record AdPlacementDto(
    Guid Id,
    string PlacementKey,
    string AssetReadUrl,
    string TargetType,
    Guid? TargetId,
    DateTime DisplayEndsAtUtc,
    bool IsSponsored);

public sealed record EligibleAdPlacementsDto(IReadOnlyList<AdPlacementDto> Placements, DateTime ServerNowUtc);

/// <summary>
/// Returns paid (Awarded), approved and currently-in-window placements for a slot.
/// A paused auction system does NOT hide these: only a placement stop action does.
/// An ad is always marked sponsored and never grants a discount.
/// </summary>
public sealed class GetEligibleAdPlacementsQueryHandler : IQueryHandler<GetEligibleAdPlacementsQuery, EligibleAdPlacementsDto>
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly IFileStorage? _fileStorage;
    private readonly ICurrentUser? _currentUser;

    public GetEligibleAdPlacementsQueryHandler(IAppDbContext context, IDateTimeProvider clock, IFileStorage? fileStorage = null, ICurrentUser? currentUser = null)
    {
        _context = context;
        _clock = clock;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<EligibleAdPlacementsDto>> Handle(GetEligibleAdPlacementsQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlacementKey))
        {
            return Error.Validation("Advertising.PlacementKeyRequired", "A placement key is required.");
        }

        var now = _clock.UtcNow;
        if (_currentUser is null || !_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
            return Result.Success(new EligibleAdPlacementsDto([], now));

        var address = await _context.CustomerAddresses.AsNoTracking()
            .Where(a => a.UserId == _currentUser.UserId.Value && a.IsDefault)
            .FirstOrDefaultAsync(ct);
        if (address?.RegionId is null)
            return Result.Success(new EligibleAdPlacementsDto([], now));

        var placements = await _context.AdPlacements
            .AsNoTracking()
            .Include(p => p.AdAuction)
            .ThenInclude(a => a.AdSpace)
            .Where(p => p.AdAuction.AdSpace.PlacementKey == request.PlacementKey &&
                        p.AdAuction.AdSpace.IsActive &&
                        p.Status == AdPlacementStatus.Active &&
                        p.AdAuction.Status == AdAuctionStatus.Awarded &&
                        p.DisplayStartsAtUtc != null && p.DisplayStartsAtUtc <= now &&
                        p.DisplayEndsAtUtc != null && p.DisplayEndsAtUtc > now)
            .OrderBy(p => p.DisplayStartsAtUtc)
            .ToListAsync(ct);

        // MarketIdsJson contains region identifiers. Parse and compare exact GUIDs;
        // substring matching can leak a placement for a different market.
        placements = placements.Where(p => IsMarketEligible(p.AdAuction.AdSpace.MarketIdsJson, address.RegionId.Value))
            .Take(5).ToList();

        var dtos = new List<AdPlacementDto>(placements.Count);
        foreach (var p in placements)
        {
            var assetUrl = string.Empty;
            if (_fileStorage is not null && !string.IsNullOrWhiteSpace(p.CreativeStorageKey))
            {
                // Short-lived read URL; the storage service enforces access.
                assetUrl = await _fileStorage.GetReadUrlAsync(p.CreativeStorageKey!, TimeSpan.FromMinutes(30), ct);
            }

            dtos.Add(new AdPlacementDto(
                p.Id,
                p.AdAuction.AdSpace.PlacementKey,
                assetUrl,
                p.TargetType ?? string.Empty,
                p.TargetId,
                p.DisplayEndsAtUtc!.Value,
                IsSponsored: true));
        }

        // Server clock drives any countdown; device clocks are never trusted for eligibility.
        return Result.Success(new EligibleAdPlacementsDto(dtos, now));
    }

    private static bool IsMarketEligible(string? marketIdsJson, Guid customerRegionId)
    {
        if (string.IsNullOrWhiteSpace(marketIdsJson)) return false;
        try
        {
            var ids = JsonSerializer.Deserialize<Guid[]>(marketIdsJson);
            return ids is { Length: 0 } || ids?.Contains(customerRegionId) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record RecordAdEventCommand(
    Guid PlacementId,
    string EventId,
    string EventType) : ICommand;

public sealed class RecordAdEventCommandHandler : ICommandHandler<RecordAdEventCommand>
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;

    public RecordAdEventCommandHandler(IAppDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result> Handle(RecordAdEventCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EventId) || request.EventId.Length > 128)
        {
            return Error.Validation("Advertising.EventIdInvalid", "A valid event id is required.");
        }

        if (request.EventType is not ("impression" or "click"))
        {
            return Error.Validation("Advertising.EventTypeInvalid", "Event type must be 'impression' or 'click'.");
        }

        // Analytics never fabricate numbers: the same event id counts once, and unknown
        // placements are rejected instead of being logged as phantom traffic.
        var eventId = request.EventId.Trim();
        var exists = await _context.AdPlacementEvents
            .AnyAsync(e => e.PlacementId == request.PlacementId && e.EventId == eventId, ct);
        if (exists)
        {
            return Result.Success();
        }

        var placementExists = await _context.AdPlacements
            .AnyAsync(p => p.Id == request.PlacementId && p.Status == AdPlacementStatus.Active, ct);
        if (!placementExists)
        {
            return Error.NotFound("Advertising.PlacementNotFound", "Placement is not active.");
        }

        _context.AdPlacementEvents.Add(new AdPlacementEvent
        {
            PlacementId = request.PlacementId,
            EventId = eventId,
            EventType = request.EventType,
            OccurredAtUtc = _clock.UtcNow
        });

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }
}
