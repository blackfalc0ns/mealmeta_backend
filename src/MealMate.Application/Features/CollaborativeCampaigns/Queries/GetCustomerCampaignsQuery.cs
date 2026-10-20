using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record GetCustomerCampaignsQuery(
    Guid CustomerId,
    int PageSize = 20,
    string? Cursor = null) : IQuery<CustomerCampaignsPageDto>;

/// <summary>
/// Customer campaign discovery list: Active campaigns inside the sales window only
/// (startsAt &lt;= now &lt; endsAt), with approved participants and a subscriber slot remaining.
/// MinPrice is emitted only for a policy-backed, fully-serveable option — never fabricated.
/// </summary>
public sealed class GetCustomerCampaignsQueryHandler : IQueryHandler<GetCustomerCampaignsQuery, CustomerCampaignsPageDto>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly CustomerCampaignOptionProjector _projector;

    public GetCustomerCampaignsQueryHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        CustomerCampaignOptionProjector projector)
    {
        _context = context;
        _clock = clock;
        _projector = projector;
    }

    public async Task<Result<CustomerCampaignsPageDto>> Handle(
        GetCustomerCampaignsQuery request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        var cursor = CampaignCursor.Decode(request.Cursor);
        if (request.Cursor is not null && cursor is null)
        {
            return Result.Failure<CustomerCampaignsPageDto>(
                Error.Validation("Campaign.InvalidCursor", "The provided pagination cursor is not valid."));
        }

        var query = _context.Campaigns.AsNoTracking()
            .Where(c => c.Status == CampaignStatus.Active && c.StartsAtUtc <= now && now < c.EndsAtUtc);

        if (cursor is { } c)
        {
            query = query.Where(x => x.StartsAtUtc > c.StartsAtUtc
                || (x.StartsAtUtc == c.StartsAtUtc && x.CreatedAtUtc > c.CreatedAtUtc)
                || (x.StartsAtUtc == c.StartsAtUtc && x.CreatedAtUtc == c.CreatedAtUtc && c.LastCampaignId.HasValue && x.Id != c.LastCampaignId.Value));
        }

        var campaigns = await query
            .OrderBy(x => x.StartsAtUtc)
            .ThenBy(x => x.CreatedAtUtc)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasNext = campaigns.Count > pageSize;
        if (hasNext)
        {
            campaigns = campaigns.Take(pageSize).ToList();
        }

        var campaignIds = campaigns.Select(x => x.Id).ToList();

        // Live (not-yet-expired) reservation sessions count toward the subscriber cap.
        var reservedByCampaign = await _context.CampaignCheckoutSessions.AsNoTracking()
            .Where(s => campaignIds.Contains(s.CampaignId)
                && (s.Status == CampaignCheckoutSessionStatus.Pending || s.Status == CampaignCheckoutSessionStatus.PaymentPending)
                && s.ExpiresAtUtc > now)
            .GroupBy(s => s.CampaignId)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count, cancellationToken);

        var participantIdsByCampaign = await _projector.LoadApprovedParticipantIdsAsync(campaignIds, cancellationToken);

        var items = new List<CustomerCampaignListItemDto>(campaigns.Count);
        foreach (var campaign in campaigns)
        {
            var reserved = reservedByCampaign.GetValueOrDefault(campaign.Id);
            var remainingSlots = campaign.MaxSubscribers is { } max
                ? Math.Max(0, max - campaign.CurrentSubscribers - reserved)
                : (int?)null;
            if (remainingSlots is 0)
            {
                // A cap-complete campaign is no longer discoverable for subscription.
                continue;
            }

            if (!participantIdsByCampaign.TryGetValue(campaign.Id, out var participantIds) || participantIds.Count == 0)
            {
                continue;
            }

            var projection = await _projector.ProjectAsync(campaign, participantIds, now, cancellationToken);
            if (projection.Options.Count == 0)
            {
                continue;
            }

            var (minPrice, minBundleId, minDuration, minCurrency) = projection.MinConfirmablePrice();
            items.Add(new CustomerCampaignListItemDto(
                campaign.Id,
                campaign.NameAr,
                campaign.NameEn,
                null,
                null,
                campaign.StartsAtUtc,
                campaign.EndsAtUtc,
                minPrice,
                minCurrency,
                minBundleId,
                minDuration,
                remainingSlots,
                projection.Options.Any(o => o.FullSequenceAvailable)));
        }

        string? nextCursor = hasNext && campaigns.Count > 0
            ? CampaignCursor.Encode(campaigns[^1].StartsAtUtc, campaigns[^1].CreatedAtUtc, campaigns[^1].Id)
            : null;

        return Result.Success(new CustomerCampaignsPageDto(items, nextCursor, now));
    }
}

public static class CampaignCursor
{
    public static string Encode(DateTime startsAtUtc, DateTime createdAtUtc, Guid lastCampaignId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{startsAtUtc.Ticks}:{createdAtUtc.Ticks}:{lastCampaignId:N}"));

    public static (DateTime StartsAtUtc, DateTime CreatedAtUtc, Guid? LastCampaignId)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            return null;
        }

        var parts = raw.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[0], out var startTicks) || !long.TryParse(parts[1], out var createdTicks))
        {
            return null;
        }

        var lastId = Guid.TryParseExact(parts[2], "N", out var id) ? id : (Guid?)null;
        return (new DateTime(startTicks, DateTimeKind.Utc), new DateTime(createdTicks, DateTimeKind.Utc), lastId);
    }
}
