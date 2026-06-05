using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;

namespace UniMap360.Controllers.Api;

[Route("api/listings")]
[ApiController]
public class ListingsController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public ListingsController(UniMap360ProContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet("cards")]
    public async Task<IActionResult> GetCards(
        [FromQuery] string type = "all",
        [FromQuery] string? keyword = null,
        [FromQuery] string? province = null,
        [FromQuery] string? district = null,
        [FromQuery] string? ward = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool onlyFeatured = false)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var normalizedType = type.Trim().ToLowerInvariant();
        var query = _context.VGlobalMapFeeds.AsNoTracking().AsQueryable();

        if (normalizedType == "room")
            query = query.Where(x => x.ItemType == "Room");
        else if (normalizedType == "job")
            query = query.Where(x => x.ItemType == "Job");

        if (onlyFeatured)
        {
            var nowUtc = DateTime.UtcNow;
            query = from item in query
                    join fl in _context.FeaturedListings.AsNoTracking()
                        on new { TargetType = item.ItemType, TargetId = item.Id }
                        equals new { fl.TargetType, fl.TargetId }
                    where fl.Status == "Active" && fl.FeatureType == "ExplorePriority" && fl.EndsAt > nowUtc
                    select item;
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var escapedKeyword = EscapeLikePattern(keyword.Trim());
            query = query.Where(x =>
                EF.Functions.Like(x.Title, $"%{escapedKeyword}%", @"\")
                || EF.Functions.Like(x.AddressText, $"%{escapedKeyword}%", @"\"));
        }

        if (!string.IsNullOrWhiteSpace(province) && !string.Equals(province, "all", StringComparison.OrdinalIgnoreCase))
        {
            var provinceRaw = province.Trim();
            var provinceRawLower = provinceRaw.ToLowerInvariant();
            var provinceNormalized = NormalizeText(provinceRaw);
            var escapedProvinceRawLower = EscapeLikePattern(provinceRawLower);
            var escapedProvinceNormalized = EscapeLikePattern(provinceNormalized);

            if (provinceNormalized.Contains("ho chi minh"))
            {
                query = query.Where(x =>
                    EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), $"%{escapedProvinceRawLower}%", @"\")
                    || EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), $"%{escapedProvinceNormalized}%", @"\")
                    || EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), "%tp.hcm%")
                    || EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), "%tp hcm%")
                    || EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), "%tp ho chi minh%")
                    || EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), "%sai gon%")
                    || EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), "%thu duc%"));
            }
            else
            {
                query = query.Where(x =>
                    EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), $"%{escapedProvinceRawLower}%", @"\")
                    || EF.Functions.Like((x.AddressText ?? string.Empty).ToLower(), $"%{escapedProvinceNormalized}%", @"\"));
            }
        }

        if (!string.IsNullOrWhiteSpace(district) && !string.Equals(district, "all", StringComparison.OrdinalIgnoreCase))
        {
            var escapedDistrict = EscapeLikePattern(district.Trim());
            query = query.Where(x => EF.Functions.Like(x.AddressText, $"%{escapedDistrict}%", @"\"));
        }

        if (!string.IsNullOrWhiteSpace(ward) && !string.Equals(ward, "all", StringComparison.OrdinalIgnoreCase))
        {
            var escapedWard = EscapeLikePattern(ward.Trim());
            query = query.Where(x => EF.Functions.Like(x.AddressText, $"%{escapedWard}%", @"\"));
        }

        var total = await query.CountAsync();

        var now = DateTime.UtcNow;
        var projectedQuery = from item in query
                             join fl in _context.FeaturedListings.AsNoTracking()
                                on new { TargetType = item.ItemType, TargetId = item.Id }
                                equals new { fl.TargetType, fl.TargetId } into flGroup
                             from flActive in flGroup.Where(f => f.Status == "Active" && f.FeatureType == "ExplorePriority" && f.EndsAt > now).DefaultIfEmpty()
                             select new { Item = item, IsFeatured = flActive != null };

        var feedItemsWithFeatured = await projectedQuery
            .OrderByDescending(x => x.IsFeatured)
            .ThenByDescending(x => x.Item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var feedItems = feedItemsWithFeatured.Select(x => x.Item).ToList();
        var featuredSet = feedItemsWithFeatured.ToDictionary(x => (x.Item.ItemType, x.Item.Id), x => x.IsFeatured);

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

        var roomVerifications = await _context.Rooms
            .AsNoTracking()
            .Where(r => roomIds.Contains(r.RoomId))
            .Select(r => new { r.RoomId, IsVerified = (r.Host.IsVerified == true) })
            .ToDictionaryAsync(r => r.RoomId, r => r.IsVerified);

        var jobVerifications = await _context.Jobs
            .AsNoTracking()
            .Where(j => jobIds.Contains(j.JobId))
            .Select(j => new { j.JobId, IsVerified = j.Employer.IsVerified })
            .ToDictionaryAsync(j => j.JobId, j => j.IsVerified);

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

        var items = feedItems.Select(item =>
        {
            var mediaForItem = mediaItems
                .Where(m => m.TargetType == item.ItemType && m.TargetId == item.Id)
                .OrderByDescending(m => m.IsThumbnail == true)
                .ThenBy(m => m.MediaId)
                .ToList();

            var selectedImage = mediaForItem
                .Select(m => m.MediaUrl)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

            var itemType = item.ItemType == "Room" ? "room" : "job";
            var fallbackImage = itemType == "room"
                ? "/images/fallback-room.svg"
                : "/images/fallback-job.svg";

            var displayPrice = FormatPrice(item, jobSalaries);
            var isRoom = itemType == "room";
            bool isFeatured = featuredSet.GetValueOrDefault((item.ItemType, item.Id), false);
            bool isVerified = isRoom 
                ? roomVerifications.GetValueOrDefault(item.Id, false)
                : jobVerifications.GetValueOrDefault(item.Id, false);

            return new
            {
                id = item.Id,
                type = itemType,
                title = item.Title,
                address = item.AddressText,
                price = displayPrice,
                lat = item.Latitude,
                lng = item.Longitude,
                thumbnail = string.IsNullOrWhiteSpace(selectedImage) ? fallbackImage : selectedImage,
                category = item.CategoryName,
                priceStr = isRoom ? item.Value : null,
                isExternal = item.IsExternal ?? false,
                sourceUrl = item.SourceUrl,
                isFeatured = isFeatured,
                isVerified = isVerified
            };
        }).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return this.ApiOk(new
        {
            page,
            pageSize,
            total,
            totalPages,
            items
        });
    }

    private static string FormatPrice(VGlobalMapFeed item, IReadOnlyDictionary<int, string> jobSalaries)
    {
        if (item.ItemType == "Room" && item.Value.HasValue)
        {
            if (item.Value.Value >= 1000000)
                return $"{item.Value.Value / 1000000m:0.#} Triệu";

            return $"{item.Value.Value / 1000m:0}k";
        }

        if (item.ItemType == "Job")
            return jobSalaries.GetValueOrDefault(item.Id, "Thỏa thuận");

        return "Liên hệ";
    }

    private static string NormalizeText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static string EscapeLikePattern(string input)
    {
        return input
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");
    }
}
