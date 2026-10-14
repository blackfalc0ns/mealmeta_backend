using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Advertising.Admin;

// ── Settings ─────────────────────────────────────────────────────────────

public sealed record GetAdvertisingSettingsQuery : IQuery<AdvertisingSettingsDto>;

public sealed record AdvertisingSettingsDto(
    bool AuctionsEnabled,
    string BusinessTimezone,
    int ExpectedVersion,
    Guid? LastChangedByUserId,
    DateTime? LastChangedAtUtc,
    string? LastChangeReason);

public sealed class GetAdvertisingSettingsQueryHandler : IQueryHandler<GetAdvertisingSettingsQuery, AdvertisingSettingsDto>
{
    private readonly IAppDbContext _context;
    public GetAdvertisingSettingsQueryHandler(IAppDbContext context) => _context = context;

    public async Task<Result<AdvertisingSettingsDto>> Handle(GetAdvertisingSettingsQuery request, CancellationToken ct)
    {
        var settings = await _context.AdvertisingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        // Honest empty state: the module starts disabled and every field must be set explicitly by an admin.
        return Result.Success(new AdvertisingSettingsDto(
            settings?.AuctionsEnabled ?? false,
            settings?.BusinessTimezone ?? "Asia/Kuwait",
            settings?.ExpectedVersion ?? 0,
            settings?.LastChangedByUserId,
            settings?.LastChangedAtUtc,
            settings?.LastChangeReason));
    }
}

public sealed record UpdateAdvertisingSettingsCommand(
    bool AuctionsEnabled,
    string BusinessTimezone,
    int ExpectedVersion,
    string Reason) : ICommand<AdvertisingSettingsDto>;

public sealed class UpdateAdvertisingSettingsCommandHandler : ICommandHandler<UpdateAdvertisingSettingsCommand, AdvertisingSettingsDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UpdateAdvertisingSettingsCommandHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<AdvertisingSettingsDto>> Handle(UpdateAdvertisingSettingsCommand request, CancellationToken ct)
    {
        if (!_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation("Advertising.ReasonRequired", "A reason is required when changing advertising settings.");
        }

        var settings = await _context.AdvertisingSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new AdvertisingSettings
            {
                BusinessTimezone = request.BusinessTimezone,
                AuctionsEnabled = request.AuctionsEnabled,
                ExpectedVersion = 1,
                CreatedAtUtc = _clock.UtcNow,
                CreatedByUserId = _currentUser.UserId
            };
            _context.AdvertisingSettings.Add(settings);
        }
        else
        {
            if (settings.ExpectedVersion != request.ExpectedVersion)
            {
                return Error.Conflict("Advertising.SettingsVersionConflict",
                    $"Settings changed concurrently (expected v{request.ExpectedVersion}, current v{settings.ExpectedVersion}).");
            }

            settings.AuctionsEnabled = request.AuctionsEnabled;
            settings.BusinessTimezone = request.BusinessTimezone;
            settings.ExpectedVersion++;
        }

        settings.LastChangedByUserId = _currentUser.UserId;
        settings.LastChangedAtUtc = _clock.UtcNow;
        settings.LastChangeReason = request.Reason.Trim();

        await _context.SaveChangesAsync(ct);

        return Result.Success(new AdvertisingSettingsDto(
            settings.AuctionsEnabled,
            settings.BusinessTimezone,
            settings.ExpectedVersion,
            settings.LastChangedByUserId,
            settings.LastChangedAtUtc,
            settings.LastChangeReason));
    }
}

// ── Commercial policy ────────────────────────────────────────────────────

public sealed record ApproveAdCommercialPolicyCommand(
    string CurrencyCode,
    decimal MinimumBid,
    decimal BidStep,
    int WinnerPaymentDeadlineMinutes,
    string StopSettlementRule) : ICommand<AdCommercialPolicyDto>;

