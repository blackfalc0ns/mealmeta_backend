using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

public sealed class SubscriptionCancellationHandlers :
    ICommandHandler<RequestSubscriptionCancellationCommand, CustomerCancellationResultDto>,
    IQueryHandler<GetSubscriptionCancellationQuoteQuery, CustomerCancellationQuoteDto>,
    ICommandHandler<ProcessAdminSubscriptionCancellationCommand, CustomerCancellationResultDto>,
    ICommandHandler<CancelUncompletedSubscriptionCommand>
{
    private readonly ISubscriptionCancellationService _service;

    public SubscriptionCancellationHandlers(ISubscriptionCancellationService service)
    {
        _service = service;
    }

    public Task<Result<CustomerCancellationResultDto>> Handle(
        RequestSubscriptionCancellationCommand request,
        CancellationToken cancellationToken)
    {
        return _service.RequestCancellationAsync(
            request.SubscriptionId,
            request.CallerUserId,
            request.CallerUserType,
            request.Reason,
            cancellationToken);
    }

    public Task<Result<CustomerCancellationQuoteDto>> Handle(
        GetSubscriptionCancellationQuoteQuery request,
        CancellationToken cancellationToken)
    {
        return _service.GetQuoteAsync(
            request.SubscriptionId,
            request.CallerUserId,
            request.CallerUserType,
            cancellationToken);
    }

    public Task<Result<CustomerCancellationResultDto>> Handle(
        ProcessAdminSubscriptionCancellationCommand request,
        CancellationToken cancellationToken)
    {
        return _service.AdminCancelAsync(
            request.SubscriptionId,
            request.AdminUserId,
            request.Reason,
            request.ForceImmediate,
            cancellationToken);
    }

    public Task<Result> Handle(
        CancelUncompletedSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        return _service.CancelUncompletedSubscriptionAsync(
            request.SubscriptionId,
            request.CallerUserId,
            cancellationToken);
    }
}
