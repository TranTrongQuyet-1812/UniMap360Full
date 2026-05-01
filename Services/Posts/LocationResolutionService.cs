using System.Text;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using UniMap360.Models;

namespace UniMap360.Services.Posts;

public sealed class LocationResolveInput
{
    public int LocationId { get; set; }
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

    public LocationResolutionService(UniMap360ProContext context)
    {
        _context = context;
    }

    public async Task<(int? LocationId, string? Error)> ResolveLocationIdAsync(LocationResolveInput request, CancellationToken cancellationToken = default)
    {
        if (request.LocationId > 0)
        {
            var exists = await _context.Locations.AnyAsync(l => l.LocationId == request.LocationId, cancellationToken);
            return exists ? (request.LocationId, null) : (null, "Location không tồn tại.");
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

        var provinceName = request.UseProvinceCodeCanonicalization
            ? CanonicalizeProvinceName(request.ProvinceCode, request.ProvinceName)
            : TrimOrNull(request.ProvinceName);

        var addressText = BuildAddressText(request, provinceName);
        var fullAddressNormalized = BuildFullAddressNormalized(addressText);

        var existingLocationId = await _context.Locations
            .AsNoTracking()
            .Where(l => l.FullAddressNormalized == fullAddressNormalized)
            .OrderByDescending(l => l.LocationId)
            .Select(l => (int?)l.LocationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingLocationId.HasValue)
        {
            return (existingLocationId.Value, null);
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
            FullAddressNormalized = fullAddressNormalized
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync(cancellationToken);
        return (location.LocationId, null);
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

