using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record CustomerOfferDetailsDto(OfferDto Offer, DateTime ServerNowUtc);

public sealed record GetCustomerOfferDetailsQuery(
    Guid OfferId,
    Guid CustomerId = default,
    decimal CartAmount = 0m,
    int DurationDays = 0,
    string? DeliveryGovernorate = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    Guid? RestaurantId = null) : IQuery<CustomerOfferDetailsDto>;

/// <summary>Deep links use the same visibility predicate as the offers hub, never admin detail access.</summary>
public sealed class GetCustomerOfferDetailsQueryHandler : IQueryHandler<GetCustomerOfferDetailsQuery, CustomerOfferDetailsDto>
{
    private readonly GetCustomerAvailableOffersQueryHandler _available;
    private readonly IDateTimeProvider _clock;

    public GetCustomerOfferDetailsQueryHandler(GetCustomerAvailableOffersQueryHandler available, IDateTimeProvider clock)
    {
        _available = available;
        _clock = clock;
    }

    public async Task<Result<CustomerOfferDetailsDto>> Handle(GetCustomerOfferDetailsQuery request, CancellationToken ct)
    {
        var available = await _available.Handle(new GetCustomerAvailableOffersQuery(
            request.CustomerId, request.CartAmount, request.DurationDays, request.DeliveryGovernorate,
            request.ProgramId, request.BundleId, request.RestaurantId), ct);
        if (available.IsFailure)
            return Result.Failure<CustomerOfferDetailsDto>(available.Error!);

        var offer = available.Value!.FirstOrDefault(o => o.Id == request.OfferId);
        if (offer is null)
            return Error.NotFound("offers.customer.unavailable", string.Empty);

        return Result.Success(new CustomerOfferDetailsDto(offer, _clock.UtcNow));
    }
}
