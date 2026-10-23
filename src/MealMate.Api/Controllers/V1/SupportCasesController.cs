using MealMate.Api.Common;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/support/cases")]
public sealed class SupportCasesController(SupportCaseService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] SupportCaseListQuery query, CancellationToken ct)
        => HandleResult(await service.ListAsync(query, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupportCaseRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { caseId = result.Value.Id }, result.Value) : HandleResult(result);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<IActionResult> Get(Guid caseId, CancellationToken ct)
        => HandleResult(await service.GetAsync(caseId, ct));

    [HttpGet("{caseId:guid}/task-recipients")]
    public async Task<IActionResult> TaskRecipients(Guid caseId, CancellationToken ct)
        => HandleResult(await service.GetTaskRecipientsAsync(caseId, ct));

    [HttpGet("{caseId:guid}/assignment-recipients")]
    public async Task<IActionResult> AssignmentRecipients(Guid caseId, CancellationToken ct)
        => HandleResult(await service.GetAssignmentRecipientsAsync(caseId, ct));

    [HttpPost("{caseId:guid}/assignment-transfer-requests")]
    public async Task<IActionResult> RequestAssignmentTransfer(Guid caseId, [FromBody] SupportCaseTransferRequest request, CancellationToken ct)
        => HandleResult(await service.RequestAssignmentTransferAsync(caseId, request, ct));

    [HttpPost("{caseId:guid}/assignment-transfer-requests/{decision}")]
    public async Task<IActionResult> DecideAssignmentTransfer(Guid caseId, string decision, [FromBody] SupportCaseTransferDecisionRequest request, CancellationToken ct)
        => HandleResult(await service.DecideAssignmentTransferAsync(caseId, decision, request, ct));

    [HttpPost("{caseId:guid}/tasks/{taskId:guid}/reassign")]
    public async Task<IActionResult> ReassignPartyTask(Guid caseId, Guid taskId, [FromBody] ReassignSupportTaskRequest request, CancellationToken ct)
        => HandleResult(await service.ReassignPartyTaskAsync(caseId, taskId, request, ct));

    [HttpPost("{caseId:guid}/messages")]
    public async Task<IActionResult> AddMessage(Guid caseId, [FromBody] AddSupportMessageRequest request, CancellationToken ct)
        => HandleResult(await service.AddMessageAsync(caseId, request, ct));

    [HttpPost("{caseId:guid}/internal-notes")]
    public async Task<IActionResult> AddInternalNote(Guid caseId, [FromBody] AddSupportInternalNoteRequest request, CancellationToken ct)
        => HandleResult(await service.AddInternalNoteAsync(caseId, request, ct));

    [HttpPost("{caseId:guid}/actions/{operation}")]
    public async Task<IActionResult> Execute(Guid caseId, string operation, [FromBody] SupportCaseActionRequest request, CancellationToken ct)
        => HandleResult(await service.ExecuteActionAsync(caseId, request with { Action = operation }, ct));

    [HttpPost("{caseId:guid}/links")]
    public async Task<IActionResult> AddLink(Guid caseId, [FromBody] AddSupportCaseLinkRequest request, CancellationToken ct)
        => HandleResult(await service.AddLinkAsync(caseId, request, ct));

    [HttpPost("/api/v1/support/attachments")]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Problem(statusCode: StatusCodes.Status400BadRequest, title: SupportCaseProblemCodes.AttachmentUnsupported);
        await using var stream = file.OpenReadStream();
        var result = await service.UploadAttachmentAsync(file.FileName, file.ContentType, stream, ct);
        return HandleResult(result);
    }

    [HttpGet("{caseId:guid}/messages/{messageId:guid}/attachments/{index:int}")]
    public async Task<IActionResult> ReadAttachment(Guid caseId, Guid messageId, int index,
        CancellationToken ct)
    {
        var result = await service.GetAttachmentAsync(caseId, messageId, index, ct);
        if (!result.IsSuccess) return HandleResult(result);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value.Bytes, result.Value.ContentType, fileDownloadName: result.Value.FileName, enableRangeProcessing: false);
    }
}
