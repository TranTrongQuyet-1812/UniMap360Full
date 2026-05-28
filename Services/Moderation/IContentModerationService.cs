using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace UniMap360.Services.Moderation;

public interface IContentModerationService
{
    Task<bool> ApproveAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default);
    Task<bool> HideAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default);
    Task<bool> RestoreAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default);
    Task<bool> RejectAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default);
}