public sealed record AdCommercialPolicyDto(
    Guid Id,
    int Version,
    string CurrencyCode,
    decimal MinimumBid,
    decimal BidStep,
    string TiePolicy,
    int WinnerPaymentDeadlineMinutes,
    string NextBidderPolicy,
    string StopSettlementRule,
    Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc);

public sealed class ApproveAdCommercialPolicyCommandHandler
    : ICommandHandler<ApproveAdCommercialPolicyCommand, AdCommercialPolicyDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ApproveAdCommercialPolicyCommandHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<AdCommercialPolicyDto>> Handle(ApproveAdCommercialPolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        var nextVersion = await _context.AdCommercialPolicies.AnyAsync(ct)
            ? await _context.AdCommercialPolicies.MaxAsync(p => p.Version, ct) + 1
            : 1;

        var create = AdCommercialPolicy.Create(
            nextVersion,
            request.CurrencyCode,
            request.MinimumBid,
            request.BidStep,
            AdCommercialPolicy.TiePolicyFirstAccepted,
            request.WinnerPaymentDeadlineMinutes,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent,
            request.StopSettlementRule);

        if (create.IsFailure)
        {
            return Error.Validation("Advertising.PolicyInvalid", create.Error ?? "Invalid advertising policy.");
        }

        var policy = create.Value;
        var approve = policy.Approve(_currentUser.UserId.Value, _clock.UtcNow);
        if (approve.IsFailure)
        {
            return Error.Validation("Advertising.PolicyInvalid", approve.Error ?? "Policy could not be approved.");
        }

        policy.CreatedAtUtc = _clock.UtcNow;
        policy.CreatedByUserId = _currentUser.UserId;
        _context.AdCommercialPolicies.Add(policy);
        await _context.SaveChangesAsync(ct);

        return Result.Success(new AdCommercialPolicyDto(
            policy.Id, policy.Version, policy.CurrencyCode, policy.MinimumBid, policy.BidStep,
            policy.TiePolicy, policy.WinnerPaymentDeadlineMinutes, policy.NextBidderPolicy,
            policy.StopSettlementRule, policy.ApprovedByUserId, policy.ApprovedAtUtc));
    }
}

public sealed record GetAdCommercialPolicyQuery : IQuery<AdCommercialPolicyDto?>;

public sealed class GetAdCommercialPolicyQueryHandler : IQueryHandler<GetAdCommercialPolicyQuery, AdCommercialPolicyDto?>
{
    private readonly IAppDbContext _context;
    public GetAdCommercialPolicyQueryHandler(IAppDbContext context) => _context = context;

    public async Task<Result<AdCommercialPolicyDto?>> Handle(GetAdCommercialPolicyQuery request, CancellationToken ct)
    {
        var policy = await _context.AdCommercialPolicies
            .AsNoTracking()
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        return Result.Success<AdCommercialPolicyDto?>(policy is null
            ? null
            : new AdCommercialPolicyDto(
                policy.Id, policy.Version, policy.CurrencyCode, policy.MinimumBid, policy.BidStep,
                policy.TiePolicy, policy.WinnerPaymentDeadlineMinutes, policy.NextBidderPolicy,
                policy.StopSettlementRule, policy.ApprovedByUserId, policy.ApprovedAtUtc));
    }
}

// ── Spaces ───────────────────────────────────────────────────────────────

public sealed record CreateAdSpaceCommand(
    string PlacementKey,
    string NameAr,
    string NameEn,
    int WidthPx,
    int HeightPx,
    IReadOnlyList<string> MarketIds) : ICommand<AdSpaceDto>;

public sealed record AdSpaceDto(
    Guid Id,
    string PlacementKey,
    string NameAr,
    string NameEn,
    int WidthPx,
    int HeightPx,
    IReadOnlyList<string> MarketIds,
    bool IsActive);

