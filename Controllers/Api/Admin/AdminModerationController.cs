using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Services.Admin;

namespace UniMap360.Controllers.Api.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
public sealed class AdminModerationController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IAdminAuditService _auditService;
    private readonly ICloudinaryAssetPurger _cloudinaryAssetPurger;

    public AdminModerationController(
        UniMap360ProContext context,
        IAdminAuditService auditService,
        ICloudinaryAssetPurger cloudinaryAssetPurger)
    {
        _context = context;
        _auditService = auditService;
        _cloudinaryAssetPurger = cloudinaryAssetPurger;
    }

    [HttpGet("api/admin/rooms")]
    public async Task<IActionResult> GetRooms(
        [FromQuery] string? status,
        [FromQuery] string? search,
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
                location = r.Location.AddressText
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
                room.Location.WardName
            },
            media
        });
    }

    [HttpGet("api/admin/jobs")]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? status,
        [FromQuery] string? search,
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
                location = j.Location.AddressText
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
                job.Location.WardName
            },
            media
        });
    }

    [HttpPost("api/admin/rooms/{roomId:int}/approve")]
    public Task<IActionResult> ApproveRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetRoomStatus(roomId, "Available", "room_approve", request, cancellationToken);

    [HttpPost("api/admin/rooms/{roomId:int}/reject")]
    public Task<IActionResult> RejectRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetRoomStatus(roomId, "Rejected", "room_reject", request, cancellationToken);

    [HttpPost("api/admin/rooms/{roomId:int}/hide")]
    public Task<IActionResult> HideRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetRoomStatus(roomId, "Hidden", "room_hide", request, cancellationToken);

    [HttpPost("api/admin/rooms/{roomId:int}/restore")]
    public Task<IActionResult> RestoreRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetRoomStatus(roomId, "Available", "room_restore", request, cancellationToken);

    [HttpDelete("api/admin/rooms/{roomId:int}")]
    public async Task<IActionResult> DeleteRoom(int roomId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var room = await _context.Rooms
            .FirstOrDefaultAsync(r => r.RoomId == roomId, cancellationToken);
        if (room is null)
            return NotFound(new { message = "Không tìm thấy phòng." });
        var recipientAccountId = await _context.HostProfiles
            .Where(h => h.HostId == room.HostId)
            .Select(h => (int?)h.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var mediaRows = await _context.Media
            .Where(m => m.TargetType == ContentTargetTypes.Room && m.TargetId == roomId)
            .ToListAsync(cancellationToken);

        foreach (var media in mediaRows)
        {
            var publicId = _cloudinaryAssetPurger.TryExtractPublicIdFromUrl(media.MediaUrl);
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                await _cloudinaryAssetPurger.TryPurgeByPublicIdAsync(publicId, cancellationToken);
            }
        }

        _context.Media.RemoveRange(mediaRows);
        _context.Rooms.Remove(room);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            "room_delete",
            ContentTargetTypes.Room,
            roomId,
            new { room.RoomId, room.Title, room.RoomStatus },
            null,
            request.Reason,
            HttpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                "room_delete",
                ContentTargetTypes.Room,
                roomId,
                room.Title,
                request.Reason,
                cancellationToken);
        }

        return Ok(new { message = "Đã xóa phòng.", roomId });
    }

    [HttpPost("api/admin/jobs/{jobId:int}/approve")]
    public Task<IActionResult> ApproveJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetJobStatus(jobId, "Open", "job_approve", request, cancellationToken);

    [HttpPost("api/admin/jobs/{jobId:int}/reject")]
    public Task<IActionResult> RejectJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetJobStatus(jobId, "Rejected", "job_reject", request, cancellationToken);

    [HttpPost("api/admin/jobs/{jobId:int}/hide")]
    public Task<IActionResult> HideJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetJobStatus(jobId, "Hidden", "job_hide", request, cancellationToken);

    [HttpPost("api/admin/jobs/{jobId:int}/restore")]
    public Task<IActionResult> RestoreJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetJobStatus(jobId, "Open", "job_restore", request, cancellationToken);

    [HttpDelete("api/admin/jobs/{jobId:int}")]
    public async Task<IActionResult> DeleteJob(int jobId, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var job = await _context.Jobs
            .FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
        if (job is null)
            return NotFound(new { message = "Không tìm thấy việc làm." });
        var recipientAccountId = await _context.EmployerProfiles
            .Where(e => e.EmployerId == job.EmployerId)
            .Select(e => (int?)e.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var mediaRows = await _context.Media
            .Where(m => m.TargetType == ContentTargetTypes.Job && m.TargetId == jobId)
            .ToListAsync(cancellationToken);

        foreach (var media in mediaRows)
        {
            var publicId = _cloudinaryAssetPurger.TryExtractPublicIdFromUrl(media.MediaUrl);
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                await _cloudinaryAssetPurger.TryPurgeByPublicIdAsync(publicId, cancellationToken);
            }
        }

        _context.Media.RemoveRange(mediaRows);

        var applications = await _context.JobApplications
            .Where(a => a.JobId == jobId)
            .ToListAsync(cancellationToken);
        _context.JobApplications.RemoveRange(applications);

        _context.Jobs.Remove(job);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            "job_delete",
            ContentTargetTypes.Job,
            jobId,
            new { job.JobId, job.JobTitle, job.JobStatus },
            null,
            request.Reason,
            HttpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                "job_delete",
                ContentTargetTypes.Job,
                jobId,
                job.JobTitle,
                request.Reason,
                cancellationToken);
        }

        return Ok(new { message = "Đã xóa việc làm.", jobId });
    }

    [HttpGet("api/admin/roommates")]
    public async Task<IActionResult> GetRoommates(
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

        if (isActive.HasValue)
            q = q.Where(r => r.IsActive == isActive.Value);

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
                status = r.IsActive ? "Active" : "Hidden",
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
            .Where(m => m.TargetType == "Roommate" && m.TargetId == id)
            .OrderByDescending(m => m.IsThumbnail)
            .ThenBy(m => m.MediaId)
            .Select(m => new { m.MediaId, m.MediaUrl, m.IsThumbnail })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            post.Id,
            post.Title,
            status = post.IsActive ? "Active" : "Hidden",
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
    public Task<IActionResult> HideRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetRoommateStatus(id, false, "roommate_hide", request, cancellationToken);

    [HttpPost("api/admin/roommates/{id:int}/restore")]
    public Task<IActionResult> RestoreRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetRoommateStatus(id, true, "roommate_restore", request, cancellationToken);

    [HttpPost("api/admin/roommates/{id:int}/reject")]
    public Task<IActionResult> RejectRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
        => SetRoommateStatus(id, false, "roommate_reject", request, cancellationToken);

    [HttpDelete("api/admin/roommates/{id:int}")]
    public async Task<IActionResult> DeleteRoommate(int id, [FromBody] ModerationActionRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var post = await _context.RoommatePosts
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (post is null)
            return NotFound(new { message = "Không tìm thấy bài đăng ở ghép." });

        var recipientAccountId = await _context.StudentProfiles
            .Where(s => s.StudentId == post.StudentId)
            .Select(s => (int?)s.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var mediaRows = await _context.Media
            .Where(m => m.TargetType == "Roommate" && m.TargetId == id)
            .ToListAsync(cancellationToken);

        foreach (var media in mediaRows)
        {
            var publicId = _cloudinaryAssetPurger.TryExtractPublicIdFromUrl(media.MediaUrl);
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                await _cloudinaryAssetPurger.TryPurgeByPublicIdAsync(publicId, cancellationToken);
            }
        }

        _context.Media.RemoveRange(mediaRows);
        _context.RoommatePosts.Remove(post);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            "roommate_delete",
            "Roommate",
            id,
            new { post.Id, post.Title, post.IsActive },
            null,
            request.Reason,
            HttpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                "roommate_delete",
                "Roommate",
                id,
                post.Title,
                request.Reason,
                cancellationToken);
        }

        return Ok(new { message = "Đã xóa bài đăng ở ghép.", id });
    }

    private async Task<IActionResult> SetRoomStatus(
        int roomId,
        string newStatus,
        string action,
        ModerationActionRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var room = await _context.Rooms
            .FirstOrDefaultAsync(r => r.RoomId == roomId, cancellationToken);
        if (room is null)
            return NotFound(new { message = "Không tìm thấy phòng." });
        var recipientAccountId = await _context.HostProfiles
            .Where(h => h.HostId == room.HostId)
            .Select(h => (int?)h.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var before = new { room.RoomStatus };
        room.RoomStatus = newStatus;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            action,
            ContentTargetTypes.Room,
            roomId,
            before,
            new { room.RoomStatus },
            request.Reason,
            HttpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                action,
                ContentTargetTypes.Room,
                roomId,
                room.Title,
                request.Reason,
                cancellationToken);
        }

        return Ok(new { message = "Cập nhật trạng thái phòng thành công.", roomId, status = room.RoomStatus });
    }

    private async Task<IActionResult> SetJobStatus(
        int jobId,
        string newStatus,
        string action,
        ModerationActionRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var job = await _context.Jobs
            .FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
        if (job is null)
            return NotFound(new { message = "Không tìm thấy việc làm." });
        var recipientAccountId = await _context.EmployerProfiles
            .Where(e => e.EmployerId == job.EmployerId)
            .Select(e => (int?)e.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var before = new { job.JobStatus };
        job.JobStatus = newStatus;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            action,
            ContentTargetTypes.Job,
            jobId,
            before,
            new { job.JobStatus },
            request.Reason,
            HttpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                action,
                ContentTargetTypes.Job,
                jobId,
                job.JobTitle,
                request.Reason,
                cancellationToken);
        }

        return Ok(new { message = "Cập nhật trạng thái việc làm thành công.", jobId, status = job.JobStatus });
    }

    private async Task<IActionResult> SetRoommateStatus(
        int id,
        bool isActive,
        string action,
        ModerationActionRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var post = await _context.RoommatePosts
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (post is null)
            return NotFound(new { message = "Không tìm thấy bài đăng ở ghép." });
        
        var recipientAccountId = await _context.StudentProfiles
            .Where(s => s.StudentId == post.StudentId)
            .Select(s => (int?)s.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        var before = new { post.IsActive };
        post.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            action,
            "Roommate",
            id,
            before,
            new { post.IsActive },
            request.Reason,
            HttpContext,
            cancellationToken);

        if (recipientAccountId.HasValue)
        {
            await CreateModerationNotificationAsync(
                recipientAccountId.Value,
                action,
                "Roommate",
                id,
                post.Title,
                request.Reason,
                cancellationToken);
        }

        var statusString = post.IsActive ? "Active" : "Hidden";
        return Ok(new { message = "Cập nhật trạng thái ở ghép thành công.", id, status = statusString });
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

        _context.Notifications.Add(new Notification
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
        });

        await _context.SaveChangesAsync(cancellationToken);
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
