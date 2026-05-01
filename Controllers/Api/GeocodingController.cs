using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace UniMap360.Controllers.Api;

[Route("api/geocoding")]
[ApiController]
public class GeocodingController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GeocodingController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [AllowAnonymous]
    [HttpPost("forward")]
    public async Task<IActionResult> Forward([FromBody] ForwardGeocodingRequest request)
    {
        if (request is null)
            return BadRequest("Dữ liệu geocoding không hợp lệ.");

        var primaryQuery = BuildAddressQuery(request);
        if (string.IsNullOrWhiteSpace(primaryQuery))
            return BadRequest("Địa chỉ geocoding đang trống.");

        try
        {
            var candidates = new List<GeocodingCandidate>();
            var queries = BuildFallbackQueries(request, primaryQuery);

            foreach (var query in queries)
            {
                var found = await SearchNominatimAsync(query, request);
                if (found.Count <= 0) continue;

                candidates.AddRange(found);
                break;
            }

            var ordered = candidates
                .OrderByDescending(x => x.Confidence)
                .ToList();

            var best = ordered.FirstOrDefault();

            return Ok(new
            {
                query = primaryQuery,
                total = ordered.Count,
                bestCandidate = best,
                candidates = ordered
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Không thể geocode địa chỉ: {ex.Message}");
        }
    }

    [AllowAnonymous]
    [HttpPost("reverse")]
    public async Task<IActionResult> Reverse([FromBody] ReverseGeocodingRequest request)
    {
        if (request is null)
            return BadRequest("Dữ liệu reverse geocoding không hợp lệ.");

        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return BadRequest("Tọa độ reverse geocoding không hợp lệ.");

        try
        {
            var resolved = await ReverseNominatimAsync(request.Latitude, request.Longitude);
            if (resolved is null)
            {
                return Ok(new
                {
                    latitude = request.Latitude,
                    longitude = request.Longitude,
                    found = false
                });
            }

            var matchResult = ComputeAdministrativeMatch(resolved, request);

            return Ok(new
            {
                latitude = request.Latitude,
                longitude = request.Longitude,
                found = true,
                displayName = resolved.DisplayName,
                resolvedProvince = resolved.Province,
                resolvedDistrict = resolved.District,
                resolvedWard = resolved.Ward,
                isAdministrativeMatch = matchResult.IsMatch,
                confidence = matchResult.Confidence
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Không thể reverse geocode tọa độ: {ex.Message}");
        }
    }

    private async Task<List<GeocodingCandidate>> SearchNominatimAsync(string query, ForwardGeocodingRequest request)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(25);
        var endpoint = "https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=5&q=" + Uri.EscapeDataString(query);
        var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.UserAgent.ParseAdd("UniMap360/1.0 (contact: support@unimap360.local)");

        using var resp = await client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return new List<GeocodingCandidate>();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return new List<GeocodingCandidate>();

        var candidates = new List<GeocodingCandidate>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var latStr = item.TryGetProperty("lat", out var latEl) ? latEl.GetString() : null;
            var lonStr = item.TryGetProperty("lon", out var lonEl) ? lonEl.GetString() : null;
            var display = item.TryGetProperty("display_name", out var displayEl) ? displayEl.GetString() : null;

            if (!double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                continue;
            if (!double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                continue;

            var confidence = ComputeConfidence(display, request);
            candidates.Add(new GeocodingCandidate
            {
                Latitude = lat,
                Longitude = lng,
                DisplayName = display,
                Confidence = confidence,
                IsAdministrativeMatch = confidence >= 0.6
            });
        }

        return candidates;
    }

    private async Task<ReverseGeocodingResolved?> ReverseNominatimAsync(double latitude, double longitude)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        var endpoint = string.Format(
            CultureInfo.InvariantCulture,
            "https://nominatim.openstreetmap.org/reverse?format=jsonv2&addressdetails=1&zoom=18&lat={0}&lon={1}",
            latitude,
            longitude);

        var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.UserAgent.ParseAdd("UniMap360/1.0 (contact: support@unimap360.local)");

        using var resp = await client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var displayName = root.TryGetProperty("display_name", out var displayEl)
            ? displayEl.GetString()
            : null;

        string? state = null;
        string? city = null;
        string? county = null;
        string? cityDistrict = null;
        string? suburb = null;
        string? quarter = null;
        string? village = null;
        string? town = null;

        if (root.TryGetProperty("address", out var addressEl) && addressEl.ValueKind == JsonValueKind.Object)
        {
            state = ReadString(addressEl, "state");
            city = ReadString(addressEl, "city");
            county = ReadString(addressEl, "county");
            cityDistrict = ReadString(addressEl, "city_district");
            suburb = ReadString(addressEl, "suburb");
            quarter = ReadString(addressEl, "quarter");
            village = ReadString(addressEl, "village");
            town = ReadString(addressEl, "town");
        }

        return new ReverseGeocodingResolved
        {
            DisplayName = displayName,
            Province = state ?? city,
            District = cityDistrict ?? county ?? city,
            Ward = suburb ?? quarter ?? village ?? town
        };
    }

    private static IReadOnlyList<string> BuildFallbackQueries(ForwardGeocodingRequest request, string primaryQuery)
    {
        var queries = new List<string> { primaryQuery };

        var districtProvince = string.Join(", ", new[]
        {
            request.WardName,
            request.DistrictName,
            request.ProvinceName,
            "Việt Nam"
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        var provinceOnly = string.Join(", ", new[]
        {
            request.ProvinceName,
            "Việt Nam"
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        if (!string.IsNullOrWhiteSpace(districtProvince))
            queries.Add(districtProvince);

        if (!string.IsNullOrWhiteSpace(provinceOnly))
            queries.Add(provinceOnly);

        return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildAddressQuery(ForwardGeocodingRequest request)
    {
        var parts = new List<string>();

        var houseStreet = string.Join(' ', new[]
        {
            request.HouseNumber?.Trim(),
            request.Street?.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        if (!string.IsNullOrWhiteSpace(houseStreet))
            parts.Add(houseStreet);

        if (!string.IsNullOrWhiteSpace(request.WardName))
            parts.Add(request.WardName.Trim());
        if (!string.IsNullOrWhiteSpace(request.DistrictName))
            parts.Add(request.DistrictName.Trim());
        if (!string.IsNullOrWhiteSpace(request.ProvinceName))
            parts.Add(request.ProvinceName.Trim());

        parts.Add("Việt Nam");
        return string.Join(", ", parts);
    }

    private static AdministrativeMatchResult ComputeAdministrativeMatch(ReverseGeocodingResolved resolved, ReverseGeocodingRequest expected)
    {
        var score = 0.2;
        if (NameMatches(expected.ProvinceName, resolved.Province)) score += 0.45;
        if (NameMatches(expected.DistrictName, resolved.District)) score += 0.3;
        if (NameMatches(expected.WardName, resolved.Ward)) score += 0.25;

        var confidence = Math.Min(1.0, Math.Round(score, 3));
        return new AdministrativeMatchResult
        {
            Confidence = confidence,
            IsMatch = confidence >= 0.7
        };
    }

    private static bool NameMatches(string? expectedName, string? resolvedName)
    {
        var left = NormalizeAdministrativeName(expectedName);
        var right = NormalizeAdministrativeName(resolvedName);

        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return left == right || left.Contains(right) || right.Contains(left);
    }

    private static string NormalizeAdministrativeName(string? value)
    {
        var normalized = NormalizeText(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var words = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("tinh" or "thanh" or "pho" or "tp" or "quan" or "huyen" or "thi" or "xa" or "phuong" or "xa" or "thi" or "tran"))
            .ToArray();

        return string.Join(' ', words);
    }

    private static double ComputeConfidence(string? displayName, ForwardGeocodingRequest request)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return 0.0;

        var normalizedDisplay = NormalizeText(displayName);
        var score = 0.2;

        if (!string.IsNullOrWhiteSpace(request.ProvinceName) && normalizedDisplay.Contains(NormalizeText(request.ProvinceName)))
            score += 0.35;
        if (!string.IsNullOrWhiteSpace(request.DistrictName) && normalizedDisplay.Contains(NormalizeText(request.DistrictName)))
            score += 0.3;
        if (!string.IsNullOrWhiteSpace(request.WardName) && normalizedDisplay.Contains(NormalizeText(request.WardName)))
            score += 0.15;

        if (!string.IsNullOrWhiteSpace(request.Street) && normalizedDisplay.Contains(NormalizeText(request.Street)))
            score += 0.1;
        if (!string.IsNullOrWhiteSpace(request.HouseNumber) && normalizedDisplay.Contains(NormalizeText(request.HouseNumber)))
            score += 0.1;

        return Math.Min(1.0, Math.Round(score, 3));
    }

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value.ToLowerInvariant().Replace("đ", "d").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
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

    private static string? ReadString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }

    public sealed class ForwardGeocodingRequest
    {
        public string? ProvinceName { get; set; }
        public string? DistrictName { get; set; }
        public string? WardName { get; set; }
        public string? HouseNumber { get; set; }
        public string? Street { get; set; }
    }

    public sealed class ReverseGeocodingRequest
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ProvinceName { get; set; }
        public string? DistrictName { get; set; }
        public string? WardName { get; set; }
    }

    private sealed class GeocodingCandidate
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? DisplayName { get; set; }
        public double Confidence { get; set; }
        public bool IsAdministrativeMatch { get; set; }
    }

    private sealed class ReverseGeocodingResolved
    {
        public string? DisplayName { get; set; }
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? Ward { get; set; }
    }

    private sealed class AdministrativeMatchResult
    {
        public bool IsMatch { get; set; }
        public double Confidence { get; set; }
    }
}
