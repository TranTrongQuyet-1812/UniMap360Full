using Microsoft.AspNetCore.Mvc;

namespace UniMap360.Models.Api;

/// <summary>
/// Extension methods giúp Controller trả về ApiResponse một cách ngắn gọn.
/// </summary>
public static class ApiResponseExtensions
{
    /// <summary>
    /// Trả về 200 OK với ApiResponse envelope chuẩn.
    /// </summary>
    public static OkObjectResult ApiOk(this ControllerBase controller, object? data = null)
    {
        return controller.Ok(ApiResponse<object?>.Ok(data));
    }

    /// <summary>
    /// Trả về 400 BadRequest với ApiResponse envelope chuẩn.
    /// </summary>
    public static BadRequestObjectResult ApiBadRequest(this ControllerBase controller, string message, string code = "BAD_REQUEST")
    {
        var error = new ApiError(message, code);
        var traceId = controller.HttpContext.TraceIdentifier;
        return controller.BadRequest(ApiResponse<object>.Fail(error, traceId));
    }

    /// <summary>
    /// Trả về 401 Unauthorized với ApiResponse envelope chuẩn.
    /// </summary>
    public static UnauthorizedObjectResult ApiUnauthorized(this ControllerBase controller, string message, string code = "UNAUTHORIZED")
    {
        var error = new ApiError(message, code);
        var traceId = controller.HttpContext.TraceIdentifier;
        return controller.Unauthorized(ApiResponse<object>.Fail(error, traceId));
    }

    /// <summary>
    /// Trả về 404 NotFound với ApiResponse envelope chuẩn.
    /// </summary>
    public static NotFoundObjectResult ApiNotFound(this ControllerBase controller, string message, string code = "NOT_FOUND")
    {
        var error = new ApiError(message, code);
        var traceId = controller.HttpContext.TraceIdentifier;
        return controller.NotFound(ApiResponse<object>.Fail(error, traceId));
    }

    /// <summary>
    /// Trả về 409 Conflict với ApiResponse envelope chuẩn.
    /// </summary>
    public static ConflictObjectResult ApiConflict(this ControllerBase controller, string message, string code = "CONFLICT")
    {
        var error = new ApiError(message, code);
        var traceId = controller.HttpContext.TraceIdentifier;
        return controller.Conflict(ApiResponse<object>.Fail(error, traceId));
    }
}
