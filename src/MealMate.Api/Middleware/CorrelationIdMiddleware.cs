using MealMate.Application.Common.Abstractions;

namespace MealMate.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = context.TraceIdentifier;
        }

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await _next(context);
    }
}

public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string CorrelationId
    {
        get
        {
            var http = _httpContextAccessor.HttpContext;
            if (http?.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) == true
                && value is string s
                && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }

            return http?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        }
    }
}
