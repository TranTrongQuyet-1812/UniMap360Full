using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Services.Admin;
using UniMap360.Services.Posts;

namespace UniMap360.Controllers.Api;

[Route("api/employer/jobs")]
[ApiController]
[Authorize(Roles = AppRoles.Employer)]
public class EmployerJobsController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EmployerJobsController> _logger;
    private readonly IManagePostsContextService _managePostsContextService;
    private readonly ILocationResolutionService _locationResolutionService;
    private readonly ICloudinaryAssetPurger _cloudinaryAssetPurger;
    private const string MapFeedCacheKey = "GlobalMapFeed";

    public EmployerJobsController(
        UniMap360ProContext context,
        IMemoryCache cache,
        ILogger<EmployerJobsController> logger,
        IManagePostsContextService managePostsContextService,
        ILocationResolutionService locationResolutionService,
        ICloudinaryAssetPurger cloudinaryAssetPurger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _managePostsContextService = managePostsContextService;
        _locationResolutionService = locationResolutionService;
        _cloudinaryAssetPurger = cloudinaryAssetPurger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyJobs()
    {
        var employer = await _managePostsContextService.GetCurrentEmployerAsync(User);
        if (employer is null) return NotFound("Không tìm thấy hồ sơ nhà tuyển dụng.");

        var jobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.EmployerId == employer.EmployerId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new
            {
                j.JobId,
                j.JobTitle,
                j.SalaryRange,
                j.Description,
                j.ContactPhone,
                j.JobType,
                j.JobStatus,
                j.CategoryId,
                j.LocationId,
                j.CreatedAt,
                j.IsExternal,
                j.SourceUrl
            })
            .ToListAsync();

        return Ok(new { total = jobs.Count, items = jobs });
    }

    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        var employer = await _managePostsContextService.GetCurrentEmployerAsync(User);
        if (employer is null) return NotFound("Không tìm thấy hồ sơ nhà tuyển dụng.");

        var validationError = await ValidateCategoryAsync(request.CategoryId);
        if (validationError is not null) return BadRequest(validationError);

        var locationResult = await ResolveLocationIdAsync(request);
        if (locationResult.Error is not null) return BadRequest(locationResult.Error);

        var job = new Job
        {
            EmployerId = employer.EmployerId,
            CategoryId = request.CategoryId,
            LocationId = locationResult.LocationId!.Value,
            JobTitle = request.JobTitle.Trim(),
            SalaryRange = string.IsNullOrWhiteSpace(request.SalaryRange) ? "Thỏa thuận" : request.SalaryRange.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            JobType = string.IsNullOrWhiteSpace(request.JobType) ? "Part-time" : request.JobType.Trim(),
            JobStatus = string.IsNullOrWhiteSpace(request.JobStatus) ? "Open" : request.JobStatus.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsExternal = false,
            SourceUrl = null
        };

        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();
        _cache.Remove(MapFeedCacheKey);

        return Ok(new { message = "Tạo việc làm thành công.", jobId = job.JobId });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateJob(int id, [FromBody] UpdateJobRequest request)
    {
        var employer = await _managePostsContextService.GetCurrentEmployerAsync(User);
        if (employer is null) return NotFound("Không tìm thấy hồ sơ nhà tuyển dụng.");

        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == id && j.EmployerId == employer.EmployerId);
        if (job is null) return NotFound("Không tìm thấy việc làm hoặc bạn không có quyền chỉnh sửa.");

        var validationError = await ValidateCategoryAsync(request.CategoryId);
        if (validationError is not null) return BadRequest(validationError);

        var locationResult = await ResolveLocationIdAsync(request);
        if (locationResult.Error is not null) return BadRequest(locationResult.Error);

        job.CategoryId = request.CategoryId;
        job.LocationId = locationResult.LocationId!.Value;
        job.JobTitle = request.JobTitle.Trim();
        job.SalaryRange = string.IsNullOrWhiteSpace(request.SalaryRange) ? job.SalaryRange : request.SalaryRange.Trim();
        job.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        job.ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim();
        job.JobType = string.IsNullOrWhiteSpace(request.JobType) ? job.JobType : request.JobType.Trim();
        job.JobStatus = string.IsNullOrWhiteSpace(request.JobStatus) ? job.JobStatus : request.JobStatus.Trim();

        await _context.SaveChangesAsync();
        _cache.Remove(MapFeedCacheKey);
        return Ok(new { message = "Cập nhật việc làm thành công.", jobId = job.JobId });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteJob(int id)
    {
        var employer = await _managePostsContextService.GetCurrentEmployerAsync(User);
        if (employer is null) return NotFound("Không tìm thấy hồ sơ nhà tuyển dụng.");

        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == id && j.EmployerId == employer.EmployerId);
        if (job is null) return NotFound("Không tìm thấy việc làm hoặc bạn không có quyền xóa.");

        var mediaRows = await _context.Media
            .Where(m => m.TargetType == "Job" && m.TargetId == id)
            .ToListAsync();

        if (mediaRows.Count > 0)
        {
            await DeleteCloudinaryAssetsBestEffortAsync(mediaRows.Select(m => m.MediaUrl));
            _context.Media.RemoveRange(mediaRows);
        }

        _context.Jobs.Remove(job);
        await _context.SaveChangesAsync();
        _cache.Remove(MapFeedCacheKey);
        return Ok(new { message = "Xóa việc làm thành công.", jobId = id });
    }

    private async Task DeleteCloudinaryAssetsBestEffortAsync(IEnumerable<string?> mediaUrls)
    {
        var publicIds = mediaUrls
            .Select(_cloudinaryAssetPurger.TryExtractPublicIdFromUrl)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var publicId in publicIds)
        {
            try
            {
                await _cloudinaryAssetPurger.TryPurgeByPublicIdAsync(publicId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cloudinary destroy failed for job media. PublicId={PublicId}", publicId);
            }
        }
    }

    private async Task<string?> ValidateCategoryAsync(int categoryId)
    {
        if (categoryId <= 0) return "CategoryId phải lớn hơn 0.";

        var category = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryId == categoryId);
        if (category is null) return "Category không tồn tại.";
        if (!string.Equals(category.CategoryType, "Job", StringComparison.OrdinalIgnoreCase))
            return "CategoryId phải thuộc loại Job.";

        return null;
    }

    private Task<(int? LocationId, string? Error)> ResolveLocationIdAsync(JobLocationRequestBase request)
    {
        return _locationResolutionService.ResolveLocationIdAsync(new LocationResolveInput
        {
            LocationId = request.LocationId,
            ProvinceCode = request.ProvinceCode,
            ProvinceName = request.ProvinceName,
            DistrictCode = request.DistrictCode,
            DistrictName = request.DistrictName,
            WardCode = request.WardCode,
            WardName = request.WardName,
            HouseNumber = request.HouseNumber,
            Street = request.Street,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            UseProvinceCodeCanonicalization = false,
            MissingPinMessage = "Vui lòng ghim vị trí bản đồ trước khi đăng việc làm."
        });
    }

    public abstract class JobLocationRequestBase
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
    }

    public sealed class CreateJobRequest : JobLocationRequestBase
    {
        public int CategoryId { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public string? SalaryRange { get; set; }
        public string? Description { get; set; }
        public string? ContactPhone { get; set; }
        public string? JobType { get; set; }
        public string? JobStatus { get; set; }
    }

    public sealed class UpdateJobRequest : JobLocationRequestBase
    {
        public int CategoryId { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public string? SalaryRange { get; set; }
        public string? Description { get; set; }
        public string? ContactPhone { get; set; }
        public string? JobType { get; set; }
        public string? JobStatus { get; set; }
    }
}
