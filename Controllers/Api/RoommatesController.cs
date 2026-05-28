using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniMap360.Models;
using UniMap360.Options;
using UniMap360.Services.Admin;
using UniMap360.Services.Posts;
using UniMap360.Constants;

namespace UniMap360.Controllers.Api;

[Route("api/[controller]")]
[ApiController]
public class RoommatesController : ControllerBase
{
    private const string MediaStorageTargetType = "Room";
    private const string RoommateMediaTargetType = "RMP";
    private static readonly string[] CompatibleRoommateMediaTargetTypes = new[] { "RMP", "Roommate", "RoommatePost" };

    private readonly UniMap360ProContext _context;
    private readonly IManagePostsContextService _managePostsContextService;
    private readonly ILogger<RoommatesController> _logger;
    private readonly CloudinarySettings _cloudinarySettings;
    private readonly ICloudinaryAssetPurger _cloudinaryAssetPurger;

    public RoommatesController(
        UniMap360ProContext context,
        IManagePostsContextService managePostsContextService,
        ILogger<RoommatesController> logger,
        IOptions<CloudinarySettings> cloudinaryOptions,
        ICloudinaryAssetPurger cloudinaryAssetPurger)
    {
        _context = context;
        _managePostsContextService = managePostsContextService;
        _logger = logger;
        _cloudinaryAssetPurger = cloudinaryAssetPurger;
        _cloudinarySettings = cloudinaryOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetFeed([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var query = _context.RoommatePosts
                .AsNoTracking()
                .Include(p => p.Student)
                .ThenInclude(s => s.Account)
                .Where(p => p.Status == RoommateStatuses.Active);

            var totalCount = await query.CountAsync();

            var posts = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    AuthorAccountId = p.Student.AccountId,
                    AuthorName = p.Student.FullName ?? p.Student.Account.Email,
                    AuthorAvatar = p.Student.Account.AvatarUrl ?? $"https://api.dicebear.com/7.x/identicon/svg?seed={Uri.EscapeDataString(p.Student.Account.Email)}",
                    p.Title,
                    p.Description,
                    p.TargetGender,
                    p.BudgetPerMonth,
                    p.Habits,
                    p.AreaPreference,
                    p.CreatedAt
                })
                .ToListAsync();

            var postIds = posts.Select(p => p.Id).ToList();
            var mediaMap = new Dictionary<int, List<string>>();

            if (postIds.Count > 0)
            {
                var legacyAndNewTypeMediaRows = await _context.Media
                    .AsNoTracking()
                    .Where(m => postIds.Contains(m.TargetId) && CompatibleRoommateMediaTargetTypes.Contains(m.TargetType))
                    .OrderBy(m => m.MediaId)
                    .Select(m => new { m.TargetId, m.MediaUrl })
                    .ToListAsync();

                var storageTargetIds = postIds.Select(ToStorageTargetId).ToList();
                var storageRows = await _context.Media
                    .AsNoTracking()
                    .Where(m => m.TargetType == MediaStorageTargetType && storageTargetIds.Contains(m.TargetId))
                    .OrderBy(m => m.MediaId)
                    .Select(m => new { m.TargetId, m.MediaUrl })
                    .ToListAsync();

                var storageMappedRows = storageRows.Select(x => new
                {
                    TargetId = Math.Abs(x.TargetId),
                    x.MediaUrl
                });

                var allRows = legacyAndNewTypeMediaRows
                    .Concat(storageMappedRows)
                    .ToList();

                mediaMap = allRows
                    .GroupBy(x => x.TargetId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => x.MediaUrl).Where(x => !string.IsNullOrWhiteSpace(x)).ToList());
            }

            var response = posts.Select(p => new
            {
                p.Id,
                p.AuthorAccountId,
                p.AuthorName,
                p.AuthorAvatar,
                p.Title,
                p.Description,
                p.TargetGender,
                p.BudgetPerMonth,
                p.Habits,
                p.AreaPreference,
                p.CreatedAt,
                Media = mediaMap.TryGetValue(p.Id, out var urls) ? urls : new List<string>()
            }).ToList();

