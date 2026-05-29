using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Services.Admin;

namespace UniMap360.Services.Moderation;

public class ContentModerationService : IContentModerationService
{
    private readonly UniMap360ProContext _context;
    private readonly IAdminAuditService _auditService;
    private readonly ICloudinaryAssetPurger _cloudinaryAssetPurger;
    private readonly UniMap360.Services.Realtime.IRealtimeNotifier _realtimeNotifier;

    public ContentModerationService(
        UniMap360ProContext context,
        IAdminAuditService auditService,
        ICloudinaryAssetPurger cloudinaryAssetPurger,
        UniMap360.Services.Realtime.IRealtimeNotifier realtimeNotifier)
    {
        _context = context;
        _auditService = auditService;
        _cloudinaryAssetPurger = cloudinaryAssetPurger;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<bool> ApproveAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default)
    {
        if (string.Equals(targetType, ContentTargetTypes.Room, StringComparison.OrdinalIgnoreCase))
        {
            return await SetRoomStatusAsync(adminAccountId, targetId, "Available", "room_approve", reason, httpContext, cancellationToken);
        }
        if (string.Equals(targetType, ContentTargetTypes.Job, StringComparison.OrdinalIgnoreCase))
        {
            return await SetJobStatusAsync(adminAccountId, targetId, "Open", "job_approve", reason, httpContext, cancellationToken);
        }
        if (string.Equals(targetType, ContentTargetTypes.Roommate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetType, "RoommatePost", StringComparison.OrdinalIgnoreCase))
        {
            return await SetRoommateStatusAsync(adminAccountId, targetId, RoommateStatuses.Active, "roommate_restore", reason, httpContext, cancellationToken);
        }
        return false;
    }

