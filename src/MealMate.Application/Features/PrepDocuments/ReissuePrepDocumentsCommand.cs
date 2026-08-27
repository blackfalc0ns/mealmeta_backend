using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.PrepDocuments;

public sealed record ReissuePrepDocumentsCommand(Guid SubscriptionDayId, bool NewBarcode) : ICommand;

public sealed class ReissuePrepDocumentsCommandHandler(IPrepDocumentsGenerator generator)
    : ICommandHandler<ReissuePrepDocumentsCommand>
{
    public Task<Result> Handle(ReissuePrepDocumentsCommand request, CancellationToken cancellationToken)
        => generator.GenerateForDayAsync(
            request.SubscriptionDayId,
            forceReissue: true,
            rotateBarcode: request.NewBarcode,
            cancellationToken);
}
