using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Advertising;
using MealMate.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Advertising.Restaurant;

public sealed record GetAdOpportunitiesQuery : IQuery<IReadOnlyList<AdOpportunityDto>>;

public sealed record AdOpportunityDto(
    Guid AuctionId,
    Guid AdSpaceId,
    string PlacementKey,
    string SpaceNameAr,
    string SpaceNameEn,
    int WidthPx,
    int HeightPx,
    DateTime BidsOpenAtUtc,
    DateTime BidsCloseAtUtc,
    DateTime DisplayStartsAtUtc,
    DateTime DisplayEndsAtUtc,
    string CurrencyCode,
    decimal MinimumBid,
    decimal BidStep,
    Guid? MyLeadingBidId,
    decimal? MyHighestBidAmount);

public sealed class GetAdOpportunitiesQueryHandler : IQueryHandler<GetAdOpportunitiesQuery, IReadOnlyList<AdOpportunityDto>>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetAdOpportunitiesQueryHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<AdOpportunityDto>>> Handle(GetAdOpportunitiesQuery request, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue)
        {
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only restaurant actors can view advertising opportunities.");
        }

        var now = _clock.UtcNow;
        var policy = await _context.AdCommercialPolicies
            .AsNoTracking()
            .Where(p => p.ApprovedByUserId != null)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        var auctions = await _context.AdAuctions
            .AsNoTracking()
            .Include(a => a.AdSpace)
            .Include(a => a.Bids.Where(b => b.RestaurantId == _currentUser.RestaurantId.Value))
            .Where(a => a.Status == AdAuctionStatus.Open && a.BidsCloseAtUtc > now)
            .OrderBy(a => a.BidsCloseAtUtc)
            .ToListAsync(ct);

        var dtos = auctions.Select(a => new AdOpportunityDto(
            a.Id, a.AdSpaceId, a.AdSpace.PlacementKey, a.AdSpace.NameAr, a.AdSpace.NameEn,
            a.AdSpace.WidthPx, a.AdSpace.HeightPx,
            a.BidsOpenAtUtc, a.BidsCloseAtUtc, a.DisplayStartsAtUtc, a.DisplayEndsAtUtc,
            a.CurrencyCode ?? policy?.CurrencyCode ?? string.Empty,
            policy?.MinimumBid ?? 0m, policy?.BidStep ?? 0m,
            a.Bids.OrderByDescending(b => b.Amount).FirstOrDefault()?.Id,
            a.Bids.Count == 0 ? null : a.Bids.Max(b => b.Amount))).ToList();

        return Result.Success<IReadOnlyList<AdOpportunityDto>>(dtos);
    }
}

public sealed record PlaceAdBidCommand(
    Guid AuctionId,
    decimal Amount,
    string IdempotencyKey) : ICommand<AdBidDto>;

public sealed record AdBidDto(
    Guid BidId,
    Guid AuctionId,
    decimal Amount,
    string CurrencyCode,
    DateTime AcceptedAtUtc,
    bool IsLeading);

