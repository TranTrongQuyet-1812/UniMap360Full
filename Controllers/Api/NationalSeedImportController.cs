using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using UniMap360.Models;

namespace UniMap360.Controllers.Api;

[Route("api/import/seed")]
[ApiController]
[Authorize(Roles = "Admin")]
public class NationalSeedImportController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, (double lat, double lng)> _provinceCoordinateCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<Dictionary<string, string>> ProvinceNameBySlug = new(LoadProvinceNameBySlug);
    private static readonly IReadOnlyDictionary<string, (double lat, double lng)> ProvinceCentroids =
        new Dictionary<string, (double lat, double lng)>(StringComparer.OrdinalIgnoreCase)
        {
            ["an-giang"] = (10.5216, 105.1259),
            ["ba-ria-vung-tau"] = (10.5417, 107.2429),
            ["bac-giang"] = (21.2819, 106.1970),
            ["bac-kan"] = (22.1470, 105.8348),
            ["bac-lieu"] = (9.2850, 105.7244),
            ["bac-ninh"] = (21.1861, 106.0763),
            ["ben-tre"] = (10.2415, 106.3759),
            ["binh-dinh"] = (13.7820, 109.2197),
            ["binh-duong"] = (11.3254, 106.4770),
            ["binh-phuoc"] = (11.7512, 106.7234),
            ["binh-thuan"] = (10.9804, 108.2615),
            ["ca-mau"] = (9.1768, 105.1524),
            ["can-tho"] = (10.0452, 105.7469),
            ["cao-bang"] = (22.6666, 106.2570),
            ["da-nang"] = (16.0544, 108.2022),
            ["dak-lak"] = (12.6667, 108.0382),
            ["dak-nong"] = (12.2646, 107.6098),
            ["dien-bien"] = (21.8042, 103.1077),
            ["dong-nai"] = (10.9453, 106.8240),
            ["dong-thap"] = (10.4938, 105.6882),
            ["gia-lai"] = (13.8079, 108.1094),
            ["ha-giang"] = (22.8233, 104.9836),
            ["ha-nam"] = (20.5835, 105.9229),
            ["ha-noi"] = (21.0278, 105.8342),
            ["ha-tinh"] = (18.3559, 105.8877),
            ["hai-duong"] = (20.9373, 106.3140),
            ["hai-phong"] = (20.8449, 106.6881),
            ["hau-giang"] = (9.7579, 105.6413),
            ["hoa-binh"] = (20.8172, 105.3376),
            ["ho-chi-minh"] = (10.8231, 106.6297),
            ["hung-yen"] = (20.8526, 106.0169),
            ["khanh-hoa"] = (12.2585, 109.0526),
            ["kien-giang"] = (10.0125, 105.0809),
            ["kon-tum"] = (14.3497, 108.0005),
            ["lai-chau"] = (22.3964, 103.4707),
            ["lam-dong"] = (11.9404, 108.4583),
            ["lang-son"] = (21.8537, 106.7615),
            ["lao-cai"] = (22.3381, 104.1487),
            ["long-an"] = (10.6956, 106.2431),
            ["nam-dinh"] = (20.4388, 106.1621),
            ["nghe-an"] = (18.6730, 105.6920),
            ["ninh-binh"] = (20.2506, 105.9745),
            ["ninh-thuan"] = (11.6739, 108.8629),
            ["phu-tho"] = (21.2684, 105.2046),
            ["phu-yen"] = (13.0882, 109.0929),
            ["quang-binh"] = (17.6103, 106.3487),
            ["quang-nam"] = (15.5394, 108.0191),
            ["quang-ngai"] = (15.1205, 108.7923),
            ["quang-ninh"] = (21.0064, 107.2925),
            ["quang-tri"] = (16.7403, 107.1855),
            ["soc-trang"] = (9.6025, 105.9739),
            ["son-la"] = (21.1022, 103.7289),
            ["tay-ninh"] = (11.3352, 106.1099),
            ["thai-binh"] = (20.4463, 106.3366),
            ["thai-nguyen"] = (21.5672, 105.8252),
            ["thanh-hoa"] = (19.8067, 105.7852),
            ["thua-thien-hue"] = (16.4674, 107.5909),
            ["tien-giang"] = (10.4493, 106.3420),
            ["tra-vinh"] = (9.8127, 106.2993),
            ["tuyen-quang"] = (21.7767, 105.2280),
            ["vinh-long"] = (10.2537, 105.9722),
            ["vinh-phuc"] = (21.3609, 105.5474),
            ["yen-bai"] = (21.7168, 104.8986)
        };

    static NationalSeedImportController()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public NationalSeedImportController(UniMap360ProContext context)
    {
        _context = context;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "UniMap360NationalSeed/1.0");
    }

    [HttpPost("csv")]
    public async Task<IActionResult> ImportSeedCsv(
        [FromQuery] string filePath = "tools/national-seed/national-seed.csv",
        [FromQuery] bool excludeHcm = false)
    {
        var absPath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(Directory.GetCurrentDirectory(), filePath);

        if (!System.IO.File.Exists(absPath))
            return NotFound($"Seed CSV không tìm thấy: {absPath}");

        var rows = ReadSeedCsv(absPath);
        if (rows.Count == 0)
            return BadRequest("Seed CSV rỗng hoặc không parse được dữ liệu.");

        var botAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == "system_bot@unimap.vn");
        if (botAccount == null)
            return BadRequest("Không tìm thấy tài khoản Bot (system_bot@unimap.vn)!");

        var botHost = await _context.HostProfiles.FirstOrDefaultAsync(h => h.AccountId == botAccount.AccountId);
        var botEmployer = await _context.EmployerProfiles.FirstOrDefaultAsync(e => e.AccountId == botAccount.AccountId);
        if (botHost == null || botEmployer == null)
            return BadRequest("Thiếu HostProfile hoặc EmployerProfile của Bot!");

        var roomCategory = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryType == "Room");
        var jobCategory = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryType == "Job");
        if (roomCategory == null || jobCategory == null)
            return BadRequest("Thi?u category Room/Job trong CSDL!");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var details = new List<object>();

        int imported = 0;
        int importedRooms = 0;
        int importedJobs = 0;
        int skipped = 0;
        int skippedHcm = 0;
        int geocodeFailed = 0;

        foreach (var row in rows)
        {
            NormalizeSeedRow(row);

            var provinceSlug = ResolveProvinceSlug(row);
            var provinceName = ResolveProvinceName(row.Province, provinceSlug);

            if (excludeHcm && IsHcm(provinceName, provinceSlug))
            {
                skippedHcm++;
                continue;
            }

            var normalizedType = NormalizeType(row.Type);
            if (normalizedType == null)
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Title))
            {
                skipped++;
                continue;
            }

            var key = BuildFingerprint(normalizedType, row.Title, row.Address, row.SourceUrl, provinceName);
            if (!seen.Add(key))
            {
                skipped++;
                continue;
            }

            try
            {
                if (normalizedType == "Room")
                {
                    bool exists;
                    if (!string.IsNullOrWhiteSpace(row.SourceUrl))
                    {
                        exists = await _context.Rooms.AnyAsync(r => r.SourceUrl == row.SourceUrl);
                    }
                    else
                    {
                        exists = await _context.Rooms.AnyAsync(r => r.Title == row.Title);
                    }
                    if (exists) { skipped++; continue; }

                    var (lat, lng) = await ResolveCoordinates(row, normalizedType, provinceName, provinceSlug);
                    if (lat == 0 && lng == 0) geocodeFailed++;

                    var location = CreateLocation(row.Address, provinceName, lat, lng);
                    _context.Locations.Add(location);
                    await _context.SaveChangesAsync();

                    var room = new Room
                    {
                        HostId = botHost.HostId,
                        LocationId = location.LocationId,
                        CategoryId = roomCategory.CategoryId,
                        Title = TrimTo(row.Title, 255),
                        Price = ParsePrice(row.Price) > 0 ? ParsePrice(row.Price) : 3000000,
                        Area = 20,
                        RoomStatus = "Available",
                        IsExternal = true,
                        SourceUrl = row.SourceUrl
                    };
                    _context.Rooms.Add(room);
                    await _context.SaveChangesAsync();

                    if (!string.IsNullOrWhiteSpace(row.Thumbnail))
                    {
                        _context.Media.Add(new Medium
                        {
                            TargetType = "Room",
                            TargetId = room.RoomId,
                            MediaUrl = row.Thumbnail,
                            IsThumbnail = true
                        });
                        await _context.SaveChangesAsync();
                    }

                    imported++;
                    importedRooms++;
                    details.Add(new { type = "Room", title = room.Title, province = provinceName });
                }
                else
                {
                    bool exists;
                    if (!string.IsNullOrWhiteSpace(row.SourceUrl))
                    {
                        exists = await _context.Jobs.AnyAsync(j => j.SourceUrl == row.SourceUrl);
                    }
                    else
                    {
                        exists = await _context.Jobs.AnyAsync(j => j.JobTitle == row.Title);
                    }
                    if (exists) { skipped++; continue; }

                    var (lat, lng) = await ResolveCoordinates(row, normalizedType, provinceName, provinceSlug);
                    if (lat == 0 && lng == 0) geocodeFailed++;

                    var location = CreateLocation(row.Address, provinceName, lat, lng);
                    _context.Locations.Add(location);
                    await _context.SaveChangesAsync();

                    var salary = string.IsNullOrWhiteSpace(row.SalaryRange) ? "Thỏa thuận" : TrimTo(row.SalaryRange, 100);
                    var job = new Job
                    {
                        EmployerId = botEmployer.EmployerId,
                        LocationId = location.LocationId,
                        CategoryId = jobCategory.CategoryId,
                        JobTitle = TrimTo(row.Title, 255),
                        SalaryRange = salary,
                        JobType = "Part-time",
                        JobStatus = "Open",
                        IsExternal = true,
                        SourceUrl = row.SourceUrl
                    };
                    _context.Jobs.Add(job);
                    await _context.SaveChangesAsync();

                    if (!string.IsNullOrWhiteSpace(row.Thumbnail))
                    {
                        _context.Media.Add(new Medium
                        {
                            TargetType = "Job",
                            TargetId = job.JobId,
                            MediaUrl = row.Thumbnail,
                            IsThumbnail = true
                        });
                        await _context.SaveChangesAsync();
                    }

                    imported++;
                    importedJobs++;
                    details.Add(new { type = "Job", title = job.JobTitle, province = provinceName });
                }
            }
            catch (Exception ex)
            {
                skipped++;
                details.Add(new { title = row.Title, error = ex.Message });
            }
        }

        return Ok(new
        {
            message = $"Seed import hoàn tất: {imported} ({importedRooms} room, {importedJobs} job), skipped={skipped}, skippedHcm={skippedHcm}, geocodeFailed={geocodeFailed}",
            imported,
            importedRooms,
            importedJobs,
            skipped,
            skippedHcm,
            geocodeFailed,
            details
        });
    }

    private static List<SeedRow> ReadSeedCsv(string filePath)
    {
        var rows = new List<SeedRow>();
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            Delimiters = new[] { "," },
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };

        var headers = parser.ReadFields();
        if (headers == null) return rows;
        var map = headers
            .Select((h, i) => new { Key = (h ?? string.Empty).Trim(), Index = i })
            .ToDictionary(x => x.Key, x => x.Index, StringComparer.OrdinalIgnoreCase);

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields == null) continue;

            rows.Add(new SeedRow
            {
                Source = GetField(fields, map, "source"),
                Type = GetField(fields, map, "type"),
                Province = GetField(fields, map, "province"),
                Title = GetField(fields, map, "title"),
                Address = GetField(fields, map, "address"),
                Price = GetField(fields, map, "price"),
                SalaryRange = GetField(fields, map, "salaryRange"),
                Thumbnail = GetField(fields, map, "thumbnail"),
                SourceUrl = GetField(fields, map, "sourceUrl"),
                Description = GetField(fields, map, "description")
            });
        }

        return rows;
    }

    private static string GetField(string[] fields, Dictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var index)) return string.Empty;
        if (index < 0 || index >= fields.Length) return string.Empty;
        return fields[index]?.Trim() ?? string.Empty;
    }

    private static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        var t = type.Trim().ToLowerInvariant();
        if (t == "room") return "Room";
        if (t == "job") return "Job";
        return null;
    }

    private static bool IsHcm(string? province, string? slug = null)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var s = slug.Trim().ToLowerInvariant();
            if (s == "ho-chi-minh" || s == "tp-hcm" || s == "hcm")
                return true;
        }

        if (string.IsNullOrWhiteSpace(province)) return false;
        var p = province.ToLowerInvariant();
        return p.Contains("hồ chí minh") || p.Contains("ho chi minh") || p.Contains("tp hcm") || p.Contains("tp.hcm") || p.Contains("hcm");
    }

    private static string TrimTo(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLen ? value : value[..maxLen];
    }

    private static string BuildFingerprint(string type, string title, string address, string? sourceUrl, string province)
    {
        string normalize(string s) => Regex.Replace(s.Trim().ToLowerInvariant(), "\\s+", " ");
        var src = sourceUrl?.Split('?', 2)[0] ?? string.Empty;
        return $"{normalize(type)}|{normalize(title)}|{normalize(address)}|{normalize(province)}|{normalize(src)}";
    }

    private static decimal ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var val = text.ToLowerInvariant().Replace(" ", string.Empty);
        var m1 = Regex.Match(val, @"(\d+)[\.,]?(\d+)?(tr|trieu|triệu)");
        if (m1.Success)
        {
            var major = decimal.Parse(m1.Groups[1].Value, CultureInfo.InvariantCulture);
            var minor = m1.Groups[2].Success ? decimal.Parse("0." + m1.Groups[2].Value, CultureInfo.InvariantCulture) : 0m;
            return (major + minor) * 1000000m;
        }

        var m2 = Regex.Match(val, @"(\d+)(k|nghìn|nghin)");
        if (m2.Success)
        {
            return decimal.Parse(m2.Groups[1].Value, CultureInfo.InvariantCulture) * 1000m;
        }

        var digits = Regex.Replace(val, "[^0-9]", "");
        if (decimal.TryParse(digits, out var raw) && raw >= 500000) return raw;
        return 0;
    }

    private static void NormalizeSeedRow(SeedRow row)
    {
        row.Source = NormalizeText(row.Source);
        row.Type = NormalizeText(row.Type);
        row.Province = NormalizeText(row.Province);
        row.Title = NormalizeText(row.Title);
        row.Address = NormalizeText(row.Address);
        row.Price = NormalizeText(row.Price);
        row.SalaryRange = NormalizeText(row.SalaryRange);
        row.Thumbnail = NormalizeText(row.Thumbnail);
        row.SourceUrl = NormalizeText(row.SourceUrl);
        row.Description = NormalizeText(row.Description);
    }

    private static string ResolveProvinceName(string? province, string? slug)
    {
        if (!string.IsNullOrWhiteSpace(slug) && ProvinceNameBySlug.Value.TryGetValue(slug, out var canonicalName))
            return canonicalName;

        if (!string.IsNullOrWhiteSpace(province))
            return province;

        if (!string.IsNullOrWhiteSpace(slug))
            return slug.Replace('-', ' ');

        return "Việt Nam";
    }

    private static string ResolveProvinceSlug(SeedRow row)
    {
        var slug = ExtractSlug(row.SourceUrl).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(slug))
            return slug;

        return SlugifyVietnamese(row.Province);
    }

    private static Dictionary<string, string> LoadProvinceNameBySlug()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "tools", "national-seed", "provinces.exclude-hcm.json");
            if (System.IO.File.Exists(path))
            {
                var raw = System.IO.File.ReadAllText(path, Encoding.UTF8);
                var arr = JsonSerializer.Deserialize<List<ProvinceSeed>>(raw);
                if (arr != null)
                {
                    foreach (var item in arr)
                    {
                        if (string.IsNullOrWhiteSpace(item.Slug) || string.IsNullOrWhiteSpace(item.Name)) continue;
                        map[item.Slug.Trim().ToLowerInvariant()] = item.Name.Trim();
                    }
                }
            }
        }
        catch
        {
            // keep fallback map below
        }

        map["ho-chi-minh"] = "TP Hồ Chí Minh";
        return map;
    }

    private async Task<(double lat, double lng)> ResolveCoordinates(SeedRow row, string normalizedType, string provinceName, string provinceSlug)
    {
        if (IsPlaceholderSeed(row))
        {
            var provincePoint = DeterministicProvincePoint(provinceSlug, provinceName);
            return AddMarkerJitter(provincePoint, normalizedType, row.Title);
        }

        var (lat, lng) = await GeocodeAddress($"{row.Address}, {provinceName}, Việt Nam");
        if (lat != 0 || lng != 0)
            return (lat, lng);

        var provinceCoord = await ResolveProvinceCoordinates(provinceName, provinceSlug);
        if (provinceCoord.lat != 0 || provinceCoord.lng != 0)
            return AddMarkerJitter(provinceCoord, normalizedType, row.Title);

        return (0, 0);
    }

    private async Task<(double lat, double lng)> ResolveProvinceCoordinates(string provinceName, string provinceSlug)
    {
        var cacheKey = string.IsNullOrWhiteSpace(provinceSlug) ? provinceName.ToLowerInvariant() : provinceSlug;
        if (_provinceCoordinateCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var attempts = new List<string>();
        if (!string.IsNullOrWhiteSpace(provinceName))
            attempts.Add($"{provinceName}, Việt Nam");
        if (!string.IsNullOrWhiteSpace(provinceSlug))
            attempts.Add($"{provinceSlug.Replace('-', ' ')}, Việt Nam");

        foreach (var query in attempts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var coord = await GeocodeAddress(query);
            if (coord.lat != 0 || coord.lng != 0)
            {
                _provinceCoordinateCache[cacheKey] = coord;
                return coord;
            }
        }

        var fallback = FallbackVietnamPoint(provinceSlug, provinceName);
        _provinceCoordinateCache[cacheKey] = fallback;
        return fallback;
    }

    private static (double lat, double lng) DeterministicProvincePoint(string provinceSlug, string provinceName)
    {
        var normalizedSlug = string.IsNullOrWhiteSpace(provinceSlug) ? SlugifyVietnamese(provinceName) : provinceSlug;
        if (ProvinceCentroids.TryGetValue(normalizedSlug, out var centroid))
            return centroid;

        var fallback = FallbackVietnamPoint(normalizedSlug, provinceName);
        return (Math.Clamp(fallback.lat, 8.5, 23.3), Math.Clamp(fallback.lng, 102.5, 109.5));
    }

    private static bool IsPlaceholderSeed(SeedRow row)
    {
        return row.Source.Equals("seed-placeholder", StringComparison.OrdinalIgnoreCase)
            || row.SourceUrl.StartsWith("https://seed.unimap360.local/", StringComparison.OrdinalIgnoreCase);
    }

    private static (double lat, double lng) AddMarkerJitter((double lat, double lng) basePoint, string normalizedType, string title)
    {
        var hash = HashCode.Combine(normalizedType, title);
        var rnd = new Random(hash);
        var lat = basePoint.lat + (rnd.NextDouble() - 0.5) * 0.05;
        var lng = basePoint.lng + (rnd.NextDouble() - 0.5) * 0.05;
        return (lat, lng);
    }

    private static string NormalizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var value = Regex.Replace(input.Trim(), "\\s+", " ");
        var fixedValue = TryFixMojibake(value);
        return fixedValue;
    }

    private static string TryFixMojibake(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var candidate = value;
        try
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            var repaired = Encoding.UTF8.GetString(bytes);
            if (MojibakeScore(repaired) < MojibakeScore(candidate))
                candidate = repaired;
        }
        catch
        {
            // keep original value
        }

        return candidate;
    }

    private static int MojibakeScore(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        return value.Count(ch => ch == '\uFFFD' || ch == '?');
    }

    private static string SlugifyVietnamese(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var text = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            var normalized = ch switch
            {
                'd' => 'd',
                _ => ch
            };

            if ((normalized >= 'a' && normalized <= 'z') || (normalized >= '0' && normalized <= '9'))
                sb.Append(normalized);
            else if (normalized == ' ' || normalized == '-' || normalized == '_')
                sb.Append('-');
        }

        var slug = Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return slug;
    }

    private static string ExtractSlug(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)) return string.Empty;
        var trimmed = sourceUrl.Trim().TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        if (idx < 0 || idx == trimmed.Length - 1) return string.Empty;
        return trimmed[(idx + 1)..];
    }

    private Location CreateLocation(string address, string province, double lat, double lng)
    {
        var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        return new Location
        {
            AddressText = string.IsNullOrWhiteSpace(address) ? province : address,
            Coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(lng, lat)),
            District = province
        };
    }

    private async Task<(double lat, double lng)> GeocodeAddress(string address)
    {
        try
        {
            await Task.Delay(1100);
            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(address)}&format=json&limit=1&countrycodes=vn";
            var response = await _httpClient.GetStringAsync(url);
            var arr = JsonSerializer.Deserialize<JsonElement[]>(response);
            if (arr != null && arr.Length > 0)
            {
                var lat = double.Parse(arr[0].GetProperty("lat").GetString()!);
                var lng = double.Parse(arr[0].GetProperty("lon").GetString()!);
                return (lat, lng);
            }
        }
        catch
        {
            // swallow and fallback
        }

        return (0, 0);
    }

    private static (double lat, double lng) FallbackVietnamPoint(string slug, string provinceName)
    {
        var normalizedSlug = string.IsNullOrWhiteSpace(slug) ? SlugifyVietnamese(provinceName) : slug;
        var rnd = new Random(HashCode.Combine(normalizedSlug, provinceName));

        // Region anchors in Vietnam to avoid random spill to neighboring countries.
        (double lat, double lng) anchor;
        if (IsNorthernProvince(normalizedSlug))
            anchor = (21.0, 105.8);
        else if (IsCentralProvince(normalizedSlug))
            anchor = (16.2, 107.9);
        else
            anchor = (10.8, 106.9);

        var lat = anchor.lat + (rnd.NextDouble() - 0.5) * 0.8;
        var lng = anchor.lng + (rnd.NextDouble() - 0.5) * 0.8;
        return (lat, lng);
    }

    private static bool IsNorthernProvince(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return false;
        var north = new[]
        {
            "ha-noi","hai-phong","bac-giang","bac-kan","bac-ninh","cao-bang","dien-bien","ha-giang","ha-nam",
            "hai-duong","hoa-binh","hung-yen","lai-chau","lang-son","lao-cai","nam-dinh","ninh-binh","phu-tho",
            "quang-ninh","son-la","thai-binh","thai-nguyen","tuyen-quang","vinh-phuc","yen-bai"
        };
        return north.Contains(slug);
    }

    private static bool IsCentralProvince(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return false;
        var central = new[]
        {
            "da-nang","binh-dinh","binh-thuan","dak-lak","dak-nong","gia-lai","ha-tinh","khanh-hoa","kon-tum",
            "lam-dong","nghe-an","ninh-thuan","phu-yen","quang-binh","quang-nam","quang-ngai","quang-tri",
            "thanh-hoa","thua-thien-hue"
        };
        return central.Contains(slug);
    }

    private class SeedRow
    {
        public string Source { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
        public string SalaryRange { get; set; } = string.Empty;
        public string Thumbnail { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private class ProvinceSeed
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }
}
