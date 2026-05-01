using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;

namespace UniMap360.Controllers.Api;

[Route("api/dashboard")]
[ApiController]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public DashboardController(UniMap360ProContext context)
    {
        _context = context;
    }

    [HttpGet("admin/overview")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminOverview()
    {
        var totalAccounts = await _context.Accounts.CountAsync();
        var totalRooms = await _context.Rooms.CountAsync();
        var totalJobs = await _context.Jobs.CountAsync();
        var totalLocations = await _context.Locations.CountAsync();
        var totalMedia = await _context.Media.CountAsync();

        return this.ApiOk(new
        {
            totalAccounts,
            totalRooms,
            totalJobs,
            totalLocations,
            totalMedia
        });
    }

    [HttpGet("host/overview")]
    [Authorize(Roles = "Host")]
    public async Task<IActionResult> HostOverview()
    {
        var accountId = GetCurrentAccountId();
        if (accountId is null) return this.ApiUnauthorized("Token không hợp lệ.");

        var host = await _context.HostProfiles.FirstOrDefaultAsync(h => h.AccountId == accountId.Value);
        if (host is null) return this.ApiNotFound("Không tìm thấy hồ sơ chủ trọ.");

        var rooms = await _context.Rooms
            .Where(r => r.HostId == host.HostId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.RoomId,
                r.Title,
                r.Price,
                r.RoomStatus,
                r.CreatedAt
            })
            .Take(10)
            .ToListAsync();

        return this.ApiOk(new
        {
            hostId = host.HostId,
            host.FullName,
            totalRooms = await _context.Rooms.CountAsync(r => r.HostId == host.HostId),
            recentRooms = rooms
        });
    }

    [HttpGet("employer/overview")]
    [Authorize(Roles = "Employer")]
    public async Task<IActionResult> EmployerOverview()
    {
        var accountId = GetCurrentAccountId();
        if (accountId is null) return this.ApiUnauthorized("Token không hợp lệ.");

        var employer = await _context.EmployerProfiles.FirstOrDefaultAsync(e => e.AccountId == accountId.Value);
        if (employer is null) return this.ApiNotFound("Không tìm thấy hồ sơ nhà tuyển dụng.");

        var jobs = await _context.Jobs
            .Where(j => j.EmployerId == employer.EmployerId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new
            {
                j.JobId,
                j.JobTitle,
                j.SalaryRange,
                j.JobStatus,
                j.CreatedAt
            })
            .Take(10)
            .ToListAsync();

        return this.ApiOk(new
        {
            employerId = employer.EmployerId,
            employer.CompanyName,
            totalJobs = await _context.Jobs.CountAsync(j => j.EmployerId == employer.EmployerId),
            recentJobs = jobs
        });
    }

    [HttpGet("student/overview")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> StudentOverview()
    {
        var accountId = GetCurrentAccountId();
        if (accountId is null) return this.ApiUnauthorized("Token không hợp lệ.");

        var student = await _context.StudentProfiles.FirstOrDefaultAsync(s => s.AccountId == accountId.Value);
        if (student is null) return this.ApiNotFound("Không tìm thấy hồ sơ sinh viên.");

        var totalFavorites = await _context.Favorites.CountAsync(f => f.StudentId == student.StudentId);
        var totalApplications = await _context.JobApplications.CountAsync(a => a.StudentId == student.StudentId);

        return this.ApiOk(new
        {
            studentId = student.StudentId,
            student.FullName,
            totalFavorites,
            totalApplications
        });
    }

    private int? GetCurrentAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }
}
