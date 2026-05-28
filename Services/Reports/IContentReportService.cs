using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniMap360.Models;

namespace UniMap360.Services.Reports;

public interface IContentReportService
{
    Task<ContentReport> CreateReportAsync(int reporterAccountId, string targetType, int targetId, string reason, CancellationToken cancellationToken = default);
    Task<List<ContentReport>> GetReportsAsync(string? targetType, string? status, CancellationToken cancellationToken = default);
    Task<ContentReport?> GetReportDetailAsync(int reportId, CancellationToken cancellationToken = default);
    Task<bool> MarkReviewingAsync(int adminAccountId, int reportId, CancellationToken cancellationToken = default);
    Task<bool> ResolveAsync(int adminAccountId, int reportId, string action, string? reason, bool applyToAllPendingReportsOfSameTarget, Microsoft.AspNetCore.Http.HttpContext? httpContext, CancellationToken cancellationToken = default);
    Task<bool> DismissAsync(int adminAccountId, int reportId, string? reason, CancellationToken cancellationToken = default);
}