public sealed class CreateAdSpaceCommandHandler : ICommandHandler<CreateAdSpaceCommand, AdSpaceDto>
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;

    public CreateAdSpaceCommandHandler(IAppDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<AdSpaceDto>> Handle(CreateAdSpaceCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlacementKey) ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.PlacementKey, @"^[a-z0-9]+(?:[.\-_][a-z0-9]+)*$"))
        {
            return Error.Validation("Advertising.InvalidPlacementKey",
                "Placement key must be machine-readable (lowercase letters, digits, dots, dashes).");
        }

        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
        {
            return Error.Validation("Advertising.NameRequired", "Arabic and English space names are required.");
        }

        if (request.WidthPx <= 0 || request.HeightPx <= 0)
        {
            return Error.Validation("Advertising.InvalidDimensions", "Width and height must be greater than zero.");
        }

        var exists = await _context.AdSpaces
            .AnyAsync(s => s.PlacementKey == request.PlacementKey, ct);
        if (exists)
        {
            return Error.Conflict("Advertising.PlacementKeyExists", $"Ad space '{request.PlacementKey}' already exists.");
        }

        var space = new AdSpace
        {
            PlacementKey = request.PlacementKey.Trim(),
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            WidthPx = request.WidthPx,
            HeightPx = request.HeightPx,
            MarketIdsJson = JsonSerializer.Serialize(request.MarketIds ?? Array.Empty<string>()),
            CreatedAtUtc = _clock.UtcNow
        };

        _context.AdSpaces.Add(space);
        await _context.SaveChangesAsync(ct);

        return Result.Success(new AdSpaceDto(
            space.Id, space.PlacementKey, space.NameAr, space.NameEn,
            space.WidthPx, space.HeightPx, request.MarketIds ?? Array.Empty<string>(), space.IsActive));
    }
}

public sealed record GetAdSpacesQuery : IQuery<IReadOnlyList<AdSpaceDto>>;

public sealed class GetAdSpacesQueryHandler : IQueryHandler<GetAdSpacesQuery, IReadOnlyList<AdSpaceDto>>
{
    private readonly IAppDbContext _context;
    public GetAdSpacesQueryHandler(IAppDbContext context) => _context = context;

    public async Task<Result<IReadOnlyList<AdSpaceDto>>> Handle(GetAdSpacesQuery request, CancellationToken ct)
    {
        var spaces = await _context.AdSpaces
            .AsNoTracking()
            .OrderBy(s => s.PlacementKey)
            .ToListAsync(ct);

        var dtos = spaces.Select(s => new AdSpaceDto(
            s.Id, s.PlacementKey, s.NameAr, s.NameEn, s.WidthPx, s.HeightPx,
            JsonSerializer.Deserialize<IReadOnlyList<string>>(s.MarketIdsJson) ?? Array.Empty<string>(),
            s.IsActive)).ToList();

        return Result.Success<IReadOnlyList<AdSpaceDto>>(dtos);
    }
}

// ── Auctions ─────────────────────────────────────────────────────────────

public sealed record CreateAdAuctionCommand(
    Guid AdSpaceId,
    DateTime BidsOpenAtUtc,
    DateTime BidsCloseAtUtc,
    DateTime DisplayStartsAtUtc,
    DateTime DisplayEndsAtUtc) : ICommand<AdAuctionDto>;

public sealed record AdAuctionDto(
    Guid Id,
    Guid AdSpaceId,
    string PlacementKey,
    string Status,
    DateTime BidsOpenAtUtc,
    DateTime BidsCloseAtUtc,
    DateTime DisplayStartsAtUtc,
    DateTime DisplayEndsAtUtc,
    string CurrencyCode,
    decimal? WinningAmount,
    Guid? WinningRestaurantId,
    int PolicyVersion,
    int BidCount);

