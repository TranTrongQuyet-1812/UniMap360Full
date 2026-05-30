using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Services.Admin;
using UniMap360.Services.Moderation;

namespace UniMap360.Controllers.Api.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
public sealed class AdminModerationController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IContentModerationService _moderationService;

    public AdminModerationController(
        UniMap360ProContext context,
        IContentModerationService moderationService)
    {
        _context = context;
        _moderationService = moderationService;
    }

    [HttpGet("api/admin/rooms")]
    public async Task<IActionResult> GetRooms(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] bool? onlySuspicious,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        limit = limit <= 0 ? 200 : Math.Min(limit, 1000);

        var q = _context.Rooms
            .AsNoTracking()
            .Include(r => r.Host)
            .ThenInclude(h => h.Account)
            .Include(r => r.Location)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.RoomStatus == status.Trim());

        if (onlySuspicious == true)
            q = q.Where(r => r.Location.LocationSuspicious == true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escapedKeyword = EscapeLikePattern(search.Trim());
            q = q.Where(r => EF.Functions.Like(r.Title, $"%{escapedKeyword}%", @"\"));
        }

        var items = await q
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new
            {
                r.RoomId,
                r.Title,
                status = r.RoomStatus,
                r.Price,
                r.Area,
                r.Description,
                r.ContactPhone,
                r.IsExternal,
                r.SourceUrl,
                r.CreatedAt,
                hostAccountId = r.Host.AccountId,
                hostName = r.Host.FullName,
                hostEmail = r.Host.Account.Email,
                location = r.Location.AddressText,
                locationDistanceMeters = r.Location.LocationDistanceMeters,
                locationConfidence = r.Location.LocationConfidence,
                locationSuspicious = r.Location.LocationSuspicious,
                geocodeSource = r.Location.GeocodeSource
            })
            .ToListAsync(cancellationToken);

        return Ok(new { total = items.Count, items });
    }

    [HttpGet("api/admin/rooms/{roomId:int}")]
    public async Task<IActionResult> GetRoomDetail(int roomId, CancellationToken cancellationToken = default)
    {
        var room = await _context.Rooms
            .AsNoTracking()
            .Include(r => r.Host)
            .ThenInclude(h => h.Account)
            .Include(r => r.Location)
            .FirstOrDefaultAsync(r => r.RoomId == roomId, cancellationToken);

        if (room is null)
            return NotFound(new { message = "Không tìm thấy phòng." });

        var media = await _context.Media
            .AsNoTracking()
            .Where(m => m.TargetType == ContentTargetTypes.Room && m.TargetId == roomId)
            .OrderByDescending(m => m.IsThumbnail)
            .ThenBy(m => m.MediaId)
            .Select(m => new { m.MediaId, m.MediaUrl, m.IsThumbnail })
            .ToListAsync(cancellationToken);

        return Ok(new
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
            host = new
            {
                room.Host.HostId,
                room.Host.AccountId,
                room.Host.FullName,
                room.Host.Account.Email,
                room.Host.Phone
            },
            location = new
            {
                room.Location.LocationId,
                room.Location.AddressText,
                room.Location.ProvinceName,
                room.Location.DistrictName,
                room.Location.WardName,
                latitude = room.Location.Coordinates != null ? room.Location.Coordinates.Coordinate.Y : (double?)null,
                longitude = room.Location.Coordinates != null ? room.Location.Coordinates.Coordinate.X : (double?)null,
                geocodedLatitude = room.Location.GeocodedLatitude,
                geocodedLongitude = room.Location.GeocodedLongitude,
                locationDistanceMeters = room.Location.LocationDistanceMeters,
                locationConfidence = room.Location.LocationConfidence,
                locationSuspicious = room.Location.LocationSuspicious,
                geocodeSource = room.Location.GeocodeSource
            },
            media
        });
    }

    [HttpGet("api/admin/jobs")]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] bool? onlySuspicious,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        limit = limit <= 0 ? 200 : Math.Min(limit, 1000);

        var q = _context.Jobs
            .AsNoTracking()
            .Include(j => j.Employer)
            .ThenInclude(e => e.Account)
            .Include(j => j.Location)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(j => j.JobStatus == status.Trim());

        if (onlySuspicious == true)
            q = q.Where(j => j.Location.LocationSuspicious == true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escapedKeyword = EscapeLikePattern(search.Trim());
            q = q.Where(j => EF.Functions.Like(j.JobTitle, $"%{escapedKeyword}%", @"\"));
        }

        var items = await q
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .Select(j => new
            {
                j.JobId,
                title = j.JobTitle,
                status = j.JobStatus,
                j.SalaryRange,
                j.JobType,
                j.Description,
                j.ContactPhone,
                j.IsExternal,
                j.SourceUrl,
                j.CreatedAt,
                employerAccountId = j.Employer.AccountId,
                employerName = j.Employer.CompanyName,
                employerEmail = j.Employer.Account.Email,
                location = j.Location.AddressText,
                locationDistanceMeters = j.Location.LocationDistanceMeters,
                locationConfidence = j.Location.LocationConfidence,
                locationSuspicious = j.Location.LocationSuspicious,
                geocodeSource = j.Location.GeocodeSource
            })
            .ToListAsync(cancellationToken);

        return Ok(new { total = items.Count, items });
    }

    [HttpGet("api/admin/jobs/{jobId:int}")]
    public async Task<IActionResult> GetJobDetail(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .Include(j => j.Employer)
            .ThenInclude(e => e.Account)
            .Include(j => j.Location)
            .FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);

        if (job is null)
            return NotFound(new { message = "Không tìm thấy việc làm." });

        var media = await _context.Media
            .AsNoTracking()
            .Where(m => m.TargetType == ContentTargetTypes.Job && m.TargetId == jobId)
            .OrderByDescending(m => m.IsThumbnail)
            .ThenBy(m => m.MediaId)
            .Select(m => new { m.MediaId, m.MediaUrl, m.IsThumbnail })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            job.JobId,
            title = job.JobTitle,
            status = job.JobStatus,
            job.SalaryRange,
            job.JobType,
            job.Description,
            job.ContactPhone,
            job.IsExternal,
            job.SourceUrl,
            job.CreatedAt,
            employer = new
            {
                job.Employer.EmployerId,
                job.Employer.AccountId,
                job.Employer.CompanyName,
                job.Employer.Account.Email,
                job.Employer.Website
            },
            location = new
            {
                job.Location.LocationId,
                job.Location.AddressText,
                job.Location.ProvinceName,
                job.Location.DistrictName,
                job.Location.WardName,
                latitude = job.Location.Coordinates != null ? job.Location.Coordinates.Coordinate.Y : (double?)null,
                longitude = job.Location.Coordinates != null ? job.Location.Coordinates.Coordinate.X : (double?)null,
                geocodedLatitude = job.Location.GeocodedLatitude,
                geocodedLongitude = job.Location.GeocodedLongitude,
                locationDistanceMeters = job.Location.LocationDistanceMeters,
                locationConfidence = job.Location.LocationConfidence,
                locationSuspicious = job.Location.LocationSuspicious,
                geocodeSource = job.Location.GeocodeSource
            },
            media
        });
    }

    [HttpPost("api/admin/rooms/{roomId:int}/approve")]
    public async Task<IActionResult> ApproveRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.ApproveAsync(adminId.Value, ContentTargetTypes.Room, roomId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy phòng." });
        return Ok(new { message = "Cập nhật trạng thái phòng thành công.", roomId, status = "Available" });
    }

    [HttpPost("api/admin/rooms/{roomId:int}/reject")]
    public async Task<IActionResult> RejectRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.RejectAsync(adminId.Value, ContentTargetTypes.Room, roomId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy phòng." });
        return Ok(new { message = "Cập nhật trạng thái phòng thành công.", roomId, status = "Rejected" });
    }

    [HttpPost("api/admin/rooms/{roomId:int}/hide")]
    public async Task<IActionResult> HideRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.HideAsync(adminId.Value, ContentTargetTypes.Room, roomId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy phòng." });
        return Ok(new { message = "Cập nhật trạng thái phòng thành công.", roomId, status = "Hidden" });
    }

    [HttpPost("api/admin/rooms/{roomId:int}/restore")]
    public async Task<IActionResult> RestoreRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.RestoreAsync(adminId.Value, ContentTargetTypes.Room, roomId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy phòng." });
        return Ok(new { message = "Cập nhật trạng thái phòng thành công.", roomId, status = "Available" });
    }

    [HttpDelete("api/admin/rooms/{roomId:int}")]
    public async Task<IActionResult> DeleteRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.DeleteAsync(adminId.Value, ContentTargetTypes.Room, roomId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy phòng." });
        return Ok(new { message = "Đã xóa phòng.", roomId });
    }

    [HttpPost("api/admin/jobs/{jobId:int}/approve")]
    public async Task<IActionResult> ApproveJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.ApproveAsync(adminId.Value, ContentTargetTypes.Job, jobId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy việc làm." });
        return Ok(new { message = "Cập nhật trạng thái việc làm thành công.", jobId, status = "Open" });
    }

    [HttpPost("api/admin/jobs/{jobId:int}/reject")]
    public async Task<IActionResult> RejectJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.RejectAsync(adminId.Value, ContentTargetTypes.Job, jobId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy việc làm." });
        return Ok(new { message = "Cập nhật trạng thái việc làm thành công.", jobId, status = "Rejected" });
    }

    [HttpPost("api/admin/jobs/{jobId:int}/hide")]
    public async Task<IActionResult> HideJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.HideAsync(adminId.Value, ContentTargetTypes.Job, jobId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy việc làm." });
        return Ok(new { message = "Cập nhật trạng thái việc làm thành công.", jobId, status = "Hidden" });
    }

    [HttpPost("api/admin/jobs/{jobId:int}/restore")]
    public async Task<IActionResult> RestoreJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.RestoreAsync(adminId.Value, ContentTargetTypes.Job, jobId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy việc làm." });
        return Ok(new { message = "Cập nhật trạng thái việc làm thành công.", jobId, status = "Open" });
    }

    [HttpDelete("api/admin/jobs/{jobId:int}")]
    public async Task<IActionResult> DeleteJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.DeleteAsync(adminId.Value, ContentTargetTypes.Job, jobId, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy việc làm." });
        return Ok(new { message = "Đã xóa việc làm.", jobId });
    }

    [HttpGet("api/admin/roommates")]
    public async Task<IActionResult> GetRoommates(
        [FromQuery] string? status,
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        limit = limit <= 0 ? 200 : Math.Min(limit, 1000);

        var q = _context.RoommatePosts
            .AsNoTracking()
            .Include(r => r.Student)
            .ThenInclude(s => s.Account)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            q = q.Where(r => r.Status == status.Trim());
        }
        else if (isActive.HasValue)
        {
            q = q.Where(r => r.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escapedKeyword = EscapeLikePattern(search.Trim());
            q = q.Where(r => EF.Functions.Like(r.Title, $"%{escapedKeyword}%", @"\"));
        }

        var items = await q
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new
            {
                r.Id,
                r.Title,
                status = r.Status,
                r.BudgetPerMonth,
                r.TargetGender,
                r.AreaPreference,
                r.CreatedAt,
                studentAccountId = r.Student.AccountId,
                studentName = r.Student.FullName,
                studentEmail = r.Student.Account.Email
            })
            .ToListAsync(cancellationToken);

        return Ok(new { total = items.Count, items });
    }

    [HttpGet("api/admin/roommates/{id:int}")]
    public async Task<IActionResult> GetRoommateDetail(int id, CancellationToken cancellationToken = default)
    {
        var post = await _context.RoommatePosts
            .AsNoTracking()
            .Include(r => r.Student)
            .ThenInclude(s => s.Account)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (post is null)
            return NotFound(new { message = "Không tìm thấy bài đăng ở ghép." });

        var media = await _context.Media
            .AsNoTracking()
            .Where(m =>
                (m.TargetId == id && (m.TargetType == "Roommate" || m.TargetType == "RMP" || m.TargetType == "RoommatePost"))
                || (m.TargetType == "Room" && m.TargetId == -id))
            .OrderByDescending(m => m.IsThumbnail)
            .ThenBy(m => m.MediaId)
            .Select(m => new { m.MediaId, m.MediaUrl, m.IsThumbnail })
            .ToListAsync(cancellationToken);

        return Ok(new
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
            student = new
            {
                post.Student.StudentId,
                post.Student.AccountId,
                post.Student.FullName,
                post.Student.Account.Email,
                post.Student.Major,
                post.Student.University
            },
            media
        });
    }

    [HttpPost("api/admin/roommates/{id:int}/hide")]
    public async Task<IActionResult> HideRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.HideAsync(adminId.Value, "Roommate", id, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy bài đăng ở ghép." });
        return Ok(new { message = "Cập nhật trạng thái ở ghép thành công.", id, status = "Hidden" });
    }

    [HttpPost("api/admin/roommates/{id:int}/restore")]
    public async Task<IActionResult> RestoreRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.RestoreAsync(adminId.Value, "Roommate", id, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy bài đăng ở ghép." });
        return Ok(new { message = "Cập nhật trạng thái ở ghép thành công.", id, status = "Active" });
    }

    [HttpPost("api/admin/roommates/{id:int}/reject")]
    public async Task<IActionResult> RejectRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.RejectAsync(adminId.Value, "Roommate", id, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy bài đăng ở ghép." });
        return Ok(new { message = "Cập nhật trạng thái ở ghép thành công.", id, status = "Rejected" });
    }

    [HttpDelete("api/admin/roommates/{id:int}")]
    public async Task<IActionResult> DeleteRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue) return Unauthorized(new { message = "Token không hợp lệ." });
        var success = await _moderationService.DeleteAsync(adminId.Value, "Roommate", id, request.Reason, HttpContext, cancellationToken);
        if (!success) return NotFound(new { message = "Không tìm thấy bài đăng ở ghép." });
        return Ok(new { message = "Đã xóa bài đăng ở ghép.", id });
    }

    private int? GetCurrentAdminAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }

    private static string EscapeLikePattern(string input)
    {
        return input
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");
    }

    public sealed class ModerationActionRequest
    {
        public string? Reason { get; set; }
    }
}
