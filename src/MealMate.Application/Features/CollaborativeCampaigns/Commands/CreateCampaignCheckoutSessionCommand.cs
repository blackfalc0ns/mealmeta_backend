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

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CreateCampaignCheckoutSessionCommand(
    string IdempotencyKey,
    Guid CampaignId,
    Guid BundleId,
    Guid CustomerId,
    int DurationDays,
    IReadOnlyList<DateOnly> RequestedServiceDates,
    string? DeliveryGovernorate = null) : ICommand<CampaignCheckoutSessionDto>;

public sealed class CreateCampaignCheckoutSessionCommandHandler : ICommandHandler<CreateCampaignCheckoutSessionCommand, CampaignCheckoutSessionDto>
{
    private const string IdempotencyScope = "CampaignCheckoutSession";

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICampaignEligibilityEngine _eligibilityEngine;
    private readonly ICampaignReservationCoordinator _reservationCoordinator;

    public CreateCampaignCheckoutSessionCommandHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICampaignEligibilityEngine eligibilityEngine,
        ICampaignReservationCoordinator reservationCoordinator)
    {
        _context = context;
        _clock = clock;
        _eligibilityEngine = eligibilityEngine;
        _reservationCoordinator = reservationCoordinator;
    }

    public async Task<Result<CampaignCheckoutSessionDto>> Handle(
        CreateCampaignCheckoutSessionCommand request,
        CancellationToken cancellationToken)
    {
        var requestHash = ComputeRequestHash(request);

        // 1. Idempotency verification
        var existingRecord = await _context.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Scope == IdempotencyScope && r.Key == request.IdempotencyKey, cancellationToken);

        if (existingRecord != null)
        {
            if (existingRecord.RequestHash != requestHash)
            {
                return Result.Failure<CampaignCheckoutSessionDto>(
                    Error.Conflict("Idempotency.Conflict", "An operation with this idempotency key already exists with different payload parameters."));
            }

            if (!string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
            {
                var cachedDto = JsonSerializer.Deserialize<CampaignCheckoutSessionDto>(existingRecord.ResponseBody);
                if (cachedDto != null)
                {
                    return Result.Success(cachedDto);
                }
            }
        }

        // 2. Validate Campaign
        var campaign = await _context.Campaigns
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found."));
        }

        // 3. Validate Bundle Duration Pricing
        var pricing = await _context.CampaignBundleDurationPrices
            .FirstOrDefaultAsync(p => p.CampaignBundleId == request.BundleId && p.DurationDays == request.DurationDays, cancellationToken);

        if (pricing == null)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.NotFound("Campaign.PricingNotFound", $"No reference price found for Bundle '{request.BundleId}' with duration {request.DurationDays} days."));
        }

        // 3b. Pricing requires an explicitly approved policy covering the bundle currency.
        //     No approved policy means checkout is unavailable — never a silent fallback price.
        var policy = await _context.CampaignPricingPolicies
            .Where(p => p.ApprovedByUserId != null && p.CurrencyCode == pricing.ReferenceCustomerPrice.CurrencyCode)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (policy == null)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation(MessageCodes.Campaign.PricingPolicyNotApproved,
                    "No approved campaign pricing policy exists for this currency."));
        }

        // 3c. Single pricing source: the campaign pricing calculator, under the approved policy.
        var discountSplit = Domain.CollaborativeCampaigns.ValueObjects.CampaignDiscountSplit.Of(
            Domain.ValueObjects.Percentage.FromFraction(campaign.TotalDiscountRate.Value),
            Domain.ValueObjects.Percentage.FromFraction(campaign.RestaurantShareRate.Value),
            Domain.ValueObjects.Percentage.FromFraction(campaign.MealMateShareRate.Value));

        var priceResult = new Domain.CollaborativeCampaigns.Services.CampaignPricingCalculator()
            .Calculate(pricing.ReferenceCustomerPrice, discountSplit, request.DurationDays);

        // 4. Validate Customer Eligibility
        var profile = new CustomerEligibilityProfile(
            request.CustomerId,
            PriorSubscriptionCount: 0,
            ActiveCampaignSubscriptionCount: 0,
            DeliveryGovernorate: request.DeliveryGovernorate,
            RequestedDurationDays: request.DurationDays);

        var eligibilityResult = _eligibilityEngine.Evaluate(
            campaign,
            profile,
            new CampaignEligibilityCriteria(),
            _clock.UtcNow);

        if (!eligibilityResult.IsEligible)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation("Campaign.Ineligible", string.Join("; ", eligibilityResult.IneligibilityReasons)));
        }

        // 5. Fetch available daily capacities
        var dates = request.RequestedServiceDates.Select(d => (DateOnly?)d).Distinct().ToList();
        var capacityEntities = await _context.CampaignRestaurantCapacities
            .Where(c => c.CampaignId == request.CampaignId && dates.Contains(c.ServiceDate))
            .ToListAsync(cancellationToken);

        var availabilityList = capacityEntities
            .Where(c => c.ServiceDate.HasValue)
            .Select(c => new RestaurantDailyAvailability(
                c.RestaurantId,
                c.ServiceDate!.Value,
                AvailableCapacity: Math.Max(0, c.MaxBoxes - c.ReservedBoxes)))
            .ToList();

        var capState = new SubscriberCapState(
            campaign.MaxSubscribers,
            ReservedSlots: 0,
            CommittedSlots: campaign.CurrentSubscribers);

        // 6. Coordinate Atomic Reservation
        var reservationResult = _reservationCoordinator.CoordinateReservation(new ReservationRequest(
            campaign.Id,
            request.BundleId,
            request.CustomerId,
            capState,
            request.RequestedServiceDates,
            availabilityList,
            ReservationTtl: TimeSpan.FromMinutes(15)));

        if (!reservationResult.IsSuccess)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Conflict("Campaign.CapacityUnavailable", reservationResult.FailureReason ?? "Requested campaign capacity is unavailable."));
        }

        // 7. Persist Reservation Allocations & Update Capacity Counts
        var sessionId = reservationResult.ReservationId ?? Guid.NewGuid();
        var expiresAtUtc = reservationResult.ExpiresAtUtc ?? _clock.UtcNow.AddMinutes(15);

        var session = new CampaignCheckoutSession(sessionId)
        {
            CampaignId = campaign.Id,
            BundleId = request.BundleId,
            CustomerId = request.CustomerId,
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = expiresAtUtc,
            PriceQuoteSnapshotId = pricing.Id,
            FinalAmount = priceResult.CampaignCustomerPrice.Amount,
            CurrencyCode = priceResult.CampaignCustomerPrice.CurrencyCode,
            PricingPolicyId = policy.Id,
            PricingPolicyVersion = policy.Version,
            CampaignSnapshotVersion = campaign.CurrentSnapshotVersion
        };

        var allocationDtos = new List<CampaignReservationAllocationDto>(reservationResult.Allocations.Count);

        foreach (var alloc in reservationResult.Allocations)
        {
            var capEntity = capacityEntities.FirstOrDefault(c => c.RestaurantId == alloc.RestaurantId && c.ServiceDate == alloc.ServiceDate);
            if (capEntity != null)
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

        var dto = new CampaignCheckoutSessionDto(
            sessionId,
            campaign.Id,
            request.BundleId,
            request.CustomerId,
            request.DurationDays,
            priceResult.CampaignCustomerPrice.Amount,
            priceResult.CampaignCustomerPrice.CurrencyCode,
            expiresAtUtc,
            nameof(CampaignCheckoutSessionStatus.Pending),
            allocationDtos);

        // 8. Record Idempotency
        var record = new IdempotencyRecord
        {
            Scope = IdempotencyScope,
            Key = request.IdempotencyKey,
            RequestHash = requestHash,
            ResponseBody = JsonSerializer.Serialize(dto),
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = _clock.UtcNow.AddDays(7)
        };

        _context.IdempotencyRecords.Add(record);
        _context.CampaignCheckoutSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(dto);
    }

    private static string ComputeRequestHash(CreateCampaignCheckoutSessionCommand request)
    {
        var raw = $"{request.CampaignId:N}:{request.BundleId:N}:{request.CustomerId:N}:{request.DurationDays}:{string.Join(",", request.RequestedServiceDates.OrderBy(d => d))}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