public sealed class CreateAdAuctionCommandHandler : ICommandHandler<CreateAdAuctionCommand, AdAuctionDto>
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;

    public CreateAdAuctionCommandHandler(IAppDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<AdAuctionDto>> Handle(CreateAdAuctionCommand request, CancellationToken ct)
    {
        var settings = await _context.AdvertisingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || !settings.AuctionsEnabled)
        {
            // The global stop switch blocks NEW auctions; existing paid placements keep serving.
            return Error.Validation("Advertising.AuctionsDisabled",
                "Advertising auctions are disabled; enable them in advertising settings first.");
        }

        var policy = await _context.AdCommercialPolicies
            .AsNoTracking()
            .Where(p => p.ApprovedByUserId != null)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);
        if (policy is null)
        {
            return Error.Validation("Advertising.CommercialPolicyNotApproved",
                "No approved advertising commercial policy exists; auctions cannot be created.");
        }

        var space = await _context.AdSpaces.FirstOrDefaultAsync(s => s.Id == request.AdSpaceId, ct);
        if (space is null || !space.IsActive)
        {
            return Error.NotFound("Advertising.SpaceNotFound", "Ad space was not found or is inactive.");
        }

        var now = _clock.UtcNow;
        if (request.BidsOpenAtUtc >= request.BidsCloseAtUtc)
        {
            return Error.Validation("Advertising.InvalidBiddingWindow", "Bids open must precede bids close.");
        }

        if (request.BidsCloseAtUtc >= request.DisplayStartsAtUtc)
        {
            return Error.Validation("Advertising.BiddingMustPrecedeDisplay",
                "The bidding window must close before the display window starts.");
        }

        if (request.DisplayStartsAtUtc >= request.DisplayEndsAtUtc)
        {
            return Error.Validation("Advertising.InvalidDisplayWindow", "Display start must precede display end.");
        }

        if (request.BidsCloseAtUtc < now)
        {
            return Error.Validation("Advertising.BiddingWindowInPast", "The bidding window must end in the future.");
        }

        var overlaps = await _context.AdAuctions.AnyAsync(a =>
            a.AdSpaceId == space.Id &&
            a.Status != AdAuctionStatus.Cancelled && a.Status != AdAuctionStatus.NoFill &&
            request.DisplayStartsAtUtc < a.DisplayEndsAtUtc && request.DisplayEndsAtUtc > a.DisplayStartsAtUtc, ct);
        if (overlaps)
        {
            return Error.Conflict("Advertising.DisplayWindowOverlap",
                "Another auction already owns this advertising space during the requested display window.");
        }

        var auction = new AdAuction
        {
            AdSpaceId = space.Id,
            Status = AdAuctionStatus.Open,
            BidsOpenAtUtc = request.BidsOpenAtUtc,
            BidsCloseAtUtc = request.BidsCloseAtUtc,
            DisplayStartsAtUtc = request.DisplayStartsAtUtc,
            DisplayEndsAtUtc = request.DisplayEndsAtUtc,
            PolicyId = policy.Id,
            PolicyVersion = policy.Version,
            CurrencyCode = policy.CurrencyCode,
            CreatedAtUtc = now
        };

        _context.AdAuctions.Add(auction);
        await _context.SaveChangesAsync(ct);

        return Result.Success(new AdAuctionDto(
            auction.Id, auction.AdSpaceId, space.PlacementKey, auction.Status.ToString(),
            auction.BidsOpenAtUtc, auction.BidsCloseAtUtc, auction.DisplayStartsAtUtc, auction.DisplayEndsAtUtc,
            auction.CurrencyCode!, null, null, policy.Version, 0));
    }
}

public sealed record GetOpenAuctionsQuery : IQuery<IReadOnlyList<AdAuctionDto>>;

public sealed class GetOpenAuctionsQueryHandler : IQueryHandler<GetOpenAuctionsQuery, IReadOnlyList<AdAuctionDto>>
{
    private readonly IAppDbContext _context;
    public GetOpenAuctionsQueryHandler(IAppDbContext context) => _context = context;

