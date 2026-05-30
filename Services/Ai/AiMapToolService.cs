using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using UniMap360.Models;

namespace UniMap360.Services.Ai
{
    public sealed class AiMapToolService : IAiMapToolService
    {
        private readonly UniMap360ProContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AiMapToolService> _logger;

        public AiMapToolService(
            UniMap360ProContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<AiMapToolService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<(double? Lat, double? Lng, string? DisplayName, string? Error)> GeocodeLocationAsync(string locationText, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(locationText))
            {
                return (null, null, null, "Tên địa điểm không hợp lệ.");
            }

            var normalizedQuery = NormalizeAddress(locationText);

            try
            {
                // 1. Kiểm tra cache trong DB (bảng Locations)
                var existing = await _context.Locations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.FullAddressNormalized == normalizedQuery && l.GeocodeSource == "Server", ct);

                if (existing != null)
                {
                    if (existing.GeocodedLatitude.HasValue && existing.GeocodedLongitude.HasValue)
                    {
                        return (existing.GeocodedLatitude.Value, existing.GeocodedLongitude.Value, existing.AddressText, null);
                    }
                    if (existing.Coordinates is Point p)
                    {
                        return (p.Y, p.X, existing.AddressText, null);
                    }
                }

                // 2. Nếu không có cache, gọi Nominatim API
                var client = _httpClientFactory.CreateClient("NominatimGeocoding");
                var query = locationText + ", Việt Nam";
                var endpoint = "https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=1&q=" + Uri.EscapeDataString(query);
                
                var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                req.Headers.UserAgent.ParseAdd("UniMap360/1.0 (contact: support@unimap360.local)");

                using var resp = await client.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Nominatim API geocoding failed with status: {StatusCode}", resp.StatusCode);
                    return (null, null, null, "Không thể liên lạc với máy chủ định vị địa lý.");
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, default, ct);
                
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    var latStr = first.TryGetProperty("lat", out var latProp) ? latProp.GetString() : null;
                    var lonStr = first.TryGetProperty("lon", out var lonProp) ? lonProp.GetString() : null;
                    var displayName = first.TryGetProperty("display_name", out var dnProp) ? dnProp.GetString() : null;

                    if (latStr != null && lonStr != null &&
                        double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng))
                    {
                        // Giới hạn trong lãnh thổ Việt Nam
                        // South-West: [4.0, 99.0], North-East: [24.5, 120.0]
                        if (lat < 4.0 || lat > 24.5 || lng < 99.0 || lng > 120.0)
                        {
                            return (null, null, null, "Địa điểm tìm kiếm nằm ngoài lãnh thổ Việt Nam.");
                        }

                        // Rút gọn displayName cho ngắn đẹp
                        string shortName = displayName ?? "Địa điểm đã tìm";
                        if (!string.IsNullOrWhiteSpace(displayName))
                        {
                            var parts = displayName.Split(',');
                            if (parts.Length > 2)
                            {
                                shortName = string.Join(",", parts.Take(3)).Trim();
                            }
                        }

                        // Lưu cache vào bảng Locations
                        var cachedLoc = new UniMap360.Models.Location
                        {
                            AddressText = shortName,
                            Coordinates = new Point(lng, lat) { SRID = 4326 },
                            FullAddressNormalized = normalizedQuery,
                            GeocodedLatitude = lat,
                            GeocodedLongitude = lng,
                            GeocodeSource = "Server",
                            LocationConfidence = "High",
                            LocationSuspicious = false
                        };

                        _context.Locations.Add(cachedLoc);
                        await _context.SaveChangesAsync(ct);

                        return (lat, lng, shortName, null);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình geocode: {Message}", ex.Message);
            }

            return (null, null, null, "Không xác định được vị trí địa lý của địa điểm này.");
        }

        public async Task<(List<ProximityListingItem> Items, string? Error)> SearchNearbyListingsAsync(double lat, double lng, double radiusKm, IReadOnlyCollection<string> types, CancellationToken ct = default)
        {
            var results = new List<ProximityListingItem>();
            if (_context == null)
            {
                return (results, null);
            }

            try
            {
                var query = _context.VGlobalMapFeeds.AsNoTracking().Where(x => x.Latitude != null && x.Longitude != null);
                
                var dbTypes = new List<string>();
                foreach (var t in types)
                {
                    if (t == "room") dbTypes.Add("Room");
                    if (t == "job") dbTypes.Add("Job");
                }
                
                if (dbTypes.Count > 0)
                {
                    query = query.Where(x => dbTypes.Contains(x.ItemType));
                }

                var feedItems = await query.ToListAsync(ct);

                var jobIds = feedItems.Where(x => x.ItemType == "Job").Select(x => x.Id).Distinct().ToList();
                var jobSalaries = new Dictionary<int, string>();
                if (jobIds.Count > 0)
                {
                    jobSalaries = await _context.Jobs
                        .AsNoTracking()
                        .Where(j => jobIds.Contains(j.JobId))
                        .ToDictionaryAsync(j => j.JobId, j => j.SalaryRange ?? "Thỏa thuận", ct);
                }

                foreach (var item in feedItems)
                {
                    double itemLat = item.Latitude!.Value;
                    double itemLng = item.Longitude!.Value;

                    double distance = CalculateDistance(lat, lng, itemLat, itemLng);
                    if (distance <= radiusKm)
                    {
                        var normalizedType = item.ItemType.ToLowerInvariant();
                        string displayPrice = "Liên hệ";
                        
                        if (normalizedType == "room" && item.Value.HasValue)
                        {
                            displayPrice = item.Value.Value >= 1000000
                                ? $"{item.Value.Value / 1000000m:0.#} Triệu"
                                : $"{item.Value.Value / 1000m:0}k";
                        }
                        else if (normalizedType == "job")
                        {
                            displayPrice = jobSalaries.GetValueOrDefault(item.Id, "Thỏa thuận");
                        }

                        results.Add(new ProximityListingItem
                        {
                            Id = item.Id,
                            Type = normalizedType,
                            Title = item.Title,
                            Price = displayPrice,
                            Lat = itemLat,
                            Lng = itemLng,
                            Address = item.AddressText,
                            DistanceKm = distance,
                            DetailUrl = $"/api/{normalizedType}s/{item.Id}"
                        });
                    }
                }

                return (results.OrderBy(x => x.DistanceKm).Take(30).ToList(), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình tính toán tìm kiếm cận lộ: {Message}", ex.Message);
                return (new List<ProximityListingItem>(), "Hệ thống cơ sở dữ liệu tìm kiếm đang gặp sự cố. Vui lòng thử lại sau.");
            }
        }

        private static string NormalizeAddress(string addressText)
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

        private static double CalculateDistance(double lat1, double lng1, double lat2, double lng2)
        {
            double r = 6371.0; // Bán kính Trái Đất (km)
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLng = (lng2 - lng1) * Math.PI / 180.0;

            double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLng / 2.0) * Math.Sin(dLng / 2.0);

            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return r * c;
        }
    }
}
