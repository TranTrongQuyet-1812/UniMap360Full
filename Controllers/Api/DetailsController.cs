using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;

namespace UniMap360.Controllers.Api;

[Route("api")]
[ApiController]
public class DetailsController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public DetailsController(UniMap360ProContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet("rooms/{id:int}")]
    public async Task<IActionResult> GetRoomDetail(int id)
    {
        var room = await _context.Rooms
            .AsNoTracking()
            .Include(r => r.Location)
            .Include(r => r.Category)
            .Include(r => r.Host)
            .FirstOrDefaultAsync(r => r.RoomId == id);

        if (room is null) return this.ApiNotFound("Không tìm thấy phòng.");

        var media = await _context.Media
            .AsNoTracking()
            .Where(m => m.TargetType == "Room" && m.TargetId == id)
            .OrderByDescending(m => m.IsThumbnail == true)
            .ThenBy(m => m.MediaId)
            .Select(m => new
            {
                m.MediaId,
                m.MediaUrl,
                isThumbnail = m.IsThumbnail == true
            })
            .ToListAsync();

        var thumbnail = media.FirstOrDefault(m => m.isThumbnail)?.MediaUrl
            ?? media.FirstOrDefault()?.MediaUrl
            ?? "/images/fallback-room.svg";

        return this.ApiOk(new
        {
            room.RoomId,
            room.Title,
            room.Price,
            room.Area,
            room.Description,
            room.ContactPhone,
            room.RoomStatus,
            room.CreatedAt,
            room.IsExternal,
            room.SourceUrl,
            ownerAccountId = room.Host.AccountId,
            ownerDisplayName = room.Host.FullName,
            category = room.Category.CategoryName,
            location = new
            {
                room.Location.LocationId,
                room.Location.AddressText,
                room.Location.District,
                lat = room.Location.Coordinates.Coordinate.Y,
                lng = room.Location.Coordinates.Coordinate.X
            },
            thumbnail,
            media
        });
    }

    [AllowAnonymous]
    [HttpGet("jobs/{id:int}")]
    public async Task<IActionResult> GetJobDetail(int id)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .Include(j => j.Location)
            .Include(j => j.Category)
            .Include(j => j.Employer)
            .FirstOrDefaultAsync(j => j.JobId == id);

        if (job is null) return this.ApiNotFound("Không tìm thấy việc làm.");

        var media = await _context.Media
            .AsNoTracking()
            .Where(m => m.TargetType == "Job" && m.TargetId == id)
            .OrderByDescending(m => m.IsThumbnail == true)
            .ThenBy(m => m.MediaId)
            .Select(m => new
            {
                m.MediaId,
                m.MediaUrl,
                isThumbnail = m.IsThumbnail == true
            })
            .ToListAsync();

        var thumbnail = media.FirstOrDefault(m => m.isThumbnail)?.MediaUrl
            ?? media.FirstOrDefault()?.MediaUrl
            ?? "/images/fallback-job.svg";

        return this.ApiOk(new
        {
            job.JobId,
            job.JobTitle,
            job.SalaryRange,
            job.Description,
            job.ContactPhone,
            job.JobType,
            job.JobStatus,
            job.CreatedAt,
            job.IsExternal,
            job.SourceUrl,
            ownerAccountId = job.Employer.AccountId,
            ownerDisplayName = job.Employer.CompanyName,
            category = job.Category.CategoryName,
            location = new
            {
                job.Location.LocationId,
                job.Location.AddressText,
                job.Location.District,
                lat = job.Location.Coordinates.Coordinate.Y,
                lng = job.Location.Coordinates.Coordinate.X
            },
            thumbnail,
            media
        });
    }
}
