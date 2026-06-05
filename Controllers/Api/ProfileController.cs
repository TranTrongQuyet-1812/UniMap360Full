using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Options;
using UniMap360.Services.Admin;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using UniMap360.Services.Business;

namespace UniMap360.Controllers.Api;

[Route("api/profile")]
[ApiController]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly CloudinarySettings _cloudinarySettings;
    private readonly ILogger<ProfileController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly ICloudinaryAssetPurger _cloudinaryPurger;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5MB

    public ProfileController(
        UniMap360ProContext context,
        IOptions<CloudinarySettings> cloudinaryOptions,
        ILogger<ProfileController> logger,
        IWebHostEnvironment environment,
        ICloudinaryAssetPurger cloudinaryPurger)
    {
        _context = context;
        _cloudinarySettings = cloudinaryOptions.Value;
        _logger = logger;
        _environment = environment;
        _cloudinaryPurger = cloudinaryPurger;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile([FromServices] IBillingSettingsService billingSettingsService)
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value 
                           ?? User.FindFirst("id")?.Value;

        if (!int.TryParse(accountIdClaim, out var accountId)) return Unauthorized();

        var role = User.FindFirst(ClaimTypes.Role)?.Value 
                  ?? User.FindFirst("role")?.Value;
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
        
        if (account == null) return NotFound("Account not found");

        object? profileData = null;

        if (role == AppRoles.Student)
        {
            var profile = await _context.StudentProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
            if (profile != null)
            {
                profileData = new
                {
                    FullName = profile.FullName,
                    University = profile.University,
                    Major = profile.Major
                };
            }
        }
        else if (role == AppRoles.Host)
        {
            var profile = await _context.HostProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
            if (profile != null)
            {
                profileData = new
                {
                    FullName = profile.FullName,
                    Phone = profile.Phone,
                    Idcard = profile.Idcard
                };
            }
        }
        else if (role == AppRoles.Employer)
        {
            var profile = await _context.EmployerProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
            if (profile != null)
            {
                profileData = new
                {
                    CompanyName = profile.CompanyName,
                    TaxCode = profile.TaxCode,
                    Website = profile.Website
                };
            }
        }

        var isBillingEnabled = await billingSettingsService.IsBillingEnforcedAsync();
        var isVip = await _context.AccountSubscriptions
            .AnyAsync(s => s.AccountId == accountId && s.ExpiresAt > DateTime.UtcNow && s.Status == "Active");

        return Ok(new
        {
            Email = account.Email,
            AvatarUrl = account.AvatarUrl,
            Role = role,
            isVip = isVip,
            billingEnforced = isBillingEnabled,
            Profile = profileData
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value 
                           ?? User.FindFirst("id")?.Value;

        if (!int.TryParse(accountIdClaim, out var accountId)) return Unauthorized();

        var role = User.FindFirst(ClaimTypes.Role)?.Value 
                  ?? User.FindFirst("role")?.Value;

        if (role == AppRoles.Student)
        {
            var profile = await _context.StudentProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
            if (profile == null)
            {
                profile = new StudentProfile { AccountId = accountId, FullName = request.FullName ?? "Sinh viên" };
                _context.StudentProfiles.Add(profile);
            }
            if (!string.IsNullOrWhiteSpace(request.FullName)) profile.FullName = request.FullName;
            profile.University = request.University;
            profile.Major = request.Major;
        }
        else if (role == AppRoles.Host)
        {
            var profile = await _context.HostProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
            if (profile == null)
            {
                profile = new HostProfile { AccountId = accountId, FullName = request.FullName ?? "Chủ trọ" };
                _context.HostProfiles.Add(profile);
            }
            if (!string.IsNullOrWhiteSpace(request.FullName)) profile.FullName = request.FullName;
            profile.Phone = request.Phone;
            profile.Idcard = request.Idcard;
        }
        else if (role == AppRoles.Employer)
        {
            var profile = await _context.EmployerProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
            if (profile == null)
            {
                profile = new EmployerProfile { AccountId = accountId, CompanyName = request.CompanyName ?? "Công ty" };
                _context.EmployerProfiles.Add(profile);
            }
            if (!string.IsNullOrWhiteSpace(request.CompanyName)) profile.CompanyName = request.CompanyName;
            profile.TaxCode = request.TaxCode;
            profile.Website = request.Website;
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Cập nhật hồ sơ thành công" });
    }

    [HttpPost("avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile avatar)
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value 
                           ?? User.FindFirst("id")?.Value;
        if (!int.TryParse(accountIdClaim, out var accountId)) return Unauthorized();

        if (avatar == null || avatar.Length == 0)
            return BadRequest("Vui lòng chọn ảnh đại diện.");

        if (avatar.Length > MaxImageSizeBytes)
            return BadRequest("Ảnh đại diện chỉ được tối đa 5MB.");

        var extension = Path.GetExtension(avatar.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
            return BadRequest("Định dạng ảnh không hỗ trợ. Chỉ chấp nhận: .jpg, .jpeg, .png, .webp.");

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
        if (account == null) return NotFound("Account not found");

        try
        {
            // Trích xuất publicId của ảnh cũ (nếu có và không phải ảnh mặc định dicebear)
            string? oldPublicId = null;
            if (!string.IsNullOrWhiteSpace(account.AvatarUrl) && !account.AvatarUrl.Contains("dicebear.com"))
            {
                oldPublicId = _cloudinaryPurger.TryExtractPublicIdFromUrl(account.AvatarUrl);
            }

            var avatarUrl = await UploadAvatarToCloudinaryAsync(avatar, accountId);
            account.AvatarUrl = avatarUrl;
            await _context.SaveChangesAsync();

            // Cố gắng xóa ảnh cũ trên nền (không chờ đợi để tránh block request lâu)
            if (!string.IsNullOrWhiteSpace(oldPublicId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = HttpContext.RequestServices.CreateScope();
                        var purger = scope.ServiceProvider.GetRequiredService<ICloudinaryAssetPurger>();
                        await purger.TryPurgeByPublicIdAsync(oldPublicId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to purge old avatar {OldPublicId} in background.", oldPublicId);
                    }
                });
            }

            return Ok(new { message = "Cập nhật ảnh đại diện thành công.", avatarUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload avatar to Cloudinary for AccountId={AccountId}", accountId);
            return StatusCode(500, "Có lỗi xảy ra khi tải ảnh lên Cloudinary.");
        }
    }

    private async Task<string> UploadAvatarToCloudinaryAsync(IFormFile image, int accountId)
    {
        var baseFolder = string.IsNullOrWhiteSpace(_cloudinarySettings.BaseFolder)
            ? "unimap360"
            : _cloudinarySettings.BaseFolder.Trim().Trim('/');

        var folder = $"{baseFolder}/avatars/account-{accountId}";
        var credentials = ResolveCloudinaryCredentials();
        
        if (string.IsNullOrWhiteSpace(credentials.CloudName) || string.IsNullOrWhiteSpace(credentials.ApiKey) || string.IsNullOrWhiteSpace(credentials.ApiSecret))
        {
            throw new InvalidOperationException("Cloudinary credentials are incomplete.");
        }

        var publicId = $"avatar_{accountId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeCloudinarySignature(folder, publicId, timestamp, credentials.ApiSecret);
        
        var contentType = string.IsNullOrWhiteSpace(image.ContentType) ? "application/octet-stream" : image.ContentType;
        await using var stream = image.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var base64 = Convert.ToBase64String(memory.ToArray());
        var dataUri = $"data:{contentType};base64,{base64}";

        var endpoint = $"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(credentials.CloudName)}/image/upload";

        using var httpClient = new HttpClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = credentials.ApiKey,
            ["timestamp"] = timestamp.ToString(),
            ["folder"] = folder,
            ["public_id"] = publicId,
            ["signature"] = signature,
            ["file"] = dataUri
        });

        using var response = await httpClient.PostAsync(endpoint, content);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cloudinary upload failed: {responseText}");
        }

        using var json = JsonDocument.Parse(responseText);
        if (!json.RootElement.TryGetProperty("secure_url", out var secureUrlElement))
        {
            throw new InvalidOperationException("Cloudinary upload succeeded but secure_url is missing.");
        }

        return secureUrlElement.GetString()!;
    }

    private (string? CloudName, string? ApiKey, string? ApiSecret) ResolveCloudinaryCredentials()
    {
        if (!string.IsNullOrWhiteSpace(_cloudinarySettings.CloudinaryUrl))
        {
            var raw = _cloudinarySettings.CloudinaryUrl.Trim();
            if (raw.StartsWith("CLOUDINARY_URL=", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring("CLOUDINARY_URL=".Length).Trim();

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
}

public class UpdateProfileRequest
{
    // Common/Student/Host
    public string? FullName { get; set; }
    
    // Student
    public string? University { get; set; }
    public string? Major { get; set; }

    // Host
    public string? Phone { get; set; }
    public string? Idcard { get; set; }

    // Employer
    public string? CompanyName { get; set; }
    public string? TaxCode { get; set; }
    public string? Website { get; set; }
}
