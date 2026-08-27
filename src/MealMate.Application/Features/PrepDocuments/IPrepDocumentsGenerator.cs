using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.PrepDocuments;

public interface IPrepDocumentsGenerator
{
  Task<Result> GenerateForDayAsync(
        Guid subscriptionDayId,
        bool forceReissue = false,
        bool rotateBarcode = false,
        CancellationToken cancellationToken = default);
}
