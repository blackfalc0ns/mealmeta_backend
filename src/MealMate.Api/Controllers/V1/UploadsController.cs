using System.Security.Claims;
using MealMate.Api.Common;
using MealMate.Api.Security;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/uploads")]
public sealed class UploadsController : ApiControllerBase
{
    private const string DriversDeliveryPrefix = "uploads/drivers/delivery/";
    private const string DriverIssueEvidencePrefix = "uploads/drivers/issue-evidence/";

    [HttpGet("content")]
    [AllowAnonymous]
    public async Task<IActionResult> Read(
        [FromQuery] string key,
        [FromServices] IFileStorage storage,
        [FromServices] IAppDbContext db,
        [FromServices] SupportEvidenceScanService evidenceScans,
        [FromServices] MealMate.Application.Features.UnifiedSupport.DriverEvidenceAccessService staffEvidenceAccess,
        CancellationToken cancellationToken)
    {
        var normalizedKey = key?.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedKey)
            || (!normalizedKey.StartsWith("uploads/restaurant/meals/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/restaurant/branches/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/restaurant/ingredients/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/restaurant/logos/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/customers/profile/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/drivers/profile/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/drivers/documents/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/drivers/driver-registration/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/drivers/delivery/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith(DriverIssueEvidencePrefix, StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/programs/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/bundles/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/tiers/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/family/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/offers/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/marketing/", StringComparison.OrdinalIgnoreCase)
                && !normalizedKey.StartsWith("uploads/admin/ads/", StringComparison.OrdinalIgnoreCase)))
        {
            return NotFound();
        }

        // Private evidence requires the uploader/staff session or a short-lived
        // capability issued in an authorized response for this exact file.
        if (normalizedKey.StartsWith(DriversDeliveryPrefix, StringComparison.OrdinalIgnoreCase)
            || normalizedKey.StartsWith(DriverIssueEvidencePrefix, StringComparison.OrdinalIgnoreCase))
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            var links = HttpContext.RequestServices.GetService<DriverEvidenceReadLinks>();
            var hasReadCapability = links?.Validate(normalizedKey, Request.Query["readToken"].ToString()) == true;
            var record = await db.UploadFileRecords.AsNoTracking()
                .SingleOrDefaultAsync(r => r.StorageKey == normalizedKey, cancellationToken);
            var expectedFolder = normalizedKey.StartsWith(DriversDeliveryPrefix, StringComparison.OrdinalIgnoreCase)
                ? "delivery" : "issue-evidence";
            if (record is null || record.Scope != "drivers" || record.Folder != expectedFolder
                || record.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return NotFound();
            var userId = User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.UserId)
                ?? User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier
                    && Guid.TryParse(c.Value, out _))?.Value;
            var isUploader = false;
            if (User?.Identity?.IsAuthenticated == true && Guid.TryParse(userId, out var callerUserId))
            {
                isUploader = record.UploadedByUserId == callerUserId
                    && (await db.GetActiveAccountUserIdsAsync(new[] { callerUserId }, cancellationToken)).Contains(callerUserId);
            }
            var hasBoundStaffAccess = !hasReadCapability && !isUploader
                && User?.Identity?.IsAuthenticated == true
                && await staffEvidenceAccess.CanReadAsync(normalizedKey, cancellationToken);
            if (!hasReadCapability && !isUploader && !hasBoundStaffAccess)
                return User?.Identity?.IsAuthenticated == true ? Forbid() : Unauthorized();

            var scanned = await evidenceScans.ReadCleanAsync(record, cancellationToken);
            if (scanned.Bytes is null) return NotFound();
            return File(scanned.Bytes, record.ContentType, enableRangeProcessing: false);
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
