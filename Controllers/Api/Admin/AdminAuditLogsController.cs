using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;

namespace UniMap360.Controllers.Api.Admin;

[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = "Admin")]
public sealed class AdminAuditLogsController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public AdminAuditLogsController(UniMap360ProContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? action,
        [FromQuery] string? targetType,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = limit <= 0 ? 100 : Math.Min(limit, 1000);

        var q = _context.AdminAuditLogs
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(x => x.Action == action.Trim());

        if (!string.IsNullOrWhiteSpace(targetType))
            q = q.Where(x => x.TargetType == targetType.Trim());

        var items = await q
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new
            {
                x.AuditId,
                x.AdminAccountId,
                x.Action,
                x.TargetType,
                x.TargetId,
                x.Reason,
                x.IpAddress,
                x.UserAgent,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { total = items.Count, items });
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string? action,
        [FromQuery] string? targetType,
        [FromQuery] int limit = 2000,
        CancellationToken cancellationToken = default)
    {
        limit = limit <= 0 ? 2000 : Math.Min(limit, 10000);

        var q = _context.AdminAuditLogs
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(x => x.Action == action.Trim());

        if (!string.IsNullOrWhiteSpace(targetType))
            q = q.Where(x => x.TargetType == targetType.Trim());

        var rows = await q
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new
            {
                x.AuditId,
                x.AdminAccountId,
                x.Action,
                x.TargetType,
                x.TargetId,
                x.Reason,
                x.IpAddress,
                x.UserAgent,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        static string Escape(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var escaped = raw.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        var sb = new StringBuilder();
        sb.AppendLine("AuditId,AdminAccountId,Action,TargetType,TargetId,Reason,IpAddress,UserAgent,CreatedAt");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                row.AuditId.ToString(),
                row.AdminAccountId.ToString(),
                Escape(row.Action),
                Escape(row.TargetType),
                row.TargetId?.ToString() ?? string.Empty,
                Escape(row.Reason),
                Escape(row.IpAddress),
                Escape(row.UserAgent),
                row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            }));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"admin-audit-logs-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}