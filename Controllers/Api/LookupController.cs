using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using UniMap360.Models;

namespace UniMap360.Controllers.Api;

[Route("api")]
[ApiController]
public class LookupController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IHttpClientFactory _httpClientFactory;

    public LookupController(UniMap360ProContext context, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
    }

    [AllowAnonymous]
    [HttpGet("locations/provinces")]
    public async Task<IActionResult> GetProvinces()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var req = new HttpRequestMessage(HttpMethod.Get, "https://provinces.open-api.vn/api/p/");
            req.Headers.UserAgent.ParseAdd("UniMap360/1.0");

            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, "Không tải được danh sách tỉnh/thành.");

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var items = new List<object>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var code = item.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : 0;
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (code <= 0 || string.IsNullOrWhiteSpace(name)) continue;

                items.Add(new { code = code.ToString(), name, displayName = name });
            }

            return Ok(new { total = items.Count, items });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Không tải được danh sách tỉnh/thành: {ex.Message}");
        }
    }

    [AllowAnonymous]
    [HttpGet("locations/districts")]
    public async Task<IActionResult> GetDistricts([FromQuery] string? provinceCode = null)
    {
        if (!int.TryParse(provinceCode, out var code) || code <= 0)
            return BadRequest("provinceCode không hợp lệ.");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://provinces.open-api.vn/api/p/{code}?depth=2");
            req.Headers.UserAgent.ParseAdd("UniMap360/1.0");

            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, "Không tải được danh sách quận/huyện.");

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("districts", out var districtsEl))
                return Ok(new { total = 0, items = Array.Empty<object>() });

            var items = new List<object>();
            foreach (var item in districtsEl.EnumerateArray())
            {
                var districtCode = item.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : 0;
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (districtCode <= 0 || string.IsNullOrWhiteSpace(name)) continue;

                items.Add(new { code = districtCode.ToString(), name, displayName = name });
            }

            return Ok(new { total = items.Count, items });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Không tải được danh sách quận/huyện: {ex.Message}");
        }
    }

    [AllowAnonymous]
    [HttpGet("locations/wards")]
    public async Task<IActionResult> GetWards([FromQuery] string? districtCode = null)
    {
        if (!int.TryParse(districtCode, out var code) || code <= 0)
            return BadRequest("districtCode không hợp lệ.");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://provinces.open-api.vn/api/d/{code}?depth=2");
            req.Headers.UserAgent.ParseAdd("UniMap360/1.0");

            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, "Không tải được danh sách phường/xã.");

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("wards", out var wardsEl))
                return Ok(new { total = 0, items = Array.Empty<object>() });

            var items = new List<object>();
            foreach (var item in wardsEl.EnumerateArray())
            {
                var wardCode = item.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : 0;
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (wardCode <= 0 || string.IsNullOrWhiteSpace(name)) continue;

                items.Add(new { code = wardCode.ToString(), name, displayName = name });
            }

            return Ok(new { total = items.Count, items });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Không tải được danh sách phường/xã: {ex.Message}");
        }
    }

    [AllowAnonymous]
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories([FromQuery] string? type = null)
    {
        var query = _context.Categories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalized = type.Trim().ToLowerInvariant();
            if (normalized == "room" || normalized == "job")
            {
                query = query.Where(c => c.CategoryType != null && c.CategoryType.ToLower() == normalized);
            }
        }

        var items = await query
            .OrderBy(c => c.CategoryName)
            .Select(c => new
            {
                c.CategoryId,
                c.CategoryName,
                c.CategoryType
            })
            .ToListAsync();

        return Ok(new { total = items.Count, items });
    }

    [AllowAnonymous]
    [HttpGet("locations")]
    public async Task<IActionResult> GetLocations(
        [FromQuery] string? keyword = null,
        [FromQuery] int take = 200,
        [FromQuery] string? groupBy = null,
        [FromQuery] string? provinceName = null,
        [FromQuery] string? districtName = null,
        [FromQuery] string? wardName = null)
    {
        take = Math.Clamp(take, 1, 500);
        var groupByProvince = string.Equals(groupBy, "province", StringComparison.OrdinalIgnoreCase);

        var query = _context.Locations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(l => l.AddressText.Contains(key) || (l.District != null && l.District.Contains(key)));
        }

        if (!string.IsNullOrWhiteSpace(provinceName))
        {
            var key = provinceName.Trim();
            query = query.Where(l => l.ProvinceName != null && l.ProvinceName.Contains(key));
        }

        if (!string.IsNullOrWhiteSpace(districtName))
        {
            var key = districtName.Trim();
            query = query.Where(l => l.DistrictName != null && l.DistrictName.Contains(key));
        }

        if (!string.IsNullOrWhiteSpace(wardName))
        {
            var key = wardName.Trim();
            query = query.Where(l => l.WardName != null && l.WardName.Contains(key));
        }

        var items = await query
            .OrderBy(l => l.District)
            .ThenBy(l => l.AddressText)
            .Take(groupByProvince ? 5000 : take)
            .Select(l => new
            {
                l.LocationId,
                l.AddressText,
                l.District,
                l.ProvinceCode,
                l.ProvinceName,
                l.DistrictCode,
                l.DistrictName,
                l.WardCode,
                l.WardName,
                displayName = string.IsNullOrWhiteSpace(l.District)
                    ? l.AddressText
                    : l.District + " - " + l.AddressText
            })
            .ToListAsync();

        var normalizedItems = items.Select(l =>
        {
            var addressText = FixPotentialMojibake(l.AddressText);
            var district = FixPotentialMojibake(l.District);
            var displayName = string.IsNullOrWhiteSpace(district)
                ? addressText
                : district + " - " + addressText;

            return new
            {
                l.LocationId,
                AddressText = addressText,
                District = district,
                l.ProvinceCode,
                l.ProvinceName,
                l.DistrictCode,
                l.DistrictName,
                l.WardCode,
                l.WardName,
                displayName
            };
        }).ToList();

        if (groupByProvince)
        {
            var canonicalProvinces = GetVietnamProvinceNames();

            var provinceItems = normalizedItems
                .Select(l =>
                {
                    var rawProvince = ExtractProvinceName(l.District, l.AddressText);
                    var provinceName = ResolveCanonicalProvinceName(rawProvince, canonicalProvinces);
                    return new
                    {
                        l.LocationId,
                        ProvinceName = provinceName,
                        l.AddressText
                    };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.ProvinceName))
                .GroupBy(x => x.ProvinceName!, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var representative = g.OrderBy(x => x.LocationId).First();
                    return new
                    {
                        representative.LocationId,
                        AddressText = representative.ProvinceName,
                        District = representative.ProvinceName,
                        displayName = representative.ProvinceName
                    };
                })
                .OrderBy(x => x.displayName)
                .Take(take)
                .ToList();

            return Ok(new { total = provinceItems.Count, items = provinceItems });
        }

        return Ok(new { total = normalizedItems.Count, items = normalizedItems });
    }

    private static string ExtractProvinceName(string? district, string? addressText)
    {
        if (!string.IsNullOrWhiteSpace(district))
            return district.Trim();

        if (string.IsNullOrWhiteSpace(addressText))
            return string.Empty;

        var parts = addressText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parts.Count == 0)
            return addressText.Trim();

        // Common format: "... , Province, Viet Nam" -> take the part before country.
        var last = parts[^1].ToLowerInvariant();
        if ((last == "viet nam" || last == "vietnam") && parts.Count >= 2)
            return parts[^2];

        return parts[^1];
    }

    private static string? ResolveCanonicalProvinceName(string? rawProvince, IReadOnlyList<string> canonicalProvinces)
    {
        if (string.IsNullOrWhiteSpace(rawProvince)) return null;

        var key = NormalizeProvinceKey(rawProvince);
        if (string.IsNullOrWhiteSpace(key)) return null;

        foreach (var province in canonicalProvinces)
        {
            var pKey = NormalizeProvinceKey(province);
            if (key == pKey || key.Contains(pKey) || pKey.Contains(key))
                return province;
        }

        return null;
    }

    private static string NormalizeProvinceKey(string value)
    {
        var text = value.ToLowerInvariant()
            .Replace("đ", "d")
            .Replace("ð", "d")
            .Replace("?", string.Empty)
            .Replace("tp.", "thanh pho ")
            .Replace("tp ", "thanh pho ");

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                sb.Append(ch);
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlyList<string> GetVietnamProvinceNames()
    {
        return new[]
        {
            "An Giang", "Bà Rịa - Vũng Tàu", "Bắc Giang", "Bắc Kạn", "Bạc Liêu", "Bắc Ninh", "Bến Tre",
            "Bình Định", "Bình Dương", "Bình Phước", "Bình Thuận", "Cà Mau", "Cần Thơ", "Cao Bằng",
            "Đà Nẵng", "Đắk Lắk", "Đắk Nông", "Điện Biên", "Đồng Nai", "Đồng Tháp", "Gia Lai", "Hà Giang",
            "Hà Nam", "Hà Nội", "Hà Tĩnh", "Hải Dương", "Hải Phòng", "Hậu Giang", "Hòa Bình", "Hưng Yên",
            "Khánh Hòa", "Kiên Giang", "Kon Tum", "Lai Châu", "Lâm Đồng", "Lạng Sơn", "Lào Cai", "Long An",
            "Nam Định", "Nghệ An", "Ninh Bình", "Ninh Thuận", "Phú Thọ", "Phú Yên", "Quảng Bình",
            "Quảng Nam", "Quảng Ngãi", "Quảng Ninh", "Quảng Trị", "Sóc Trăng", "Sơn La", "Tây Ninh",
            "Thái Bình", "Thái Nguyên", "Thanh Hóa", "Thừa Thiên Huế", "Tiền Giang", "TP Hồ Chí Minh",
            "Trà Vinh", "Tuyên Quang", "Vĩnh Long", "Vĩnh Phúc", "Yên Bái"
        };
    }

    private static string? FixPotentialMojibake(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        if (!LooksLikeMojibake(input))
            return input;

        try
        {
            var latin1Bytes = Encoding.GetEncoding(1252).GetBytes(input);
            var utf8 = Encoding.UTF8.GetString(latin1Bytes);
            return string.IsNullOrWhiteSpace(utf8) ? input : utf8;
        }
        catch
        {
            return input;
        }
    }

    private static bool LooksLikeMojibake(string value)
    {
        // Common markers when UTF-8 Vietnamese text was decoded using legacy code pages.
        return value.Contains('\uFFFD')
            || value.Contains("Ã")
            || value.Contains("Ä")
            || value.Contains("áº")
            || value.Contains("á»")
            || value.Contains("â€™")
            || value.Contains("â€œ")
            || value.Contains("â€");
    }
}
