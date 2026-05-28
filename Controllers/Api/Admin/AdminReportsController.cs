using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Models.Api;
using UniMap360.Models.Requests;
using UniMap360.Services.Reports;

namespace UniMap360.Controllers.Api.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
public class AdminReportsController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IContentReportService _reportService;

    public AdminReportsController(
        UniMap360ProContext context,
        IContentReportService reportService)
    {
        _context = context;
        _reportService = reportService;
    }

    [HttpGet("api/admin/reports")]
    public async Task<IActionResult> GetReports(
        [FromQuery] string? targetType,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        try
        {
            var reports = await _reportService.GetReportsAsync(targetType, status, cancellationToken);
            
            var items = new List<object>();
            foreach (var r in reports)
            {
                items.Add(new
                {
                    r.ReportId,
                    r.TargetType,
                    r.TargetId,
                    r.Reason,
                    r.Status,
                    r.ResolutionAction,
                    r.ResolutionNote,
                    r.CreatedAt,
                    r.ReviewedAt,
                    r.TargetTitleSnapshot,
                    r.OwnerAccountIdSnapshot,
                    reporterName = r.ReporterAccount?.Email ?? "N/A",
                    adminName = r.ReviewedByAdminAccount?.Email ?? "N/A"
                });
            }

            return this.ApiOk(new { total = items.Count, items });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hệ thống khi tải danh sách tố cáo.", detail = ex.Message });
        }
    }

    [HttpGet("api/admin/reports/{id:int}")]
    public async Task<IActionResult> GetReportDetail(int id, CancellationToken cancellationToken)
    {
        try
        {
            var report = await _reportService.GetReportDetailAsync(id, cancellationToken);
            if (report == null)
                return this.ApiNotFound("Không tìm thấy báo cáo.");

            object? targetDetail = null;

            if (string.Equals(report.TargetType, ContentTargetTypes.Room, StringComparison.OrdinalIgnoreCase))
            {
                var room = await _context.Rooms
                    .AsNoTracking()
                    .Include(r => r.Host)
                    .ThenInclude(h => h.Account)
                    .Include(r => r.Location)
                    .FirstOrDefaultAsync(r => r.RoomId == report.TargetId, cancellationToken);

                if (room != null)
                {
                    var media = await _context.Media
                        .AsNoTracking()
                        .Where(m => m.TargetType == ContentTargetTypes.Room && m.TargetId == room.RoomId)
                        .Select(m => new { m.MediaId, m.MediaUrl, m.IsThumbnail })
                        .ToListAsync(cancellationToken);

                    targetDetail = new
                    {
                        room.RoomId,
                        room.Title,
                        status = room.RoomStatus,
                        room.Price,
                        room.Area,
                        room.Description,
                        room.ContactPhone,
                        room.IsExternal,
                        room.SourceUrl,
                        room.CreatedAt,
                        ownerName = room.Host.FullName,
                        ownerEmail = room.Host.Account.Email,
                        location = room.Location.AddressText,
                        media
                    };
                }
            }
            else if (string.Equals(report.TargetType, ContentTargetTypes.Job, StringComparison.OrdinalIgnoreCase))
            {
                var job = await _context.Jobs
                    .AsNoTracking()
                    .Include(j => j.Employer)
                    .ThenInclude(e => e.Account)
                    .Include(j => j.Location)
                    .FirstOrDefaultAsync(j => j.JobId == report.TargetId, cancellationToken);

                if (job != null)
                {
                    var media = await _context.Media
                        .AsNoTracking()
                        .Where(m => m.TargetType == ContentTargetTypes.Job && m.TargetId == job.JobId)
                        .Select(m => new { m.MediaId, m.MediaUrl, m.IsThumbnail })
                        .ToListAsync(cancellationToken);

                    targetDetail = new
                    {
                        job.JobId,
                        job.JobTitle,
                        status = job.JobStatus,
                        job.SalaryRange,
                        job.JobType,
                        job.Description,
                        job.ContactPhone,
                        job.IsExternal,
                        job.SourceUrl,
                        job.CreatedAt,
                        ownerName = job.Employer.CompanyName,
                        ownerEmail = job.Employer.Account.Email,
                        location = job.Location.AddressText,
                        media
                    };
                }
            }
            else if (string.Equals(report.TargetType, ContentTargetTypes.Roommate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(report.TargetType, "RoommatePost", StringComparison.OrdinalIgnoreCase))
            {
                var post = await _context.RoommatePosts
                    .AsNoTracking()
                    .Include(p => p.Student)
                    .ThenInclude(s => s.Account)
                    .FirstOrDefaultAsync(p => p.Id == report.TargetId, cancellationToken);

                if (post != null)
                {
                    var media = await _context.Media
                        .AsNoTracking()
                        .Where(m =>
                            (m.TargetId == post.Id && (m.TargetType == "Roommate" || m.TargetType == "RMP" || m.TargetType == "RoommatePost"))
                            || (m.TargetType == "Room" && m.TargetId == -post.Id))
                        .Select(m => new { m.MediaId, m.MediaUrl, m.IsThumbnail })
                        .ToListAsync(cancellationToken);

                    targetDetail = new
                    {
                        post.Id,
                        post.Title,
                        status = post.Status,
                        post.BudgetPerMonth,
                        post.TargetGender,
                        post.AreaPreference,
                        post.Habits,
                        post.Description,
                        post.CreatedAt,
                        ownerName = post.Student.FullName,
                        ownerEmail = post.Student.Account.Email,
                        media
                    };
                }
            }

            var totalReportsForThisTarget = await _context.ContentReports
                .CountAsync(r => r.TargetType == report.TargetType && r.TargetId == report.TargetId, cancellationToken);

            var result = new
            {
                report.ReportId,
                report.TargetType,
                report.TargetId,
                report.Reason,
                report.Status,
                report.ResolutionAction,
                report.ResolutionNote,
                report.CreatedAt,
                report.ReviewedAt,
                report.TargetTitleSnapshot,
                report.OwnerAccountIdSnapshot,
                reporterEmail = report.ReporterAccount?.Email ?? "N/A",
                adminEmail = report.ReviewedByAdminAccount?.Email ?? "N/A",
                totalReportsForThisTarget,
                targetDetail
            };

            return this.ApiOk(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hệ thống khi tải chi tiết tố cáo.", detail = ex.Message });
        }
    }

    [HttpPost("api/admin/reports/{id:int}/review")]
    public async Task<IActionResult> MarkReviewing(int id, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return this.ApiUnauthorized("Token không hợp lệ.");

        try
        {
            var success = await _reportService.MarkReviewingAsync(adminId.Value, id, cancellationToken);
            if (!success)
                return this.ApiBadRequest("Không thể cập nhật trạng thái xem xét cho tố cáo này.");

            return this.ApiOk(new { message = "Đã đánh dấu đang xem xét tố cáo." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hệ thống.", detail = ex.Message });
        }
    }

    [HttpPost("api/admin/reports/{id:int}/resolve")]
    public async Task<IActionResult> ResolveReport(int id, [FromBody] ResolveReportRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return this.ApiUnauthorized("Token không hợp lệ.");

        try
        {
            var success = await _reportService.ResolveAsync(
                adminId.Value,
                id,
                request.Action,
                request.Reason,
                request.ApplyToAllPendingReportsOfSameTarget,
                HttpContext,
                cancellationToken);

            if (!success)
                return this.ApiBadRequest("Giải quyết tố cáo thất bại. Vui lòng kiểm tra lại trạng thái bài viết hoặc hành động.");

            return this.ApiOk(new { message = "Đã giải quyết tố cáo thành công." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hệ thống khi giải quyết tố cáo.", detail = ex.Message });
        }
    }

    [HttpPost("api/admin/reports/{id:int}/dismiss")]
    public async Task<IActionResult> DismissReport(int id, [FromBody] DismissReportRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return this.ApiUnauthorized("Token không hợp lệ.");

        try
        {
            var success = await _reportService.DismissAsync(adminId.Value, id, request.Reason, cancellationToken);
            if (!success)
                return this.ApiBadRequest("Không thể bỏ qua tố cáo này.");

            return this.ApiOk(new { message = "Đã bỏ qua tố cáo." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hệ thống.", detail = ex.Message });
        }
    }

    private int? GetCurrentAdminAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }
}
