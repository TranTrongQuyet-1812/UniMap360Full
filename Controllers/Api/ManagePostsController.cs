using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Services.Posts;

namespace UniMap360.Controllers.Api;

[Route("api/manage-posts")]
[ApiController]
[Authorize(Roles = $"{AppRoles.Host},{AppRoles.Employer}")]
public class ManagePostsController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IManagePostsContextService _managePostsContextService;

    public ManagePostsController(UniMap360ProContext context, IManagePostsContextService managePostsContextService)
    {
        _context = context;
        _managePostsContextService = managePostsContextService;
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        var role = _managePostsContextService.GetCurrentRole(User);
        if (role is null)
            return Unauthorized("Không xác định được vai trò tài khoản.");

        if (role == AppRoles.Host)
        {
            var host = await _managePostsContextService.GetCurrentHostAsync(User);
            if (host is null) return NotFound("Không tìm thấy hồ sơ chủ trọ.");

            var rooms = await _context.Rooms
                .AsNoTracking()
                .Where(r => r.HostId == host.HostId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.RoomId,
                    r.Title,
                    r.Price,
                    r.Area,
                    r.Description,
                    r.ContactPhone,
                    r.RoomStatus,
                    r.CategoryId,
                    r.LocationId,
                    r.CreatedAt,
                    r.IsExternal,
                    r.SourceUrl
                })
                .ToListAsync();

            var locationMap = await _managePostsContextService.LoadLocationMapAsync(rooms.Select(x => x.LocationId));

            var enrichedRooms = rooms.Select(r =>
            {
                locationMap.TryGetValue(r.LocationId, out var location);
                return new
                {
                    r.RoomId,
                    r.Title,
                    r.Price,
                    r.Area,
                    r.Description,
                    r.ContactPhone,
                    r.RoomStatus,
                    r.CategoryId,
                    r.LocationId,
                    r.CreatedAt,
                    r.IsExternal,
                    r.SourceUrl,
                    location = location
                };
            }).ToList();

            return Ok(new { role, total = enrichedRooms.Count, items = enrichedRooms });
        }

        var employer = await _managePostsContextService.GetCurrentEmployerAsync(User);
        if (employer is null) return NotFound("Không tìm thấy hồ sơ nhà tuyển dụng.");

        var jobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.EmployerId == employer.EmployerId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new
            {
                j.JobId,
                j.JobTitle,
                j.SalaryRange,
                j.Description,
                j.ContactPhone,
                j.JobType,
                j.JobStatus,
                j.CategoryId,
                j.LocationId,
                j.CreatedAt,
                j.IsExternal,
                j.SourceUrl
            })
            .ToListAsync();

        var jobLocationMap = await _managePostsContextService.LoadLocationMapAsync(jobs.Select(x => x.LocationId));

        var enrichedJobs = jobs.Select(j =>
        {
            jobLocationMap.TryGetValue(j.LocationId, out var location);
            return new
            {
                j.JobId,
                j.JobTitle,
                j.SalaryRange,
                j.Description,
                j.ContactPhone,
                j.JobType,
                j.JobStatus,
                j.CategoryId,
                j.LocationId,
                j.CreatedAt,
                j.IsExternal,
                j.SourceUrl,
                location = location
            };
        }).ToList();

        return Ok(new { role, total = enrichedJobs.Count, items = enrichedJobs });
    }

    [HttpGet("metadata")]
    public async Task<IActionResult> GetMetadata()
    {
        var role = _managePostsContextService.GetCurrentRole(User);
        if (role is null)
            return Unauthorized("Không xác định được vai trò tài khoản.");

        var categoryType = role == AppRoles.Host ? "room" : "job";

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.CategoryType != null && c.CategoryType.ToLower() == categoryType)
            .OrderBy(c => c.CategoryName)
            .Select(c => new
            {
                c.CategoryId,
                c.CategoryName,
                c.CategoryType
            })
            .ToListAsync();

        var locations = await _context.Locations
            .AsNoTracking()
            .OrderByDescending(l => l.LocationId)
            .Take(500)
            .Select(l => new
            {
                l.LocationId,
                displayName = BuildLocationDisplayName(l)
            })
            .ToListAsync();

        return Ok(new
        {
            role,
            categories,
            locations,
            totalCategories = categories.Count,
            totalLocations = locations.Count
        });
    }

    private static string BuildLocationDisplayName(Models.Location location)
    {
        var streetPart = string.Join(' ', new[]
        {
            location.HouseNumber,
            location.Street
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim()));

        var parts = new[]
        {
            string.IsNullOrWhiteSpace(streetPart) ? null : streetPart,
            location.WardName,
            location.DistrictName,
            location.ProvinceName
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        if (parts.Count > 0)
            return string.Join(", ", parts);

        if (!string.IsNullOrWhiteSpace(location.District))
            return location.District.Trim();

        return string.IsNullOrWhiteSpace(location.AddressText)
            ? $"Location #{location.LocationId}"
            : location.AddressText.Trim();
    }

}
