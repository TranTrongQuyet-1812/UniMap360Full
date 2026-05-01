using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;

using UniMap360.Services.Admin;

namespace UniMap360.Controllers.Api.Admin;

[ApiController]
[Route("api/admin/cleanup")]
[Authorize(Roles = "Admin")]
public sealed class AdminCleanupController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly ICloudinaryAssetPurger _cloudinaryAssetPurger;

    public AdminCleanupController(UniMap360ProContext context, ICloudinaryAssetPurger cloudinaryAssetPurger)
    {
        _context = context;
        _cloudinaryAssetPurger = cloudinaryAssetPurger;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var adminAuditLogsTotal = await _context.AdminAuditLogs.AsNoTracking().CountAsync(cancellationToken);
        var systemLogsTotal = await _context.SystemLogs.AsNoTracking().CountAsync(cancellationToken);

        var notificationsTotal = await _context.Notifications.AsNoTracking().CountAsync(cancellationToken);
        var notificationsRead = await _context.Notifications.AsNoTracking().CountAsync(x => x.IsRead, cancellationToken);

        var messagesTotal = await _context.Messages.AsNoTracking().CountAsync(cancellationToken);
        var conversationsTotal = await _context.Conversations.AsNoTracking().CountAsync(cancellationToken);

        var jobApplicationsTotal = await _context.JobApplications.AsNoTracking().CountAsync(cancellationToken);
        var jobApplicationsHandled = await _context.JobApplications.AsNoTracking()
            .CountAsync(x => x.Status == "Accepted" || x.Status == "Rejected", cancellationToken);

        var appointmentsTotal = await _context.RoomViewingAppointments.AsNoTracking().CountAsync(cancellationToken);
        var appointmentsHandled = await _context.RoomViewingAppointments.AsNoTracking()
            .CountAsync(x => x.Status == "Confirmed" || x.Status == "Rejected", cancellationToken);

        var mediaTotal = await _context.Media.AsNoTracking().CountAsync(cancellationToken);
        var mediaOrphan = await _context.Media.AsNoTracking()
            .CountAsync(m =>
                (m.TargetType == "Room" && !_context.Rooms.Any(r => r.RoomId == m.TargetId))
                || (m.TargetType == "Job" && !_context.Jobs.Any(j => j.JobId == m.TargetId))
                || (m.TargetType == "Roommate" && !_context.RoommatePosts.Any(r => r.Id == m.TargetId))
                || (m.TargetType != "Room" && m.TargetType != "Job" && m.TargetType != "Roommate" && m.TargetType != "Account"),
                cancellationToken);

        var locationsTotal = await _context.Locations.AsNoTracking().CountAsync(cancellationToken);
        var locationsOrphan = await _context.Locations.AsNoTracking()
            .CountAsync(l =>
                !_context.Rooms.Any(r => r.LocationId == l.LocationId)
                && !_context.Jobs.Any(j => j.LocationId == l.LocationId),
                cancellationToken);

        var oneYearAgo = DateTime.UtcNow.AddYears(-1);
        var ghostAccounts = await _context.Accounts.AsNoTracking()
            .CountAsync(a => !a.IsActive && (a.LastLoginAt < oneYearAgo || (a.LastLoginAt == null && a.CreatedAt < oneYearAgo)), cancellationToken);

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var expiredPosts = await _context.Rooms.AsNoTracking()
            .CountAsync(r => (r.RoomStatus == "Hidden" || r.RoomStatus == "Rejected") && r.CreatedAt < thirtyDaysAgo, cancellationToken);
        expiredPosts += await _context.Jobs.AsNoTracking()
            .CountAsync(j => (j.JobStatus == "Hidden" || j.JobStatus == "Rejected") && j.CreatedAt < thirtyDaysAgo, cancellationToken);
        expiredPosts += await _context.RoommatePosts.AsNoTracking()
            .CountAsync(r => !r.IsActive && r.CreatedAt < thirtyDaysAgo, cancellationToken);

        var notificationsUnread = notificationsTotal - notificationsRead;
        var jobApplicationsPending = jobApplicationsTotal - jobApplicationsHandled;
        var appointmentsPending = appointmentsTotal - appointmentsHandled;

        return Ok(new
        {
            adminAuditLogsTotal,
            systemLogsTotal,
            notificationsTotal,
            notificationsRead,
            notificationsUnread,
            messagesTotal,
            conversationsTotal,
            jobApplicationsTotal,
            jobApplicationsHandled,
            jobApplicationsPending,
            appointmentsTotal,
            appointmentsHandled,
            appointmentsPending,
            mediaTotal,
            mediaOrphan,
            locationsTotal,
            locationsOrphan,
            ghostAccounts,
            expiredPosts
        });
    }

    [HttpPost("purge")]
    public async Task<IActionResult> Purge([FromBody] PurgeCleanupRequest? request, CancellationToken cancellationToken)
    {
        request ??= new PurgeCleanupRequest();

        var deletedReadNotifications = 0;
        var deletedMessages = 0;
        var deletedConversationParticipants = 0;
        var deletedConversations = 0;
        var deletedHandledJobApplications = 0;
        var deletedHandledAppointments = 0;
        var deletedSystemLogs = 0;
        var deletedAdminAuditLogs = 0;
        var deletedOrphanMedia = 0;
        var deletedOrphanLocations = 0;

        if (request.DeleteReadNotifications)
        {
            deletedReadNotifications = await _context.Notifications
                .Where(x => x.IsRead)
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (request.DeleteHandledJobApplications)
        {
            deletedHandledJobApplications = await _context.JobApplications
                .Where(x => x.Status == "Accepted" || x.Status == "Rejected")
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (request.DeleteHandledAppointments)
        {
            deletedHandledAppointments = await _context.RoomViewingAppointments
                .Where(x => x.Status == "Confirmed" || x.Status == "Rejected")
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (request.DeleteSystemLogs)
        {
            deletedSystemLogs = await _context.SystemLogs.ExecuteDeleteAsync(cancellationToken);
        }

        if (request.DeleteAdminAuditLogs)
        {
            deletedAdminAuditLogs = await _context.AdminAuditLogs.ExecuteDeleteAsync(cancellationToken);
        }

        if (request.DeleteOrphanMedia)
        {
            var orphanMediaRows = await _context.Media
                .Where(m =>
                    (m.TargetType == "Room" && !_context.Rooms.Any(r => r.RoomId == m.TargetId))
                    || (m.TargetType == "Job" && !_context.Jobs.Any(j => j.JobId == m.TargetId))
                    || (m.TargetType == "Roommate" && !_context.RoommatePosts.Any(r => r.Id == m.TargetId))
                    || (m.TargetType != "Room" && m.TargetType != "Job" && m.TargetType != "Roommate" && m.TargetType != "Account"))
                .ToListAsync(cancellationToken);

            foreach (var media in orphanMediaRows)
            {
                var publicId = _cloudinaryAssetPurger.TryExtractPublicIdFromUrl(media.MediaUrl);
                if (!string.IsNullOrWhiteSpace(publicId))
                {
                    await _cloudinaryAssetPurger.TryPurgeByPublicIdAsync(publicId, cancellationToken);
                }
            }

            if (orphanMediaRows.Count > 0)
            {
                _context.Media.RemoveRange(orphanMediaRows);
                deletedOrphanMedia = orphanMediaRows.Count;
            }
        }

        if (request.DeleteOrphanLocations)
        {
            deletedOrphanLocations = await _context.Locations
                .Where(l =>
                    !_context.Rooms.Any(r => r.LocationId == l.LocationId)
                    && !_context.Jobs.Any(j => j.LocationId == l.LocationId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (request.DeleteAllChatData)
        {
            deletedMessages = await _context.Messages.ExecuteDeleteAsync(cancellationToken);
            deletedConversationParticipants = await _context.ConversationParticipants.ExecuteDeleteAsync(cancellationToken);
            deletedConversations = await _context.Conversations.ExecuteDeleteAsync(cancellationToken);
        }
        else if (request.DeleteEmptyConversations)
        {
            var emptyConversationIds = await _context.Conversations
                .AsNoTracking()
                .Where(c => !_context.Messages.Any(m => m.ConversationId == c.ConversationId))
                .Select(c => c.ConversationId)
                .ToListAsync(cancellationToken);

            if (emptyConversationIds.Count > 0)
            {
                deletedConversationParticipants = await _context.ConversationParticipants
                    .Where(x => emptyConversationIds.Contains(x.ConversationId))
                    .ExecuteDeleteAsync(cancellationToken);

                deletedConversations = await _context.Conversations
                    .Where(x => emptyConversationIds.Contains(x.ConversationId))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        var stats = await BuildStatsSnapshotAsync(cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Da don dep du lieu thanh cong.",
            deleted = new
            {
                deletedReadNotifications,
                deletedMessages,
                deletedConversationParticipants,
                deletedConversations,
                deletedHandledJobApplications,
                deletedHandledAppointments,
                deletedSystemLogs,
                deletedAdminAuditLogs,
                deletedOrphanMedia,
                deletedOrphanLocations
            },
            stats
        });
    }

    private async Task<object> BuildStatsSnapshotAsync(CancellationToken cancellationToken)
    {
        var adminAuditLogsTotal = await _context.AdminAuditLogs.AsNoTracking().CountAsync(cancellationToken);
        var systemLogsTotal = await _context.SystemLogs.AsNoTracking().CountAsync(cancellationToken);
        var notificationsTotal = await _context.Notifications.AsNoTracking().CountAsync(cancellationToken);
        var notificationsRead = await _context.Notifications.AsNoTracking().CountAsync(x => x.IsRead, cancellationToken);
        var messagesTotal = await _context.Messages.AsNoTracking().CountAsync(cancellationToken);
        var conversationsTotal = await _context.Conversations.AsNoTracking().CountAsync(cancellationToken);
        var jobApplicationsTotal = await _context.JobApplications.AsNoTracking().CountAsync(cancellationToken);
        var jobApplicationsHandled = await _context.JobApplications.AsNoTracking()
            .CountAsync(x => x.Status == "Accepted" || x.Status == "Rejected", cancellationToken);
        var appointmentsTotal = await _context.RoomViewingAppointments.AsNoTracking().CountAsync(cancellationToken);
        var appointmentsHandled = await _context.RoomViewingAppointments.AsNoTracking()
            .CountAsync(x => x.Status == "Confirmed" || x.Status == "Rejected", cancellationToken);
        var mediaTotal = await _context.Media.AsNoTracking().CountAsync(cancellationToken);
        var mediaOrphan = await _context.Media.AsNoTracking()
            .CountAsync(m =>
                (m.TargetType == "Room" && !_context.Rooms.Any(r => r.RoomId == m.TargetId))
                || (m.TargetType == "Job" && !_context.Jobs.Any(j => j.JobId == m.TargetId))
                || (m.TargetType == "Roommate" && !_context.RoommatePosts.Any(r => r.Id == m.TargetId))
                || (m.TargetType != "Room" && m.TargetType != "Job" && m.TargetType != "Roommate" && m.TargetType != "Account"),
                cancellationToken);
        var locationsTotal = await _context.Locations.AsNoTracking().CountAsync(cancellationToken);
        var locationsOrphan = await _context.Locations.AsNoTracking()
            .CountAsync(l =>
                !_context.Rooms.Any(r => r.LocationId == l.LocationId)
                && !_context.Jobs.Any(j => j.LocationId == l.LocationId),
                cancellationToken);

        var oneYearAgo = DateTime.UtcNow.AddYears(-1);
        var ghostAccounts = await _context.Accounts.AsNoTracking()
            .CountAsync(a => !a.IsActive && (a.LastLoginAt < oneYearAgo || (a.LastLoginAt == null && a.CreatedAt < oneYearAgo)), cancellationToken);

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var expiredPosts = await _context.Rooms.AsNoTracking()
            .CountAsync(r => (r.RoomStatus == "Hidden" || r.RoomStatus == "Rejected") && r.CreatedAt < thirtyDaysAgo, cancellationToken);
        expiredPosts += await _context.Jobs.AsNoTracking()
            .CountAsync(j => (j.JobStatus == "Hidden" || j.JobStatus == "Rejected") && j.CreatedAt < thirtyDaysAgo, cancellationToken);
        expiredPosts += await _context.RoommatePosts.AsNoTracking()
            .CountAsync(r => !r.IsActive && r.CreatedAt < thirtyDaysAgo, cancellationToken);

        return new
        {
            adminAuditLogsTotal,
            systemLogsTotal,
            notificationsTotal,
            notificationsRead,
            notificationsUnread = notificationsTotal - notificationsRead,
            messagesTotal,
            conversationsTotal,
            jobApplicationsTotal,
            jobApplicationsHandled,
            jobApplicationsPending = jobApplicationsTotal - jobApplicationsHandled,
            appointmentsTotal,
            appointmentsHandled,
            appointmentsPending = appointmentsTotal - appointmentsHandled,
            mediaTotal,
            mediaOrphan,
            locationsTotal,
            locationsOrphan,
            ghostAccounts,
            expiredPosts
        };
    }
    [HttpPost("audit-integrity")]
    public async Task<IActionResult> AuditIntegrity(CancellationToken cancellationToken)
    {
        var report = new List<string>();
        
        // 1. Kiểm tra tọa độ bài đăng (Dùng NetTopologySuite)
        var invalidLocations = await _context.Locations
            .AsNoTracking()
            .Where(l => l.Coordinates == null || l.Coordinates.IsEmpty)
            .CountAsync(cancellationToken);
        if (invalidLocations > 0) report.Add($"Phát hiện {invalidLocations} vị trí bị thiếu tọa độ hoặc tọa độ rỗng.");

        // 2. Kiểm tra bài đăng phòng trọ thiếu ảnh (Tra cứu bảng Media)
        var roomsNoImage = await _context.Rooms
            .AsNoTracking()
            .Where(r => !_context.Media.Any(m => m.TargetType == "Room" && m.TargetId == r.RoomId))
            .CountAsync(cancellationToken);
        if (roomsNoImage > 0) report.Add($"Có {roomsNoImage} bài đăng phòng trọ không có bất kỳ ảnh nào trong hệ thống.");

        // 3. Kiểm tra bài đăng Ở ghép không hoạt động nhưng chưa bị ẩn
        var invalidRoommates = await _context.RoommatePosts
            .AsNoTracking()
            .Where(r => r.CreatedAt < DateTime.UtcNow.AddMonths(-6) && r.IsActive)
            .CountAsync(cancellationToken);
        if (invalidRoommates > 0) report.Add($"Phát hiện {invalidRoommates} bài đăng Ở ghép đã quá 6 tháng nhưng vẫn đang hiển thị.");

        // 4. Kiểm tra dữ liệu mồ côi (Media)
        var orphanMedia = await _context.Media.AsNoTracking()
            .CountAsync(m =>
                (m.TargetType == "Room" && !_context.Rooms.Any(r => r.RoomId == m.TargetId))
                || (m.TargetType == "Job" && !_context.Jobs.Any(j => j.JobId == m.TargetId))
                || (m.TargetType == "Roommate" && !_context.RoommatePosts.Any(r => r.Id == m.TargetId)),
                cancellationToken);
        if (orphanMedia > 0) report.Add($"Phát hiện {orphanMedia} tệp tin media 'mồ côi' (không thuộc bài đăng nào).");

        // 5. Kiểm tra người dùng chưa xác thực email (nếu có)
        var unverifiedUsers = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.UserRole == "Student" && a.CreatedAt < DateTime.UtcNow.AddDays(-7) && a.IsActive == false)
            .CountAsync(cancellationToken);
        if (unverifiedUsers > 0) report.Add($"Có {unverifiedUsers} tài khoản sinh viên chưa kích hoạt quá 7 ngày.");

        return Ok(new { 
            timestamp = DateTime.UtcNow,
            totalChecks = 5,
            issuesFound = report.Count,
            details = report 
        });
    }
}

public sealed class PurgeCleanupRequest
{
    public bool DeleteReadNotifications { get; set; }
    public bool DeleteHandledJobApplications { get; set; }
    public bool DeleteHandledAppointments { get; set; }
    public bool DeleteAllChatData { get; set; }
    public bool DeleteEmptyConversations { get; set; }
    public bool DeleteSystemLogs { get; set; }
    public bool DeleteAdminAuditLogs { get; set; }
    public bool DeleteOrphanMedia { get; set; }
    public bool DeleteOrphanLocations { get; set; }
}
