using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;

namespace UniMap360.Controllers.Api;

[Route("api/reports")]
[ApiController]
public class ReportsController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public ReportsController(UniMap360ProContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ReportListing(
        [FromBody] ReportRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetType) || request.TargetId <= 0)
            return this.ApiBadRequest("Thiếu thông tin bài đăng.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return this.ApiBadRequest("Vui lòng nhập lý do báo cáo.");

        // Tìm tất cả tài khoản Admin
        var adminIds = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.UserRole == "Admin")
            .Select(a => a.AccountId)
            .ToListAsync(cancellationToken);

        if (adminIds.Count == 0)
            return this.ApiOk(new { message = "Đã ghi nhận báo cáo." });

        var title = request.TargetType == "Room"
            ? $"Báo cáo phòng trọ #{request.TargetId}"
            : $"Báo cáo việc làm #{request.TargetId}";

        var notifications = adminIds.Select(adminId => new Notification
        {
            RecipientAccountId = adminId,
            Type = "report",
            Title = title,
            Message = $"Lý do: {request.Reason}",
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync(cancellationToken);

        return this.ApiOk(new { message = "Cảm ơn bạn đã báo cáo. Quản trị viên sẽ xem xét trong thời gian sớm nhất." });
    }
}

public class ReportRequest
{
    public string TargetType { get; set; } = "";
    public int TargetId { get; set; }
    public string Reason { get; set; } = "";
}
