using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public sealed record UpdateOperationalPoliciesCommand(
    OperationalPolicySettings Settings,
    Guid AdminUserId) : ICommand<OperationalPolicyDto>;

public sealed class UpdateOperationalPoliciesCommandHandler : ICommandHandler<UpdateOperationalPoliciesCommand, OperationalPolicyDto>
{
    private readonly IOperationalPolicyService _policyService;
    private readonly IDateTimeProvider _clock;

    public UpdateOperationalPoliciesCommandHandler(IOperationalPolicyService policyService, IDateTimeProvider clock)
    {
        _policyService = policyService;
        _clock = clock;
    }

    public async Task<Result<OperationalPolicyDto>> Handle(UpdateOperationalPoliciesCommand request, CancellationToken ct)
    {
        var updateResult = await _policyService.UpdateSettingsAsync(request.Settings, request.AdminUserId, ct);
        if (updateResult.IsFailure)
        {
            return Result.Failure<OperationalPolicyDto>(Error.Validation("operational_policies.update_failed", updateResult.Error));
        }

        var preview = PolicySimulationHelper.GenerateSimulationPreview(updateResult.Value, _clock, _policyService);
        return Result.Success(new OperationalPolicyDto(updateResult.Value.Cutoff, updateResult.Value.Cancellation, updateResult.Value.Freeze, preview));
    }
}
