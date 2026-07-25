using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/documents")]
public sealed class DocumentsController : ApiControllerBase
{
    [HttpPost]
    [HasPermission(PermissionCodes.DocumentsUpload)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] DocumentOwnerType ownerType,
        [FromForm] Guid ownerId,
        [FromForm] DocumentType documentType,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(new UploadDocumentCommand(
            ownerType,
            ownerId,
            documentType,
            file.FileName,
            file.ContentType,
            stream), cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/documents/{result.Value.Id}", result.Value)
            : HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetDocumentQuery(id), cancellationToken));

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ListForOwner([FromQuery] DocumentOwnerType ownerType, [FromQuery] Guid ownerId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListOwnerDocumentsQuery(ownerType, ownerId), cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.DocumentsReview)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ReviewDocumentRequest? body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ReviewDocumentCommand(id, true, body?.Reason), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [HasPermission(PermissionCodes.DocumentsReview)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ReviewDocumentRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ReviewDocumentCommand(id, false, body.Reason), cancellationToken));
}

public sealed record ReviewDocumentRequest(string? Reason);
