using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniMap360.Models;

namespace UniMap360.Controllers.Api;

/// <summary>
/// API import dữ liệu từ Facebook CSV (Instant Data Scraper).
/// Endpoint: GET /api/import/preview  => Xem trước dữ liệu.
/// Endpoint: POST /api/import/facebook => Import vào DB.
/// </summary>
[Route("api/import")]
[ApiController]
[Authorize(Roles = "Admin")]
public class ImportController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        UniMap360ProContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<ImportController> logger)
    {
        _context = context;
        _httpClient = httpClientFactory.CreateClient(nameof(ImportController));
        _logger = logger;
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UniMap360App/1.0");
        }
    }

    /// <summary>
    /// Đọc CSV và tách dữ liệu có nghĩa từ mỗi dòng.
    /// CSV từ Instant Data Scraper có nhiều cột và thường lẫn dữ liệu rác.
    /// </summary>
    private List<FacebookPost> ParseCsvFile(string filePath, string? onlyType = null)
    {
        var posts = new List<FacebookPost>();

        using var parser = new TextFieldParser(filePath)
        {
            TextFieldType = FieldType.Delimited,
            Delimiters = new[] { "," },
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };

        var headers = parser.ReadFields();
        if (headers == null) return posts;

        while (!parser.EndOfData)
        {
            try
            {
                var fields = parser.ReadFields();
                if (fields == null) continue;

                var allTexts = new List<string>();
                var allUrls = new List<string>();
                var allImages = new List<string>();

                for (int i = 0; i < fields.Length; i++)
                {
                    var val = fields[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    var valForMatch = NormalizeForMatch(val);
                    if (valForMatch.Length <= 1) continue;

                    if (valForMatch == "facebook" || valForMatch == "[object object]") continue;
                    if (valForMatch is "xem them" or "thich" or "tra loi" or "chia se" or "theo doi") continue;

                    if (valForMatch.StartsWith("viet binh luan")
                        || valForMatch.StartsWith("viet cau tra loi")
                        || valForMatch.StartsWith("da chia se voi")
                        || valForMatch.StartsWith("xem tat ca")
                        || valForMatch.StartsWith("anh tu bai viet")
                        || valForMatch.StartsWith("nguoi dung gop")
                        || valForMatch.StartsWith("nguoi tham gia"))
                    {
                        continue;
                    }

                    if (valForMatch.Contains("muc moi")
                        || valForMatch == "phu hop nhat"
                        || valForMatch == "bai viet an danh"
                        || valForMatch == "ban viet gi di...")
                    {
                        continue;
                    }

                    if (val.StartsWith("data:image", StringComparison.OrdinalIgnoreCase)) continue;

                    if (val.Contains("fbcdn.net", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
                            || val.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || val.Contains(".png", StringComparison.OrdinalIgnoreCase)
                            || val.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                        {
                            allImages.Add(val);
                        }
                        continue;
                    }

                    if (val.Contains("facebook.com/groups", StringComparison.OrdinalIgnoreCase)
                        || val.Contains("facebook.com/photo", StringComparison.OrdinalIgnoreCase))
                    {
                        allUrls.Add(val);
                        continue;
                    }

                    allTexts.Add(val);
                }

                if (allTexts.Count < 2) continue;

                var post = ExtractPostInfo(allTexts, allUrls, allImages);
                if (post != null && (onlyType == null || post.PostType.Equals(onlyType, StringComparison.OrdinalIgnoreCase)))
                {
                    posts.Add(post);
                }
            }
            catch
            {
                // Bỏ qua dòng lỗi.
            }
        }

        return posts;
    }

    /// <summary>
    /// Trích xuất thông tin bài đăng từ danh sách text.
    /// </summary>
    private FacebookPost? ExtractPostInfo(List<string> texts, List<string> urls, List<string> images)
    {
        var fullText = string.Join(" ", texts);
        var normalizedFullText = NormalizeForMatch(fullText);

        var rentalKeywords = new[]
        {
            "phong", "cho thue", "can ho", "tro", "studio", "chung cu", "chdv",
            "ky tuc", "gac", "ban cong", "dat coc", "o ghep"
        };

        var jobKeywords = new[]
        {
            "tuyen", "tuyen dung", "part-time", "part time", "full-time", "full time",
            "nhan vien", "ung tuyen", "thu nhap", "luong", "ca lam", "cv", "viec lam"
        };

        var rentalScore = CountKeywordHits(normalizedFullText, rentalKeywords);
        var jobScore = CountKeywordHits(normalizedFullText, jobKeywords);

        var findKeywords = new[] { "tim tro", "tim phong", "can tim", "can thue" };
        bool isFindPost = findKeywords.Any(k => normalizedFullText.Contains(k, StringComparison.Ordinal));

        var findJobKeywords = new[] { "tim viec", "can viec", "xin viec" };
        bool isFindJobPost = findJobKeywords.Any(k => normalizedFullText.Contains(k, StringComparison.Ordinal));

        if (isFindPost || isFindJobPost) return null;
        if (rentalScore == 0 && jobScore == 0) return null;

        var post = new FacebookPost
        {
            PostType = jobScore >= rentalScore ? "Job" : "Room"
        };

        foreach (var text in texts)
        {
            if (text.Length < 10) continue;

            if (Regex.IsMatch(text, @"^[\p{Lu}\d\s,\-\.\/]+$") && text.Length >= 15)
            {
                post.Title = text;
                break;
            }

            var upper = text.ToUpperInvariant();
            if (upper.StartsWith("PHÒNG")
                || upper.StartsWith("PHONG")
                || upper.StartsWith("CĂN HỘ")
                || upper.StartsWith("CAN HO")
                || upper.StartsWith("CHO THUÊ")
                || upper.StartsWith("CHO THUE")
                || upper.StartsWith("KHAI TRƯƠNG")
                || upper.StartsWith("KHAI TRUONG"))
            {
                post.Title = text;
                break;
            }
        }

        if (string.IsNullOrEmpty(post.Title))
        {
            post.Title = texts
                .Where(t => t.Length >= 20 && !t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !t.Contains("facebook.com", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.Length)
                .FirstOrDefault() ?? string.Empty;

            if (post.Title.Length > 120)
            {
                post.Title = post.Title[..120] + "...";
            }
        }

        if (string.IsNullOrEmpty(post.Title)) return null;

        post.Price = ExtractPrice(fullText);
        post.Address = ExtractAddress(fullText);
        post.Description = string.Join("\n", texts.Where(t => t.Length >= 30 && t != post.Title).Take(3));
        post.SourceUrl = urls.FirstOrDefault(u => u.Contains("facebook.com/groups", StringComparison.OrdinalIgnoreCase) && !u.Contains("comment_id", StringComparison.OrdinalIgnoreCase) && u.Length > 60);
        post.ThumbnailUrl = images.FirstOrDefault();

        return post;
    }

    /// <summary>
    /// Bước 0: Xem trước dữ liệu (không chèn DB).
    /// </summary>
    [HttpGet("preview")]
    public IActionResult PreviewCsv()
    {
        return PreviewCsvInternal();
    }

    [HttpGet("preview/jobs")]
    public IActionResult PreviewJobCsv()
    {
        return PreviewCsvInternal("Job");
    }

    [HttpGet("preview/rooms")]
    public IActionResult PreviewRoomCsv()
    {
        return PreviewCsvInternal("Room");
    }

    private IActionResult PreviewCsvInternal(string? onlyType = null)
    {
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "csdl", "facebook.csv");
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("File facebook.csv không tìm thấy!");
        }

        var posts = ParseCsvFile(filePath, onlyType);

        return Ok(new
        {
            totalParsed = posts.Count,
            posts = posts.Select((p, i) => new
            {
                index = i + 1,
                type = p.PostType,
                title = p.Title,
                price = p.Price,
                priceFormatted = p.Price > 0 ? $"{p.Price / 1000000m:0.##} triệu" : "Chưa xác định",
                address = p.Address,
                description = p.Description?.Length > 100 ? p.Description[..100] + "..." : p.Description,
                sourceUrl = p.SourceUrl,
                hasThumbnail = !string.IsNullOrEmpty(p.ThumbnailUrl)
            })
        });
    }

    /// <summary>
    /// Bước 1-4: Import vào Database.
    /// </summary>
    [HttpPost("facebook")]
    public async Task<IActionResult> ImportFacebookData()
    {
        return await ImportFacebookDataInternal();
    }

    [HttpPost("facebook/jobs")]
    public async Task<IActionResult> ImportFacebookJobs()
    {
        return await ImportFacebookDataInternal("Job");
    }

    [HttpPost("facebook/rooms")]
    public async Task<IActionResult> ImportFacebookRooms()
    {
        return await ImportFacebookDataInternal("Room");
    }

    private async Task<IActionResult> ImportFacebookDataInternal(string? onlyType = null)
    {
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "csdl", "facebook.csv");
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("File facebook.csv không tìm thấy!");
        }

        var botAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == "system_bot@unimap.vn");
        if (botAccount == null)
        {
            return BadRequest("Không tìm thấy tài khoản Bot (system_bot@unimap.vn)!");
        }

        var botHost = await _context.HostProfiles.FirstOrDefaultAsync(h => h.AccountId == botAccount.AccountId);
        if (botHost == null)
        {
            return BadRequest("Không tìm thấy HostProfile của Bot!");
        }

        var botEmployer = await _context.EmployerProfiles.FirstOrDefaultAsync(e => e.AccountId == botAccount.AccountId);
        if (botEmployer == null)
        {
            return BadRequest("Không tìm thấy EmployerProfile của Bot!");
        }

        var defaultRoomCategory = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryType == "Room");
        if (defaultRoomCategory == null)
        {
            return BadRequest("Không có Category loại 'Room'!");
        }

        var defaultJobCategory = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryType == "Job");
        if (defaultJobCategory == null)
        {
            return BadRequest("Không có Category loại 'Job'!");
        }

        var posts = ParseCsvFile(filePath, onlyType);
        var seenFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<object>();
        int imported = 0;
        int importedRooms = 0;
        int importedJobs = 0;
        int skipped = 0;
        int geocodeFailed = 0;

        foreach (var post in posts)
        {
            var fingerprint = BuildFingerprint(post);
            if (!seenFingerprints.Add(fingerprint))
            {
                skipped++;
                continue;
            }

            try
            {
                if (post.PostType == "Room")
                {
                    bool existsRoom = await _context.Rooms.AnyAsync(r =>
                        r.Title == post.Title ||
                        (!string.IsNullOrEmpty(post.SourceUrl) && r.SourceUrl == post.SourceUrl));
                    if (existsRoom)
                    {
                        skipped++;
                        continue;
                    }

                    var (roomLat, roomLng) = await GeocodeAddress(post.Address);
                    if (roomLat == 0 && roomLng == 0)
                    {
                        (roomLat, roomLng) = await GeocodeAddress(post.Address + ", TP. Hồ Chí Minh");
                    }
                    if (roomLat == 0 && roomLng == 0)
                    {
                        geocodeFailed++;
                        var roomRnd = new Random();
                        roomLat = 10.75 + roomRnd.NextDouble() * 0.10;
                        roomLng = 106.62 + roomRnd.NextDouble() * 0.10;
                    }

                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    var roomLocation = new Location
                    {
                        AddressText = post.Address,
                        Coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(roomLng, roomLat)),
                        District = "TP.HCM"
                    };
                    _context.Locations.Add(roomLocation);
                    await _context.SaveChangesAsync();

                    var room = new Room
                    {
                        HostId = botHost.HostId,
                        LocationId = roomLocation.LocationId,
                        CategoryId = defaultRoomCategory.CategoryId,
                        Title = post.Title.Length > 255 ? post.Title[..255] : post.Title,
                        Price = post.Price > 0 ? post.Price : 3000000,
                        Area = ExtractArea(post.Description ?? "") > 0 ? ExtractArea(post.Description ?? "") : 20,
                        RoomStatus = "Available",
                        IsExternal = true,
                        SourceUrl = post.SourceUrl
                    };
                    _context.Rooms.Add(room);
                    await _context.SaveChangesAsync();

                    if (!string.IsNullOrEmpty(post.ThumbnailUrl))
                    {
                        var media = new Medium
                        {
                            TargetType = "Room",
                            TargetId = room.RoomId,
                            MediaUrl = post.ThumbnailUrl,
                            IsThumbnail = true
                        };
                        _context.Media.Add(media);
                        await _context.SaveChangesAsync();
                    }

                    imported++;
                    importedRooms++;
                    results.Add(new
                    {
                        type = "Room",
                        title = room.Title,
                        price = $"{room.Price / 1000000m:0.##}tr",
                        address = post.Address,
                        lat = Math.Round(roomLat, 6),
                        lng = Math.Round(roomLng, 6),
                        geocoded = roomLat != 0
                    });
                }
                else
                {
                    bool existsJob = await _context.Jobs.AnyAsync(j =>
                        j.JobTitle == post.Title ||
                        (!string.IsNullOrEmpty(post.SourceUrl) && j.SourceUrl == post.SourceUrl));
                    if (existsJob)
                    {
                        skipped++;
                        continue;
                    }

                    var (jobLat, jobLng) = await GeocodeAddress(post.Address);
                    if (jobLat == 0 && jobLng == 0)
                    {
                        (jobLat, jobLng) = await GeocodeAddress(post.Address + ", TP. Hồ Chí Minh");
                    }
                    if (jobLat == 0 && jobLng == 0)
                    {
                        geocodeFailed++;
                        var jobRnd = new Random();
                        jobLat = 10.75 + jobRnd.NextDouble() * 0.10;
                        jobLng = 106.62 + jobRnd.NextDouble() * 0.10;
                    }

                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    var jobLocation = new Location
                    {
                        AddressText = post.Address,
                        Coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(jobLng, jobLat)),
                        District = "TP.HCM"
                    };
                    _context.Locations.Add(jobLocation);
                    await _context.SaveChangesAsync();

                    var salaryRange = post.Price > 0 ? $"{post.Price / 1000000m:0.##} triệu" : "Thỏa thuận";
                    var job = new Job
                    {
                        EmployerId = botEmployer.EmployerId,
                        LocationId = jobLocation.LocationId,
                        CategoryId = defaultJobCategory.CategoryId,
                        JobTitle = post.Title.Length > 255 ? post.Title[..255] : post.Title,
                        SalaryRange = salaryRange,
                        JobType = "Part-time",
                        JobStatus = "Open",
                        IsExternal = true,
                        SourceUrl = post.SourceUrl
                    };
                    _context.Jobs.Add(job);
                    await _context.SaveChangesAsync();

                    if (!string.IsNullOrEmpty(post.ThumbnailUrl))
                    {
                        var media = new Medium
                        {
                            TargetType = "Job",
                            TargetId = job.JobId,
                            MediaUrl = post.ThumbnailUrl,
                            IsThumbnail = true
                        };
                        _context.Media.Add(media);
                        await _context.SaveChangesAsync();
                    }

                    imported++;
                    importedJobs++;
                    results.Add(new
                    {
                        type = "Job",
                        title = job.JobTitle,
                        salary = job.SalaryRange,
                        address = post.Address,
                        lat = Math.Round(jobLat, 6),
                        lng = Math.Round(jobLng, 6),
                        geocoded = jobLat != 0
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new { title = post.Title, error = ex.Message });
                skipped++;
            }
        }

        return Ok(new
        {
            message = $"✅ Import hoàn tất! {imported} bản ghi đã nhập ({importedRooms} phòng, {importedJobs} việc làm), {skipped} bỏ qua, {geocodeFailed} không geocode được.",
            imported,
            importedRooms,
            importedJobs,
            skipped,
            geocodeFailed,
            details = results
        });
    }

    // ==================== HELPER METHODS ====================

    private static int CountKeywordHits(string normalizedText, IEnumerable<string> keywords)
    {
        return keywords.Count(k => normalizedText.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return Regex.Replace(text.Trim().ToLowerInvariant(), "\\s+", " ");
    }

    private static string NormalizeForMatch(string? text)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrEmpty(normalized)) return string.Empty;
        var noAccent = RemoveDiacritics(normalized);
        return Regex.Replace(noAccent, "\\s+", " ").Trim();
    }

    private static string RemoveDiacritics(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb
            .ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormC);
    }

    private static string BuildFingerprint(FacebookPost post)
    {
        var source = post.SourceUrl?.Split("?", 2)[0] ?? string.Empty;
        return $"{NormalizeForMatch(post.PostType)}|{NormalizeForMatch(post.Title)}|{NormalizeForMatch(post.Address)}|{NormalizeForMatch(source)}";
    }

    private static decimal ExtractPrice(string text)
    {
        var normalized = NormalizeForMatch(text);
        var compact = normalized.Replace(" ", string.Empty);

        // "3tr5" => 3,500,000
        var match = Regex.Match(compact, @"(\d+)tr(\d+)");
        if (match.Success)
        {
            var m = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var r = match.Groups[2].Value;
            return r.Length switch
            {
                1 => m * 1000000m + int.Parse(r, CultureInfo.InvariantCulture) * 100000m,
                3 => m * 1000000m + int.Parse(r, CultureInfo.InvariantCulture) * 1000m,
                _ => m * 1000000m
            };
        }

        // "3.5 trieu" hoặc "3,5tr"
        match = Regex.Match(normalized, @"(\d+)[.,](\d+)\s*(?:trieu|tr)");
        if (match.Success)
        {
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 1000000m
                 + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 100000m;
        }

        // "3 trieu" hoặc "3tr"
        match = Regex.Match(normalized, @"(\d+)\s*(?:trieu|tr\b)");
        if (match.Success)
        {
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 1000000m;
        }

        // "#3tr6" => 3,600,000
        match = Regex.Match(compact, @"#(\d+)tr(\d+)?");
        if (match.Success)
        {
            var m = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var r = match.Groups[2].Success ? match.Groups[2].Value : "0";
            return m * 1000000m + (r.Length == 1 ? int.Parse(r, CultureInfo.InvariantCulture) * 100000m : 0);
        }

        // "3,500,000" hoặc "3.500.000"
        match = Regex.Match(text, @"(\d{1,3}(?:[.,]\d{3})+)");
        if (match.Success)
        {
            var numStr = match.Groups[1].Value.Replace(",", "").Replace(".", "");
            if (decimal.TryParse(numStr, out var num) && num >= 500000)
            {
                return num;
            }
        }

        return 0;
    }

    private static string ExtractAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "TP. Hồ Chí Minh";

        var oneLine = Regex.Replace(text, @"\s+", " ").Trim();

        // Số nhà + cụm đường.
        var match = Regex.Match(oneLine, @"\b\d+[\/\d]*\s+[^,\n]{6,80}");
        if (match.Success) return match.Value.Trim();

        // Quận/huyện/TP...
        match = Regex.Match(oneLine, @"\b(?:Q\.?\s*\d{1,2}|Quận\s*\d{1,2}|Huyện\s+[^\.,]{2,30}|TP\.?\s*[^\.,]{2,30})", RegexOptions.IgnoreCase);
        if (match.Success) return match.Value.Trim();

        // Một số khu vực phổ biến.
        var normalized = NormalizeForMatch(oneLine);
        if (normalized.Contains("thu duc")) return "Thủ Đức";
        if (normalized.Contains("di an")) return "Dĩ An";
        if (normalized.Contains("binh thanh")) return "Bình Thạnh";
        if (normalized.Contains("go vap")) return "Gò Vấp";
        if (normalized.Contains("tan binh")) return "Tân Bình";
        if (normalized.Contains("tan phu")) return "Tân Phú";
        if (normalized.Contains("phu nhuan")) return "Phú Nhuận";
        if (normalized.Contains("binh tan")) return "Bình Tân";
        if (normalized.Contains("binh duong")) return "Bình Dương";
        if (normalized.Contains("ha noi")) return "Hà Nội";
        if (normalized.Contains("da nang")) return "Đà Nẵng";
        if (normalized.Contains("can tho")) return "Cần Thơ";
        if (normalized.Contains("hai phong")) return "Hải Phòng";

        // "Gần trường/chợ..." => fallback TP.HCM
        if (Regex.IsMatch(normalized, @"\b(?:gan|ngay)\s+(?:dai hoc|dh|aeon|truong|cho)\b", RegexOptions.IgnoreCase))
        {
            return "TP.HCM";
        }

        return "TP. Hồ Chí Minh";
    }

    private static int ExtractArea(string text)
    {
        var normalized = NormalizeForMatch(text);
        var match = Regex.Match(normalized, @"(\d{1,3})\s*(?:m2|m\^2|m²)");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private async Task<(double lat, double lng)> GeocodeAddress(string address)
    {
        try
        {
            await Task.Delay(1100); // Rate limit Nominatim: 1 req/s
            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(address)}&format=json&limit=1&countrycodes=vn";
            var response = await _httpClient.GetStringAsync(url);
            var results = JsonSerializer.Deserialize<JsonElement[]>(response);
            if (results != null && results.Length > 0)
            {
                var lat = double.Parse(results[0].GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
                var lng = double.Parse(results[0].GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
                return (lat, lng);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Geocode error for address: {Address}", address);
        }
        return (0, 0);
    }

    // ==================== DATA MODEL ====================
    private class FacebookPost
    {
        public string PostType { get; set; } = "Room";
        public string Title { get; set; } = "";
        public decimal Price { get; set; }
        public string Address { get; set; } = "TP. Hồ Chí Minh";
        public string? Description { get; set; }
        public string? SourceUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
    }
}
