using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.CustomerCalendar;

public sealed class CustomerCalendarHandlers :
    IQueryHandler<GetCustomerCalendarQuery, CustomerCalendarScreenDto>,
    IQueryHandler<GetCustomerCalendarDayQuery, CustomerCalendarDayDto>,
    IQueryHandler<GetCustomerDayRestaurantsQuery, CustomerDayRestaurantsDto>,
    IQueryHandler<GetCustomerDayRestaurantMealsQuery, CustomerDayRestaurantMealsDto>,
    IQueryHandler<GetCustomerDayAvailableMealsQuery, CustomerDayRestaurantMealsDto>,
    ICommandHandler<SelectCustomerDayRestaurantCommand, CustomerCalendarDayDto>,
    ICommandHandler<SelectCustomerDaySlotMealCommand, CustomerCalendarDayDto>,
    ICommandHandler<CompleteCustomerDayBoxCommand, CustomerCalendarDayDto>,
    ICommandHandler<AutoSelectCustomerDayBoxCommand, CustomerCalendarDayDto>
{
    private readonly CustomerCalendarEngine _engine;

    public CustomerCalendarHandlers(CustomerCalendarEngine engine) => _engine = engine;

    public Task<Result<CustomerCalendarScreenDto>> Handle(GetCustomerCalendarQuery query, CancellationToken cancellationToken)
        => _engine.GetCalendarAsync(query.SubscriptionId, cancellationToken);

    public Task<Result<CustomerCalendarDayDto>> Handle(GetCustomerCalendarDayQuery query, CancellationToken cancellationToken)
        => _engine.GetDayAsync(query.SubscriptionId, query.ServiceDate, cancellationToken);

    public Task<Result<CustomerDayRestaurantsDto>> Handle(GetCustomerDayRestaurantsQuery query, CancellationToken cancellationToken)
        => _engine.GetDayRestaurantsAsync(query.SubscriptionId, query.ServiceDate, query.Query, cancellationToken);

    public Task<Result<CustomerDayRestaurantMealsDto>> Handle(GetCustomerDayRestaurantMealsQuery query, CancellationToken cancellationToken)
        => _engine.GetRestaurantMealsAsync(query.SubscriptionId, query.ServiceDate, query.RestaurantId, query.Slot, cancellationToken);

    public Task<Result<CustomerDayRestaurantMealsDto>> Handle(GetCustomerDayAvailableMealsQuery query, CancellationToken cancellationToken)
        => _engine.GetAvailableMealsAsync(query.SubscriptionId, query.ServiceDate, query.Slot, cancellationToken);

    public Task<Result<CustomerCalendarDayDto>> Handle(SelectCustomerDayRestaurantCommand command, CancellationToken cancellationToken)
        => _engine.SelectRestaurantAsync(command.SubscriptionId, command.ServiceDate, command.RestaurantId, cancellationToken);

    public Task<Result<CustomerCalendarDayDto>> Handle(SelectCustomerDaySlotMealCommand command, CancellationToken cancellationToken)
        => _engine.SelectSlotMealAsync(command.SubscriptionId, command.ServiceDate, command.MealId, command.Slot, cancellationToken);

    public Task<Result<CustomerCalendarDayDto>> Handle(CompleteCustomerDayBoxCommand command, CancellationToken cancellationToken)
        => _engine.CompleteBoxAsync(command.SubscriptionId, command.ServiceDate, command.IdempotencyKey, cancellationToken);

    public Task<Result<CustomerCalendarDayDto>> Handle(AutoSelectCustomerDayBoxCommand command, CancellationToken cancellationToken)
        => _engine.AutoSelectFullBoxAsync(command.SubscriptionId, command.ServiceDate, command.LockAfter, requireOwner: true, cancellationToken);
}
