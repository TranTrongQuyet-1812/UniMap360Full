using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using UniMap360.Models;

namespace UniMap360.Controllers.Api;

[Route("api/seed/jobs")]
[ApiController]
[Authorize(Roles = "Admin")]
public class JobSeedController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public JobSeedController(UniMap360ProContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Tạo dữ liệu Job demo theo từng tỉnh (không cần cào web) để phủ map nhanh.
    /// </summary>
    /// <example>POST /api/seed/jobs/province?provinceSlug=ha-noi&target=10</example>
    [HttpPost("province")]
    public async Task<IActionResult> SeedJobsForProvince(
        [FromQuery] string provinceSlug,
        [FromQuery] int target = 10,
        [FromQuery] string seedVersion = "v1")
    {
        provinceSlug = (provinceSlug ?? string.Empty).Trim().ToLowerInvariant();
        seedVersion = (seedVersion ?? "v1").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provinceSlug))
            return BadRequest("provinceSlug là bắt buộc (ví dụ: ha-noi, ho-chi-minh).");
        if (target is < 1 or > 200)
            return BadRequest("target phải trong khoảng 1..200.");
        if (string.IsNullOrWhiteSpace(seedVersion) || seedVersion.Length > 20)
            return BadRequest("seedVersion không hợp lệ (1..20 ký tự).");

        var employerBots = await _context.EmployerProfiles
            .AsNoTracking()
            .OrderBy(e => e.EmployerId)
            .Take(5)
            .ToListAsync();
        if (employerBots.Count == 0)
            return BadRequest("Không có Employer bot nào trong DB. Hãy tạo bot trước.");

        // EF không translate được NormalizeForMatch xuống SQL, nên lấy lên memory rồi chọn category.
        var jobCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.CategoryType == "Job")
            .ToListAsync();

        var jobCategory = jobCategories
            .OrderByDescending(c => NormalizeForMatch(c.CategoryName).Contains("thuc tap sinh") ? 1 : 0)
            .ThenBy(c => c.CategoryId)
            .FirstOrDefault();
        if (jobCategory is null)
            return BadRequest("Thiếu Category loại 'Job' trong DB.");

        var provinceName = ProvinceNameBySlug.GetValueOrDefault(provinceSlug, provinceSlug.Replace('-', ' '));
        var basePoint = ProvinceCentroids.GetValueOrDefault(provinceSlug);
        if (basePoint.lat == 0 && basePoint.lng == 0)
            basePoint = (16.2, 107.9);

        var existingUrls = new HashSet<string>(
            await _context.Jobs.AsNoTracking().Where(j => j.SourceUrl != null).Select(j => j.SourceUrl!).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        var skipped = 0;
        var details = new List<object>();

        for (var i = 1; i <= target; i++)
        {
            var sourceUrl = $"https://seed.unimap360.local/job/{seedVersion}/{provinceSlug}/{i}";
            if (!existingUrls.Add(sourceUrl))
            {
                skipped++;
                continue;
            }

            var profile = BuildJobProfile(i);
            var title = profile.Title;
            var salary = profile.SalaryRange;
            var jobType = profile.JobType;

            var jitter = DeterministicJitter(i, title);
            var (lat, lng) = BuildProvincePoint(provinceSlug, basePoint, jitter);

            var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
            var location = new Location
            {
                AddressText = $"{provinceName}, Việt Nam",
                ProvinceName = provinceName,
                District = provinceName,
                FullAddressNormalized = $"{provinceName}, Việt Nam",
                Coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(lng, lat))
            };
            _context.Locations.Add(location);
            await _context.SaveChangesAsync();

            var employer = employerBots[(i - 1) % employerBots.Count];
            var job = new Job
            {
                EmployerId = employer.EmployerId,
                LocationId = location.LocationId,
                CategoryId = jobCategory.CategoryId,
                JobTitle = title.Length > 255 ? title[..255] : title,
                SalaryRange = salary.Length > 100 ? salary[..100] : salary,
                JobType = jobType,
                JobStatus = "Open",
                IsExternal = true,
                SourceUrl = sourceUrl
            };
            _context.Jobs.Add(job);
            await _context.SaveChangesAsync();

            inserted++;
        }

        details.Add(new { provinceSlug, province = provinceName, inserted, skipped });
        return Ok(new
        {
            source = "seed",
            provinceSlug,
            province = provinceName,
            target,
            inserted,
            skipped,
            note = "Jobs demo phủ tỉnh bằng centroid+jitter để đủ marker; có thể thay bằng cào TopCV/nguồn khác sau.",
            details
        });
    }

    private static JobProfile BuildJobProfile(int i)
    {
        var profiles = new[]
        {
            new JobProfile("Thực Tập Sinh Backend (.NET)", "4-6 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Frontend (React/JS)", "4-6 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Data/SQL", "4-6 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh QA/Tester", "3-5 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Marketing", "3-5 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Content Writer", "3-5 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Kế toán", "3-5 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Nhân sự (HR)", "3-5 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Sales", "Thỏa thuận", "Intern"),
            new JobProfile("Nhân viên Part-time (Bán hàng)", "20-30k/giờ", "Part-time"),
            new JobProfile("Nhân viên Part-time (Phục vụ)", "20-30k/giờ", "Part-time"),
            new JobProfile("Gia sư Toán/Lý/Hóa", "120-200k/buổi", "Part-time"),
            new JobProfile("CTV Nhập liệu", "Thỏa thuận", "Part-time"),
            new JobProfile("Nhân viên CSKH", "5-7 triệu", "Full-time"),
            new JobProfile("Nhân viên Thu ngân", "5-7 triệu", "Full-time"),
            new JobProfile("Nhân viên Kho/Logistics", "6-8 triệu", "Full-time"),
            new JobProfile("Thực Tập Sinh Thiết kế (Canva)", "3-5 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Truyền thông", "3-5 triệu", "Intern"),
            new JobProfile("Thực Tập Sinh Nhà hàng - Khách sạn", "Thỏa thuận", "Intern"),
            new JobProfile("Thực Tập Sinh Điều dưỡng", "Thỏa thuận", "Intern")
        };
        return profiles[(i - 1) % profiles.Length];
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

        // Tinh ven bien de marker "an toan" vao dat lien hon.
        if (CoastalProvinceSlugs.Contains(provinceSlug))
            lng -= 0.035;

        return (lat, lng);
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.ToLowerInvariant().Replace("đ", "d").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)) sb.Append(ch);
        }
        return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
    }

    private sealed record JobProfile(string Title, string SalaryRange, string JobType);

    private static readonly HashSet<string> CoastalProvinceSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "quang-ninh","hai-phong","thai-binh","nam-dinh","ninh-binh","thanh-hoa","nghe-an",
        "ha-tinh","quang-binh","quang-tri","thua-thien-hue","da-nang","quang-nam","quang-ngai",
        "binh-dinh","phu-yen","khanh-hoa","ninh-thuan","binh-thuan","ba-ria-vung-tau",
        "ho-chi-minh","tien-giang","ben-tre","tra-vinh","soc-trang","bac-lieu","ca-mau",
        "kien-giang"
    };

    private static readonly IReadOnlyDictionary<string, string> ProvinceNameBySlug =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["an-giang"] = "An Giang",
            ["ba-ria-vung-tau"] = "Bà Rịa - Vũng Tàu",
            ["bac-giang"] = "Bắc Giang",
            ["bac-kan"] = "Bắc Kạn",
            ["bac-lieu"] = "Bạc Liêu",
            ["bac-ninh"] = "Bắc Ninh",
            ["ben-tre"] = "Bến Tre",
            ["binh-dinh"] = "Bình Định",
            ["binh-duong"] = "Bình Dương",
            ["binh-phuoc"] = "Bình Phước",
            ["binh-thuan"] = "Bình Thuận",
            ["ca-mau"] = "Cà Mau",
            ["can-tho"] = "Cần Thơ",
            ["cao-bang"] = "Cao Bằng",
            ["da-nang"] = "Đà Nẵng",
            ["dak-lak"] = "Đắk Lắk",
            ["dak-nong"] = "Đắk Nông",
            ["dien-bien"] = "Điện Biên",
            ["dong-nai"] = "Đồng Nai",
            ["dong-thap"] = "Đồng Tháp",
            ["gia-lai"] = "Gia Lai",
            ["ha-giang"] = "Hà Giang",
            ["ha-nam"] = "Hà Nam",
            ["ha-noi"] = "Hà Nội",
            ["ha-tinh"] = "Hà Tĩnh",
            ["hai-duong"] = "Hải Dương",
            ["hai-phong"] = "Hải Phòng",
            ["hau-giang"] = "Hậu Giang",
            ["hoa-binh"] = "Hòa Bình",
            ["ho-chi-minh"] = "TP Hồ Chí Minh",
            ["hung-yen"] = "Hưng Yên",
            ["khanh-hoa"] = "Khánh Hòa",
            ["kien-giang"] = "Kiên Giang",
            ["kon-tum"] = "Kon Tum",
            ["lai-chau"] = "Lai Châu",
            ["lam-dong"] = "Lâm Đồng",
            ["lang-son"] = "Lạng Sơn",
            ["lao-cai"] = "Lào Cai",
            ["long-an"] = "Long An",
            ["nam-dinh"] = "Nam Định",
            ["nghe-an"] = "Nghệ An",
            ["ninh-binh"] = "Ninh Bình",
            ["ninh-thuan"] = "Ninh Thuận",
            ["phu-tho"] = "Phú Thọ",
            ["phu-yen"] = "Phú Yên",
            ["quang-binh"] = "Quảng Bình",
            ["quang-nam"] = "Quảng Nam",
            ["quang-ngai"] = "Quảng Ngãi",
            ["quang-ninh"] = "Quảng Ninh",
            ["quang-tri"] = "Quảng Trị",
            ["soc-trang"] = "Sóc Trăng",
            ["son-la"] = "Sơn La",
            ["tay-ninh"] = "Tây Ninh",
            ["thai-binh"] = "Thái Bình",
            ["thai-nguyen"] = "Thái Nguyên",
            ["thanh-hoa"] = "Thanh Hóa",
            ["thua-thien-hue"] = "Thừa Thiên Huế",
            ["tien-giang"] = "Tiền Giang",
            ["tra-vinh"] = "Trà Vinh",
            ["tuyen-quang"] = "Tuyên Quang",
            ["vinh-long"] = "Vĩnh Long",
            ["vinh-phuc"] = "Vĩnh Phúc",
            ["yen-bai"] = "Yên Bái"
        };

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
}

