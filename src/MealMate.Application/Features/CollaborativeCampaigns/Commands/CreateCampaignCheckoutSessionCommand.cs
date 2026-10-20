using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using QuoteEntity = MealMate.Domain.CollaborativeCampaigns.CustomerCampaignQuote;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CreateCampaignCheckoutSessionCommand(
    string IdempotencyKey,
    Guid CampaignId,
    Guid BundleId,
    Guid CustomerId,
    int DurationDays,
    IReadOnlyList<DateOnly> RequestedServiceDates,
    string? DeliveryGovernorate = null,
    Guid? AddressId = null,
    /// <summary>Quote-driven flow: selections, schedule and price derive from the saved quote;
    /// client-sent dates and amounts are ignored. Absent keeps the legacy client-dated flow.</summary>
    Guid? QuoteId = null) : ICommand<CampaignCheckoutSessionDto>;

public sealed class CreateCampaignCheckoutSessionCommandHandler : ICommandHandler<CreateCampaignCheckoutSessionCommand, CampaignCheckoutSessionDto>
{
    /// <summary>Idempotency scope is per customer: identical keys from different customers never collide.</summary>
    private static string ScopeFor(Guid customerId) => $"CampaignCheckoutSession:{customerId:N}";

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerCampaignEvaluationService _evaluation;
    private readonly ICampaignReservationCoordinator _reservationCoordinator;
    private readonly ICampaignCapacityLockRepository _lockRepository;

    public CreateCampaignCheckoutSessionCommandHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICustomerCampaignEvaluationService evaluation,
        ICampaignReservationCoordinator reservationCoordinator,
        ICampaignCapacityLockRepository lockRepository)
    {
        _context = context;
        _clock = clock;
        _evaluation = evaluation;
        _reservationCoordinator = reservationCoordinator;
        _lockRepository = lockRepository;
    }

    public async Task<Result<CampaignCheckoutSessionDto>> Handle(
        CreateCampaignCheckoutSessionCommand request,
        CancellationToken cancellationToken)
    {
        var requestHash = ComputeRequestHash(request);
        var scope = ScopeFor(request.CustomerId);

        // 1. Idempotency fast path (re-checked inside the transaction against concurrent inserts).
        var cached = await TryGetCachedResponseAsync(scope, request.IdempotencyKey, requestHash, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // 2. Atomic reservation: every competitor takes campaign row → day rows in the same
        //    deterministic order inside one transaction; capacities and slots are re-read
        //    authoritatively under those locks.
        var outcome = await _lockRepository.ExecuteInTransactionWithRetryAsync(
            ct => ReserveAsync(request, requestHash, scope, ct),
            cancellationToken);

        if (outcome.IsFailure)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(outcome.Error!);
        }

        return Result.Success(outcome.Value!);
    }

    private async Task<Result<CampaignCheckoutSessionDto>?> TryGetCachedResponseAsync(
        string scope, string idempotencyKey, string requestHash, CancellationToken cancellationToken)
    {
        var existingRecord = await _context.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Scope == scope && r.Key == idempotencyKey, cancellationToken);

        if (existingRecord is null)
        {
            return null;
        }

        if (existingRecord.RequestHash != requestHash)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Conflict("Idempotency.Conflict", "An operation with this idempotency key already exists with different payload parameters."));
        }

        if (!string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
        {
            var cachedDto = JsonSerializer.Deserialize<CampaignCheckoutSessionDto>(existingRecord.ResponseBody);
            if (cachedDto is not null)
            {
                return Result.Success(cachedDto);
            }
        }

        return null;
    }

    private async Task<Result<CampaignCheckoutSessionDto>> ReserveAsync(
        CreateCampaignCheckoutSessionCommand request,
        string requestHash,
        string scope,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // Idempotency under the transaction: a concurrent insert of the same key resolves here.
        var cached = await TryGetCachedResponseAsync(scope, request.IdempotencyKey, requestHash, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // 3. Lock the campaign row first (all competitors take this lock in the same order).
        var campaign = await _lockRepository.LockCampaignAsync(request.CampaignId, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found."));
        }

        // 4. Quote-driven flow: the saved quote is the only trusted selection, schedule and price.
        QuoteEntity? quote = null;
        if (request.QuoteId.HasValue)
        {
            quote = await _context.CustomerCampaignQuotes
                .FirstOrDefaultAsync(q => q.Id == request.QuoteId.Value, cancellationToken);

            if (quote is null || quote.CustomerId != request.CustomerId)
            {
                return Result.Failure<CampaignCheckoutSessionDto>(
                    Error.NotFound("Campaign.QuoteNotFound", $"Quote '{request.QuoteId}' was not found for this customer."));
            }

            if (quote.ExpiresAtUtc <= now)
            {
                return Result.Failure<CampaignCheckoutSessionDto>(
                    new Error("Campaign.QuoteExpired",
                    "The saved quote has expired. Request a new quote.", ErrorType.Gone, null, new Dictionary<string, object?>
                    {
                        ["refreshSummary"] = true,
                        ["refreshAvailability"] = true
                    }));
            }

            if (quote.CampaignId != campaign.Id || quote.CampaignBundleId != request.BundleId)
            {
                return Result.Failure<CampaignCheckoutSessionDto>(
                    Error.Conflict("Campaign.QuoteStale", "The quote does not match the requested selection. Request a new quote."));
            }
        }

        // 5. Shared evaluation: eligibility, profile gates and approved policy from real data.
        var evaluation = await _evaluation.EvaluateAsync(
            new CustomerCampaignEvaluationRequest(
                request.CampaignId,
                request.BundleId,
                request.DurationDays,
                request.CustomerId,
                request.AddressId),
            cancellationToken);

        if (evaluation.IsFailure)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(evaluation.Error!);
        }

        var e = evaluation.Value;

        if (e.Policy is null)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation(MessageCodes.Campaign.PricingPolicyNotApproved,
                    "No approved campaign pricing policy exists for this currency."));
        }

        if (e.LegacyIneligibilityReasons.Count > 0)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation("Campaign.Ineligible", string.Join("; ", e.LegacyIneligibilityReasons)));
        }

        if (e.HasProfileGateBlocks)
        {
            var gates = e.Blocks.Where(b => b.Code.StartsWith("profile.", StringComparison.Ordinal));
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation("Campaign.Ineligible",
                    string.Join("; ", gates.Select(g => $"{g.Code}: {g.MessageEn}"))));
        }

        // Quote staleness under the lock: price, terms or campaign state moved since quoting.
        if (quote is not null)
        {
            if (quote.CampaignSnapshotVersion != campaign.CurrentSnapshotVersion
                || quote.PricingPolicyId != e.Policy.Id
                || quote.PricingPolicyVersion != e.Policy.Version
                || quote.FinalPrice != e.Price.CampaignCustomerPrice.Amount)
            {
                return Result.Failure<CampaignCheckoutSessionDto>(
                    Error.Conflict("Campaign.QuoteStale",
                        "Price, terms or campaign state changed since the quote was issued. Request a new quote."));
            }
        }

        // 6. Service dates: quote-driven flow derives them from the saved schedule; the legacy
        //    flow keeps its client-dated contract. Client dates never extend the quota.
        var requestedDates = quote is not null
            ? quote.ServiceDates
            : request.RequestedServiceDates;
        if (requestedDates.Count == 0)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation("Campaign.ScheduleUnavailable", "No service dates are available for this selection."));
        }

        // 7. Lock every participant capacity row for the requested dates (deterministic order
        //    inside the repository) and read the authoritative availability from locked rows.
        var distinctDates = requestedDates.Select(d => d).Distinct().ToList();
        var candidateRows = await _context.CampaignRestaurantCapacities
            .Where(c => c.CampaignId == request.CampaignId && c.ServiceDate != null && distinctDates.Contains(c.ServiceDate!.Value))
            .Select(c => new { c.RestaurantId, c.ServiceDate })
            .ToListAsync(cancellationToken);

        var targets = candidateRows
            .Where(c => c.ServiceDate.HasValue)
            .Select(c => (c.ServiceDate!.Value, c.RestaurantId))
            .Distinct()
            .ToList();
        var capacityEntities = await _lockRepository.LockDailyCapacitiesAsync(request.CampaignId, targets, cancellationToken);

        // 7b. Release this customer's own superseded live sessions for the same campaign so a
        //     retry never self-overbooks, then count remaining live reservations for the cap.
        var ownLiveSessions = await _context.CampaignCheckoutSessions
            .Include(s => s.Allocations)
            .Where(s => s.CampaignId == campaign.Id && s.CustomerId == request.CustomerId
                && (s.Status == CampaignCheckoutSessionStatus.Pending || s.Status == CampaignCheckoutSessionStatus.PaymentPending)
                && s.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var superseded in ownLiveSessions)
        {
            if (superseded.MarkExpired(now).IsFailure)
            {
                // A processing lease (payment in flight) keeps the hold alive.
                continue;
            }

            if (!superseded.ReservationsReleasedAtUtc.HasValue)
            {
                foreach (var alloc in superseded.Allocations)
                {
                    var capEntity = capacityEntities.FirstOrDefault(c => c.RestaurantId == alloc.RestaurantId && c.ServiceDate == alloc.ServiceDate);
                    if (capEntity is not null)
                    {
                        capEntity.ReservedBoxes = Math.Max(0, capEntity.ReservedBoxes - alloc.Units);
                    }
                }
                superseded.ReservationsReleasedAtUtc = now;
            }
        }

        var availabilityList = capacityEntities
            .Where(c => c.ServiceDate.HasValue)
            .Select(c => new RestaurantDailyAvailability(
                c.RestaurantId,
                c.ServiceDate!.Value,
                AvailableCapacity: Math.Max(0, c.MaxBoxes - c.ReservedBoxes)))
            .ToList();

        // 8. Subscriber slots: committed subscribers plus live (unexpired) reservation sessions
        //    counted from the book of record — never a hardcoded zero.
        var liveReservedSlots = await _context.CampaignCheckoutSessions
            .Where(s => s.CampaignId == campaign.Id
                && (s.Status == CampaignCheckoutSessionStatus.Pending || s.Status == CampaignCheckoutSessionStatus.PaymentPending)
                && s.ExpiresAtUtc > now)
            .CountAsync(cancellationToken);

        var capState = new SubscriberCapState(
            campaign.MaxSubscribers,
            ReservedSlots: liveReservedSlots,
            CommittedSlots: campaign.CurrentSubscribers);

        // 9. Coordinate the atomic reservation over the locked, authoritative availability.
        var reservationResult = _reservationCoordinator.CoordinateReservation(new ReservationRequest(
            campaign.Id,
            request.BundleId,
            request.CustomerId,
            capState,
            requestedDates,
            availabilityList,
            ReservationTtl: TimeSpan.FromMinutes(15)));

        if (!reservationResult.IsSuccess)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Conflict("Campaign.CapacityUnavailable", reservationResult.FailureReason ?? "Requested campaign capacity is unavailable."));
        }

        // 10. Persist allocations and capacity mutation inside the same transaction.
        var sessionId = reservationResult.ReservationId ?? Guid.NewGuid();
        var expiresAtUtc = reservationResult.ExpiresAtUtc ?? now.AddMinutes(15);

        var session = new CampaignCheckoutSession(sessionId)
        {
            CampaignId = campaign.Id,
            BundleId = request.BundleId,
            CustomerId = request.CustomerId,
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = expiresAtUtc,
            PriceQuoteSnapshotId = e.DurationPrice.Id,
            CustomerQuoteId = quote?.Id,
            FinalAmount = quote?.FinalPrice ?? e.Price.CampaignCustomerPrice.Amount,
            CurrencyCode = e.Price.CampaignCustomerPrice.CurrencyCode,
            PricingPolicyId = e.Policy.Id,
            PricingPolicyVersion = e.Policy.Version,
            CampaignSnapshotVersion = campaign.CurrentSnapshotVersion
        };

        var allocationDtos = new List<CampaignReservationAllocationDto>(reservationResult.Allocations.Count);

        foreach (var alloc in reservationResult.Allocations)
        {
            var capEntity = capacityEntities.FirstOrDefault(c => c.RestaurantId == alloc.RestaurantId && c.ServiceDate == alloc.ServiceDate);
            if (capEntity is not null)
            {
                capEntity.ReservedBoxes += alloc.Units;
            }

            session.Allocations.Add(new CampaignReservationAllocation
            {
                CheckoutSessionId = sessionId,
                CampaignId = campaign.Id,
                BundleId = request.BundleId,
                ServiceDate = alloc.ServiceDate,
                RestaurantId = alloc.RestaurantId,
                Units = alloc.Units
            });

            allocationDtos.Add(new CampaignReservationAllocationDto(alloc.ServiceDate, alloc.RestaurantId, alloc.Units));
        }

        var amount = session.FinalAmount ?? e.Price.CampaignCustomerPrice.Amount;
        var dto = new CampaignCheckoutSessionDto(
            sessionId,
            campaign.Id,
            request.BundleId,
            request.CustomerId,
            request.DurationDays,
            amount,
            e.Price.CampaignCustomerPrice.CurrencyCode,
            expiresAtUtc,
            nameof(CampaignCheckoutSessionStatus.Pending),
            allocationDtos);

        _context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = scope,
            Key = request.IdempotencyKey,
            RequestHash = requestHash,
            ResponseBody = JsonSerializer.Serialize(dto),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        });
        _context.CampaignCheckoutSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(dto);
    }

    private static string ComputeRequestHash(CreateCampaignCheckoutSessionCommand request)
    {
        var raw = $"{request.CampaignId:N}:{request.BundleId:N}:{request.CustomerId:N}:{request.DurationDays}:{string.Join(",", request.RequestedServiceDates.OrderBy(d => d))}:{request.AddressId?.ToString("N")}:{request.QuoteId?.ToString("N")}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
