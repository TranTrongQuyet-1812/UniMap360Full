using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Services.Admin;
using UniMap360.Services.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Caching.Memory;
using UniMap360.Services.Business;

namespace UniMap360.Controllers.Api.Admin;

/// <summary>
/// API quản lý người dùng dành cho Quản trị viên (Admin)
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly ISuperAdminGuardService _guardService;
    private readonly IAdminAuditService _auditService;
    private readonly ICloudinaryAssetPurger _cloudinaryAssetPurger;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly IBillingSettingsService _billingSettingsService;
    private readonly IMemoryCache _cache;

    public AdminUsersController(
        UniMap360ProContext context,
        ISuperAdminGuardService guardService,
        IAdminAuditService auditService,
        ICloudinaryAssetPurger cloudinaryAssetPurger,
        IRealtimeNotifier realtimeNotifier,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IBillingSettingsService billingSettingsService,
        IMemoryCache cache)
    {
        _context = context;
        _guardService = guardService;
        _auditService = auditService;
        _cloudinaryAssetPurger = cloudinaryAssetPurger;
        _realtimeNotifier = realtimeNotifier;
        _configuration = configuration;
        _env = env;
        _billingSettingsService = billingSettingsService;
        _cache = cache;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ người dùng có phân trang và lọc theo role
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] UserListQuery query, CancellationToken cancellationToken)
    {
        var q = _context.Accounts
            .AsNoTracking()
            .Include(x => x.HostProfile)
            .Include(x => x.EmployerProfile)
            .Include(x => x.StudentProfile)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Role))
            q = q.Where(x => x.UserRole == query.Role);

        if (query.IsActive.HasValue)
            q = q.Where(x => x.IsActive == query.IsActive.Value);

        if (query.IsLocked.HasValue)
            q = q.Where(x => x.IsLocked == query.IsLocked.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            q = q.Where(x => x.Email.ToLower().Contains(search));
        }

        var items = await q
            .OrderByDescending(x => x.CreatedAt)
            .Take(query.Limit <= 0 ? 200 : Math.Min(query.Limit, 500))
            .Select(x => new
            {
                x.AccountId,
                x.Email,
                role = x.UserRole,
                x.IsActive,
                x.IsLocked,
                x.LockedReason,
                x.CreatedAt,
                x.LastLoginAt,
                isVerified = x.UserRole == "Host"
                    ? (x.HostProfile != null && x.HostProfile.IsVerified == true)
                    : x.UserRole == "Employer"
                        ? (x.EmployerProfile != null && x.EmployerProfile.IsVerified == true)
                        : false,
                isVip = _context.AccountSubscriptions.Any(s => s.AccountId == x.AccountId && s.ExpiresAt > DateTime.UtcNow && s.Status == "Active"),
                profileSummary = x.UserRole == "Host"
                    ? (x.HostProfile != null ? x.HostProfile.FullName : null)
                    : x.UserRole == "Employer"
                        ? (x.EmployerProfile != null ? x.EmployerProfile.CompanyName : null)
                        : (x.StudentProfile != null ? x.StudentProfile.FullName : null)
            })
            .ToListAsync(cancellationToken);

        return Ok(new { total = items.Count, items });
    }

    [HttpGet("{accountId:int}")]
    public async Task<IActionResult> GetUserDetail(int accountId, CancellationToken cancellationToken)
    {
        var account = await _context.Accounts
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .Select(x => new
            {
                x.AccountId,
                x.Email,
                role = x.UserRole,
                x.IsActive,
                x.IsLocked,
                x.LockedReason,
                x.LockedAt,
                x.CreatedAt,
                x.LastLoginAt,
                x.UpdatedAt,
                hostProfile = x.HostProfile == null
                    ? null
                    : new
                    {
                        x.HostProfile.HostId,
                        x.HostProfile.AccountId,
                        x.HostProfile.FullName,
                        x.HostProfile.Phone,
                        x.HostProfile.Idcard,
                        x.HostProfile.IsVerified
                    },
                employerProfile = x.EmployerProfile == null
                    ? null
                    : new
                    {
                        x.EmployerProfile.EmployerId,
                        x.EmployerProfile.AccountId,
                        x.EmployerProfile.CompanyName,
                        x.EmployerProfile.TaxCode,
                        x.EmployerProfile.Website,
                        x.EmployerProfile.IsVerified
                    },
                studentProfile = x.StudentProfile == null
                    ? null
                    : new
                    {
                        x.StudentProfile.StudentId,
                        x.StudentProfile.AccountId,
                        x.StudentProfile.FullName,
                        x.StudentProfile.University,
                        x.StudentProfile.Major,
                        x.StudentProfile.Cvlink
                    }
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
            return NotFound(new { message = "Không tìm thấy tài khoản." });

        var isVip = await _context.AccountSubscriptions
            .AnyAsync(s => s.AccountId == accountId && s.ExpiresAt > DateTime.UtcNow && s.Status == "Active", cancellationToken);

        object? profile = (account.role ?? string.Empty) switch
        {
            "Host" => account.hostProfile,
            "Employer" => account.employerProfile,
            "Student" => account.studentProfile,
            _ => null
        };

        return Ok(new
        {
            account.AccountId,
            account.Email,
            account.role,
            account.IsActive,
            account.IsLocked,
            account.LockedReason,
            account.LockedAt,
            account.CreatedAt,
            account.LastLoginAt,
            account.UpdatedAt,
            isVip,
            profile
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email và mật khẩu là bắt buộc." });

        var role = NormalizeUserRole(request.Role);
        if (role is null)
            return BadRequest(new { message = "Role không hợp lệ." });

        if (role == "Admin")
            return BadRequest(new { message = "Không cho phép tạo role Admin từ UI." });

        var email = request.Email.Trim().ToLowerInvariant();
        var exists = await _context.Accounts.AnyAsync(x => x.Email == email, cancellationToken);
        if (exists)
            return Conflict(new { message = "Email đã tồn tại." });

        var account = new Account
        {
            Email = email,
            PasswordHash = request.Password,
            UserRole = role,
            IsActive = true,
            IsLocked = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);

        await EnsureRoleProfileAsync(account, role, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            "account_create",
            "Account",
            account.AccountId,
            null,
            new { account.AccountId, account.Email, account.UserRole },
            request.Reason,
            HttpContext,
            cancellationToken);

        return Ok(new
        {
            message = "Tạo user thành công.",
            accountId = account.AccountId,
            email = account.Email,
            role = account.UserRole
        });
    }

    [HttpPut("{accountId:int}")]
    public async Task<IActionResult> UpdateUser(int accountId, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var account = await _context.Accounts
            .Include(x => x.HostProfile)
            .Include(x => x.EmployerProfile)
            .FirstOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

        if (account is null)
            return NotFound(new { message = "Không tìm thấy tài khoản." });

        bool? beforeVerified = account.UserRole == "Host"
            ? account.HostProfile?.IsVerified
            : account.UserRole == "Employer"
                ? account.EmployerProfile?.IsVerified
                : null;

        var before = new
        {
            account.Email,
            account.IsActive,
            account.IsLocked,
            account.LockedReason,
            IsVerified = beforeVerified
        };

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var emailExists = await _context.Accounts
                .AnyAsync(x => x.AccountId != accountId && x.Email == email, cancellationToken);
            if (emailExists)
                return Conflict(new { message = "Email đã tồn tại." });

            account.Email = email;
        }

        if (request.IsActive.HasValue)
            account.IsActive = request.IsActive.Value;

        if (request.IsLocked.HasValue)
        {
            account.IsLocked = request.IsLocked.Value;
            account.LockedAt = request.IsLocked.Value ? DateTime.UtcNow : null;
            account.LockedReason = request.IsLocked.Value ? request.LockedReason?.Trim() : null;
        }

        if (request.IsVerified.HasValue)
        {
            if (account.UserRole == "Host" && account.HostProfile != null)
            {
                account.HostProfile.IsVerified = request.IsVerified.Value;
            }
            else if (account.UserRole == "Employer" && account.EmployerProfile != null)
            {
                account.EmployerProfile.IsVerified = request.IsVerified.Value;
            }
        }

        Notification? verificationNotification = null;
        if (request.IsVerified.HasValue && request.IsVerified.Value != beforeVerified)
        {
            var isNowVerified = request.IsVerified.Value;
            var notifType = isNowVerified ? "account_verified" : "account_unverified";
            var notifTitle = isNowVerified ? "Tài khoản đã được xác minh!" : "Đã gỡ xác minh tài khoản";
            var notifMessage = isNowVerified
                ? "Tài khoản của bạn đã được xác minh tích xanh chính thức trên hệ thống."
                : "Huy hiệu xác minh tích xanh trên tài khoản của bạn đã bị gỡ bỏ.";

            verificationNotification = new Notification
            {
                RecipientAccountId = accountId,
                Type = notifType,
                Title = notifTitle,
                Message = notifMessage,
                TargetType = "Account",
                TargetId = accountId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                MetaJson = string.IsNullOrWhiteSpace(request.Reason)
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(new { reason = request.Reason.Trim() })
            };

            _context.Notifications.Add(verificationNotification);
        }

        account.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        if (verificationNotification != null)
        {
            await _realtimeNotifier.NotifyNotificationCreatedAsync(accountId, verificationNotification, cancellationToken);
            await _realtimeNotifier.NotifyNotificationUnreadChangedAsync(accountId, cancellationToken);
        }

        bool? afterVerified = account.UserRole == "Host"
            ? account.HostProfile?.IsVerified
            : account.UserRole == "Employer"
                ? account.EmployerProfile?.IsVerified
                : null;

        await _auditService.WriteAsync(
            adminId.Value,
            "account_update",
            "Account",
            account.AccountId,
            before,
            new
            {
                account.Email,
                account.IsActive,
                account.IsLocked,
                account.LockedReason,
                IsVerified = afterVerified
            },
            request.Reason,
            HttpContext,
            cancellationToken);

        return Ok(new { message = "Cập nhật tài khoản thành công." });
    }

    [HttpPatch("{accountId:int}/role")]
    public async Task<IActionResult> ChangeRole(int accountId, [FromBody] ChangeRoleRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var account = await _context.Accounts
            .Include(x => x.HostProfile)
            .Include(x => x.EmployerProfile)
            .Include(x => x.StudentProfile)
            .FirstOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

        if (account is null)
            return NotFound(new { message = "Không tìm thấy tài khoản." });

        if (await _guardService.IsOwnerAdminAsync(accountId, cancellationToken))
            return BadRequest(new { message = "Không thể đổi role của Owner Admin." });

        var role = NormalizeUserRole(request.NewRole);
        if (role is null || role == "Admin")
            return BadRequest(new { message = "Role chuyển đổi không hợp lệ." });

        if (account.UserRole == role)
            return Ok(new { message = "Role không thay đổi." });

        var beforeRole = account.UserRole;
        account.UserRole = role;
        account.UpdatedAt = DateTime.UtcNow;

        await EnsureRoleProfileAsync(account, role, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            "account_role_change",
            "Account",
            account.AccountId,
            new { role = beforeRole },
            new { role },
            request.Reason,
            HttpContext,
            cancellationToken);

        return Ok(new { message = "Đổi role thành công.", role });
    }

    [HttpPost("{accountId:int}/lock")]
    public async Task<IActionResult> LockUser(int accountId, [FromBody] ActionReasonRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Bắt buộc nhập lý do khóa." });

        return await SetLockStateAsync(accountId, true, request.Reason, cancellationToken);
    }

    [HttpPost("{accountId:int}/unlock")]
    public Task<IActionResult> UnlockUser(int accountId, [FromBody] ActionReasonRequest request, CancellationToken cancellationToken)
    {
        return SetLockStateAsync(accountId, false, request.Reason, cancellationToken);
    }

    [HttpPost("{accountId:int}/deactivate")]
    public Task<IActionResult> DeactivateUser(int accountId, [FromBody] ActionReasonRequest request, CancellationToken cancellationToken)
    {
        return SetActiveStateAsync(accountId, false, request.Reason, cancellationToken);
    }

    [HttpPost("{accountId:int}/reactivate")]
    public Task<IActionResult> ReactivateUser(int accountId, [FromBody] ActionReasonRequest request, CancellationToken cancellationToken)
    {
        return SetActiveStateAsync(accountId, true, request.Reason, cancellationToken);
    }

    [HttpDelete("{accountId:int}")]
    public async Task<IActionResult> DeleteUser(int accountId, [FromBody] ActionReasonRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Bắt buộc nhập lý do xóa tài khoản." });

        if (adminId.Value == accountId)
            return BadRequest(new { message = "Không thể tự xóa chính tài khoản đang đăng nhập." });

        if (await _guardService.IsOwnerAdminAsync(accountId, cancellationToken))
            return BadRequest(new { message = "Không thể xóa Owner Admin." });

        var account = await _context.Accounts
            .Include(x => x.HostProfile)
            .Include(x => x.EmployerProfile)
            .Include(x => x.StudentProfile)
            .FirstOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

        if (account is null)
            return NotFound(new { message = "Không tìm thấy tài khoản." });

        var before = new
        {
            account.AccountId,
            account.Email,
            account.UserRole,
            account.IsActive,
            account.IsLocked
        };

        // 1. Xác định ID các thực thể liên quan
        var roomIds = Array.Empty<int>();
        if (account.HostProfile is not null)
        {
            roomIds = await _context.Rooms
                .Where(r => r.HostId == account.HostProfile.HostId)
                .Select(r => r.RoomId)
                .ToArrayAsync(cancellationToken);
        }

        var jobIds = Array.Empty<int>();
        if (account.EmployerProfile is not null)
        {
            jobIds = await _context.Jobs
                .Where(j => j.EmployerId == account.EmployerProfile.EmployerId)
                .Select(j => j.JobId)
                .ToArrayAsync(cancellationToken);
        }

        // 2. Dọn dẹp Tin nhắn, Hội thoại, Thông báo và Lịch hẹn (XỬ LÝ TRƯỚC ĐỂ TRÁNH RESTRICT FK)
        var messages = await _context.Messages
            .Where(m => m.SenderAccountId == accountId)
            .ToListAsync(cancellationToken);
        _context.Messages.RemoveRange(messages);

        var participants = await _context.ConversationParticipants
            .Where(p => p.AccountId == accountId)
            .ToListAsync(cancellationToken);
        _context.ConversationParticipants.RemoveRange(participants);

        var notifications = await _context.Notifications
            .Where(n => n.RecipientAccountId == accountId)
            .ToListAsync(cancellationToken);
        _context.Notifications.RemoveRange(notifications);

        if (account.StudentProfile != null)
        {
            var studentAppointments = await _context.RoomViewingAppointments
                .Where(a => a.StudentId == account.StudentProfile.StudentId)
                .ToListAsync(cancellationToken);
            _context.RoomViewingAppointments.RemoveRange(studentAppointments);
        }
        if (account.HostProfile != null)
        {
            var hostAppointments = await _context.RoomViewingAppointments
                .Where(a => a.HostId == account.HostProfile.HostId)
                .ToListAsync(cancellationToken);
            _context.RoomViewingAppointments.RemoveRange(hostAppointments);
        }

        // 3. Dọn dẹp Media (Cloudinary & Database)
        var mediaRows = await _context.Media
            .Where(m =>
                (m.TargetType == "Account" && m.TargetId == accountId)
                || (m.TargetType == "Room" && roomIds.Contains(m.TargetId))
                || (m.TargetType == "Job" && jobIds.Contains(m.TargetId))
                || (m.TargetType == "Roommate" && _context.RoommatePosts.Where(r => r.Student.AccountId == accountId).Select(r => r.Id).Contains(m.TargetId)))
            .ToListAsync(cancellationToken);

        var purgeTasks = new List<Task>();
        foreach (var media in mediaRows)
        {
            var publicId = _cloudinaryAssetPurger.TryExtractPublicIdFromUrl(media.MediaUrl);
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                purgeTasks.Add(_cloudinaryAssetPurger.TryPurgeByPublicIdAsync(publicId, cancellationToken));
            }
        }
        await Task.WhenAll(purgeTasks);
        
        _context.Media.RemoveRange(mediaRows);

        // 4. Dọn dẹp dữ liệu Host (Rooms)
        if (roomIds.Length > 0)
        {
            var rooms = await _context.Rooms.Where(r => roomIds.Contains(r.RoomId)).ToListAsync(cancellationToken);
            _context.Rooms.RemoveRange(rooms);
        }

        // 5. Dọn dẹp dữ liệu Employer (Jobs & Applications)
        if (jobIds.Length > 0)
        {
            var applications = await _context.JobApplications.Where(a => jobIds.Contains(a.JobId)).ToListAsync(cancellationToken);
            _context.JobApplications.RemoveRange(applications);

            var jobs = await _context.Jobs.Where(j => jobIds.Contains(j.JobId)).ToListAsync(cancellationToken);
            _context.Jobs.RemoveRange(jobs);
        }

        // 6. Dọn dẹp dữ liệu Student (Favorites, Reviews, v.v.)
        if (account.StudentProfile is not null)
        {
            var studentId = account.StudentProfile.StudentId;
            var favorites = await _context.Favorites.Where(f => f.StudentId == studentId).ToListAsync(cancellationToken);
            _context.Favorites.RemoveRange(favorites);

            var reviews = await _context.Reviews.Where(r => r.StudentId == studentId).ToListAsync(cancellationToken);
            _context.Reviews.RemoveRange(reviews);

            var studentApps = await _context.JobApplications.Where(a => a.StudentId == studentId).ToListAsync(cancellationToken);
            _context.JobApplications.RemoveRange(studentApps);

            var roommates = await _context.RoommatePosts.Where(r => r.StudentId == studentId).ToListAsync(cancellationToken);
            _context.RoommatePosts.RemoveRange(roommates);

            _context.StudentProfiles.Remove(account.StudentProfile);
        }

        // 7. Xóa Profile còn lại
        if (account.HostProfile is not null)
            _context.HostProfiles.Remove(account.HostProfile);

        if (account.EmployerProfile is not null)
            _context.EmployerProfiles.Remove(account.EmployerProfile);

        // 8. Xóa Log và Tài khoản chính
        var logs = await _context.SystemLogs.Where(l => l.AccountId == accountId).ToListAsync(cancellationToken);
        _context.SystemLogs.RemoveRange(logs);

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync(cancellationToken);

        // 9. Ghi log kiểm toán
        await _auditService.WriteAsync(
            adminId.Value,
            "account_hard_delete",
            "Account",
            accountId,
            before,
            null,
            request.Reason,
            HttpContext,
            cancellationToken);

        return Ok(new
        {
            message = "Đã xóa vĩnh viễn tài khoản.",
            accountId
        });
    }

    private async Task<IActionResult> SetLockStateAsync(int accountId, bool isLocked, string? reason, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        if (await _guardService.IsOwnerAdminAsync(accountId, cancellationToken))
            return BadRequest(new { message = "Không thể thao tác với Owner Admin." });

        var account = await _context.Accounts.FirstOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (account is null)
            return NotFound(new { message = "Không tìm thấy tài khoản." });

        var before = new { account.IsLocked, account.LockedReason, account.LockedAt };

        account.IsLocked = isLocked;
        account.LockedReason = isLocked ? reason?.Trim() : null;
        account.LockedAt = isLocked ? DateTime.UtcNow : null;
        account.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            isLocked ? "account_lock" : "account_unlock",
            "Account",
            accountId,
            before,
            new { account.IsLocked, account.LockedReason, account.LockedAt },
            reason,
            HttpContext,
            cancellationToken);

        return Ok(new { message = isLocked ? "Đã khóa tài khoản." : "Đã mở khóa tài khoản." });
    }

    private async Task<IActionResult> SetActiveStateAsync(int accountId, bool isActive, string? reason, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        if (await _guardService.IsOwnerAdminAsync(accountId, cancellationToken))
            return BadRequest(new { message = "Không thể thao tác với Owner Admin." });

        var account = await _context.Accounts.FirstOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (account is null)
            return NotFound(new { message = "Không tìm thấy tài khoản." });

        var before = new { account.IsActive, account.IsLocked };

        account.IsActive = isActive;
        if (!isActive)
        {
            account.IsLocked = true;
            account.LockedAt = DateTime.UtcNow;
            account.LockedReason = string.IsNullOrWhiteSpace(reason) ? "Deactivated by admin." : reason.Trim();
        }

        account.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            isActive ? "account_reactivate" : "account_deactivate",
            "Account",
            accountId,
            before,
            new { account.IsActive, account.IsLocked, account.LockedReason },
            reason,
            HttpContext,
            cancellationToken);

        return Ok(new { message = isActive ? "Đã kích hoạt lại tài khoản." : "Đã vô hiệu hóa tài khoản." });
    }

    private async Task EnsureRoleProfileAsync(Account account, string normalizedRole, CancellationToken cancellationToken)
    {
        if (normalizedRole == "Host")
        {
            var host = await _context.HostProfiles.FirstOrDefaultAsync(x => x.AccountId == account.AccountId, cancellationToken);
            if (host is null)
            {
                _context.HostProfiles.Add(new HostProfile
                {
                    AccountId = account.AccountId,
                    FullName = BuildAliasFromEmail(account.Email, account.AccountId, "Host"),
                    Idcard = $"HOST{account.AccountId:D10}",
                    IsVerified = false
                });
            }
        }
        else if (normalizedRole == "Employer")
        {
            var emp = await _context.EmployerProfiles.FirstOrDefaultAsync(x => x.AccountId == account.AccountId, cancellationToken);
            if (emp is null)
            {
                _context.EmployerProfiles.Add(new EmployerProfile
                {
                    AccountId = account.AccountId,
                    CompanyName = $"Doanh nghiệp {BuildAliasFromEmail(account.Email, account.AccountId, "Employer")}",
                    TaxCode = $"EMP{account.AccountId:D10}"
                });
            }
        }
        else if (normalizedRole == "Student")
        {
            var student = await _context.StudentProfiles.FirstOrDefaultAsync(x => x.AccountId == account.AccountId, cancellationToken);
            if (student is null)
            {
                _context.StudentProfiles.Add(new StudentProfile
                {
                    AccountId = account.AccountId,
                    FullName = BuildAliasFromEmail(account.Email, account.AccountId, "Student")
                });
            }
        }
    }

    private int? GetCurrentAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }

    private static string BuildAliasFromEmail(string email, int accountId, string fallbackPrefix)
    {
        var alias = email.Split('@', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (string.IsNullOrWhiteSpace(alias)) alias = $"{fallbackPrefix} {accountId}";
        if (alias.Length > 100) alias = alias[..100];
        return alias;
    }

    private static string? NormalizeUserRole(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "admin" => "Admin",
            "host" => "Host",
            "employer" => "Employer",
            "student" => "Student",
            _ => null
        };
    }

    public sealed class UserListQuery
    {
        public string? Search { get; set; }
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsLocked { get; set; }
        public int Limit { get; set; } = 100;
    }

    public sealed class CreateUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Student";
        public string? Reason { get; set; }
    }

    public sealed class UpdateUserRequest
    {
        public string? Email { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsLocked { get; set; }
        public string? LockedReason { get; set; }
        public string? Reason { get; set; }
        public bool? IsVerified { get; set; }
    }

    public sealed class ChangeRoleRequest
    {
        public string NewRole { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public sealed class ActionReasonRequest
    {
        public string? Reason { get; set; }
    }

    [HttpGet("billing-status")]
    public async Task<IActionResult> GetBillingStatus()
    {
        var isEnabled = await _billingSettingsService.IsBillingEnforcedAsync();
        return Ok(new { enabled = isEnabled });
    }

    [HttpPost("billing-toggle")]
    public async Task<IActionResult> ToggleBilling([FromBody] ToggleBillingRequest request, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var beforeEnabled = await _billingSettingsService.IsBillingEnforcedAsync(cancellationToken);
        var targetEnabled = request.Enabled;

        if (beforeEnabled == targetEnabled)
        {
            return Ok(new { message = "Trạng thái không đổi.", enabled = beforeEnabled });
        }

        try
        {
            await _billingSettingsService.SetBillingEnforcedAsync(targetEnabled, cancellationToken);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Không thể cập nhật cấu hình: {ex.Message}" });
        }

        await _auditService.WriteAsync(
            adminId.Value,
            targetEnabled ? "billing_enforce_enable" : "billing_enforce_disable",
            "SystemConfig",
            0,
            new { enabled = beforeEnabled },
            new { enabled = targetEnabled },
            "Toggle billing enforcement state",
            HttpContext,
            cancellationToken);

        return Ok(new { message = "Cập nhật thành công.", enabled = targetEnabled });
    }

    [HttpPost("{accountId:int}/toggle-vip")]
    public async Task<IActionResult> ToggleVip(int accountId, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAccountId();
        if (!adminId.HasValue)
            return Unauthorized(new { message = "Token không hợp lệ." });

        var account = await _context.Accounts
            .Include(x => x.HostProfile)
            .Include(x => x.EmployerProfile)
            .FirstOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

        if (account is null)
            return NotFound(new { message = "Không tìm thấy tài khoản." });

        if (account.UserRole != "Host" && account.UserRole != "Employer")
            return BadRequest(new { message = "Chỉ có thể cấp VIP cho Chủ trọ hoặc Nhà tuyển dụng." });

        var activeSub = await _context.AccountSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.AccountId == accountId && s.ExpiresAt > DateTime.UtcNow && s.Status == "Active", cancellationToken);

        bool isGranting = activeSub is null;

        if (isGranting)
        {
            string planCode = account.UserRole == "Host" ? "HostVIP" : "EmployerVIP";
            string planName = account.UserRole == "Host" ? "Gói VIP Chủ Trọ" : "Gói VIP Nhà Tuyển Dụng";
            string roleScope = account.UserRole == "Host" ? "Host" : "Employer";

            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);
            if (plan is null)
            {
                plan = new SubscriptionPlan
                {
                    Code = planCode,
                    Name = planName,
                    RoleScope = roleScope,
                    PriceVnd = 36000,
                    BillingCycle = "Monthly",
                    IsActive = true
                };
                _context.SubscriptionPlans.Add(plan);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var oldSubscriptions = await _context.AccountSubscriptions
                .Where(s => s.AccountId == accountId && s.Status == "Active")
                .ToListAsync(cancellationToken);

            foreach (var oldSub in oldSubscriptions)
            {
                oldSub.Status = "Cancelled";
                oldSub.UpdatedAt = DateTime.UtcNow;
            }

            var subscription = new AccountSubscription
            {
                AccountId = accountId,
                PlanId = plan.PlanId,
                Status = "Active",
                StartedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddYears(1)
            };

            _context.AccountSubscriptions.Add(subscription);
        }
        else
        {
            activeSub!.Status = "Cancelled";
            activeSub.ExpiresAt = DateTime.UtcNow;
            activeSub.UpdatedAt = DateTime.UtcNow;

            var activeFeaturedListings = await _context.FeaturedListings
                .Where(f => f.OwnerAccountId == accountId && f.Status == "Active")
                .ToListAsync(cancellationToken);

            foreach (var featured in activeFeaturedListings)
            {
                featured.Status = "Expired";
                featured.EndsAt = DateTime.UtcNow;
                featured.CancelledAt = DateTime.UtcNow;
                featured.UpdatedAt = DateTime.UtcNow;
            }

            // Invalidate Map Feed Cache
            _cache.Remove("GlobalMapFeed");
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.WriteAsync(
            adminId.Value,
            isGranting ? "account_vip_grant" : "account_vip_revoke",
            "Account",
            accountId,
            new { isVip = !isGranting },
            new { isVip = isGranting },
            isGranting ? "Admin granted VIP status" : "Admin revoked VIP status",
            HttpContext,
            cancellationToken);

        return Ok(new { message = isGranting ? "Đã cấp VIP thành công." : "Đã hủy VIP thành công.", isVip = isGranting });
    }

    public sealed class ToggleBillingRequest
    {
        public bool Enabled { get; set; }
    }
}
