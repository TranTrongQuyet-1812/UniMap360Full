namespace UniMap360.Services.Admin;

public interface IAdminAuditService
{
    Task WriteAsync(
        int adminAccountId,
        string action,
        string targetType,
        int? targetId,
        object? before,
        object? after,
        string? reason,
        HttpContext? httpContext,
        CancellationToken cancellationToken = default);
}