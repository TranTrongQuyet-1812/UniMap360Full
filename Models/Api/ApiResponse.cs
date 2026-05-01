namespace UniMap360.Models.Api;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public ApiError? Error { get; set; }
    public string? TraceId { get; set; }

    public static ApiResponse<T> Ok(T data)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data
        };
    }

    public static ApiResponse<T> Fail(ApiError error, string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = error,
            TraceId = traceId
        };
    }
}

public class ApiError
{
    public string Code { get; set; } = "UNKNOWN_ERROR";
    public string Message { get; set; } = "An unexpected error occurred.";
    public object? Details { get; set; }

    public ApiError() { }

    public ApiError(string message, string code = "ERROR", object? details = null)
    {
        Message = message;
        Code = code;
        Details = details;
    }
}