            return Ok(new
            {
                total = totalCount,
                page,
                pageSize,
                items = response
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading roommate feed");
            return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreatePost([FromForm] CreateRoommatePostRequest request)
    {
        RoommatePost? createdPost = null;
        try
        {
            var accountId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            var student = await _context.StudentProfiles.FirstOrDefaultAsync(s => s.AccountId == accountId);
            if (student == null)
                return BadRequest("Ban can co ho so sinh vien de dang bai.");

            var post = new RoommatePost
            {
                StudentId = student.StudentId,
                Title = request.Title,
                Description = request.Description,
                TargetGender = request.TargetGender,
                BudgetPerMonth = request.BudgetPerMonth,
                Habits = request.Habits,
                AreaPreference = request.AreaPreference,
                IsActive = true,
                Status = RoommateStatuses.Active,
                CreatedAt = DateTime.UtcNow
            };

            _context.RoommatePosts.Add(post);
            await _context.SaveChangesAsync();
            createdPost = post;

            if (request.MediaFiles != null && request.MediaFiles.Count > 0)
            {
                if (!_cloudinarySettings.Enabled)
                {
                    _context.RoommatePosts.Remove(post);
                    await _context.SaveChangesAsync();
                    return StatusCode(500, "Cloudinary dang tat. He thong khong cho phep luu media local.");
                }

                var creds = ResolveCloudinaryCredentials();
                if (string.IsNullOrWhiteSpace(creds.CloudName)
                    || string.IsNullOrWhiteSpace(creds.ApiKey)
                    || string.IsNullOrWhiteSpace(creds.ApiSecret))
                {
                    _logger.LogWarning("Cloudinary credentials are missing for roommate post upload. AccountId={AccountId}", accountId);
                    _context.RoommatePosts.Remove(post);
                    await _context.SaveChangesAsync();
                    return StatusCode(500, "Cau hinh Cloudinary chua day du. Vui long kiem tra secrets.");
                }

                var folderPath = BuildStudentRoommateFolderPath(student.StudentId, post.Id);
                foreach (var file in request.MediaFiles)
                {
                    if (file.Length == 0) continue;

                    var isVideo = file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
                    var mediaUrl = await UploadRoommateMediaToCloudinaryAsync(
                        file,
                        file.FileName,
                        file.ContentType,
                        isVideo,
                        folderPath,
                        creds.CloudName!,
                        creds.ApiKey!,
                        creds.ApiSecret!);

                    if (!IsCloudinaryUrl(mediaUrl))
                    {
                        throw new InvalidOperationException("Media upload did not return a Cloudinary URL.");
                    }

                    _context.Media.Add(new Medium
                    {
                        TargetType = MediaStorageTargetType,
                        TargetId = ToStorageTargetId(post.Id),
                        MediaUrl = mediaUrl,
                        IsThumbnail = false
                    });
                }

                await _context.SaveChangesAsync();
            }

            return Ok(new { post.Id, message = "Dang bai thanh cong!" });
        }
        catch (Exception ex)
        {
            if (createdPost is not null)
            {
                try
                {
                    var mediaRows = await _context.Media
                        .Where(m =>
                            (m.TargetId == createdPost.Id && CompatibleRoommateMediaTargetTypes.Contains(m.TargetType))
                            || (m.TargetType == MediaStorageTargetType && m.TargetId == ToStorageTargetId(createdPost.Id)))
                        .ToListAsync();
                    if (mediaRows.Count > 0)
                        _context.Media.RemoveRange(mediaRows);

                    _context.RoommatePosts.Remove(createdPost);
                    await _context.SaveChangesAsync();
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to rollback roommate post creation. PostId={PostId}", createdPost.Id);
                }
            }

            _logger.LogError(ex, "Error creating roommate post");
            var rootEx = ex;
            while (rootEx.InnerException is not null) rootEx = rootEx.InnerException;
            return StatusCode(500, new
            {
                message = "Loi he thong khi dang bai.",
                detail = ex.Message,
                inner = ex.InnerException?.Message,
                root = rootEx.Message
            });
        }
    }

    private string BuildStudentRoommateFolderPath(int studentId, int postId)
    {
        var baseFolder = string.IsNullOrWhiteSpace(_cloudinarySettings.BaseFolder)
            ? "unimap360"
            : _cloudinarySettings.BaseFolder.Trim().Trim('/');

        return $"{baseFolder}/roommates/student-{studentId}/post-{postId}";
    }

    private (string? CloudName, string? ApiKey, string? ApiSecret) ResolveCloudinaryCredentials()
    {
        if (!string.IsNullOrWhiteSpace(_cloudinarySettings.CloudinaryUrl))
        {
            var raw = _cloudinarySettings.CloudinaryUrl.Trim();
            if (raw.StartsWith("CLOUDINARY_URL=", StringComparison.OrdinalIgnoreCase))
                raw = raw["CLOUDINARY_URL=".Length..].Trim();

            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, "cloudinary", StringComparison.OrdinalIgnoreCase))
            {
                var userInfo = uri.UserInfo ?? string.Empty;
                var separator = userInfo.IndexOf(':');
                if (separator > 0)
                {
                    var apiKeyFromUrl = Uri.UnescapeDataString(userInfo[..separator]);
                    var apiSecretFromUrl = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
                    var cloudNameFromUrl = uri.Host;

                    return (
                        string.IsNullOrWhiteSpace(cloudNameFromUrl) ? null : cloudNameFromUrl.Trim(),
                        string.IsNullOrWhiteSpace(apiKeyFromUrl) ? null : apiKeyFromUrl.Trim(),
                        string.IsNullOrWhiteSpace(apiSecretFromUrl) ? null : apiSecretFromUrl.Trim());
                }
            }
        }

        return (
            _cloudinarySettings.CloudName?.Trim(),
            _cloudinarySettings.ApiKey?.Trim(),
            _cloudinarySettings.ApiSecret?.Trim());
    }

