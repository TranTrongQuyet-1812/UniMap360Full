using System.Text;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using UniMap360.Models;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace UniMap360.Services.Posts;

public sealed class LocationResolveInput
{
    public int LocationId { get; set; }
    public int? CurrentLocationId { get; set; }
    public string? ProvinceCode { get; set; }
    public string? ProvinceName { get; set; }
    public string? DistrictCode { get; set; }
    public string? DistrictName { get; set; }
    public string? WardCode { get; set; }
    public string? WardName { get; set; }
    public string? HouseNumber { get; set; }
    public string? Street { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? GeocodedLatitude { get; set; }
    public double? GeocodedLongitude { get; set; }
    public bool UseProvinceCodeCanonicalization { get; set; }
    public string MissingPinMessage { get; set; } = "Vui lòng ghim vị trí bản đồ trước khi tiếp tục.";
}

public interface ILocationResolutionService
{
    Task<(int? LocationId, string? Error)> ResolveLocationIdAsync(LocationResolveInput request, CancellationToken cancellationToken = default);
}

public sealed class LocationResolutionService : ILocationResolutionService
{
    private readonly UniMap360ProContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LocationResolutionService> _logger;

    private static readonly IReadOnlyDictionary<string, string> ProvinceNameByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "Hà Nội", ["79"] = "TP Hồ Chí Minh", ["48"] = "Đà Nẵng", ["31"] = "Hải Phòng", ["92"] = "Cần Thơ",
        ["95"] = "Bạc Liêu", ["24"] = "Bắc Giang", ["06"] = "Bắc Kạn", ["27"] = "Bắc Ninh", ["83"] = "Bến Tre",
        ["74"] = "Bình Dương", ["52"] = "Bình Định", ["70"] = "Bình Phước", ["60"] = "Bình Thuận", ["04"] = "Cao Bằng",
        ["66"] = "Đắk Lắk", ["67"] = "Đắk Nông", ["11"] = "Điện Biên", ["75"] = "Đồng Nai", ["87"] = "Đồng Tháp",
        ["64"] = "Gia Lai", ["02"] = "Hà Giang", ["35"] = "Hà Nam", ["42"] = "Hà Tĩnh", ["30"] = "Hải Dương",
        ["93"] = "Hậu Giang", ["17"] = "Hòa Bình", ["33"] = "Hưng Yên", ["56"] = "Khánh Hòa", ["91"] = "Kiên Giang",
        ["62"] = "Kon Tum", ["12"] = "Lai Châu", ["68"] = "Lâm Đồng", ["20"] = "Lạng Sơn", ["10"] = "Lào Cai",
        ["80"] = "Long An", ["36"] = "Nam Định", ["40"] = "Nghệ An", ["37"] = "Ninh Bình", ["58"] = "Ninh Thuận",
        ["25"] = "Phú Thọ", ["54"] = "Phú Yên", ["44"] = "Quảng Bình", ["49"] = "Quảng Nam", ["51"] = "Quảng Ngãi",
        ["22"] = "Quảng Ninh", ["45"] = "Quảng Trị", ["94"] = "Sóc Trăng", ["14"] = "Sơn La", ["72"] = "Tây Ninh",
        ["34"] = "Thái Bình", ["19"] = "Thái Nguyên", ["38"] = "Thanh Hóa", ["46"] = "Thừa Thiên Huế", ["82"] = "Tiền Giang",
        ["84"] = "Trà Vinh", ["08"] = "Tuyên Quang", ["86"] = "Vĩnh Long", ["26"] = "Vĩnh Phúc", ["15"] = "Yên Bái",
        ["77"] = "Bà Rịa - Vũng Tàu", ["89"] = "An Giang", ["96"] = "Cà Mau"
    };

