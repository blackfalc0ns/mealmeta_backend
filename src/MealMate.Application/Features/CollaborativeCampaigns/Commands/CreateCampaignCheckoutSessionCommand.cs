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
    string? DeliveryGovernorate = null,
    Guid? AddressId = null) : ICommand<CampaignCheckoutSessionDto>;

public sealed class CreateCampaignCheckoutSessionCommandHandler : ICommandHandler<CreateCampaignCheckoutSessionCommand, CampaignCheckoutSessionDto>
{
    private const string IdempotencyScope = "CampaignCheckoutSession";

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerCampaignEvaluationService _evaluation;
    private readonly ICampaignReservationCoordinator _reservationCoordinator;

    public CreateCampaignCheckoutSessionCommandHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICustomerCampaignEvaluationService evaluation,
        ICampaignReservationCoordinator reservationCoordinator)
    {
        _context = context;
        _clock = clock;
        _evaluation = evaluation;
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

        // 2. Single pricing/eligibility source: shared evaluation over authorized customer data.
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

        // 2b. Pricing requires an explicitly approved policy; never a silent fallback price.
        if (e.Policy is null)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation(MessageCodes.Campaign.PricingPolicyNotApproved,
                    "No approved campaign pricing policy exists for this currency."));
        }

        // 2c. Campaign-rule eligibility (legacy contract keeps its code and message shape).
        if (e.LegacyIneligibilityReasons.Count > 0)
        {
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation("Campaign.Ineligible", string.Join("; ", e.LegacyIneligibilityReasons)));
        }

        // 2d. Profile gates (address/allergies/preferences) from the customer's own data.
        if (e.HasProfileGateBlocks)
        {
            var gates = e.Blocks.Where(b => b.Code.StartsWith("profile.", StringComparison.Ordinal));
            return Result.Failure<CampaignCheckoutSessionDto>(
                Error.Validation("Campaign.Ineligible",
                    string.Join("; ", gates.Select(g => $"{g.Code}: {g.MessageEn}"))));
        }

        // 3. Fetch available daily capacities
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
            e.Campaign.MaxSubscribers,
            ReservedSlots: 0,
            CommittedSlots: e.Campaign.CurrentSubscribers);

        // 4. Coordinate Atomic Reservation
        var reservationResult = _reservationCoordinator.CoordinateReservation(new ReservationRequest(
            e.Campaign.Id,
            e.CampaignBundle.Id,
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

        // 5. Persist Reservation Allocations & Update Capacity Counts
        var sessionId = reservationResult.ReservationId ?? Guid.NewGuid();
        var expiresAtUtc = reservationResult.ExpiresAtUtc ?? _clock.UtcNow.AddMinutes(15);

        var session = new CampaignCheckoutSession(sessionId)
        {
            CampaignId = e.Campaign.Id,
            BundleId = e.CampaignBundle.Id,
            CustomerId = request.CustomerId,
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = expiresAtUtc,
            PriceQuoteSnapshotId = e.DurationPrice.Id,
            FinalAmount = e.Price.CampaignCustomerPrice.Amount,
            CurrencyCode = e.Price.CampaignCustomerPrice.CurrencyCode,
            PricingPolicyId = e.Policy.Id,
            PricingPolicyVersion = e.Policy.Version,
            CampaignSnapshotVersion = e.Campaign.CurrentSnapshotVersion
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
                CampaignId = e.Campaign.Id,
                BundleId = e.CampaignBundle.Id,
                ServiceDate = alloc.ServiceDate,
                RestaurantId = alloc.RestaurantId,
                Units = alloc.Units
            });

            allocationDtos.Add(new CampaignReservationAllocationDto(alloc.ServiceDate, alloc.RestaurantId, alloc.Units));
        }

        var dto = new CampaignCheckoutSessionDto(
            sessionId,
            e.Campaign.Id,
            e.CampaignBundle.Id,
            request.CustomerId,
            request.DurationDays,
            e.Price.CampaignCustomerPrice.Amount,
            e.Price.CampaignCustomerPrice.CurrencyCode,
            expiresAtUtc,
            nameof(CampaignCheckoutSessionStatus.Pending),
            allocationDtos);

        // 6. Record Idempotency
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
        var raw = $"{request.CampaignId:N}:{request.BundleId:N}:{request.CustomerId:N}:{request.DurationDays}:{string.Join(",", request.RequestedServiceDates.OrderBy(d => d))}:{request.AddressId?.ToString("N")}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
