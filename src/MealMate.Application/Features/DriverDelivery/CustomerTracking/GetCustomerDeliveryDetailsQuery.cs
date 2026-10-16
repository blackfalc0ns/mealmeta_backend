using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

public sealed record GetCustomerDeliveryDetailsQuery(Guid SubscriptionDayId)
    : IQuery<CustomerDeliveryDetailsResponseDto>;

/// <summary>
/// One subscription day's delivery details, built from the same shared projection as
/// the widget: usable before any TripStop exists, during delivery, and after delivery.
/// </summary>
public sealed record CustomerDeliveryDetailsResponseDto(
    DateOnly ServiceDate,
    DateTime ServerTimeUtc,
    int RefreshAfterSeconds,
    CustomerDeliveryWidgetItemDto Delivery);

/// <summary>
/// Read-only per-day details. Authorization happens before any sensitive data is read:
/// the day resolves to its subscription and the existing family calendar read gate
/// decides access. A missing day, a missing subscription and a refused gate all return
/// the same not-found so another customer's box existence is never disclosed.
/// </summary>
public sealed class GetCustomerDeliveryDetailsQueryHandler
    : IQueryHandler<GetCustomerDeliveryDetailsQuery, CustomerDeliveryDetailsResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFamilyCalendarAccessGate _accessGate;
    private readonly ICustomerDeliveryPresentationBuilder _presentationBuilder;
    private readonly IDateTimeProvider _clock;

    public GetCustomerDeliveryDetailsQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFamilyCalendarAccessGate accessGate,
        ICustomerDeliveryPresentationBuilder presentationBuilder,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _accessGate = accessGate;
        _presentationBuilder = presentationBuilder;
        _clock = clock;
    }

    public async Task<Result<CustomerDeliveryDetailsResponseDto>> Handle(
        GetCustomerDeliveryDetailsQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is null)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden);
        }

        var day = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => d.Id == request.SubscriptionDayId)
            .Select(d => new { d.Id, d.SubscriptionId, d.ServiceDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (day is null)
        {
            return NotFound();
        }

        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == day.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            return NotFound();
        }

        var authorization = await _accessGate.AuthorizeAsync(subscription, requireWrite: false, cancellationToken);
        if (authorization.IsFailure)
        {
            return NotFound();
        }

        var items = await _presentationBuilder.BuildAsync([day.Id], cancellationToken);
        if (items.Count == 0)
        {
            return NotFound();
        }

        return Result.Success(new CustomerDeliveryDetailsResponseDto(
            day.ServiceDate,
            _clock.UtcNow,
            CustomerDeliveryWidgetDefaults.RefreshSeconds,
            items[0]));
    }

    private static Error NotFound() => Error.NotFound(MessageCodes.Common.NotFound);
}
