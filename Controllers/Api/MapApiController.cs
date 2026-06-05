using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;
using Microsoft.Extensions.Caching.Memory;

namespace UniMap360.Controllers.Api;

[Route("api/map")]
[ApiController]
public class MapApiController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IMemoryCache _cache;
    private const string MapFeedCacheKey = "GlobalMapFeed";

    public MapApiController(UniMap360ProContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    [HttpGet("feed")]
    public async Task<IActionResult> GetMapFeed()
    {
        var result = await BuildMapFeedAsync();
        return this.ApiOk(result);
    }

    [HttpGet("/api/feed")]
    public async Task<IActionResult> GetLegacyFeed()
    {
        var result = await BuildMapFeedAsync();
        return this.ApiOk(result);
    }

    private async Task<List<object>> BuildMapFeedAsync()
    {
        if (_cache.TryGetValue(MapFeedCacheKey, out List<object>? cachedFeed) && cachedFeed != null)
        {
            return cachedFeed;
        }

        var feedItems = await _context.VGlobalMapFeeds
            .AsNoTracking()
            .ToListAsync();

        var roomIds = feedItems
            .Where(x => x.ItemType == "Room")
            .Select(x => x.Id)
            .Distinct()
            .ToList();

        var jobIds = feedItems
            .Where(x => x.ItemType == "Job")
            .Select(x => x.Id)
            .Distinct()
            .ToList();

        var mediaItems = await _context.Media
            .AsNoTracking()
            .Where(m =>
                (m.TargetType == "Room" && roomIds.Contains(m.TargetId))
                || (m.TargetType == "Job" && jobIds.Contains(m.TargetId)))
            .ToListAsync();

        var jobSalaries = await _context.Jobs
            .AsNoTracking()
            .Where(j => jobIds.Contains(j.JobId))
            .ToDictionaryAsync(j => j.JobId, j => j.SalaryRange ?? "Thỏa thuận");

        var roomVerified = await _context.Rooms
            .AsNoTracking()
            .Where(r => roomIds.Contains(r.RoomId))
            .Select(r => new { r.RoomId, IsVerified = (r.Host.IsVerified == true) })
            .ToDictionaryAsync(r => r.RoomId, r => r.IsVerified);

        var jobVerified = await _context.Jobs
            .AsNoTracking()
            .Where(j => jobIds.Contains(j.JobId))
            .Select(j => new { j.JobId, IsVerified = j.Employer.IsVerified })
            .ToDictionaryAsync(j => j.JobId, j => j.IsVerified);

        var now = DateTime.UtcNow;
        var activeFeatured = await _context.FeaturedListings
            .AsNoTracking()
            .Where(f => f.Status == "Active" && f.FeatureType == "MapPinned" && f.EndsAt > now)
            .ToListAsync();

        var featuredSet = new HashSet<(string TargetType, int TargetId)>(
            activeFeatured.Select(f => (f.TargetType, f.TargetId))
        );

        var result = feedItems.Select(item =>
        {
            var mediaForItem = mediaItems
                .Where(m => m.TargetType == item.ItemType && m.TargetId == item.Id)
                .OrderByDescending(m => m.IsThumbnail == true)
                .ThenBy(m => m.MediaId)
                .ToList();

            var selectedImage = mediaForItem
                .Select(m => m.MediaUrl)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

            string displayPrice;
            if (item.ItemType == "Room" && item.Value.HasValue)
            {
                displayPrice = item.Value.Value >= 1000000
                    ? $"{item.Value.Value / 1000000m:0.#} Triệu"
                    : $"{item.Value.Value / 1000m:0}k";
            }
            else if (item.ItemType == "Job")
            {
                displayPrice = jobSalaries.GetValueOrDefault(item.Id, "Thỏa thuận");
            }
            else
            {
                displayPrice = "Liên hệ";
            }

            var normalizedType = item.ItemType == "Room" ? "room" : "job";
            var title = item.Title;
            var fallbackImage = normalizedType == "room"
                ? "/images/fallback-room.svg"
                : "/images/fallback-job.svg";
            var resolvedImage = string.IsNullOrWhiteSpace(selectedImage) ? fallbackImage : selectedImage;
            bool isFeatured = featuredSet.Contains((item.ItemType, item.Id));
            bool isVerified = normalizedType == "room"
                ? roomVerified.GetValueOrDefault(item.Id, false)
                : jobVerified.GetValueOrDefault(item.Id, false);

            return new
            {
                id = item.Id,
                type = normalizedType,
                title,
                price = displayPrice,
                lat = item.Latitude,
                lng = item.Longitude,
                address = item.AddressText,
                category = item.CategoryName,
                thumbnail = resolvedImage,
                priceStr = item.ItemType == "Room" ? item.Value : null,
                isExternal = item.IsExternal ?? false,
                sourceUrl = item.SourceUrl,
                isFeatured = isFeatured,
                isVerified = isVerified
            };
        })
        .OrderByDescending(x => x.isFeatured)
        .Cast<object>()
        .ToList();

        _cache.Set(MapFeedCacheKey, result, TimeSpan.FromMinutes(10));

        return result;
    }
}
