using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.CustomerCalendar;

public interface ICapacityTransferService
{
    Task<Result> TransferCapacityAsync(
        Guid subscriptionId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        Guid? oldRestaurantId,
        Guid newRestaurantId,
        int quantity = 1,
        CancellationToken ct = default);
}
