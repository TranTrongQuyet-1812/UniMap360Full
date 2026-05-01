using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UniMap360.Models.Api;

namespace UniMap360.Filters;

/// <summary>
/// Filter tự động validate ModelState và trả ApiResponse chuẩn nếu invalid.
/// Khi đã có filter này, controller không cần check ModelState thủ công.
/// </summary>
public sealed class ApiValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;

        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .SelectMany(x => x.Value!.Errors.Select(e => new
            {
                field = x.Key,
                message = string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? "Giá trị không hợp lệ."
                    : e.ErrorMessage
            }))
            .ToList();

        var firstMessage = errors.FirstOrDefault()?.message ?? "Dữ liệu không hợp lệ.";

        var response = new
        {
            success = false,
            data = (object?)null,
            error = new
            {
                code = "VALIDATION_ERROR",
                message = firstMessage,
                details = errors
            },
            traceId = context.HttpContext.TraceIdentifier
        };

        context.Result = new BadRequestObjectResult(response);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