    public LocationResolutionService(
        UniMap360ProContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<LocationResolutionService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<(int? LocationId, string? Error)> ResolveLocationIdAsync(LocationResolveInput request, CancellationToken cancellationToken = default)
    {
        var provinceName = request.UseProvinceCodeCanonicalization
            ? CanonicalizeProvinceName(request.ProvinceCode, request.ProvinceName)
            : TrimOrNull(request.ProvinceName);

        var addressText = BuildAddressText(request, provinceName);
        var fullAddressNormalized = BuildFullAddressNormalized(addressText);

        if (request.LocationId > 0)
        {
            // Chỉ cho phép giữ LocationId cũ khi nó trùng khớp với CurrentLocationId của bài đăng
            if (!request.CurrentLocationId.HasValue || request.LocationId != request.CurrentLocationId.Value)
            {
                request.LocationId = 0;
            }
            else
            {
                var existingLocation = await _context.Locations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.LocationId == request.LocationId, cancellationToken);

                if (existingLocation == null)
                {
                    return (null, "Location không tồn tại.");
                }

                var hasLocationData = request.Latitude.HasValue ||
                                       request.Longitude.HasValue ||
                                       !string.IsNullOrWhiteSpace(request.ProvinceName) ||
                                       !string.IsNullOrWhiteSpace(request.DistrictName) ||
                                       !string.IsNullOrWhiteSpace(request.WardName) ||
                                       !string.IsNullOrWhiteSpace(request.Street) ||
                                       !string.IsNullOrWhiteSpace(request.HouseNumber);

                if (!hasLocationData)
                {
                    // Không có thông tin vị trí mới nào được gửi lên, chấp nhận giữ nguyên Location cũ
                    return (request.LocationId, null);
                }

                var coordsMatch = false;
                if (request.Latitude.HasValue && request.Longitude.HasValue && existingLocation.Coordinates is Point p)
                {
                    var dist = LocationTrustCalculator.CalculateDistanceMeters(p.Y, p.X, request.Latitude.Value, request.Longitude.Value);
                    coordsMatch = dist <= 10.0;
                }

                var addressMatch = string.Equals(existingLocation.FullAddressNormalized, fullAddressNormalized, StringComparison.OrdinalIgnoreCase);

                if (coordsMatch && addressMatch)
                {
                    return (request.LocationId, null);
                }

                // Force recalculation if coordinate/address details have changed
                request.LocationId = 0;
            }
        }

        if (string.IsNullOrWhiteSpace(request.ProvinceName) || string.IsNullOrWhiteSpace(request.DistrictName))
        {
            return (null, "Khi không truyền LocationId, bạn phải chọn đầy đủ Tỉnh/Thành và Quận/Huyện.");
        }

        if (!request.Latitude.HasValue || !request.Longitude.HasValue)
        {
            return (null, request.MissingPinMessage);
        }

        if (request.Latitude.Value is < -90 or > 90 || request.Longitude.Value is < -180 or > 180)
        {
            return (null, "Tọa độ bản đồ không hợp lệ.");
        }

        var existingLocations = await _context.Locations
            .AsNoTracking()
            .Where(l => l.FullAddressNormalized == fullAddressNormalized)
            .ToListAsync(cancellationToken);

        var matchingLocation = existingLocations.FirstOrDefault(l =>
        {
            if (!HasLocationTrustMetadata(l)) return false;

            var p = l.Coordinates as Point;
            if (p == null) return false;
            var dist = LocationTrustCalculator.CalculateDistanceMeters(p.Y, p.X, request.Latitude.Value, request.Longitude.Value);
            return dist <= 10.0;
        });

        if (matchingLocation != null)
        {
            return (matchingLocation.LocationId, null);
        }

        double? geocodedLat = null;
        double? geocodedLng = null;
        string geocodeSource = "None";

        var existingServerGeocoded = existingLocations.FirstOrDefault(l =>
            l.GeocodeSource == "Server" && l.GeocodedLatitude.HasValue && l.GeocodedLongitude.HasValue);

        if (existingServerGeocoded != null)
        {
            geocodedLat = existingServerGeocoded.GeocodedLatitude;
            geocodedLng = existingServerGeocoded.GeocodedLongitude;
            geocodeSource = "Server";
        }
        else
        {
            var backendGeocoded = await GeocodeOnBackendAsync(request, cancellationToken);
            if (backendGeocoded.Latitude.HasValue && backendGeocoded.Longitude.HasValue)
            {
                geocodedLat = backendGeocoded.Latitude;
                geocodedLng = backendGeocoded.Longitude;
                geocodeSource = "Server";
            }
            else
            {
                if (request.GeocodedLatitude.HasValue && request.GeocodedLongitude.HasValue)
                {
                    geocodedLat = request.GeocodedLatitude;
                    geocodedLng = request.GeocodedLongitude;
                    geocodeSource = "ClientFallback";
                }
            }
        }

        double? distanceMeters = null;
        string? confidence = null;
        bool? isSuspicious = null;

        if (geocodedLat.HasValue && geocodedLng.HasValue)
        {
            distanceMeters = LocationTrustCalculator.CalculateDistanceMeters(
                geocodedLat.Value, geocodedLng.Value,
                request.Latitude.Value, request.Longitude.Value);

            if (geocodeSource == "Server")
            {
                var trust = LocationTrustCalculator.DetermineConfidence(distanceMeters.Value);
                confidence = trust.Confidence;
                isSuspicious = trust.IsSuspicious;
            }
            else
            {
                confidence = "Unverified";
                isSuspicious = true;
            }
        }
        else
        {
            confidence = "Unverified";
            isSuspicious = true;
            distanceMeters = null;
        }

        var location = new UniMap360.Models.Location
        {
            AddressText = addressText,
            Coordinates = new Point(request.Longitude.Value, request.Latitude.Value) { SRID = 4326 },
            District = request.DistrictName?.Trim(),
            ProvinceCode = TrimOrNull(request.ProvinceCode),
            ProvinceName = provinceName,
            DistrictCode = TrimOrNull(request.DistrictCode),
            DistrictName = TrimOrNull(request.DistrictName),
            WardCode = TrimOrNull(request.WardCode),
            WardName = TrimOrNull(request.WardName),
            HouseNumber = TrimOrNull(request.HouseNumber),
            Street = TrimOrNull(request.Street),
            FullAddressNormalized = fullAddressNormalized,
            GeocodedLatitude = geocodedLat,
            GeocodedLongitude = geocodedLng,
            LocationDistanceMeters = distanceMeters,
            LocationConfidence = confidence,
            LocationSuspicious = isSuspicious,
            GeocodeSource = geocodeSource
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync(cancellationToken);
        return (location.LocationId, null);
    }

    private async Task<(double? Latitude, double? Longitude)> GeocodeOnBackendAsync(LocationResolveInput request, CancellationToken cancellationToken)
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
        var query = string.Join(", ", parts);

        // Che giấu số nhà (HouseNumber) trong chuỗi log để bảo vệ thông tin nhạy cảm
        var logQuery = query;
        if (!string.IsNullOrWhiteSpace(request.HouseNumber))
        {
            logQuery = query.Replace(request.HouseNumber, "***");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("NominatimGeocoding");
            var endpoint = "https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=1&q=" + Uri.EscapeDataString(query);
            var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            req.Headers.UserAgent.ParseAdd("UniMap360/1.0 (contact: support@unimap360.local)");

            using var resp = await client.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nominatim backend geocoding failed with status code {StatusCode} for query: {Query}", resp.StatusCode, logQuery);
                return (null, null);
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, default, cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                _logger.LogWarning("Nominatim backend geocoding returned no candidate arrays for query: {Query}", logQuery);
                return (null, null);
            }

            var first = doc.RootElement[0];
            var latStr = first.TryGetProperty("lat", out var latEl) ? latEl.GetString() : null;
            var lonStr = first.TryGetProperty("lon", out var lonEl) ? lonEl.GetString() : null;

            if (double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
            {
                return (lat, lng);
            }

            _logger.LogWarning("Nominatim backend geocoding returned invalid lat/lon values (lat={LatStr}, lon={LonStr}) for query: {Query}", latStr, lonStr, logQuery);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Nominatim backend geocoding timed out or was cancelled for query: {Query}", logQuery);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nominatim backend geocoding threw an exception for query: {Query}", logQuery);
        }

