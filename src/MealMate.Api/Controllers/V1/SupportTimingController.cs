using MealMate.Api.Common;
using MealMate.Application.Features.UnifiedSupport.Timing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/support/timing-policies")]
public sealed class SupportTimingController(SupportTimingPolicyService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => HandleResult(await service.ListPoliciesAsync(ct));

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
        => HandleResult(await service.GetActivePolicyAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => HandleResult(await service.GetPolicyByIdAsync(id, ct));

    [HttpPost("publish")]
    public async Task<IActionResult> Publish([FromBody] PublishSupportTimingPolicyRequest request, CancellationToken ct)
        => HandleResult(await service.PublishPolicyAsync(request, ct));

    [HttpGet("cases/{caseId:guid}/cycles")]
    public async Task<IActionResult> GetCycles(Guid caseId, CancellationToken ct)
        => HandleResult(await service.GetCyclesForCaseAsync(caseId, ct));
}