    public async Task<Result<IReadOnlyList<AdAuctionDto>>> Handle(GetOpenAuctionsQuery request, CancellationToken ct)
    {
        var auctions = await _context.AdAuctions
            .AsNoTracking()
            .Include(a => a.AdSpace)
            .Include(a => a.Bids)
            .Where(a => a.Status == AdAuctionStatus.Open || a.Status == AdAuctionStatus.AwaitingPayment || a.Status == AdAuctionStatus.Awarded)
            .OrderBy(a => a.BidsCloseAtUtc)
            .ToListAsync(ct);

        var dtos = auctions.Select(a => new AdAuctionDto(
            a.Id, a.AdSpaceId, a.AdSpace.PlacementKey, a.Status.ToString(),
            a.BidsOpenAtUtc, a.BidsCloseAtUtc, a.DisplayStartsAtUtc, a.DisplayEndsAtUtc,
            a.CurrencyCode ?? string.Empty, a.WinningAmount, a.WinningRestaurantId,
            a.PolicyVersion, a.Bids.Count)).ToList();

        return Result.Success<IReadOnlyList<AdAuctionDto>>(dtos);
    }
}

public sealed record ReviewAdCreativeCommand(
    Guid PlacementId,
    bool Approve,
    string? Notes) : ICommand;

