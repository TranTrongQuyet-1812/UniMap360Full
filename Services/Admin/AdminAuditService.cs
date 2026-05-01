using System.Text.Json;
using UniMap360.Models;

namespace UniMap360.Services.Admin;

public sealed class AdminAuditService : IAdminAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly UniMap360ProContext _context;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(UniMap360ProContext context, ILogger<AdminAuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task WriteAsync(
        int adminAccountId,
        string action,
        string targetType,
        int? targetId,
        object? before,
        object? after,
        string? reason,
        HttpContext? httpContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var log = new AdminAuditLog
            {
                AdminAccountId = adminAccountId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
                AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
                Reason = reason,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            _context.AdminAuditLogs.Add(log);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Admin action recorded. AdminId={AdminId}, Action={Action}, TargetType={TargetType}, TargetId={TargetId}, Reason={Reason}", 
                adminAccountId, action, targetType, targetId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write AdminAuditLog. Action={Action}, TargetType={TargetType}, TargetId={TargetId}", action, targetType, targetId);
        }
    }
}