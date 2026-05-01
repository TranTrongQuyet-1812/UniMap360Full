using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;

namespace UniMap360.Controllers.Api;

[Route("api/notifications")]
[ApiController]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public NotificationsController(UniMap360ProContext context)
    {
        _context = context;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var unreadCount = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientAccountId == accountId.Value && !n.IsRead)
            .CountAsync(cancellationToken);

        return this.ApiOk(new { unreadCount });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 20,
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        limit = limit <= 0 ? 20 : Math.Min(limit, 100);

        var q = _context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientAccountId == accountId.Value);

        if (unreadOnly) q = q.Where(n => !n.IsRead);

        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new
            {
                n.NotificationId,
                n.Type,
                n.Title,
                n.Message,
                n.TargetType,
                n.TargetId,
                n.IsRead,
                n.CreatedAt,
                n.ReadAt,
                n.MetaJson
            })
            .ToListAsync(cancellationToken);

        var unreadCount = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientAccountId == accountId.Value && !n.IsRead)
            .CountAsync(cancellationToken);

        return this.ApiOk(new
        {
            unreadCount,
            total = items.Count,
            items
        });
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == id && n.RecipientAccountId == accountId.Value, cancellationToken);

        if (notification is null) return this.ApiNotFound("Không tìm thấy thông báo.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return this.ApiOk(new { message = "Đã đánh dấu đã đọc.", id = notification.NotificationId });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkReadAll(CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var unreadItems = await _context.Notifications
            .Where(n => n.RecipientAccountId == accountId.Value && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unreadItems.Count == 0) return this.ApiOk(new { message = "Không có thông báo chưa đọc.", updated = 0 });

        var now = DateTime.UtcNow;
        foreach (var item in unreadItems)
        {
            item.IsRead = true;
            item.ReadAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return this.ApiOk(new { message = "Đã đánh dấu tất cả đã đọc.", updated = unreadItems.Count });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteOne(long id, CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == id && n.RecipientAccountId == accountId.Value, cancellationToken);

        if (notification is null) return this.ApiNotFound("Không tìm thấy thông báo.");

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync(cancellationToken);
        return this.ApiOk(new { message = "Đã xóa thông báo.", id });
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var items = await _context.Notifications
            .Where(n => n.RecipientAccountId == accountId.Value)
            .ToListAsync(cancellationToken);

        if (items.Count == 0) return this.ApiOk(new { message = "Không có thông báo để xóa.", deleted = 0 });

        _context.Notifications.RemoveRange(items);
        await _context.SaveChangesAsync(cancellationToken);
        return this.ApiOk(new { message = "Đã xóa toàn bộ thông báo.", deleted = items.Count });
    }

    private int? GetCurrentAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }
}

