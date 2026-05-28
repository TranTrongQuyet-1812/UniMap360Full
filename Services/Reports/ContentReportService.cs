using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Services.Moderation;

namespace UniMap360.Services.Reports;

public class ContentReportService : IContentReportService
{
    private readonly UniMap360ProContext _context;
    private readonly IContentModerationService _moderationService;

    public ContentReportService(
        UniMap360ProContext context,
        IContentModerationService moderationService)
    {
        _context = context;
        _moderationService = moderationService;
    }

    public async Task<ContentReport> CreateReportAsync(int reporterAccountId, string targetType, int targetId, string reason, CancellationToken cancellationToken = default)
    {
        string titleSnapshot = "";
        int ownerAccountIdSnapshot = 0;

        if (string.Equals(targetType, ContentTargetTypes.Room, StringComparison.OrdinalIgnoreCase))
        {
            var room = await _context.Rooms
                .AsNoTracking()
                .Include(r => r.Host)
                .FirstOrDefaultAsync(r => r.RoomId == targetId, cancellationToken);

            if (room == null)
                throw new KeyNotFoundException("Không tìm thấy phòng trọ.");

            titleSnapshot = room.Title;
            ownerAccountIdSnapshot = room.Host.AccountId;
        }
        else if (string.Equals(targetType, ContentTargetTypes.Job, StringComparison.OrdinalIgnoreCase))
        {
            var job = await _context.Jobs
                .AsNoTracking()
                .Include(j => j.Employer)
                .FirstOrDefaultAsync(j => j.JobId == targetId, cancellationToken);

            if (job == null)
                throw new KeyNotFoundException("Không tìm thấy việc làm.");

            titleSnapshot = job.JobTitle;
            ownerAccountIdSnapshot = job.Employer.AccountId;
        }
        else if (string.Equals(targetType, ContentTargetTypes.Roommate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetType, "RoommatePost", StringComparison.OrdinalIgnoreCase))
        {
            var post = await _context.RoommatePosts
                .AsNoTracking()
                .Include(p => p.Student)
                .FirstOrDefaultAsync(p => p.Id == targetId, cancellationToken);

            if (post == null)
                throw new KeyNotFoundException("Không tìm thấy bài đăng ở ghép.");

            titleSnapshot = post.Title;
            ownerAccountIdSnapshot = post.Student.AccountId;
        }
        else
        {
            throw new ArgumentException("Loại nội dung báo cáo không hợp lệ.");
        }

        if (ownerAccountIdSnapshot == reporterAccountId)
            throw new InvalidOperationException("Bạn không thể báo cáo bài đăng của chính mình.");

        // Chống duplicate report đang chờ xử lý
        var existingReport = await _context.ContentReports
            .AnyAsync(r => r.ReporterAccountId == reporterAccountId 
                && r.TargetType == targetType 
                && r.TargetId == targetId 
                && (r.Status == ContentReportStatuses.Pending || r.Status == ContentReportStatuses.Reviewing), 
                cancellationToken);

        if (existingReport)
            throw new InvalidOperationException("Bạn đã gửi báo cáo cho bài viết này và đang chờ quản trị viên xem xét.");

        var report = new ContentReport
        {
            TargetType = targetType,
            TargetId = targetId,
            ReporterAccountId = reporterAccountId,
            Reason = reason,
            Status = ContentReportStatuses.Pending,
            CreatedAt = DateTime.UtcNow,
            TargetTitleSnapshot = titleSnapshot,
            OwnerAccountIdSnapshot = ownerAccountIdSnapshot
        };

        _context.ContentReports.Add(report);
        await _context.SaveChangesAsync(cancellationToken);

        // Tạo Notification cho Admin
        var adminIds = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.UserRole == "Admin")
            .Select(a => a.AccountId)
            .ToListAsync(cancellationToken);