    private static bool IsCloudinaryUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return uri.Host.Contains("cloudinary.com", StringComparison.OrdinalIgnoreCase);
    }

    private List<string?> GetCloudinaryUploadPrefixes()
    {
        var candidates = new List<string?>();

        if (!string.IsNullOrWhiteSpace(_cloudinarySettings.UploadPrefix))
            candidates.Add(_cloudinarySettings.UploadPrefix.Trim().TrimEnd('/'));

        candidates.Add(null);
        candidates.Add("https://api-eu.cloudinary.com");
        candidates.Add("https://api-ap.cloudinary.com");

        var distinct = new List<string?>();
        foreach (var prefix in candidates)
        {
            if (distinct.Any(existing => string.Equals(existing, prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            distinct.Add(prefix);
        }

        return distinct;
    }

    private async Task<string> UploadRoommateMediaToCloudinaryAsync(
        IFormFile file,
        string fileName,
        string? contentType,
        bool isVideo,
        string folderPath,
        string cloudName,
        string apiKey,
        string apiSecret)
    {
        Exception? lastException = null;

        foreach (var uploadPrefix in GetCloudinaryUploadPrefixes())
        {
            try
            {
                var endpointBase = string.IsNullOrWhiteSpace(uploadPrefix)
                    ? "https://api.cloudinary.com"
                    : uploadPrefix.Trim().TrimEnd('/');
                var resourceType = isVideo ? "video" : "image";
                var endpoint = $"{endpointBase}/v1_1/{Uri.EscapeDataString(cloudName)}/{resourceType}/upload";

                var mediaUrl = await UploadToCloudinaryViaHttpAsync(
                    file,
                    fileName,
                    contentType,
                    endpoint,
                    folderPath,
                    apiKey,
                    apiSecret,
                    isVideo);

                if (!string.IsNullOrWhiteSpace(mediaUrl))
                    return mediaUrl;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Cloudinary upload attempt failed. UploadPrefix={UploadPrefix}", uploadPrefix ?? "(default)");
            }
        }

        throw lastException ?? new InvalidOperationException("Cloudinary upload failed.");
    }

    private static async Task<string> UploadToCloudinaryViaHttpAsync(
        IFormFile file,
        string fileName,
        string? contentType,
        string endpoint,
        string folderPath,
        string apiKey,
        string apiSecret,
        bool isVideo)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var mediaPrefix = isVideo ? "roommate_video" : "roommate_image";
        var publicId = $"{mediaPrefix}_{Guid.NewGuid():N}";
        var signature = ComputeCloudinarySignature(folderPath, publicId, timestamp, apiSecret);

        using var httpClient = new HttpClient();
        var finalContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var base64 = Convert.ToBase64String(memory.ToArray());
        var dataUri = $"data:{finalContentType};base64,{base64}";

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = apiKey,
            ["timestamp"] = timestamp.ToString(),
            ["folder"] = folderPath,
            ["public_id"] = publicId,
            ["signature"] = signature,
            ["file"] = dataUri
        });

        using var response = await httpClient.PostAsync(endpoint, form);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var cloudinaryError = ExtractCloudinaryErrorMessage(responseText);
            var statusCode = (int)response.StatusCode;
            throw new InvalidOperationException(cloudinaryError ?? $"Cloudinary upload failed with status {statusCode}.");
        }

        using var payload = JsonDocument.Parse(responseText);
        if (!payload.RootElement.TryGetProperty("secure_url", out var secureUrlElement))
            throw new InvalidOperationException("Cloudinary upload succeeded but secure_url is missing.");

        var secureUrl = secureUrlElement.GetString();
        if (string.IsNullOrWhiteSpace(secureUrl))
            throw new InvalidOperationException("Cloudinary returned empty secure_url.");

        return secureUrl;
    }

    private static string ComputeCloudinarySignature(string folder, string publicId, long timestamp, string apiSecret)
    {
        var toSign = $"folder={folder}&public_id={publicId}&timestamp={timestamp}{apiSecret}";
        var bytes = Encoding.UTF8.GetBytes(toSign);
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(bytes);

        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static string? ExtractCloudinaryErrorMessage(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        try
        {
            using var payload = JsonDocument.Parse(responseText);
            if (!payload.RootElement.TryGetProperty("error", out var errorElement))
                return null;

            if (!errorElement.TryGetProperty("message", out var messageElement))
                return null;

            return messageElement.GetString();
        }
        catch
        {
            return null;
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMyPosts([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var accountId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            var student = await _context.StudentProfiles.FirstOrDefaultAsync(s => s.AccountId == accountId);
            if (student == null)
                return BadRequest("Tai khoan chua co ho so sinh vien.");

            var query = _context.RoommatePosts
                .Where(p => p.StudentId == student.StudentId);
                
            var totalCount = await query.CountAsync();

            var posts = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    p.Title,
                    p.Description,
                    p.TargetGender,
                    p.BudgetPerMonth,
                    p.Habits,
                    p.AreaPreference,
                    IsActive = p.Status == RoommateStatuses.Active,
                    Status = p.Status,
                    p.CreatedAt,
                    IsAdminLocked = p.Status == RoommateStatuses.Rejected || (p.Status == RoommateStatuses.Hidden && _context.ContentReports
                        .Where(r => r.TargetType == "Roommate" && r.TargetId == p.Id && r.Status == ContentReportStatuses.Resolved)
                        .OrderByDescending(r => r.ReviewedAt)
                        .Select(r => r.ResolutionAction)
                        .FirstOrDefault() == ContentReportResolutionActions.Hide)
                })
                .ToListAsync();

            return Ok(new { total = totalCount, page, pageSize, items = posts });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting my roommate posts");
            return StatusCode(500, "Loi khi lay danh sach bai dang.");
        }
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeletePost(int id)
    {
        try
        {
            var accountId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            var student = await _context.StudentProfiles.FirstOrDefaultAsync(s => s.AccountId == accountId);
            if (student == null) return Unauthorized();

            var post = await _context.RoommatePosts.FirstOrDefaultAsync(p => p.Id == id && p.StudentId == student.StudentId);
            if (post == null) return NotFound("Khong tim thay bai viet hoac ban khong co quyen xoa.");

            var mediaRows = await _context.Media
                .Where(m =>
                    (m.TargetId == id && CompatibleRoommateMediaTargetTypes.Contains(m.TargetType))
                    || (m.TargetType == MediaStorageTargetType && m.TargetId == ToStorageTargetId(id)))
                .ToListAsync();

            if (mediaRows.Count > 0)
            {
                await DeleteCloudinaryAssetsBestEffortAsync(mediaRows.Select(m => m.MediaUrl));
                _context.Media.RemoveRange(mediaRows);
            }

            _context.RoommatePosts.Remove(post);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Da xoa bai dang." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting roommate post");
            return StatusCode(500, "Loi khi xoa bai dang.");
        }
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> UpdatePost(int id, [FromBody] UpdateRoommatePostRequest request)
    {
        try
        {
            var accountId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            var student = await _context.StudentProfiles.FirstOrDefaultAsync(s => s.AccountId == accountId);
            if (student == null) return Unauthorized();

            var post = await _context.RoommatePosts.FirstOrDefaultAsync(p => p.Id == id && p.StudentId == student.StudentId);
            if (post == null) return NotFound("Khong tim thay bai viet hoac ban khong co quyen sua.");

            post.Description = request.Description;
            post.BudgetPerMonth = request.BudgetPerMonth;
            post.TargetGender = request.TargetGender;
            post.Habits = request.Habits;
            post.AreaPreference = request.AreaPreference;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Da cap nhat bai dang." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating roommate post");
            return StatusCode(500, "Loi khi cap nhat bai dang.");
        }
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
                _logger.LogWarning(ex, "Cloudinary destroy failed for roommate media. PublicId={PublicId}", publicId);
            }
        }
    }

    private static int ToStorageTargetId(int postId) => -Math.Abs(postId);
}

public class CreateRoommatePostRequest
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? TargetGender { get; set; }
    public decimal? BudgetPerMonth { get; set; }
    public string? Habits { get; set; }
    public string? AreaPreference { get; set; }
    public List<IFormFile>? MediaFiles { get; set; }
}

public class UpdateRoommatePostRequest
{
    public string? Description { get; set; }
    public decimal? BudgetPerMonth { get; set; }
    public string? TargetGender { get; set; }
    public string? Habits { get; set; }
    public string? AreaPreference { get; set; }
}
