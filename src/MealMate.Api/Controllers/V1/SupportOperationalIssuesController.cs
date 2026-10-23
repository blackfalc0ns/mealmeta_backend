using MealMate.Api.Common;
using MealMate.Application.Features.UnifiedSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/support/operational-issues")]
[Authorize]
public sealed class SupportOperationalIssuesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => HandleResult(await Sender.Send(new ListOperationalSupportIssuesQuery(pageNumber, pageSize), ct));

    [HttpGet("{issueId:guid}")]
    public async Task<IActionResult> Detail(Guid issueId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetOperationalSupportIssueQuery(issueId), ct));

    [HttpGet("{issueId:guid}/evidence/{evidenceId:guid}")]
    public async Task<IActionResult> Evidence(Guid issueId, Guid evidenceId,
        [FromServices] DriverOperationalSupportService service, CancellationToken ct)
    {
        var result = await service.ReadEvidenceAsync(issueId, evidenceId, ct);
        if (!result.IsSuccess) return HandleResult(result);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value.Bytes, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("{issueId:guid}/escalate")]
    public async Task<IActionResult> Escalate(Guid issueId, [FromBody] EscalateOperationalIssueRequest request,
        CancellationToken ct)
        => HandleResult(await Sender.Send(new EscalateOperationalSupportIssueCommand(issueId, request), ct));

    [HttpPost("{issueId:guid}/progress")]
    public async Task<IActionResult> Progress(Guid issueId, [FromBody] OperationalSupportProgressRequest request,
        CancellationToken ct)
        => HandleResult(await Sender.Send(new UpdateOperationalSupportProgressCommand(issueId, request.Stage, request.Notes), ct));

    [HttpPost("{issueId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid issueId, [FromBody] OperationalSupportResolutionRequest request,
        CancellationToken ct)
        => HandleResult(await Sender.Send(new ResolveOperationalSupportIssueCommand(issueId, request.ResolutionNotes), ct));
}