    public async Task<bool> HideAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default)
    {
        if (string.Equals(targetType, ContentTargetTypes.Room, StringComparison.OrdinalIgnoreCase))
        {
            return await SetRoomStatusAsync(adminAccountId, targetId, "Hidden", "room_hide", reason, httpContext, cancellationToken);
        }
        if (string.Equals(targetType, ContentTargetTypes.Job, StringComparison.OrdinalIgnoreCase))
        {
            return await SetJobStatusAsync(adminAccountId, targetId, "Hidden", "job_hide", reason, httpContext, cancellationToken);
        }
        if (string.Equals(targetType, ContentTargetTypes.Roommate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetType, "RoommatePost", StringComparison.OrdinalIgnoreCase))
        {
            return await SetRoommateStatusAsync(adminAccountId, targetId, RoommateStatuses.Hidden, "roommate_hide", reason, httpContext, cancellationToken);
        }
        return false;
    }

    public async Task<bool> RestoreAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default)
    {
        return await ApproveAsync(adminAccountId, targetType, targetId, reason, httpContext, cancellationToken);
    }

    public async Task<bool> RejectAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default)
    {
        if (string.Equals(targetType, ContentTargetTypes.Room, StringComparison.OrdinalIgnoreCase))
        {
            return await SetRoomStatusAsync(adminAccountId, targetId, "Rejected", "room_reject", reason, httpContext, cancellationToken);
        }
        if (string.Equals(targetType, ContentTargetTypes.Job, StringComparison.OrdinalIgnoreCase))
        {
            return await SetJobStatusAsync(adminAccountId, targetId, "Rejected", "job_reject", reason, httpContext, cancellationToken);
        }
        if (string.Equals(targetType, ContentTargetTypes.Roommate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetType, "RoommatePost", StringComparison.OrdinalIgnoreCase))
        {
            return await SetRoommateStatusAsync(adminAccountId, targetId, RoommateStatuses.Rejected, "roommate_reject", reason, httpContext, cancellationToken);
        }
        return false;
    }

    public async Task<bool> DeleteAsync(int adminAccountId, string targetType, int targetId, string? reason, HttpContext? httpContext, CancellationToken cancellationToken = default)
    {
        if (string.Equals(targetType, ContentTargetTypes.Room, StringComparison.OrdinalIgnoreCase))
        {
            var room = await _context.Rooms.FirstOrDefaultAsync(r => r.RoomId == targetId, cancellationToken);
            if (room == null) return false;

            var recipientAccountId = await _context.HostProfiles
                .Where(h => h.HostId == room.HostId)
                .Select(h => (int?)h.AccountId)
                .FirstOrDefaultAsync(cancellationToken);

            var mediaRows = await _context.Media
                .Where(m => m.TargetType == ContentTargetTypes.Room && m.TargetId == targetId)
                .ToListAsync(cancellationToken);

            await DeleteCloudinaryAssetsAsync(mediaRows.Select(m => m.MediaUrl), cancellationToken);

            _context.Media.RemoveRange(mediaRows);
            _context.Rooms.Remove(room);
            await _context.SaveChangesAsync(cancellationToken);
            await ResolvePendingReportsOfTargetAsync(adminAccountId, ContentTargetTypes.Room, targetId, ContentReportResolutionActions.Delete, reason, cancellationToken);

            await _auditService.WriteAsync(
                adminAccountId,
                "room_delete",
                ContentTargetTypes.Room,
                targetId,
                new { room.RoomId, room.Title, room.RoomStatus },
                null,
                reason,
                httpContext,
                cancellationToken);

            if (recipientAccountId.HasValue)
            {
                await CreateModerationNotificationAsync(
                    recipientAccountId.Value,
                    "room_delete",
                    ContentTargetTypes.Room,
                    targetId,
                    room.Title,
                    reason,
                    cancellationToken);
            }
            return true;
        }

        if (string.Equals(targetType, ContentTargetTypes.Job, StringComparison.OrdinalIgnoreCase))
        {
            var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == targetId, cancellationToken);
            if (job == null) return false;

            var recipientAccountId = await _context.EmployerProfiles
                .Where(e => e.EmployerId == job.EmployerId)
                .Select(e => (int?)e.AccountId)
                .FirstOrDefaultAsync(cancellationToken);

            var mediaRows = await _context.Media
                .Where(m => m.TargetType == ContentTargetTypes.Job && m.TargetId == targetId)
                .ToListAsync(cancellationToken);

            await DeleteCloudinaryAssetsAsync(mediaRows.Select(m => m.MediaUrl), cancellationToken);

            _context.Media.RemoveRange(mediaRows);

            var applications = await _context.JobApplications
                .Where(a => a.JobId == targetId)
                .ToListAsync(cancellationToken);
            _context.JobApplications.RemoveRange(applications);

            _context.Jobs.Remove(job);
            await _context.SaveChangesAsync(cancellationToken);
            await ResolvePendingReportsOfTargetAsync(adminAccountId, ContentTargetTypes.Job, targetId, ContentReportResolutionActions.Delete, reason, cancellationToken);

            await _auditService.WriteAsync(
                adminAccountId,
                "job_delete",
                ContentTargetTypes.Job,
                targetId,
                new { job.JobId, job.JobTitle, job.JobStatus },
                null,
                reason,
                httpContext,
                cancellationToken);

            if (recipientAccountId.HasValue)
            {
                await CreateModerationNotificationAsync(
                    recipientAccountId.Value,
                    "job_delete",
                    ContentTargetTypes.Job,
                    targetId,
                    job.JobTitle,
                    reason,
                    cancellationToken);
            }
            return true;
        }

        if (string.Equals(targetType, ContentTargetTypes.Roommate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetType, "RoommatePost", StringComparison.OrdinalIgnoreCase))
        {
            var post = await _context.RoommatePosts.FirstOrDefaultAsync(r => r.Id == targetId, cancellationToken);
            if (post == null) return false;

            var recipientAccountId = await _context.StudentProfiles
                .Where(s => s.StudentId == post.StudentId)
                .Select(s => (int?)s.AccountId)
                .FirstOrDefaultAsync(cancellationToken);

            var mediaRows = await _context.Media
                .Where(m =>
                    (m.TargetId == targetId && (m.TargetType == "Roommate" || m.TargetType == "RMP" || m.TargetType == "RoommatePost"))
                    || (m.TargetType == "Room" && m.TargetId == -targetId))
                .ToListAsync(cancellationToken);

            await DeleteCloudinaryAssetsAsync(mediaRows.Select(m => m.MediaUrl), cancellationToken);

            _context.Media.RemoveRange(mediaRows);
            _context.RoommatePosts.Remove(post);
            await _context.SaveChangesAsync(cancellationToken);
            await ResolvePendingReportsOfTargetAsync(adminAccountId, "Roommate", targetId, ContentReportResolutionActions.Delete, reason, cancellationToken);

            await _auditService.WriteAsync(
                adminAccountId,
                "roommate_delete",
                "Roommate",
                targetId,
                new { post.Id, post.Title, post.Status, post.IsActive },
                null,
                reason,
                httpContext,
                cancellationToken);

            if (recipientAccountId.HasValue)
            {
                await CreateModerationNotificationAsync(
                    recipientAccountId.Value,
                    "roommate_delete",
                    "Roommate",
                    targetId,
                    post.Title,
                    reason,
                    cancellationToken);
            }
            return true;
        }

        return false;
    }

    private async Task<bool> SetRoomStatusAsync(int adminAccountId, int roomId, string newStatus, string action, string? reason, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId, cancellationToken);
        if (room == null) return false;

        var recipientAccountId = await _context.HostProfiles
            .Where(h => h.HostId == room.HostId)
            .Select(h => (int?)h.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var before = new { room.RoomStatus };
        room.RoomStatus = newStatus;
        await _context.SaveChangesAsync(cancellationToken);

        var reportAction = newStatus switch
        {
            "Hidden" => ContentReportResolutionActions.Hide,
            "Rejected" => ContentReportResolutionActions.Reject,
            "Available" => ContentReportResolutionActions.Restore,
            _ => ContentReportResolutionActions.NoAction
        };
        await ResolvePendingReportsOfTargetAsync(adminAccountId, ContentTargetTypes.Room, roomId, reportAction, reason, cancellationToken);

        await _auditService.WriteAsync(
            adminAccountId,
            action,
            ContentTargetTypes.Room,
            roomId,
            before,
            new { room.RoomStatus },
            reason,
            httpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                action,
                ContentTargetTypes.Room,
                roomId,
                room.Title,
                reason,
                cancellationToken);
        }
        return true;
    }

    private async Task<bool> SetJobStatusAsync(int adminAccountId, int jobId, string newStatus, string action, string? reason, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
        if (job == null) return false;

        var recipientAccountId = await _context.EmployerProfiles
            .Where(e => e.EmployerId == job.EmployerId)
            .Select(e => (int?)e.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var before = new { job.JobStatus };
        job.JobStatus = newStatus;
        await _context.SaveChangesAsync(cancellationToken);

        var reportAction = newStatus switch
        {
            "Hidden" => ContentReportResolutionActions.Hide,
            "Rejected" => ContentReportResolutionActions.Reject,
            "Open" => ContentReportResolutionActions.Restore,
            _ => ContentReportResolutionActions.NoAction
        };
        await ResolvePendingReportsOfTargetAsync(adminAccountId, ContentTargetTypes.Job, jobId, reportAction, reason, cancellationToken);

        await _auditService.WriteAsync(
            adminAccountId,
            action,
            ContentTargetTypes.Job,
            jobId,
            before,
            new { job.JobStatus },
            reason,
            httpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                action,
                ContentTargetTypes.Job,
                jobId,
                job.JobTitle,
                reason,
                cancellationToken);
        }
        return true;
    }

    private async Task<bool> SetRoommateStatusAsync(int adminAccountId, int id, string newStatus, string action, string? reason, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        var post = await _context.RoommatePosts.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (post == null) return false;

        var recipientAccountId = await _context.StudentProfiles
            .Where(s => s.StudentId == post.StudentId)
            .Select(s => (int?)s.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var before = new { post.Status, post.IsActive };
        post.Status = newStatus;
        post.IsActive = string.Equals(newStatus, RoommateStatuses.Active, StringComparison.OrdinalIgnoreCase);
        await _context.SaveChangesAsync(cancellationToken);

        var reportAction = newStatus switch
        {
            RoommateStatuses.Hidden => ContentReportResolutionActions.Hide,
            RoommateStatuses.Rejected => ContentReportResolutionActions.Reject,
            RoommateStatuses.Active => ContentReportResolutionActions.Restore,
            _ => ContentReportResolutionActions.NoAction
        };
        await ResolvePendingReportsOfTargetAsync(adminAccountId, "Roommate", id, reportAction, reason, cancellationToken);

        await _auditService.WriteAsync(
            adminAccountId,
            action,
            "Roommate",
            id,
            before,
            new { post.Status, post.IsActive },
            reason,
            httpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                action,
                "Roommate",
                id,
                post.Title,
                reason,
                cancellationToken);
        }
        return true;
    }

    private async Task DeleteCloudinaryAssetsAsync(IEnumerable<string?> mediaUrls, CancellationToken cancellationToken)
    {
        var publicIds = mediaUrls
            .Select(_cloudinaryAssetPurger.TryExtractPublicIdFromUrl)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var publicId in publicIds)
        {
            try
            {
                await _cloudinaryAssetPurger.TryPurgeByPublicIdAsync(publicId, cancellationToken);
            }
            catch
            {
                // Best effort
            }
        }
    }

    private async Task CreateModerationNotificationAsync(
        int recipientAccountId,
        string action,
        string targetType,
        int targetId,
        string? targetTitle,
        string? reason,
        CancellationToken cancellationToken)
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        var (title, message) = normalizedAction switch
        {
            "room_hide" => (
                "Bài phòng trọ đã bị ẩn",
                $"Bài phòng trọ \"{targetTitle ?? "N/A"}\" của bạn đã bị quản trị viên ẩn."
            ),
            "room_restore" => (
                "Bài phòng trọ đã được hiển thị lại",
                $"Bài phòng trọ \"{targetTitle ?? "N/A"}\" của bạn đã được hiển thị lại."
            ),
            "room_delete" => (
                "Bài phòng trọ đã bị xóa",
                $"Bài phòng trọ \"{targetTitle ?? "N/A"}\" của bạn đã bị quản trị viên xóa."
            ),
            "job_hide" => (
                "Bài tuyển dụng đã bị ẩn",
                $"Bài tuyển dụng \"{targetTitle ?? "N/A"}\" của bạn đã bị quản trị viên ẩn."
            ),
            "job_restore" => (
                "Bài tuyển dụng đã được hiển thị lại",
                $"Bài tuyển dụng \"{targetTitle ?? "N/A"}\" của bạn đã được hiển thị lại."
            ),
            "job_delete" => (
                "Bài tuyển dụng đã bị xóa",
                $"Bài tuyển dụng \"{targetTitle ?? "N/A"}\" của bạn đã bị quản trị viên xóa."
            ),
            "roommate_hide" => (
                "Bài tìm ở ghép đã bị ẩn",
                $"Bài tìm ở ghép \"{targetTitle ?? "N/A"}\" của bạn đã bị quản trị viên ẩn."
            ),
            "roommate_restore" => (
                "Bài tìm ở ghép đã được hiển thị lại",
                $"Bài tìm ở ghép \"{targetTitle ?? "N/A"}\" của bạn đã được hiển thị lại."
            ),
            "roommate_reject" => (
                "Bài tìm ở ghép đã bị từ chối",
                $"Bài tìm ở ghép \"{targetTitle ?? "N/A"}\" của bạn đã bị quản trị viên từ chối."
            ),
            "roommate_delete" => (
                "Bài tìm ở ghép đã bị xóa",
                $"Bài tìm ở ghép \"{targetTitle ?? "N/A"}\" của bạn đã bị quản trị viên xóa."
            ),
            _ => (
                "Bài đăng có thay đổi từ quản trị viên",
                $"Bài đăng \"{targetTitle ?? "N/A"}\" của bạn đã được quản trị viên cập nhật trạng thái."
            )
        };

        var metaJson = string.IsNullOrWhiteSpace(reason)
            ? null
            : System.Text.Json.JsonSerializer.Serialize(new { reason = reason.Trim() });

        var notification = new Notification
        {
            RecipientAccountId = recipientAccountId,
            Type = normalizedAction,
            Title = title,
            Message = message,
            TargetType = targetType,
            TargetId = targetId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            MetaJson = metaJson
        };
        _context.Notifications.Add(notification);

        await _context.SaveChangesAsync(cancellationToken);

        await _realtimeNotifier.NotifyNotificationCreatedAsync(recipientAccountId, notification, cancellationToken);
        await _realtimeNotifier.NotifyNotificationUnreadChangedAsync(recipientAccountId, cancellationToken);
    }

    private async Task ResolvePendingReportsOfTargetAsync(
        int adminAccountId, 
        string targetType, 
        int targetId, 
        string action, 
        string? reason, 
        CancellationToken cancellationToken)
    {
        var pendingReports = await _context.ContentReports
            .Where(r => r.TargetType == targetType 
                && r.TargetId == targetId 
                && (r.Status == ContentReportStatuses.Pending || r.Status == ContentReportStatuses.Reviewing))
            .ToListAsync(cancellationToken);

        if (pendingReports.Count > 0)
        {
            var note = string.IsNullOrWhiteSpace(reason) 
                ? "Được xử lý thông qua chức năng Kiểm duyệt nội dung." 
                : reason;

            foreach (var report in pendingReports)
            {
                report.Status = ContentReportStatuses.Resolved;
                report.ResolutionAction = action;
                report.ResolutionNote = note;
                report.ReviewedByAdminAccountId = adminAccountId;
                report.ReviewedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