public sealed class PlaceAdBidCommandHandler : ICommandHandler<PlaceAdBidCommand, AdBidDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public PlaceAdBidCommandHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<AdBidDto>> Handle(PlaceAdBidCommand request, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue)
        {
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only restaurant actors can place bids.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Error.Validation("Advertising.IdempotencyKeyRequired", "An idempotency key is required for bids.");
        }

        var restaurantId = _currentUser.RestaurantId.Value;
        var now = _clock.UtcNow;

        // Idempotent replay: same (auction, restaurant, key) returns the original outcome.
        var existing = await _context.AdBids
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.AdAuctionId == request.AuctionId &&
                                      b.RestaurantId == restaurantId &&
                                      b.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            return Result.Success(new AdBidDto(
                existing.Id, existing.AdAuctionId, existing.Amount, existing.CurrencyCode,
                existing.AcceptedAtUtc, existing.IsWinning));
        }

        // The global stop switch also refuses NEW bids; paid placements keep serving.
        var settings = await _context.AdvertisingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || !settings.AuctionsEnabled)
        {
            return Error.Validation("Advertising.AuctionsDisabled", "Advertising auctions are disabled.");
        }

        var auction = await _context.AdAuctions
            .FirstOrDefaultAsync(a => a.Id == request.AuctionId, ct);
        if (auction is null)
        {
            return Error.NotFound("Advertising.AuctionNotFound", "Auction was not found.");
        }

        if (auction.Status != AdAuctionStatus.Open)
        {
            return Error.Conflict("Advertising.AuctionNotOpen", $"Auction is '{auction.Status}', not open for bids.");
        }

        if (now < auction.BidsOpenAtUtc)
        {
            return Error.Validation("Advertising.BiddingNotOpen", "The bidding window has not opened yet.");
        }

        if (now >= auction.BidsCloseAtUtc)
        {
            return Error.Validation("Advertising.BiddingClosed", "The bidding window has closed.");
        }

        var policy = await _context.AdCommercialPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == auction.PolicyId && p.Version == auction.PolicyVersion, ct);
        if (policy is null || !policy.IsApproved)
        {
            return Error.Validation("Advertising.CommercialPolicyNotApproved",
                "The auction's commercial policy is missing or unapproved; bids cannot be accepted.");
        }

        if (!string.Equals(auction.CurrencyCode, policy.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("Advertising.CurrencyMismatch", "Auction currency does not match the approved policy.");
        }

        if (request.Amount < policy.MinimumBid)
        {
            return Error.Validation("Advertising.BidBelowMinimum",
                $"Bid must be at least the policy minimum of {policy.MinimumBid} {policy.CurrencyCode}.");
        }

        if ((request.Amount - policy.MinimumBid) % policy.BidStep != 0m)
        {
            return Error.Validation("Advertising.BidStepInvalid",
                $"Bid must follow the policy step of {policy.BidStep} {policy.CurrencyCode} from the minimum.");
        }

        var bid = new AdBid
        {
            AdAuctionId = auction.Id,
            RestaurantId = restaurantId,
            Amount = request.Amount,
            CurrencyCode = policy.CurrencyCode,
            IdempotencyKey = request.IdempotencyKey.Trim(),
            IsWinning = false,
            AcceptedAtUtc = now,
            CreatedAtUtc = now,
            CreatedByUserId = _currentUser.UserId
        };

        _context.AdBids.Add(bid);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
                                           ex.InnerException?.Message?.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Concurrent identical idempotency key: treat as replay success.
            var replay = await _context.AdBids
                .AsNoTracking()
                .FirstAsync(b => b.AdAuctionId == request.AuctionId &&
                                 b.RestaurantId == restaurantId &&
                                 b.IdempotencyKey == request.IdempotencyKey, ct);
            return Result.Success(new AdBidDto(replay.Id, replay.AdAuctionId, replay.Amount, replay.CurrencyCode,
                replay.AcceptedAtUtc, replay.IsWinning));
        }

        return Result.Success(new AdBidDto(
            bid.Id, bid.AdAuctionId, bid.Amount, bid.CurrencyCode, bid.AcceptedAtUtc, IsLeading: false));
    }
}

public sealed record GetMyAdBidsQuery : IQuery<IReadOnlyList<MyAdBidDto>>;

public sealed record MyAdBidDto(
    Guid BidId,
    Guid AuctionId,
    string PlacementKey,
    string AuctionStatus,
    decimal Amount,
    string CurrencyCode,
    bool IsWinning,
    DateTime AcceptedAtUtc,
    DateTime DisplayStartsAtUtc,
    DateTime DisplayEndsAtUtc);

public sealed class GetMyAdBidsQueryHandler : IQueryHandler<GetMyAdBidsQuery, IReadOnlyList<MyAdBidDto>>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetMyAdBidsQueryHandler(IAppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<MyAdBidDto>>> Handle(GetMyAdBidsQuery request, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue)
        {
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only restaurant actors can view their bids.");
        }

        var bids = await _context.AdBids
            .AsNoTracking()
            .Include(b => b.AdAuction)
            .ThenInclude(a => a.AdSpace)
            .Where(b => b.RestaurantId == _currentUser.RestaurantId.Value)
            .OrderByDescending(b => b.AcceptedAtUtc)
            .Take(100)
            .ToListAsync(ct);

        var dtos = bids.Select(b => new MyAdBidDto(
            b.Id, b.AdAuctionId, b.AdAuction.AdSpace.PlacementKey,
            b.AdAuction.Status.ToString(), b.Amount, b.CurrencyCode, b.IsWinning,
            b.AcceptedAtUtc, b.AdAuction.DisplayStartsAtUtc, b.AdAuction.DisplayEndsAtUtc)).ToList();

        return Result.Success<IReadOnlyList<MyAdBidDto>>(dtos);
    }
}

