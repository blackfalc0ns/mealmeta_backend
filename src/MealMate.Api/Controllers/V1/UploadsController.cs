using MealMate.Api.Common;
using MealMate.Application.Features.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/uploads")]
public sealed class UploadsController : ApiControllerBase
{
    [HttpPost]
    [Authorize]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string? folder,
        [FromForm] string? scope,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "File is required.",
                detail: "A non-empty file must be provided.");
        }

        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(new UploadFileCommand(
            file.FileName,
            file.ContentType,
            stream,
            folder,
            scope), cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/uploads/{result.Value.StorageKey}", result.Value)
            : HandleResult(result);
    }
}
