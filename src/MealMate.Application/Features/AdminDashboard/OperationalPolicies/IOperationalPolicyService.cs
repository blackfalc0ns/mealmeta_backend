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

    /// <summary>
    /// Approved first-day exception: deadline = payment verified + configured window hours when
    /// that still closes before first delivery; otherwise the standard cutoff applies (the
    /// late-payment path — no invented alternative deadline).
    /// </summary>
    DateTime CalculateFirstDaySelectionDeadlineUtc(DateTime paymentVerifiedAtUtc, DateOnly firstDeliveryDate, OperationalPolicySettings? settings = null);
}
