using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace UniMap360.Middleware;

/// <summary>
/// Thêm TraceId vào scope của ILogger để mọi log trong request đều có thông tin này,
/// giúp dễ dàng truy vết (observability).
/// </summary>
public sealed class TraceLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TraceLoggingMiddleware> _logger;

    public TraceLoggingMiddleware(RequestDelegate next, ILogger<TraceLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var traceId = context.TraceIdentifier;

        // Bắt đầu một logging scope chứa TraceId
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = traceId
        }))
        {
            return _next(context);
        }
    }
}
