using MealMate.Api.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.Uploads;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MealMate.Api.Filters;

/// <summary>Creates fresh image capabilities only on successful, authorized API DTOs.</summary>
public sealed class DriverEvidenceReadLinksFilter(DriverEvidenceReadLinks links) : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true
            || context.Result is not ObjectResult result || (result.StatusCode ?? context.HttpContext.Response.StatusCode) is < 200 or >= 300) return;
        var original = result.Value;
        result.Value = original switch
        {
            DriverSupportIssueDetailDto dto => dto with { EvidenceUrls = dto.EvidenceUrls.Select(url => links.CreateUrl(url)!).ToArray() },
            DriverSupportIssueMutationDto dto => dto with { EvidenceUrls = dto.EvidenceUrls.Select(url => links.CreateUrl(url)!).ToArray() },
            SupportProblemDetailsDto dto => dto with { EvidencePhotos = dto.EvidencePhotos.Select(photo => photo with { Url = links.CreateUrl(photo.Url), ThumbnailUrl = links.CreateUrl(photo.ThumbnailUrl) }).ToArray() },
            UploadedFileDto dto => dto with { ReadUrl = links.CreateUrl(dto.StorageKey) is { } signed && signed.Contains("readToken=", StringComparison.Ordinal) ? signed : dto.ReadUrl },
            _ => original
        };
        if (result.Value is DriverSupportIssueDetailDto or DriverSupportIssueMutationDto or SupportProblemDetailsDto
            || result.Value is UploadedFileDto upload && upload.ReadUrl.Contains("readToken=", StringComparison.Ordinal))
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
