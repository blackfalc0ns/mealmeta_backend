using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/uploads")]
public sealed class UploadsController : ApiControllerBase
{
    [HttpGet("content")]
    [AllowAnonymous]
    public async Task<IActionResult> Read(
        [FromQuery] string key,
        [FromServices] IFileStorage storage,
        CancellationToken cancellationToken)
    {
        var normalizedKey = key?.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedKey)
            || (!normalizedKey.StartsWith("uploads/restaurant/meals/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/restaurant/branches/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/restaurant/ingredients/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/customers/profile/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/programs/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/bundles/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/tiers/", StringComparison.OrdinalIgnoreCase)))
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(normalizedKey, cancellationToken);
        return File(stream, ResolveContentType(normalizedKey), enableRangeProcessing: true);
    }

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

    private static string ResolveContentType(string key)
        => Path.GetExtension(key).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
}
