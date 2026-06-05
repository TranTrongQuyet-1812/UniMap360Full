using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Models.Api;
using UniMap360.Services.Business;

namespace UniMap360.Controllers.Api;

[Route("api/business")]
[ApiController]
[Authorize]
public class BusinessDashboardController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IPostAnalyticsService _analyticsService;
    private readonly IFeaturedListingService _featuredListingService;
    private readonly IEntitlementService _entitlementService;
    private readonly ISubscriptionService _subscriptionService;

    public BusinessDashboardController(
        UniMap360ProContext context,
        IPostAnalyticsService analyticsService,
        IFeaturedListingService featuredListingService,
        IEntitlementService entitlementService,
        ISubscriptionService subscriptionService)
    {
        _context = context;
        _analyticsService = analyticsService;
        _featuredListingService = featuredListingService;
        _entitlementService = entitlementService;
        _subscriptionService = subscriptionService;
    }

    [HttpGet("analytics/overview")]
    public async Task<IActionResult> GetOverview([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var hasEntitlement = await _entitlementService.CanUseFeatureAsync(accountId.Value, "AnalyticsDashboard");
        if (!hasEntitlement)
        {
            return this.ApiForbidden("Gói cước hiện tại của bạn không hỗ trợ xem thống kê. Vui lòng nâng cấp gói cước.");
        }

        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role != AppRoles.Host && role != AppRoles.Employer)
        {
            return this.ApiForbidden("Chỉ dành cho tài khoản Chủ trọ hoặc Nhà tuyển dụng.");
        }

        var start = startDate.HasValue ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc) : DateTime.UtcNow.AddDays(-30);
        var end = endDate.HasValue ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc) : DateTime.UtcNow;

        var overview = await _analyticsService.GetOwnerOverviewAsync(accountId.Value, role, start, end);
        return this.ApiOk(overview);
    }

    [HttpGet("analytics/post/{targetType}/{targetId:int}")]
    public async Task<IActionResult> GetPostAnalytics(string targetType, int targetId, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var hasEntitlement = await _entitlementService.CanUseFeatureAsync(accountId.Value, "AnalyticsDashboard");
        if (!hasEntitlement)
        {
            return this.ApiForbidden("Gói cước hiện tại của bạn không hỗ trợ xem thống kê. Vui lòng nâng cấp gói cước.");
        }

        var type = targetType.Trim();
        if (type != "Room" && type != "Job") return this.ApiBadRequest("Loại đối tượng không hợp lệ.");

        // Kiểm tra quyền sở hữu
        if (type == "Room")
        {
            var room = await _context.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.RoomId == targetId);
            if (room == null) return this.ApiNotFound("Không tìm thấy phòng trọ.");
            
            var host = await _context.HostProfiles.AsNoTracking().FirstOrDefaultAsync(h => h.HostId == room.HostId);
            if (host == null || host.AccountId != accountId.Value) return this.ApiForbidden("Bạn không sở hữu tin đăng này.");
        }
        else
        {
            var job = await _context.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == targetId);
            if (job == null) return this.ApiNotFound("Không tìm thấy việc làm.");

            var employer = await _context.EmployerProfiles.AsNoTracking().FirstOrDefaultAsync(e => e.EmployerId == job.EmployerId);
            if (employer == null || employer.AccountId != accountId.Value) return this.ApiForbidden("Bạn không sở hữu tin đăng này.");
        }

        var start = startDate.HasValue ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc) : DateTime.UtcNow.AddDays(-30);
        var end = endDate.HasValue ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc) : DateTime.UtcNow;

        var summary = await _analyticsService.GetPostAnalyticsSummaryAsync(type, targetId, accountId.Value, start, end);
        return this.ApiOk(summary);
    }

    [HttpPost("pin")]
    public async Task<IActionResult> PinListing([FromBody] PinRequest request)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var type = request.TargetType?.Trim();
        var feature = request.FeatureType?.Trim();
        if (type != "Room" && type != "Job") return this.ApiBadRequest("Loại đối tượng phải là 'Room' hoặc 'Job'.");
        if (feature != "MapPinned" && feature != "ExplorePriority") return this.ApiBadRequest("Loại ghim phải là 'MapPinned' hoặc 'ExplorePriority'.");

        // 1. Kiểm tra Quyền Hạn gói cước (Entitlement check)
        var hasEntitlement = await _entitlementService.CanUseFeatureAsync(accountId.Value, feature);
        if (!hasEntitlement)
        {
            return this.ApiForbidden("Gói cước hiện tại của bạn không hỗ trợ tính năng ghim này. Vui lòng nâng cấp gói cước.");
        }

        // 2. Kiểm tra quyền sở hữu
        if (type == "Room")
        {
            var room = await _context.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.RoomId == request.TargetId);
            if (room == null) return this.ApiNotFound("Không tìm thấy phòng trọ.");
            
            var host = await _context.HostProfiles.AsNoTracking().FirstOrDefaultAsync(h => h.HostId == room.HostId);
            if (host == null || host.AccountId != accountId.Value) return this.ApiForbidden("Bạn không sở hữu tin đăng này.");
        }
        else
        {
            var job = await _context.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == request.TargetId);
            if (job == null) return this.ApiNotFound("Không tìm thấy việc làm.");

            var employer = await _context.EmployerProfiles.AsNoTracking().FirstOrDefaultAsync(e => e.EmployerId == job.EmployerId);
            if (employer == null || employer.AccountId != accountId.Value) return this.ApiForbidden("Bạn không sở hữu tin đăng này.");
        }

        // 3. Thực hiện ghim
        var result = await _featuredListingService.PinListingAsync(
            accountId.Value, 
            type, 
            request.TargetId, 
            feature, 
            7 // Mặc định ghim 7 ngày
        );

        if (!result.Success) return this.ApiBadRequest(result.Message);

        return this.ApiOk(new { message = result.Message });
    }

    [HttpPost("unpin")]
    public async Task<IActionResult> UnpinListing([FromBody] PinRequest request)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var type = request.TargetType?.Trim();
        var feature = request.FeatureType?.Trim();
        if (type != "Room" && type != "Job") return this.ApiBadRequest("Loại đối tượng phải là 'Room' hoặc 'Job'.");
        if (feature != "MapPinned" && feature != "ExplorePriority") return this.ApiBadRequest("Loại ghim phải là 'MapPinned' hoặc 'ExplorePriority'.");

        // Thực hiện huỷ ghim
        var result = await _featuredListingService.UnpinListingAsync(accountId.Value, type, request.TargetId, feature);
        if (!result.Success) return this.ApiBadRequest(result.Message);

        return this.ApiOk(new { message = result.Message });
    }

    [HttpPost("subscription/trial")]
    public async Task<IActionResult> RegisterTrial()
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        try
        {
            var sub = await _subscriptionService.CreateTrialSubscriptionAsync(accountId.Value);
            return this.ApiOk(new
            {
                subscription = new
                {
                    sub.SubscriptionId,
                    sub.Status,
                    sub.StartedAt,
                    sub.ExpiresAt,
                    plan = new
                    {
                        sub.Plan.PlanId,
                        sub.Plan.Code,
                        sub.Plan.Name,
                        sub.Plan.PriceVnd,
                        sub.Plan.RoleScope
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return this.ApiBadRequest(ex.Message);
        }
    }

    [HttpGet("subscription/active")]
    public async Task<IActionResult> GetActiveSubscription()
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var sub = await _subscriptionService.GetActiveSubscriptionAsync(accountId.Value);
        if (sub == null) return this.ApiNotFound("Bạn chưa đăng ký gói cước nào hoặc gói cước đã hết hạn.");

        return this.ApiOk(new
        {
            sub.SubscriptionId,
            sub.Status,
            sub.StartedAt,
            sub.ExpiresAt,
            plan = new
            {
                sub.Plan.PlanId,
                sub.Plan.Code,
                sub.Plan.Name,
                sub.Plan.PriceVnd,
                sub.Plan.RoleScope
            }
        });
    }

    private int? GetCurrentAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }

    public class PinRequest
    {
        public string TargetType { get; set; } = string.Empty;
        public int TargetId { get; set; }
        public string FeatureType { get; set; } = string.Empty;
    }
}
