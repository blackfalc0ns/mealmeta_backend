using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/driver/support/issues")]
[Authorize(Policy = "DriverPolicy")]
public sealed class DriverSupportIssuesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string status = "All", [FromQuery] string? search = null, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => HandleResult(await Sender.Send(new GetDriverSupportIssuesQuery(status, search, pageNumber, pageSize), ct));
    [HttpGet("{issueId:guid}")]
    public async Task<IActionResult> Detail(Guid issueId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetDriverSupportIssueQuery(issueId), ct));
    [HttpPost("{issueId:guid}/photos")]
    public async Task<IActionResult> AddPhotos(Guid issueId, [FromBody] AddDriverSupportIssuePhotosRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new AddDriverSupportIssuePhotosCommand(issueId, request.StorageKeys), ct));
}
