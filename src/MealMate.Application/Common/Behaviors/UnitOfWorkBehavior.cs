using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MediatR;

namespace MealMate.Application.Common.Behaviors;

/// <summary>
/// Commits a command's staged changes as one unit of work. Command handlers should
/// stage changes (Add/Update/Remove) but NOT call SaveChanges themselves; this
/// behavior saves once the handler returns a successful result. Queries are skipped.
/// </summary>
public sealed class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;

    public UnitOfWorkBehavior(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IBaseCommand || request is ISelfPersistingCommand)
        {
            // Queries never persist here, and self-persisting commands commit inside
            // their own lock/transaction — a second save would write stale tracked
            // state the handler already reconciled (F04/V07).
            return await next();
        }

        var response = await next();

        // Do not persist when the use case reported an expected business failure.
        if (response is Result { IsFailure: true })
        {
            return response;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
