using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record CustomerOffersHubDto(
    IReadOnlyList<OfferDto> Offers,
    DateTime ServerNowUtc);

/// <summary>
/// Additive offers-hub envelope for the mobile client: the same customer-visible
/// offer list as GET available, plus the authoritative server clock so the app
/// never trusts a device clock for countdowns or purchase eligibility.
/// </summary>
public sealed record GetCustomerOffersHubQuery(
    Guid CustomerId = default,
    decimal CartAmount = 0m,
    int DurationDays = 0,
    string? DeliveryGovernorate = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    Guid? RestaurantId = null) : IQuery<CustomerOffersHubDto>;

public sealed class GetCustomerOffersHubQueryHandler : IQueryHandler<GetCustomerOffersHubQuery, CustomerOffersHubDto>
{
    private readonly GetCustomerAvailableOffersQueryHandler _availableHandler;
    private readonly IDateTimeProvider _clock;

    public GetCustomerOffersHubQueryHandler(
        GetCustomerAvailableOffersQueryHandler availableHandler,
        IDateTimeProvider clock)
    {
        _availableHandler = availableHandler;
        _clock = clock;
    }

    public async Task<Result<CustomerOffersHubDto>> Handle(GetCustomerOffersHubQuery request, CancellationToken cancellationToken)
    {
        var available = await _availableHandler.Handle(
            new GetCustomerAvailableOffersQuery(
                request.CustomerId,
                request.CartAmount,
                request.DurationDays,
                request.DeliveryGovernorate,
                request.ProgramId,
                request.BundleId,
                request.RestaurantId),
            cancellationToken);

        if (available.IsFailure)
        {
            return Result.Failure<CustomerOffersHubDto>(available.Error!);
        }

        return Result.Success(new CustomerOffersHubDto(available.Value!, _clock.UtcNow));
    }
}