public sealed record SubmitAdCreativeCommand(
    Guid AuctionId,
    string CreativeStorageKey,
    string TargetType,
    Guid? TargetId,
    DateTime DisplayStartsAtUtc,
    DateTime DisplayEndsAtUtc) : ICommand;

public sealed class SubmitAdCreativeCommandHandler : ICommandHandler<SubmitAdCreativeCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SubmitAdCreativeCommandHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(SubmitAdCreativeCommand request, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue)
        {
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only the winning restaurant can submit a creative.");
        }

        var placement = await _context.AdPlacements
            .Include(p => p.AdAuction)
            .FirstOrDefaultAsync(p => p.AdAuctionId == request.AuctionId, ct);
        if (placement is null)
        {
            return Error.NotFound("Advertising.PlacementNotFound", "No placement exists for this auction.");
        }

        // Ownership: only the auction winner may submit its creative.
        if (placement.RestaurantId != _currentUser.RestaurantId.Value)
        {
            return Error.Forbidden("Advertising.PlacementOwnershipMismatch", "This placement does not belong to your restaurant.");
        }

        if (placement.AdAuction.Status != AdAuctionStatus.Awarded)
        {
            return Error.Validation("Advertising.PaymentRequired",
                "The auction win must be paid before submitting a creative.");
        }

        if (!_currentUser.UserId.HasValue)
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        var upload = await _context.UploadFileRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StorageKey == request.CreativeStorageKey &&
                                      x.UploadedByUserId == _currentUser.UserId.Value, ct);
        if (upload is null || string.IsNullOrWhiteSpace(upload.ContentType) ||
            !upload.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            (upload.Scope is not null && !string.Equals(upload.Scope, "advertising", StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Forbidden("Advertising.CreativeOwnershipInvalid", "The creative file is not owned by this user or is not an allowed advertising asset.");
        }

        if (request.DisplayStartsAtUtc != placement.AdAuction.DisplayStartsAtUtc ||
            request.DisplayEndsAtUtc != placement.AdAuction.DisplayEndsAtUtc)
        {
            return Error.Validation("Advertising.CreativeWindowMismatch", "Creative display dates must match the auction window.");
        }

        var result = placement.SubmitCreative(
            request.CreativeStorageKey, request.TargetType, request.TargetId,
            request.DisplayStartsAtUtc, request.DisplayEndsAtUtc);
        if (result.IsFailure)
        {
            return Error.Validation("Advertising.InvalidCreativeSubmission", result.Error ?? "Creative submission failed.");
        }

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Records a confirmed provider payment for an auction win and awards the auction once.</summary>
public sealed record RecordAdPaymentCommand(
    Guid AuctionId,
    string PaymentProvider,
    string ProviderTransactionId,
    decimal Amount,
    string CurrencyCode) : ICommand;

public sealed record CreateAdPaymentSessionCommand(Guid AuctionId) : ICommand<AdPaymentSessionDto>;
public sealed record AdPaymentSessionDto(string PaymentUrl, string PaymentHandle, decimal Amount, string CurrencyCode);

public sealed class CreateAdPaymentSessionCommandHandler(
    IAppDbContext context, ICurrentUser currentUser, IUserAccountService accounts,
    IPaymentGatewayService gateway, IConfiguration configuration, IDateTimeProvider clock,
    IApplicationTransactionExecutor transactions, IApplicationLockManager locks)
    : ICommandHandler<CreateAdPaymentSessionCommand, AdPaymentSessionDto>
{
    public async Task<Result<AdPaymentSessionDto>> Handle(CreateAdPaymentSessionCommand request, CancellationToken ct)
    {
        if (!currentUser.RestaurantId.HasValue || !currentUser.UserId.HasValue)
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only the auction winner can start payment.");
        var auction = await context.AdAuctions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AuctionId, ct);
        if (auction is null) return Error.NotFound("Advertising.AuctionNotFound", "Auction was not found.");
        if (auction.Status != AdAuctionStatus.AwaitingPayment || auction.WinningRestaurantId != currentUser.RestaurantId ||
            auction.WinningAmount is null || string.IsNullOrWhiteSpace(auction.CurrencyCode))
            return Error.Conflict("Advertising.AuctionNotAwaitingPayment", "This restaurant does not have a payable auction win.");
        var callbackUrl = configuration["Checkout:DefaultCallbackUrl"];
        var errorUrl = configuration["Checkout:DefaultErrorUrl"];
        if (string.IsNullOrWhiteSpace(callbackUrl) || string.IsNullOrWhiteSpace(errorUrl) ||
            !AllowedRedirect(callbackUrl, configuration) || !AllowedRedirect(errorUrl, configuration))
            return Error.Validation("Advertising.PaymentRedirectUnavailable", "Configured payment return URLs are missing or not allowed.");
        var user = await accounts.FindByIdAsync(currentUser.UserId.Value, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return Error.Validation("Advertising.PaymentContactUnavailable", "Verified restaurant contact details are required for payment.");
        if (!string.Equals(auction.CurrencyCode, "KWD", StringComparison.OrdinalIgnoreCase))
            return Error.Validation("Advertising.PaymentCurrencyUnsupported", "The configured payment gateway cannot process this auction currency.");

        var reservationId = Guid.NewGuid();
        var reservationKey = "ad-init:" + reservationId.ToString("N");
        var reserved = await transactions.ExecuteAsync(async token =>
        {
            await locks.LockAdAuctionAsync(auction.Id, token);
            var latest = await context.AdAuctions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == auction.Id, token);
            if (latest is null || latest.Status != AdAuctionStatus.AwaitingPayment ||
                latest.WinningRestaurantId != currentUser.RestaurantId || latest.WinningAmount != auction.WinningAmount ||
                !string.Equals(latest.CurrencyCode, auction.CurrencyCode, StringComparison.OrdinalIgnoreCase) ||
                await context.AdPaymentTransactions.AnyAsync(x => x.AdAuctionId == latest.Id &&
                    (x.Status == "Pending" || (x.Status == "Initializing" && x.InitiatedAtUtc > clock.UtcNow.AddMinutes(-5))), token)) return false;
            context.AdPaymentTransactions.Add(new AdPaymentTransaction
            {
                AdAuctionId = latest.Id, RestaurantId = currentUser.RestaurantId.Value,
                PaymentProvider = "Payzah", ProviderTransactionId = reservationKey,
                Amount = latest.WinningAmount!.Value, CurrencyCode = latest.CurrencyCode!,
                Status = "Initializing", InitiatedAtUtc = clock.UtcNow
            });
            await context.SaveChangesAsync(token);
            return true;
        }, ct);
        if (!reserved)
            return Error.Conflict("Advertising.PaymentAlreadyPending", "A payment attempt is already pending for this auction.");

        try
        {
            var reference = $"ad-auction:{auction.Id:N}:{currentUser.RestaurantId.Value:N}";
            var receipt = await gateway.ExecutePaymentAsync(auction.WinningAmount.Value,
                user.FullName ?? user.Email, user.Email, user.PhoneNumber, callbackUrl, errorUrl,
                reference, cancellationToken: ct);
            var reservation = await context.AdPaymentTransactions.FirstAsync(x => x.ProviderTransactionId == reservationKey, ct);
            reservation.ProviderTransactionId = receipt.PaymentHandle;
            reservation.Status = "Pending";
            await context.SaveChangesAsync(ct);
            return new AdPaymentSessionDto(receipt.PaymentUrl, receipt.PaymentHandle, auction.WinningAmount.Value, auction.CurrencyCode);
        }
        catch (Exception)
        {
            var reservation = await context.AdPaymentTransactions.FirstOrDefaultAsync(x => x.ProviderTransactionId == reservationKey, ct);
            if (reservation is not null)
            {
                reservation.Status = "Failed";
                reservation.CompletedAtUtc = clock.UtcNow;
                await context.SaveChangesAsync(ct);
            }
            return Error.Failure("Advertising.PaymentInitializationFailed", "The payment session could not be initialized.");
        }
    }

    private static bool AllowedRedirect(string value, IConfiguration configuration)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var schemes = configuration.GetSection("Checkout:AllowedRedirectSchemes").GetChildren().Select(x => x.Value).Where(x => x is not null).ToArray();
        var hosts = configuration.GetSection("Checkout:AllowedRedirectHosts").GetChildren().Select(x => x.Value).Where(x => x is not null).ToArray();
        return schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase) &&
               (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class RecordAdPaymentCommandHandler : ICommandHandler<RecordAdPaymentCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IPaymentGatewayService? _paymentGateway;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public RecordAdPaymentCommandHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock,
        IPaymentGatewayService? paymentGateway = null, IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _paymentGateway = paymentGateway;
        _transactions = transactions;
        _locks = locks;
    }

    public Task<Result> Handle(RecordAdPaymentCommand request, CancellationToken ct)
        => _transactions is null ? HandleCore(request, ct) : _transactions.ExecuteAsync(token => HandleCore(request, token), ct);

    private async Task<Result> HandleCore(RecordAdPaymentCommand request, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue)
        {
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only the winning restaurant can pay for its win.");
        }

        var auction = await _context.AdAuctions
            .Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == request.AuctionId, ct);
        if (auction is null)
        {
            return Error.NotFound("Advertising.AuctionNotFound", "Auction was not found.");
        }

        if (_locks is not null)
        {
            await _locks.LockAdAuctionAsync(auction.Id, ct);
            if (auction.Status == AdAuctionStatus.AwaitingPayment)
            {
                // The lock refreshes the auction row and winner bid before payment finalization.
            }
        }

        if (auction.WinningRestaurantId != _currentUser.RestaurantId.Value)
        {
            return Error.Forbidden("Advertising.NotTheWinner", "Only the auction winner can pay for this auction.");
        }

        if (_paymentGateway is null)
        {
            return Error.Validation("Advertising.PaymentVerificationUnavailable", "Payment verification is unavailable.");
        }

        var expectedReference = $"ad-auction:{auction.Id:N}:{_currentUser.RestaurantId.Value:N}";
        var savedAttempt = await _context.PaymentGatewayAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Provider == "Payzah" &&
                (x.PaymentId == request.ProviderTransactionId || ("pz-" + x.TrackId) == request.ProviderTransactionId), ct);
        if (request.PaymentProvider != "Payzah" || savedAttempt is null ||
            savedAttempt.CustomerReference != expectedReference ||
            savedAttempt.ExpectedAmount != auction.WinningAmount ||
            !string.Equals(savedAttempt.ExpectedCurrency, auction.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("Advertising.PaymentAttemptMismatch", "No matching server-created payment attempt exists for this auction.");
        }

        var paymentHandle = "pz-" + savedAttempt.TrackId;
        var existingTx = await _context.AdPaymentTransactions
            .FirstOrDefaultAsync(t => t.PaymentProvider == "Payzah" && t.ProviderTransactionId == paymentHandle, ct);
        if (existingTx is not null && (existingTx.AdAuctionId != request.AuctionId || existingTx.RestaurantId != _currentUser.RestaurantId.Value))
            return Error.Conflict("Advertising.PaymentReplayMismatch", "This provider transaction is already bound to another auction.");
        if (existingTx?.Status == "Succeeded") return Result.Success();

        if (auction.Status != AdAuctionStatus.AwaitingPayment)
        {
            if (auction.Status != AdAuctionStatus.Awarded || auction.WinningRestaurantId != _currentUser.RestaurantId.Value)
            {
                return Error.Conflict("Advertising.AuctionNotAwaitingPayment", "Auction is not awaiting payment.");
            }
        }

        var verified = await _paymentGateway.GetPaymentStatusAsync(paymentHandle, cancellationToken: ct);
        if (verified is not null && (
            string.Equals(verified.InvoiceStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(verified.InvoiceStatus, "Expired", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(verified.InvoiceStatus, "Canceled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(verified.InvoiceStatus, "Cancelled", StringComparison.OrdinalIgnoreCase)))
        {
            if (existingTx is not null && existingTx.Status == "Pending")
            {
                existingTx.Status = "Failed";
                existingTx.CompletedAtUtc = _clock.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
            return Error.Validation("Advertising.PaymentFailed", "The payment attempt failed or was canceled.");
        }

        if (verified is null || !string.Equals(verified.InvoiceStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
            verified.InvoiceValue != auction.WinningAmount ||
            !string.Equals(verified.CurrencyCode, auction.CurrencyCode, StringComparison.OrdinalIgnoreCase) ||
            verified.CustomerReference != expectedReference || verified.PaymentId != paymentHandle)
        {
            return Error.Validation("Advertising.PaymentNotConfirmed", "The payment provider has not confirmed this payment.");
        }

        // Amount and currency must match the recorded winning bid exactly.
        if (request.Amount != auction.WinningAmount ||
            !string.Equals(request.CurrencyCode, auction.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("Advertising.PaymentAmountMismatch",
                "Payment amount/currency does not match the winning bid.");
        }

        var now = _clock.UtcNow;
        if (existingTx is null)
        {
            existingTx = new AdPaymentTransaction
            {
                AdAuctionId = auction.Id,
                RestaurantId = auction.WinningRestaurantId.Value,
                PaymentProvider = request.PaymentProvider,
                ProviderTransactionId = paymentHandle,
                Amount = request.Amount,
                CurrencyCode = request.CurrencyCode,
                InitiatedAtUtc = now
            };
            _context.AdPaymentTransactions.Add(existingTx);
        }
        existingTx.Status = "Succeeded";
        existingTx.CompletedAtUtc = now;

        auction.Status = AdAuctionStatus.Awarded;

        var placement = await _context.AdPlacements.FirstOrDefaultAsync(p => p.AdAuctionId == auction.Id, ct);
        if (placement is null)
        {
            _context.AdPlacements.Add(new AdPlacement
            {
                AdAuctionId = auction.Id,
                RestaurantId = auction.WinningRestaurantId.Value,
                AdSpaceId = auction.AdSpaceId,
                Status = AdPlacementStatus.AwaitingCreative,
                DisplayStartsAtUtc = auction.DisplayStartsAtUtc,
                DisplayEndsAtUtc = auction.DisplayEndsAtUtc
            });
        }

        foreach (var bid in auction.Bids.Where(b => b.Id == auction.WinningBidId))
        {
            bid.IsWinning = true;
        }

        if (!await _context.AdFinancialLedgerEntries.AnyAsync(x => x.Source == "AdPayment" &&
                x.Action == "Charge" && x.ProviderTransactionId == paymentHandle, ct))
        {
            _context.AdFinancialLedgerEntries.Add(new AdFinancialLedgerEntry
            {
                AdAuctionId = auction.Id,
                RestaurantId = auction.WinningRestaurantId.Value,
                Source = "AdPayment",
                Action = "Charge",
                Amount = request.Amount,
                CurrencyCode = request.CurrencyCode,
                ProviderTransactionId = paymentHandle,
                OccurredAtUtc = now
            });
        }

        await _context.SaveChangesAsync(ct);

        return Result.Success();
    }
}

/// <summary>Shared stop flow for admin and restaurant-own placements, applying the approved settlement policy.</summary>
public sealed class StopAdPlacementCore(IAppDbContext context, IDateTimeProvider clock)
{
    public async Task<Result> StopAsync(
        Guid placementId,
        Guid actorUserId,
        Guid? expectedRestaurantId,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Advertising.StopReasonRequired", "A reason is required to stop a paid placement.");
        }

        var placement = await context.AdPlacements
            .Include(p => p.AdAuction)
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);
        if (placement is null)
        {
            return Error.NotFound("Advertising.PlacementNotFound", "Placement was not found.");
        }

        if (expectedRestaurantId.HasValue && placement.RestaurantId != expectedRestaurantId.Value)
        {
            return Error.Forbidden("Advertising.PlacementOwnershipMismatch", "This placement does not belong to your restaurant.");
        }

        // A sold placement always follows the policy snapshot captured on its auction.
        var policy = await context.AdCommercialPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == placement.AdAuction.PolicyId &&
                                      p.Version == placement.AdAuction.PolicyVersion &&
                                      p.ApprovedByUserId != null, ct);
        if (policy is null)
        {
            return Error.Conflict("Advertising.SettlementPolicyUnavailable",
                "The approved settlement policy for this placement could not be verified.");
        }

        var result = placement.Stop(actorUserId, reason.Trim(), clock.UtcNow);
        if (result.IsFailure)
        {
            return Error.Validation("Advertising.InvalidStopTransition", result.Error ?? "Stop failed.");
        }

        // Settlement follows the approved policy version the auction was sold under.
        var rule = policy.StopSettlementRule;
        var now = clock.UtcNow;

        if (rule != "NoRefund")
        {
            var payment = await context.AdPaymentTransactions
                .FirstOrDefaultAsync(t => t.AdAuctionId == placement.AdAuctionId && t.Status == "Succeeded", ct);

            if (payment is not null)
            {
                var refundDue = rule switch
                {
                    "FullRefundBeforeStart" => placement.DisplayStartsAtUtc.HasValue && now < placement.DisplayStartsAtUtc.Value
                        ? payment.Amount
                        : 0m,
                    "ProrataUnservedDays" => ComputeProrataRefund(payment, placement, now),
                    _ => 0m
                };

                if (refundDue > 0m)
                {
                    var alreadySettled = await context.AdFinancialLedgerEntries
                        .AnyAsync(e => e.AdAuctionId == placement.AdAuctionId &&
                                       e.Source == "AdStopSettlement" &&
                                       (e.Action == "RefundRequest" || e.Action == "Refund"), ct);
                    if (!alreadySettled)
                    {
                        // Refunds require explicit admin review before any provider operation.
                        payment.Status = "RefundAwaitingReview";
                        context.AdFinancialLedgerEntries.Add(new AdFinancialLedgerEntry
                        {
                            AdAuctionId = placement.AdAuctionId,
                            RestaurantId = placement.RestaurantId,
                            Source = "AdStopSettlement",
                            Action = "RefundRequest",
                            Amount = refundDue,
                            CurrencyCode = payment.CurrencyCode,
                            ProviderTransactionId = payment.ProviderTransactionId,
                            RefundReviewStatus = "Pending",
                            RefundApprovedPolicyId = policy.Id,
                            RefundApprovalPolicyVersion = policy.Version,
                            RefundReviewReason = reason.Trim(),
                            OccurredAtUtc = now
                        });
                    }
                }
            }
        }

        await context.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static decimal ComputeProrataRefund(AdPaymentTransaction payment, AdPlacement placement, DateTime now)
    {
        if (!placement.DisplayStartsAtUtc.HasValue || !placement.DisplayEndsAtUtc.HasValue)
        {
            return 0m;
        }

        if (now < placement.DisplayStartsAtUtc.Value)
        {
            return payment.Amount;
        }

        var totalSeconds = (placement.DisplayEndsAtUtc.Value - placement.DisplayStartsAtUtc.Value).TotalSeconds;
        var servedSeconds = (now - placement.DisplayStartsAtUtc.Value).TotalSeconds;
        if (totalSeconds <= 0)
        {
            return 0m;
        }

        var unservedRatio = (decimal)Math.Max(0, 1 - (servedSeconds / totalSeconds));
        return Math.Round(payment.Amount * unservedRatio, 3, MidpointRounding.AwayFromZero);
    }
}

public sealed record RestaurantStopAdPlacementCommand(Guid PlacementId, string Reason) : ICommand;

public sealed class RestaurantStopAdPlacementCommandHandler : ICommandHandler<RestaurantStopAdPlacementCommand>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly StopAdPlacementCore _core;

    public RestaurantStopAdPlacementCommandHandler(
        IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock, StopAdPlacementCore core)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _core = core;
    }

    public Task<Result> Handle(RestaurantStopAdPlacementCommand request, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || !_currentUser.UserId.HasValue)
        {
            return Task.FromResult<Result>(Error.Forbidden("Advertising.NotRestaurantActor",
                "Only the owning restaurant can stop its placement."));
        }

        return _core.StopAsync(request.PlacementId, _currentUser.UserId.Value, _currentUser.RestaurantId.Value, request.Reason, ct);
    }
}