public sealed class ReviewAdCreativeCommandHandler : ICommandHandler<ReviewAdCreativeCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ReviewAdCreativeCommandHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(ReviewAdCreativeCommand request, CancellationToken ct)
    {
        if (!_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        if (!request.Approve && string.IsNullOrWhiteSpace(request.Notes))
        {
            return Error.Validation("Advertising.ReviewNotesRequired", "Notes are required when rejecting a creative.");
        }

        var placement = await _context.AdPlacements
            .Include(p => p.AdAuction)
            .FirstOrDefaultAsync(p => p.Id == request.PlacementId, ct);
        if (placement is null)
        {
            return Error.NotFound("Advertising.PlacementNotFound", "Placement was not found.");
        }

        var result = request.Approve
            ? placement.ApproveCreative(_currentUser.UserId.Value)
            : placement.RejectCreative(_currentUser.UserId.Value, request.Notes!.Trim());

        if (result.IsFailure)
        {
            return Error.Validation("Advertising.InvalidReviewTransition", result.Error ?? "Review transition failed.");
        }

        placement.CreatedAtUtc = placement.CreatedAtUtc == default ? _clock.UtcNow : placement.CreatedAtUtc;
        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AdminStopAdPlacementCommand(Guid PlacementId, string Reason) : ICommand;

public sealed class AdminStopAdPlacementCommandHandler : ICommandHandler<AdminStopAdPlacementCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly Features.Advertising.Restaurant.StopAdPlacementCore _core;

    public AdminStopAdPlacementCommandHandler(
        IAppDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        Features.Advertising.Restaurant.StopAdPlacementCore core)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _core = core;
    }

    public async Task<Result> Handle(AdminStopAdPlacementCommand request, CancellationToken ct)
    {
        if (!_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        return await _core.StopAsync(
            request.PlacementId,
            actorUserId: _currentUser.UserId.Value,
            expectedRestaurantId: null, // admin may stop any placement
            request.Reason,
            ct);
    }
}

public sealed record AdRefundReviewRequestDto(
    Guid Id, Guid AdAuctionId, Guid RestaurantId, decimal Amount, string CurrencyCode,
    string Reason, DateTime RequestedAtUtc);

public sealed record AdPaymentReconciliationReviewDto(
    Guid PaymentId, Guid AdAuctionId, Guid RestaurantId, string Status,
    decimal Amount, string CurrencyCode, string Provider, DateTime InitiatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record GetAdPaymentReconciliationReviewsQuery : IQuery<IReadOnlyList<AdPaymentReconciliationReviewDto>>;

public sealed class GetAdPaymentReconciliationReviewsQueryHandler(IAppDbContext context)
    : IQueryHandler<GetAdPaymentReconciliationReviewsQuery, IReadOnlyList<AdPaymentReconciliationReviewDto>>
{
    public async Task<Result<IReadOnlyList<AdPaymentReconciliationReviewDto>>> Handle(
        GetAdPaymentReconciliationReviewsQuery request, CancellationToken ct)
    {
        var items = await context.AdPaymentTransactions.AsNoTracking()
            .Where(x => x.Status == "PaidNeedsManualReview" || x.Status == "RefundNeedsManualProviderReview" ||
                        x.Status == "InitializationNeedsManualReview")
            .OrderBy(x => x.InitiatedAtUtc)
            .Select(x => new AdPaymentReconciliationReviewDto(
                x.Id, x.AdAuctionId, x.RestaurantId, x.Status, x.Amount, x.CurrencyCode,
                x.PaymentProvider, x.InitiatedAtUtc, x.CompletedAtUtc))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<AdPaymentReconciliationReviewDto>>(items);
    }
}

public sealed record GetPendingAdRefundReviewsQuery : IQuery<IReadOnlyList<AdRefundReviewRequestDto>>;

public sealed class GetPendingAdRefundReviewsQueryHandler(IAppDbContext context)
    : IQueryHandler<GetPendingAdRefundReviewsQuery, IReadOnlyList<AdRefundReviewRequestDto>>
{
    public async Task<Result<IReadOnlyList<AdRefundReviewRequestDto>>> Handle(
        GetPendingAdRefundReviewsQuery request, CancellationToken ct)
    {
        var items = await context.AdFinancialLedgerEntries
            .AsNoTracking()
            .Where(e => e.Source == "AdStopSettlement" && e.Action == "RefundRequest" && e.RefundReviewStatus == "Pending")
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new AdRefundReviewRequestDto(
                e.Id, e.AdAuctionId, e.RestaurantId, e.Amount, e.CurrencyCode,
                e.RefundReviewReason ?? string.Empty, e.OccurredAtUtc))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<AdRefundReviewRequestDto>>(items);
    }
}

public sealed record ReviewAdRefundRequestCommand(Guid RequestId, bool Approve, string Reason) : ICommand;

public sealed class ReviewAdRefundRequestCommandHandler(
    IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    : ICommandHandler<ReviewAdRefundRequestCommand>
{
    public async Task<Result> Handle(ReviewAdRefundRequestCommand request, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue)
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Error.Validation("Advertising.RefundReviewReasonRequired", "A review reason is required.");

        var entry = await context.AdFinancialLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == request.RequestId && e.Source == "AdStopSettlement" &&
                                      e.Action == "RefundRequest", ct);
        if (entry is null)
            return Error.NotFound("Advertising.RefundReviewNotFound", "Refund review request was not found.");
        if (entry.RefundReviewStatus != "Pending")
            return Error.Conflict("Advertising.RefundAlreadyReviewed", "Refund request was already reviewed.");

        var auction = await context.AdAuctions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == entry.AdAuctionId, ct);
        var policy = auction is null ? null : await context.AdCommercialPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == auction.PolicyId && p.Version == auction.PolicyVersion &&
                                      p.ApprovedByUserId != null, ct);
        if (auction is null || policy is null || entry.Amount <= 0m)
            return Error.Conflict("Advertising.RefundPolicyUnavailable", "The saved refund policy or amount could not be verified.");

        var payment = await context.AdPaymentTransactions
            .FirstOrDefaultAsync(p => p.AdAuctionId == entry.AdAuctionId &&
                                      p.ProviderTransactionId == entry.ProviderTransactionId, ct);
        if (payment is null || payment.Status != "RefundAwaitingReview")
            return Error.Conflict("Advertising.RefundPaymentStateInvalid", "The payment is not awaiting refund review.");

        entry.RefundReviewStatus = request.Approve ? "Approved" : "Rejected";
        entry.RefundReviewedByUserId = currentUser.UserId.Value;
        entry.RefundReviewedAtUtc = clock.UtcNow;
        entry.RefundReviewReason = request.Reason.Trim();
        entry.RefundApprovedAmount = request.Approve ? entry.Amount : null;
        entry.RefundApprovedPolicyId = request.Approve ? auction!.PolicyId : null;
        entry.RefundApprovedPolicyVersion = request.Approve ? auction!.PolicyVersion : null;
        payment.Status = request.Approve ? "RefundApproved" : "Succeeded";
        await context.SaveChangesAsync(ct);
        return Result.Success();
    }
}
