using Microsoft.AspNetCore.Mvc;

namespace UniMap360.Middleware;

public sealed class ApiExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ApiExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
        {
            var traceId = context.TraceIdentifier;
            _logger.LogError(ex, "Unhandled API exception. TraceId={TraceId}, Method={Method}, Path={Path}", traceId, context.Request.Method, context.Request.Path);
            
            if (context.Response.HasStarted)
            {
                throw;
            }

            var error = new UniMap360.Models.Api.ApiError(
                message: _environment.IsDevelopment() ? ex.Message : "Vui lòng thử lại sau.",
                code: "INTERNAL_SERVER_ERROR"
            );

            var response = UniMap360.Models.Api.ApiResponse<object>.Fail(error, traceId);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}