        return (null, null);
    }

    private static string BuildAddressText(LocationResolveInput request, string? provinceName)
    {
        var chunks = new[]
        {
            BuildHouseStreetPart(request.HouseNumber, request.Street),
            TrimOrNull(request.WardName),
            TrimOrNull(request.DistrictName),
            provinceName,
            "Việt Nam"
        };

        return string.Join(", ", chunks.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
    }

    private static string BuildFullAddressNormalized(string addressText)
    {
        var raw = addressText.ToLowerInvariant();
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ch == ',')
            {
                sb.Append(ch == 'đ' ? 'd' : ch);
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? BuildHouseStreetPart(string? houseNumber, string? street)
    {
        var part = string.Join(' ', new[]
        {
            TrimOrNull(houseNumber),
            TrimOrNull(street)
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(part) ? null : part;
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool HasLocationTrustMetadata(UniMap360.Models.Location location)
    {
        return !string.IsNullOrWhiteSpace(location.GeocodeSource)
               && !string.IsNullOrWhiteSpace(location.LocationConfidence)
               && location.LocationDistanceMeters.HasValue;
    }

    private static string? CanonicalizeProvinceName(string? provinceCode, string? provinceName)
    {
        var code = TrimOrNull(provinceCode);
        if (!string.IsNullOrWhiteSpace(code) && ProvinceNameByCode.TryGetValue(code, out var canonicalByCode))
        {
            return canonicalByCode;
        }

        return TrimOrNull(provinceName);
    }
}

