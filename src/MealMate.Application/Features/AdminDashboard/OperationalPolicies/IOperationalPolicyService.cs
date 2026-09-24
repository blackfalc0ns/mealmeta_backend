using MealMate.Domain.Common;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public interface IOperationalPolicyService
{
    Task<OperationalPolicySettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<Result<OperationalPolicySettings>> UpdateSettingsAsync(
        OperationalPolicySettings newSettings,
        Guid adminUserId,
        CancellationToken cancellationToken = default);

    DateTime CalculateSelectionDeadlineUtc(DateOnly serviceDate, OperationalPolicySettings? settings = null);
}
