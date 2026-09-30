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
            var message = updateResult.Error ?? "Unable to publish operational policies.";
            var conflict = message.StartsWith("Policy version changed") || message.StartsWith("Policy publication conflicted");
            return Result.Failure<OperationalPolicyDto>(conflict
                ? Error.Conflict("operational_policies.version_conflict", message)
                : Error.Validation("operational_policies.update_failed", message));
        }

        var preview = PolicySimulationHelper.GenerateSimulationPreview(updateResult.Value, _clock, _policyService);
        return Result.Success(OperationalPolicyDto.From(updateResult.Value, preview));
    }
}