        if (adminIds.Count > 0)
        {
            var notifTitle = targetType == ContentTargetTypes.Room
                ? $"Báo cáo phòng trọ #{targetId}"
                : (targetType == ContentTargetTypes.Roommate || targetType == "RoommatePost")
                    ? $"Báo cáo tìm bạn ở ghép #{targetId}"
                    : $"Báo cáo việc làm #{targetId}";

            var notifications = adminIds.Select(adminId => new Notification
            {
                RecipientAccountId = adminId,
                Type = "report",
                Title = notifTitle,
                Message = $"Lý do: {reason}",
                TargetType = targetType,
                TargetId = targetId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return report;
    }

    public async Task<List<ContentReport>> GetReportsAsync(string? targetType, string? status, CancellationToken cancellationToken = default)
    {
        var q = _context.ContentReports
            .Include(r => r.ReporterAccount)
            .Include(r => r.ReviewedByAdminAccount)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(targetType))
            q = q.Where(r => r.TargetType == targetType.Trim());

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.Status == status.Trim());

        return await q
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ContentReport?> GetReportDetailAsync(int reportId, CancellationToken cancellationToken = default)
    {
        return await _context.ContentReports
            .Include(r => r.ReporterAccount)
            .Include(r => r.ReviewedByAdminAccount)
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);
    }

    public async Task<bool> MarkReviewingAsync(int adminAccountId, int reportId, CancellationToken cancellationToken = default)
    {
        var report = await _context.ContentReports.FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);
        if (report == null) return false;

        if (report.Status == ContentReportStatuses.Pending)
        {
            report.Status = ContentReportStatuses.Reviewing;
            report.ReviewedByAdminAccountId = adminAccountId;
            report.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> ResolveAsync(int adminAccountId, int reportId, string action, string? reason, bool applyToAllPendingReportsOfSameTarget, Microsoft.AspNetCore.Http.HttpContext? httpContext, CancellationToken cancellationToken = default)
    {
        var report = await _context.ContentReports.FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);
        if (report == null) return false;

        // Gọi Moderation Service để thực thi thay đổi đối với bài viết
        bool modSuccess = true;
        if (string.Equals(action, ContentReportResolutionActions.Hide, StringComparison.OrdinalIgnoreCase))
        {
            modSuccess = await _moderationService.HideAsync(adminAccountId, report.TargetType, report.TargetId, reason, httpContext, cancellationToken);
        }
        else if (string.Equals(action, ContentReportResolutionActions.Restore, StringComparison.OrdinalIgnoreCase))
        {
            modSuccess = await _moderationService.RestoreAsync(adminAccountId, report.TargetType, report.TargetId, reason, httpContext, cancellationToken);
        }
        else if (string.Equals(action, ContentReportResolutionActions.Reject, StringComparison.OrdinalIgnoreCase))
        {
            modSuccess = await _moderationService.RejectAsync(adminAccountId, report.TargetType, report.TargetId, reason, httpContext, cancellationToken);
        }
        else if (string.Equals(action, ContentReportResolutionActions.Delete, StringComparison.OrdinalIgnoreCase))
        {
            modSuccess = await _moderationService.DeleteAsync(adminAccountId, report.TargetType, report.TargetId, reason, httpContext, cancellationToken);
        }
        else if (string.Equals(action, ContentReportResolutionActions.NoAction, StringComparison.OrdinalIgnoreCase))
        {
            modSuccess = true; // Không tác động gì đến bài viết gốc
        }

        if (!modSuccess) return false;

        // Cập nhật trạng thái report hiện tại
        report.Status = ContentReportStatuses.Resolved;
        report.ResolutionAction = action;
        report.ResolutionNote = reason;
        report.ReviewedByAdminAccountId = adminAccountId;
        report.ReviewedAt = DateTime.UtcNow;

        if (applyToAllPendingReportsOfSameTarget)
        {
            var otherPendingReports = await _context.ContentReports
                .Where(r => r.TargetType == report.TargetType 
                    && r.TargetId == report.TargetId 
                    && r.ReportId != reportId 
                    && (r.Status == ContentReportStatuses.Pending || r.Status == ContentReportStatuses.Reviewing))
                .ToListAsync(cancellationToken);

            foreach (var r in otherPendingReports)
            {
                r.Status = ContentReportStatuses.Resolved;
                r.ResolutionAction = action;
                r.ResolutionNote = reason;
                r.ReviewedByAdminAccountId = adminAccountId;
                r.ReviewedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DismissAsync(int adminAccountId, int reportId, string? reason, CancellationToken cancellationToken = default)
    {
        var report = await _context.ContentReports.FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);
        if (report == null) return false;

        report.Status = ContentReportStatuses.Dismissed;
        report.ResolutionAction = ContentReportResolutionActions.NoAction;
        report.ResolutionNote = reason;
        report.ReviewedByAdminAccountId = adminAccountId;
        report.ReviewedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
