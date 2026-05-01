using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;

namespace UniMap360.Controllers.Api.Admin;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = "Admin")]
public sealed class AdminDashboardController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public AdminDashboardController(UniMap360ProContext context)
    {
        _context = context;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var totalAccounts = await _context.Accounts.AsNoTracking().CountAsync(cancellationToken);
        var activeAccounts = await _context.Accounts.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken);
        var lockedAccounts = await _context.Accounts.AsNoTracking().CountAsync(x => x.IsLocked, cancellationToken);
        
        var studentCount = await _context.Accounts.AsNoTracking().CountAsync(x => x.UserRole == "Student", cancellationToken);
        var hostCount = await _context.Accounts.AsNoTracking().CountAsync(x => x.UserRole == "Host", cancellationToken);
        var employerCount = await _context.Accounts.AsNoTracking().CountAsync(x => x.UserRole == "Employer", cancellationToken);

        var totalRooms = await _context.Rooms.AsNoTracking().CountAsync(cancellationToken);
        var hiddenRooms = await _context.Rooms.AsNoTracking().CountAsync(x => x.RoomStatus == "Hidden", cancellationToken);

        var totalJobs = await _context.Jobs.AsNoTracking().CountAsync(cancellationToken);
        var hiddenJobs = await _context.Jobs.AsNoTracking().CountAsync(x => x.JobStatus == "Hidden", cancellationToken);

        var totalRoommates = await _context.RoommatePosts.AsNoTracking().CountAsync(cancellationToken);
        var hiddenRoommates = await _context.RoommatePosts.AsNoTracking().CountAsync(x => !x.IsActive, cancellationToken);

        var recentAudits = await _context.AdminAuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new
            {
                x.AuditId,
                x.Action,
                x.TargetType,
                x.TargetId,
                x.Reason,
                x.CreatedAt,
                x.AdminAccountId
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            totalAccounts,
            activeAccounts,
            lockedAccounts,
            studentCount,
            hostCount,
            employerCount,
            totalRooms,
            hiddenRooms,
            totalJobs,
            hiddenJobs,
            totalRoommates,
            hiddenRoommates,
            recentAudits
        });
    }

    [HttpGet("timeseries")]
    public async Task<IActionResult> Timeseries([FromQuery] int days = 7, CancellationToken cancellationToken = default)
    {
        days = days is < 1 or > 90 ? 7 : days;
        var from = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var accountRows = await _context.Accounts
            .AsNoTracking()
            .Where(x => x.CreatedAt.HasValue && x.CreatedAt.Value >= from)
            .Select(x => x.CreatedAt!.Value.Date)
            .ToListAsync(cancellationToken);

        var roomRows = await _context.Rooms
            .AsNoTracking()
            .Where(x => x.CreatedAt.HasValue && x.CreatedAt.Value >= from)
            .Select(x => x.CreatedAt!.Value.Date)
            .ToListAsync(cancellationToken);

        var jobRows = await _context.Jobs
            .AsNoTracking()
            .Where(x => x.CreatedAt.HasValue && x.CreatedAt.Value >= from)
            .Select(x => x.CreatedAt!.Value.Date)
            .ToListAsync(cancellationToken);

        var roommateRows = await _context.RoommatePosts
            .AsNoTracking()
            .Where(x => x.CreatedAt >= from)
            .Select(x => x.CreatedAt.Date)
            .ToListAsync(cancellationToken);

        var accountMap = accountRows.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var roomMap = roomRows.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var jobMap = jobRows.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var roommateMap = roommateRows.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var items = new List<object>(days);
        for (var i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            items.Add(new
            {
                date = day.ToString("yyyy-MM-dd"),
                accounts = accountMap.GetValueOrDefault(day, 0),
                rooms = roomMap.GetValueOrDefault(day, 0),
                jobs = jobMap.GetValueOrDefault(day, 0),
                roommates = roommateMap.GetValueOrDefault(day, 0)
            });
        }

        return Ok(new { from, days, items });
    }
}