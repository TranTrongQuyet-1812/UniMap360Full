using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using UniMap360.Models;

namespace UniMap360.Controllers.Api;

[Route("api/scrape/rooms")]
[ApiController]
[Authorize(Roles = "Admin")]
public class RoomScrapeController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IHttpClientFactory _httpClientFactory;

    public RoomScrapeController(UniMap360ProContext context, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Cào nhanh Rooms từ phongtro123 theo tỉnh/thành (slug) và chèn thẳng vào DB.
    /// Mục tiêu: đủ số lượng marker theo tỉnh để demo map.
    /// </summary>
    /// <example>POST /api/scrape/rooms/phongtro123?provinceSlug=ha-noi&amp;target=20</example>
    [HttpPost("phongtro123")]
    public async Task<IActionResult> ScrapePhongtro123(
        [FromQuery] string provinceSlug,
        [FromQuery] int target = 20,
        [FromQuery] int maxPages = 10)
    {
        provinceSlug = (provinceSlug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provinceSlug))
            return BadRequest("provinceSlug là bắt buộc (ví dụ: ha-noi, ho-chi-minh).");
        if (target is < 1 or > 200)
            return BadRequest("target phải trong khoảng 1..200.");
        if (maxPages is < 1 or > 50)
            return BadRequest("maxPages phải trong khoảng 1..50.");

        var hostBots = await _context.HostProfiles
            .AsNoTracking()
            .OrderBy(h => h.HostId)
            .Take(5)
            .ToListAsync();
        if (hostBots.Count == 0)
            return BadRequest("Không có Host bot nào trong DB. Hãy tạo bot trước.");

        var roomCategory = await _context.Categories
            .AsNoTracking()
            .Where(c => c.CategoryType == "Room")
            .OrderBy(c => c.CategoryId)
            .FirstOrDefaultAsync();
        if (roomCategory is null)
            return BadRequest("Thiếu Category loại 'Room' trong DB.");

        var basePoint = ProvinceCentroids.GetValueOrDefault(provinceSlug);
        if (basePoint.lat == 0 && basePoint.lng == 0)
            basePoint = (16.2, 107.9);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(25);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UniMap360Scraper/1.0 (school project)");

        var inserted = 0;
        var skipped = 0;
        var errors = 0;
        var details = new List<object>();

        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingUrls = new HashSet<string>(
            await _context.Rooms.AsNoTracking().Where(r => r.SourceUrl != null).Select(r => r.SourceUrl!).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= maxPages && inserted < target; page++)
        {
            var listUrl = BuildPhongtro123ListUrl(provinceSlug, page);

            string html;
            try
            {
                using var resp = await client.GetAsync(listUrl);
                if (!resp.IsSuccessStatusCode)
                {
                    errors++;
                    details.Add(new { page, listUrl, error = $"HTTP {(int)resp.StatusCode}" });
                    continue;
                }

                html = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(html))
                {
                    errors++;
                    details.Add(new { page, listUrl, error = "Empty HTML" });
                    continue;
                }
            }
            catch (Exception ex)
            {
                errors++;
                details.Add(new { page, listUrl, error = ex.Message });
                continue;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var candidates = ExtractPhongtro123Candidates(doc);
            if (candidates.Count == 0)
            {
                details.Add(new { page, listUrl, note = "No candidates found" });
                continue;
            }

            foreach (var c in candidates)
            {
                if (inserted >= target) break;
                if (string.IsNullOrWhiteSpace(c.SourceUrl)) { skipped++; continue; }

                if (!seenUrls.Add(c.SourceUrl)) { skipped++; continue; }
                if (existingUrls.Contains(c.SourceUrl)) { skipped++; continue; }

                var title = NormalizeSpace(WebUtility.HtmlDecode(c.Title));
                if (string.IsNullOrWhiteSpace(title)) { skipped++; continue; }
                if (title.Length > 255) title = title[..255];

                var price = ParseRoomPriceToVnd(c.PriceText);
                if (price <= 0) price = 2500000m;

                var address = NormalizeSpace(WebUtility.HtmlDecode(c.AddressText));
                if (string.IsNullOrWhiteSpace(address))
                    address = provinceSlug.Replace('-', ' ');

                var jitter = DeterministicJitter(inserted, title);
                var (lat, lng) = BuildProvincePoint(provinceSlug, basePoint, jitter);

                var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                var location = new Location
                {
                    AddressText = address,
                    ProvinceName = provinceSlug.Replace('-', ' '),
                    District = provinceSlug.Replace('-', ' '),
                    FullAddressNormalized = NormalizeSpace($"{address}, {provinceSlug.Replace('-', ' ')}, Việt Nam"),
                    Coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(lng, lat))
                };
                _context.Locations.Add(location);
                await _context.SaveChangesAsync();

                var host = hostBots[inserted % hostBots.Count];
                var room = new Room
                {
                    HostId = host.HostId,
                    LocationId = location.LocationId,
                    CategoryId = roomCategory.CategoryId,
                    Title = title,
                    Price = price,
                    Area = ParseArea(c.AreaText) > 0 ? ParseArea(c.AreaText) : 20,
                    Description = NormalizeSpace(WebUtility.HtmlDecode(c.DescriptionText)),
                    RoomStatus = "Available",
                    ContactPhone = null,
                    IsExternal = true,
                    SourceUrl = c.SourceUrl
                };
                _context.Rooms.Add(room);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(c.ThumbnailUrl))
                {
                    _context.Media.Add(new Medium
                    {
                        TargetType = "Room",
                        TargetId = room.RoomId,
                        MediaUrl = c.ThumbnailUrl,
                        IsThumbnail = true
                    });
                    await _context.SaveChangesAsync();
                }

                existingUrls.Add(c.SourceUrl);
                inserted++;
            }

            await Task.Delay(700);
        }

        return Ok(new
        {
            source = "phongtro123",
            provinceSlug,
            target,
            inserted,
            skipped,
            errors,
            note = "Đang dùng tọa độ centroid theo tỉnh + jitter để đảm bảo phủ bản đồ; có thể nâng cấp sang geocode địa chỉ chi tiết nếu cần.",
            details = details.Take(30)
        });
    }

    /// <summary>
    /// Gán ảnh thumbnail hàng loạt cho các phòng chưa có ảnh.
    /// Hữu ích cho dữ liệu seed/cào khi nguồn không trả về thumbnail.
    /// </summary>
    [HttpPost("backfill-images")]
    public async Task<IActionResult> BackfillRoomImages([FromQuery] int limit = 5000)
    {
        if (limit is < 1 or > 20000)
            return BadRequest("limit phải trong khoảng 1..20000.");

        var roomIdsWithoutMedia = await _context.Rooms
            .AsNoTracking()
            .Where(r => !_context.Media.Any(m => m.TargetType == "Room" && m.TargetId == r.RoomId))
            .OrderBy(r => r.RoomId)
            .Select(r => r.RoomId)
            .Take(limit)
            .ToListAsync();

        if (roomIdsWithoutMedia.Count == 0)
            return Ok(new { message = "Không có phòng nào thiếu ảnh.", inserted = 0 });

        var rows = new List<Medium>(roomIdsWithoutMedia.Count);
        for (var i = 0; i < roomIdsWithoutMedia.Count; i++)
        {
            var roomId = roomIdsWithoutMedia[i];
            rows.Add(new Medium
            {
                TargetType = "Room",
                TargetId = roomId,
                MediaUrl = BuildSeededRoomImageUrl(roomId),
                IsThumbnail = true
            });
        }

        _context.Media.AddRange(rows);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Backfill ảnh phòng trọ thành công.",
            inserted = rows.Count,
            sampleImage = rows[0].MediaUrl
        });
    }

    /// <summary>
    /// Làm giàu ảnh cho dữ liệu phòng hiện có:
    /// đảm bảo mỗi phòng có 3-4 ảnh (không tạo thêm phòng mới).
    /// </summary>
    [HttpPost("enrich-images")]
    public async Task<IActionResult> EnrichRoomImages(
        [FromQuery] int minImages = 3,
        [FromQuery] int maxImages = 4,
        [FromQuery] int limit = 5000,
        [FromQuery] bool replaceAll = false)
    {
        if (minImages is < 1 or > 10) return BadRequest("minImages phải trong khoảng 1..10.");
        if (maxImages is < 1 or > 10) return BadRequest("maxImages phải trong khoảng 1..10.");
        if (minImages > maxImages) return BadRequest("minImages không được lớn hơn maxImages.");
        if (limit is < 1 or > 50000) return BadRequest("limit phải trong khoảng 1..50000.");

        var roomIds = await _context.Rooms
            .AsNoTracking()
            .OrderBy(r => r.RoomId)
            .Select(r => r.RoomId)
            .Take(limit)
            .ToListAsync();

        if (roomIds.Count == 0)
            return Ok(new { message = "Không có phòng nào để làm giàu ảnh.", inserted = 0, affectedRooms = 0 });

        var mediaByRoom = await _context.Media
            .Where(m => m.TargetType == "Room" && roomIds.Contains(m.TargetId))
            .OrderBy(m => m.MediaId)
            .ToListAsync();

        if (replaceAll && mediaByRoom.Count > 0)
        {
            _context.Media.RemoveRange(mediaByRoom);
            await _context.SaveChangesAsync();
            mediaByRoom.Clear();
        }

        var grouped = mediaByRoom
            .GroupBy(m => m.TargetId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var newRows = new List<Medium>();
        var affectedRooms = 0;
        var keptRooms = 0;

        foreach (var roomId in roomIds)
        {
            grouped.TryGetValue(roomId, out var existing);
            existing ??= new List<Medium>();

            var targetCount = minImages == maxImages
                ? minImages
                : minImages + Math.Abs(roomId % (maxImages - minImages + 1));

            if (existing.Count >= targetCount)
            {
                keptRooms++;
                continue;
            }

            var needed = targetCount - existing.Count;
            var used = new HashSet<string>(
                existing.Where(x => !string.IsNullOrWhiteSpace(x.MediaUrl)).Select(x => x.MediaUrl!),
                StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < needed; i++)
            {
                var mediaUrl = BuildSeededRoomImageUrl(roomId, i + existing.Count + 1);
                if (!used.Add(mediaUrl))
                    continue;

                newRows.Add(new Medium
                {
                    TargetType = "Room",
                    TargetId = roomId,
                    MediaUrl = mediaUrl,
                    IsThumbnail = existing.Count == 0 && i == 0
                });
            }

            affectedRooms++;
        }

        if (newRows.Count > 0)
        {
            _context.Media.AddRange(newRows);
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            message = "Làm giàu ảnh phòng trọ thành công.",
            processedRooms = roomIds.Count,
            affectedRooms,
            unchangedRooms = keptRooms,
            inserted = newRows.Count,
            targetRange = $"{minImages}-{maxImages}",
            replaceAll
        });
    }

    private static string BuildPhongtro123ListUrl(string provinceSlug, int page)
    {
        // Best-effort: structure may vary; scraper is defensive.
        // Example used by many VN listing sites: /tinh-thanh/<slug>?page=<n>
        var baseUrl = "https://phongtro123.com";
        var path = $"/tinh-thanh/{provinceSlug}";
        var url = page <= 1 ? $"{baseUrl}{path}" : $"{baseUrl}{path}?page={page}";
        return url;
    }

    private static List<RoomCandidate> ExtractPhongtro123Candidates(HtmlDocument doc)
    {
        var results = new List<RoomCandidate>();

        // Try multiple heuristics; site markup may change.
        var anchors = doc.DocumentNode.SelectNodes("//a[@href]") ?? new HtmlNodeCollection(doc.DocumentNode);
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (!href.Contains("phongtro123.com", StringComparison.OrdinalIgnoreCase) && !href.StartsWith("/"))
                continue;

            // Heuristic: detail pages typically include numeric id or end with .html
            if (!href.Contains(".html", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(href, @"\d{4,}"))
                continue;

            var url = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : "https://phongtro123.com" + href;

            var title = a.InnerText?.Trim() ?? string.Empty;
            if (title.Length < 10) continue;

            var containerText = NormalizeSpace(a.ParentNode?.InnerText ?? string.Empty);
            var priceText = ExtractPriceText(containerText);
            var areaText = ExtractAreaText(containerText);

            results.Add(new RoomCandidate
            {
                SourceUrl = url,
                Title = title,
                PriceText = priceText,
                AreaText = areaText,
                AddressText = string.Empty,
                ThumbnailUrl = ExtractFirstImageUrl(a),
                DescriptionText = string.Empty
            });
        }

        // Deduplicate by URL
        return results
            .GroupBy(x => x.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(200)
            .ToList();
    }

    private static string? ExtractFirstImageUrl(HtmlNode anchor)
    {
        var img = anchor.SelectSingleNode(".//img")
            ?? anchor.ParentNode?.SelectSingleNode(".//img");
        if (img is null) return null;

        var src = img.GetAttributeValue("src", string.Empty);
        if (string.IsNullOrWhiteSpace(src))
            src = img.GetAttributeValue("data-src", string.Empty);
        if (string.IsNullOrWhiteSpace(src))
            src = img.GetAttributeValue("data-original", string.Empty);
        if (string.IsNullOrWhiteSpace(src))
        {
            var srcset = img.GetAttributeValue("srcset", string.Empty);
            if (!string.IsNullOrWhiteSpace(srcset))
                src = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim().Split(' ')[0].Trim();
        }

        if (string.IsNullOrWhiteSpace(src)) return null;
        if (src.StartsWith("//")) return $"https:{src}";
        if (src.StartsWith("/")) return $"https://phongtro123.com{src}";
        return src;
    }

    private static string ExtractPriceText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var m = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(triệu|tr|nghìn|k)\b", RegexOptions.IgnoreCase);
        return m.Success ? m.Value : string.Empty;
    }

    private static string ExtractAreaText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var m = Regex.Match(text, @"(\d{1,3})\s*m(?:2|²)", RegexOptions.IgnoreCase);
        return m.Success ? m.Value : string.Empty;
    }

    private static double ParseArea(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var m = Regex.Match(text, @"(\d{1,3})");
        return m.Success && double.TryParse(m.Groups[1].Value, out var area) ? area : 0;
    }

    private static decimal ParseRoomPriceToVnd(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var raw = NormalizeSpace(text).ToLowerInvariant().Replace(" ", string.Empty);

        var m = Regex.Match(raw, @"(\d+(?:[.,]\d+)?)\s*(triệu|tr)\b", RegexOptions.IgnoreCase);
        if (m.Success && decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mil))
            return Math.Round(mil * 1000000m, 0);

        m = Regex.Match(raw, @"(\d+)\s*(k|nghìn|nghin)\b", RegexOptions.IgnoreCase);
        if (m.Success && decimal.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k))
            return k * 1000m;

        var digits = Regex.Replace(raw, "[^0-9]", "");
        if (decimal.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 500000)
            return v;

        return 0;
    }

    private static (double lat, double lng) DeterministicJitter(int index, string salt)
    {
        var rnd = new Random(HashCode.Combine(index, salt));
        var lat = (rnd.NextDouble() - 0.5) * 0.04;
        var lng = (rnd.NextDouble() - 0.5) * 0.04;
        return (lat, lng);
    }

    private static (double lat, double lng) BuildProvincePoint(
        string provinceSlug,
        (double lat, double lng) basePoint,
        (double lat, double lng) jitter)
    {
        var lat = basePoint.lat + jitter.lat;
        var lng = basePoint.lng + jitter.lng;

        if (CoastalProvinceSlugs.Contains(provinceSlug))
            lng -= 0.035;

        return (lat, lng);
    }

    private static string NormalizeSpace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Regex.Replace(value.Trim(), "\\s+", " ");
    }

    private sealed class RoomCandidate
    {
        public string SourceUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PriceText { get; set; } = string.Empty;
        public string AreaText { get; set; } = string.Empty;
        public string AddressText { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string DescriptionText { get; set; } = string.Empty;
    }

    private static readonly HashSet<string> CoastalProvinceSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "quang-ninh","hai-phong","thai-binh","nam-dinh","ninh-binh","thanh-hoa","nghe-an",
        "ha-tinh","quang-binh","quang-tri","thua-thien-hue","da-nang","quang-nam","quang-ngai",
        "binh-dinh","phu-yen","khanh-hoa","ninh-thuan","binh-thuan","ba-ria-vung-tau",
        "ho-chi-minh","tien-giang","ben-tre","tra-vinh","soc-trang","bac-lieu","ca-mau",
        "kien-giang"
    };

    private static readonly IReadOnlyDictionary<string, (double lat, double lng)> ProvinceCentroids =
        new Dictionary<string, (double lat, double lng)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ha-noi"] = (21.0278, 105.8342),
            ["ho-chi-minh"] = (10.8231, 106.6297),
            ["da-nang"] = (16.0544, 108.2022),
            ["hai-phong"] = (20.8449, 106.6881),
            ["can-tho"] = (10.0452, 105.7469),
            ["binh-duong"] = (11.3254, 106.4770),
            ["dong-nai"] = (10.9453, 106.8240),
            ["thanh-hoa"] = (19.8067, 105.7852),
            ["nghe-an"] = (18.6730, 105.6920),
            ["khanh-hoa"] = (12.2585, 109.0526),
            ["lam-dong"] = (11.9404, 108.4583),
            ["quang-ninh"] = (21.0064, 107.2925),
            ["thua-thien-hue"] = (16.4674, 107.5909),
            ["bac-ninh"] = (21.1861, 106.0763),
            ["binh-thuan"] = (10.9804, 108.2615),
            ["phu-yen"] = (13.0882, 109.0929),
            ["ninh-binh"] = (20.2506, 105.9745),
            ["tay-ninh"] = (11.3352, 106.1099),
            ["soc-trang"] = (9.6025, 105.9739),
            ["son-la"] = (21.1022, 103.7289),
            ["yen-bai"] = (21.7168, 104.8986)
        };

    // Link ảnh theo seed giúp mỗi phòng có ảnh khác nhau, không bị lặp vòng 16 ảnh.
    private static string BuildSeededRoomImageUrl(int roomId, int offset = 0)
    {
        var lockId = Math.Abs(HashCode.Combine(roomId, offset)) % 100000;
        // Ưu tiên ảnh nội thất/nhà ở để tránh ảnh random sai ngữ cảnh (động vật, phong cảnh...).
        return $"https://loremflickr.com/960/640/apartment,interior,bedroom?lock={lockId}";
    }
}

