using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MealMate.Application.Common.Localization;

namespace MealMate.Api.Localization;

/// <summary>
/// Rewrites API JSON after serialization so database snapshots, dictionaries,
/// and other untyped payloads also emit one language.
/// </summary>
public sealed class BilingualJsonResponseMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (LocalizedJsonScope.IncludeSourceFields || ShouldSkip(context.Request.Path))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Position = 0;
        if (buffer.Length == 0 || !IsJsonResponse(context.Response))
        {
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        JsonNode? node;
        try
        {
            node = await JsonNode.ParseAsync(buffer, cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        node = BilingualJsonCollapser.Collapse(node);
        context.Response.ContentLength = null;
        await JsonSerializer.SerializeAsync(originalBody, node, WriteOptions, context.RequestAborted);
    }

    private static bool ShouldSkip(PathString path)
        => path.StartsWithSegments("/swagger")
           || path.StartsWithSegments("/health")
           || path.StartsWithSegments("/hubs");

    private static bool IsJsonResponse(HttpResponse response)
    {
        var contentType = response.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
               || contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase);
    }
}
