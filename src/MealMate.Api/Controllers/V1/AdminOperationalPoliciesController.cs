using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[HasPermission(PermissionCodes.SettingsManage)]
[Route("api/v1/admin/operational-policies")]
public sealed class AdminOperationalPoliciesController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminOperationalPoliciesController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => HandleResult(await _sender.Send(new GetOperationalPoliciesQuery(), ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] OperationalPolicySettings settings, CancellationToken ct)
    {
        var adminId = _currentUser.UserId ?? Guid.Empty;
        return HandleResult(await _sender.Send(new UpdateOperationalPoliciesCommand(settings, adminId), ct));
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PolicyPreviewRequest request, CancellationToken ct)
        => HandleResult(await _sender.Send(new PreviewOperationalPoliciesQuery(
            request.Settings,
            request.SampleServiceDate,
            request.SampleSubscriptionPrice,
            request.SampleTotalDays,
            request.SampleRemainingDays), ct));
}
