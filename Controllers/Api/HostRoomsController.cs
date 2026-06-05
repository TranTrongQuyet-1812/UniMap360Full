using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Options;
using UniMap360.Services.Admin;
using UniMap360.Services.Posts;

namespace UniMap360.Controllers.Api;

[Route("api/host/rooms")]
[ApiController]
[Authorize(Roles = AppRoles.Host)]
public class HostRoomsController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly CloudinarySettings _cloudinarySettings;
    private readonly ILogger<HostRoomsController> _logger;
    private readonly IManagePostsContextService _managePostsContextService;
    private readonly ILocationResolutionService _locationResolutionService;
    private readonly ICloudinaryAssetPurger _cloudinaryAssetPurger;
    private readonly IMemoryCache _cache;
    private const string MapFeedCacheKey = "GlobalMapFeed";

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const int MaxImageCount = 8;
    private const long MaxImageSizeBytes = 5 * 1024 * 1024;

    public HostRoomsController(
        UniMap360ProContext context,
        IWebHostEnvironment environment,
        IOptions<CloudinarySettings> cloudinaryOptions,
        ILogger<HostRoomsController> logger,
        IManagePostsContextService managePostsContextService,
        ILocationResolutionService locationResolutionService,
        ICloudinaryAssetPurger cloudinaryAssetPurger,
        IMemoryCache cache)
    {
        _context = context;
        _environment = environment;
        _cloudinarySettings = cloudinaryOptions.Value;
        _logger = logger;
        _managePostsContextService = managePostsContextService;
        _locationResolutionService = locationResolutionService;
        _cloudinaryAssetPurger = cloudinaryAssetPurger;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyRooms()
    {
        var host = await _managePostsContextService.GetCurrentHostAsync(User);
        if (host is null) return NotFound("Không tìm thấy hồ sơ chủ trọ.");

        var rooms = await _context.Rooms
            .AsNoTracking()
            .Where(r => r.HostId == host.HostId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.RoomId,
                r.Title,
                r.Price,
                r.Area,
                r.Description,
                r.ContactPhone,
                r.RoomStatus,
                r.CategoryId,
                r.LocationId,
                r.CreatedAt,
                r.IsExternal,
                r.SourceUrl
            })
            .ToListAsync();

        return Ok(new { total = rooms.Count, items = rooms });
    }

    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        request.LocationId = 0; // Triệt tiêu hoàn toàn LocationId từ client khi tạo mới

        var host = await _managePostsContextService.GetCurrentHostAsync(User);
        if (host is null) return NotFound("Không tìm thấy hồ sơ chủ trọ.");

        var validationError = await ValidateCategoryAsync(request.CategoryId);
        if (validationError is not null) return BadRequest(validationError);

        var locationResult = await ResolveLocationIdAsync(request, isCreate: true);
        if (locationResult.Error is not null) return BadRequest(locationResult.Error);

        var room = new Room
        {
            HostId = host.HostId,
            CategoryId = request.CategoryId,
            LocationId = locationResult.LocationId!.Value,
            Title = request.Title.Trim(),
            Price = request.Price,
            Area = request.Area,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            RoomStatus = string.IsNullOrWhiteSpace(request.RoomStatus) ? "Available" : request.RoomStatus.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsExternal = false,
            SourceUrl = null
        };

        _context.Rooms.Add(room);
        await _context.SaveChangesAsync();
        _cache.Remove(MapFeedCacheKey);

        return Ok(new { message = "Tạo phòng thành công.", roomId = room.RoomId });
    }

    [HttpPost("with-images")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateRoomWithImages([FromForm] CreateRoomWithImagesRequest request)
    {
        request.LocationId = 0; // Triệt tiêu hoàn toàn LocationId từ client khi tạo mới

        var host = await _managePostsContextService.GetCurrentHostAsync(User);
        if (host is null) return NotFound("Không tìm thấy hồ sơ chủ trọ.");

        var validationError = await ValidateCategoryAsync(request.CategoryId);
        if (validationError is not null) return BadRequest(validationError);

        var locationResult = await ResolveLocationIdAsync(request, isCreate: true);
        if (locationResult.Error is not null) return BadRequest(locationResult.Error);

        var imageValidationError = ValidateImageFiles(request.Images);
        if (imageValidationError is not null) return BadRequest(imageValidationError);

        var room = new Room
        {
            HostId = host.HostId,
            CategoryId = request.CategoryId,
            LocationId = locationResult.LocationId!.Value,
            Title = request.Title.Trim(),
            Price = request.Price,
            Area = request.Area,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            RoomStatus = string.IsNullOrWhiteSpace(request.RoomStatus) ? "Available" : request.RoomStatus.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsExternal = false,
            SourceUrl = null
        };

        _context.Rooms.Add(room);
        await _context.SaveChangesAsync();

        try
        {
            var mediaRows = await UploadRoomImagesAsync(request.Images, host.HostId, room.RoomId);
            _context.Media.AddRange(mediaRows);
            await _context.SaveChangesAsync();
            _cache.Remove(MapFeedCacheKey);

            return Ok(new
            {
                message = "Tạo phòng và tải ảnh thành công.",
                roomId = room.RoomId,
                imageCount = mediaRows.Count,
                thumbnailUrl = mediaRows.FirstOrDefault(m => m.IsThumbnail == true)?.MediaUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateRoomWithImages failed. HostId={HostId}, RoomId={RoomId}", host.HostId, room.RoomId);
            _context.Rooms.Remove(room);
            await _context.SaveChangesAsync();
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Tải ảnh thất bại. Vui lòng thử lại sau.");
        }
    }

    private async Task<List<Medium>> UploadRoomImagesAsync(IReadOnlyList<IFormFile> images, int hostId, int roomId)
    {
        if (IsCloudinaryEnabled())
        {
            try
            {
                return await UploadRoomImagesToCloudinaryAsync(images, hostId, roomId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Cloudinary upload failed. Falling back to local storage. HostId={HostId}, RoomId={RoomId}, CloudName={CloudName}",
                    hostId,
                    roomId,
                    _cloudinarySettings.CloudName);

                if (_cloudinarySettings.RequireSuccess)
                {
                    throw;
                }
            }
        }

        return await UploadRoomImagesToLocalAsync(images, hostId, roomId);
    }

    private async Task<List<Medium>> UploadRoomImagesToCloudinaryAsync(IReadOnlyList<IFormFile> images, int hostId, int roomId)
    {
        var folder = BuildHostRoomFolderPath(hostId, roomId);
        var credentials = ResolveCloudinaryCredentials();
        var cloudName = credentials.CloudName;
        var apiKey = credentials.ApiKey;
        var apiSecret = credentials.ApiSecret;

        if (string.IsNullOrWhiteSpace(cloudName)
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException("Cloudinary credentials are incomplete.");
        }

        Exception? lastException = null;
        foreach (var uploadPrefix in GetCloudinaryUploadPrefixes())
        {
            try
            {
                var endpointBase = string.IsNullOrWhiteSpace(uploadPrefix)
                    ? "https://api.cloudinary.com"
                    : uploadPrefix.Trim().TrimEnd('/');

                var endpoint = $"{endpointBase}/v1_1/{Uri.EscapeDataString(cloudName)}/image/upload";
                return await UploadRoomImagesWithSignedHttpAsync(endpoint, apiKey, apiSecret, images, roomId, folder);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Cloudinary upload attempt failed. UploadPrefix={UploadPrefix}, HostId={HostId}, RoomId={RoomId}",
                    string.IsNullOrWhiteSpace(uploadPrefix) ? "default" : uploadPrefix,
                    hostId,
                    roomId);
            }
        }

        throw lastException ?? new InvalidOperationException("Cloudinary upload failed.");
    }

    private async Task<List<Medium>> UploadRoomImagesWithSignedHttpAsync(
        string endpoint,
        string apiKey,
        string apiSecret,
        IReadOnlyList<IFormFile> images,
        int roomId,
        string folder)
    {
        using var httpClient = new HttpClient();
        var mediaRows = new List<Medium>(images.Count);

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var publicId = $"room_{roomId}_{Guid.NewGuid():N}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = ComputeCloudinarySignature(folder, publicId, timestamp, apiSecret);
            var contentType = string.IsNullOrWhiteSpace(image.ContentType)
                ? "application/octet-stream"
                : image.ContentType;
            await using var stream = image.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var base64 = Convert.ToBase64String(memory.ToArray());
            var dataUri = $"data:{contentType};base64,{base64}";

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["api_key"] = apiKey,
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
                var cloudinaryError = ExtractCloudinaryErrorMessage(responseText);
                var statusCode = (int)response.StatusCode;
                throw new InvalidOperationException(cloudinaryError ?? $"Cloudinary upload failed with status {statusCode}.");
            }

            using var json = JsonDocument.Parse(responseText);
            if (!json.RootElement.TryGetProperty("secure_url", out var secureUrlElement))
                throw new InvalidOperationException("Cloudinary upload succeeded but secure_url is missing.");

            var secureUrl = secureUrlElement.GetString();
            if (string.IsNullOrWhiteSpace(secureUrl))
                throw new InvalidOperationException("Cloudinary returned empty secure_url.");

            mediaRows.Add(new Medium
            {
                TargetId = roomId,
                TargetType = "Room",
                MediaUrl = secureUrl,
                IsThumbnail = i == 0
            });
        }

        return mediaRows;
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

    private async Task<List<Medium>> UploadRoomImagesToLocalAsync(IReadOnlyList<IFormFile> images, int hostId, int roomId)
    {
        var uploadRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(uploadRoot))
            uploadRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

        var relativeFolder = BuildHostRoomFolderPath(hostId, roomId);
        var roomUploadFolder = Path.Combine(uploadRoot, "uploads", relativeFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(roomUploadFolder);

        var mediaRows = new List<Medium>(images.Count);
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var extension = Path.GetExtension(image.FileName);
            var fileName = $"room_{roomId}_{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(roomUploadFolder, fileName);

            await using var stream = System.IO.File.Create(filePath);
            await image.CopyToAsync(stream);

            mediaRows.Add(new Medium
            {
                TargetId = roomId,
                TargetType = "Room",
                MediaUrl = $"/uploads/{relativeFolder}/{fileName}",
                IsThumbnail = i == 0
            });
        }

        return mediaRows;
    }

    private string BuildHostRoomFolderPath(int hostId, int roomId)
    {
        var baseFolder = string.IsNullOrWhiteSpace(_cloudinarySettings.BaseFolder)
            ? "anh-cho-chu-tro"
            : _cloudinarySettings.BaseFolder.Trim().Trim('/');

        return $"{baseFolder}/host-{hostId}/rooms/{roomId}";
    }

    private bool IsCloudinaryEnabled()
    {
        if (!_cloudinarySettings.Enabled) return false;

        var credentials = ResolveCloudinaryCredentials();
        return !string.IsNullOrWhiteSpace(credentials.CloudName)
            && !string.IsNullOrWhiteSpace(credentials.ApiKey)
            && !string.IsNullOrWhiteSpace(credentials.ApiSecret);
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
            using var json = JsonDocument.Parse(responseText);
            if (!json.RootElement.TryGetProperty("error", out var errorElement))
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

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateRoom(int id, [FromBody] UpdateRoomRequest request)
    {
        var host = await _managePostsContextService.GetCurrentHostAsync(User);
        if (host is null) return NotFound("Không tìm thấy hồ sơ chủ trọ.");

        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.RoomId == id && r.HostId == host.HostId);
        if (room is null) return NotFound("Không tìm thấy phòng hoặc bạn không có quyền chỉnh sửa.");

        var validationError = await ValidateCategoryAsync(request.CategoryId);
        if (validationError is not null) return BadRequest(validationError);

        var locationResult = await ResolveLocationIdAsync(request, isCreate: false, currentLocationId: room.LocationId);
        if (locationResult.Error is not null) return BadRequest(locationResult.Error);

        room.CategoryId = request.CategoryId;
        room.LocationId = locationResult.LocationId!.Value;
        room.Title = request.Title.Trim();
        room.Price = request.Price;
        room.Area = request.Area;
        room.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        room.ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim();
        room.RoomStatus = string.IsNullOrWhiteSpace(request.RoomStatus) ? room.RoomStatus : request.RoomStatus.Trim();

        await _context.SaveChangesAsync();
        _cache.Remove(MapFeedCacheKey);
        return Ok(new { message = "Cập nhật phòng thành công.", roomId = room.RoomId });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var host = await _managePostsContextService.GetCurrentHostAsync(User);
        if (host is null) return NotFound("Không tìm thấy hồ sơ chủ trọ.");

        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.RoomId == id && r.HostId == host.HostId);
        if (room is null) return NotFound("Không tìm thấy phòng hoặc bạn không có quyền xóa.");

        var mediaRows = await _context.Media
            .Where(m => m.TargetType == "Room" && m.TargetId == id)
            .ToListAsync();

        if (mediaRows.Count > 0)
        {
            await DeleteCloudinaryAssetsBestEffortAsync(mediaRows.Select(m => m.MediaUrl));
            DeleteLocalAssetsBestEffort(mediaRows.Select(m => m.MediaUrl));
            _context.Media.RemoveRange(mediaRows);
        }

        DeleteLocalRoomFolderBestEffort(host.HostId, id);

        _context.Rooms.Remove(room);
        await _context.SaveChangesAsync();
        _cache.Remove(MapFeedCacheKey);
        return Ok(new { message = "Xóa phòng thành công.", roomId = id });
    }

    private async Task DeleteCloudinaryAssetsBestEffortAsync(IEnumerable<string?> mediaUrls)
    {
        if (!IsCloudinaryEnabled()) return;

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
                _logger.LogWarning(ex, "Cloudinary destroy failed. PublicId={PublicId}", publicId);
            }
        }
    }

    private void DeleteLocalAssetsBestEffort(IEnumerable<string?> mediaUrls)
    {
        var uploadRoots = GetCandidateUploadRoots();

        foreach (var mediaUrl in mediaUrls)
        {
            if (string.IsNullOrWhiteSpace(mediaUrl)) continue;
            if (!mediaUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) continue;

            var relativePath = mediaUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            foreach (var uploadRoot in uploadRoots)
            {
                var fullPath = Path.Combine(uploadRoot, relativePath);

                try
                {
                    _logger.LogInformation("Local media cleanup attempt. MediaUrl={MediaUrl}, Path={Path}", mediaUrl, fullPath);
                    System.IO.File.Delete(fullPath);
                    _logger.LogInformation("Local media file deleted (or already absent). Path={Path}", fullPath);
                }
                catch (FileNotFoundException)
                {
                    // Idempotent cleanup: file khong ton tai cung la ket qua hop le.
                }
                catch (DirectoryNotFoundException)
                {
                    // Idempotent cleanup: folder khong ton tai cung la ket qua hop le.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete local media file. Path={Path}", fullPath);
                }
            }
        }
    }

    private void DeleteLocalRoomFolderBestEffort(int hostId, int roomId)
    {
        var relativeFolder = BuildHostRoomFolderPath(hostId, roomId);
        var roomIdText = roomId.ToString();
        var hostFolderName = $"host-{hostId}";

        foreach (var uploadRoot in GetCandidateUploadRoots())
        {
            var roomUploadFolder = Path.Combine(uploadRoot, "uploads", relativeFolder.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                _logger.LogInformation("Local folder cleanup attempt. Folder={Folder}", roomUploadFolder);
                Directory.Delete(roomUploadFolder, recursive: true);
                _logger.LogInformation("Local room folder deleted (or already absent). Folder={Folder}", roomUploadFolder);
            }
            catch (DirectoryNotFoundException)
            {
                // Idempotent cleanup: folder da khong ton tai.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete local room upload folder. Folder={Folder}", roomUploadFolder);
            }

            // Fallback: dọn mọi path khớp host-{hostId}/rooms/{roomId} trong uploads
            try
            {
                var uploadsRoot = Path.Combine(uploadRoot, "uploads");
                if (!Directory.Exists(uploadsRoot))
                    continue;

                var candidateFolders = Directory.EnumerateDirectories(uploadsRoot, "*", SearchOption.AllDirectories)
                    .Where(dir => string.Equals(Path.GetFileName(dir), roomIdText, StringComparison.OrdinalIgnoreCase))
                    .Where(dir =>
                    {
                        var roomsDir = Directory.GetParent(dir);
                        if (roomsDir is null || !string.Equals(roomsDir.Name, "rooms", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var hostDir = roomsDir.Parent;
                        return hostDir is not null
                            && string.Equals(hostDir.Name, hostFolderName, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                foreach (var candidate in candidateFolders)
                {
                    if (Directory.Exists(candidate))
                    {
                        Directory.Delete(candidate, recursive: true);
                        _logger.LogInformation("Fallback local room folder deleted. Folder={Folder}", candidate);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Fallback local folder cleanup failed. HostId={HostId}, RoomId={RoomId}, UploadRoot={UploadRoot}",
                    hostId,
                    roomId,
                    uploadRoot);
            }
        }
    }

    private IReadOnlyList<string> GetCandidateUploadRoots()
    {
        var roots = new List<string>();

        void AddRoot(string? root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var normalized = Path.GetFullPath(root);
            if (!roots.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
                roots.Add(normalized);
        }

        AddRoot(_environment.WebRootPath);
        AddRoot(Path.Combine(_environment.ContentRootPath ?? Directory.GetCurrentDirectory(), "wwwroot"));
        AddRoot(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));

        return roots;
    }

    private async Task<string?> ValidateCategoryAsync(int categoryId)
    {
        if (categoryId <= 0) return "CategoryId phải lớn hơn 0.";

        var category = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryId == categoryId);
        if (category is null) return "Category không tồn tại.";
        if (!string.Equals(category.CategoryType, "Room", StringComparison.OrdinalIgnoreCase))
            return "CategoryId phải thuộc loại Room.";

        return null;
    }

    private Task<(int? LocationId, string? Error)> ResolveLocationIdAsync(RoomLocationRequestBase request, bool isCreate = false, int? currentLocationId = null)
    {
        return _locationResolutionService.ResolveLocationIdAsync(new LocationResolveInput
        {
            LocationId = isCreate ? 0 : request.LocationId,
            CurrentLocationId = currentLocationId,
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
            GeocodedLatitude = request.GeocodedLatitude,
            GeocodedLongitude = request.GeocodedLongitude,
            UseProvinceCodeCanonicalization = true,
            MissingPinMessage = "Vui lòng ghim vị trí bản đồ trước khi đăng bài."
        });
    }

    private static string? ValidateImageFiles(IReadOnlyCollection<IFormFile> images)
    {
        if (images.Count <= 0)
            return "Vui lòng tải lên ít nhất 1 ảnh phòng trọ.";

        if (images.Count > MaxImageCount)
            return $"Bạn chỉ có thể tải lên tối đa {MaxImageCount} ảnh.";

        foreach (var image in images)
        {
            if (image.Length <= 0)
                return "Có ảnh không hợp lệ (dung lượng bằng 0).";

            if (image.Length > MaxImageSizeBytes)
                return "Mỗi ảnh chỉ được tối đa 5MB.";

            var extension = Path.GetExtension(image.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
                return "Định dạng ảnh không hỗ trợ. Chỉ chấp nhận: .jpg, .jpeg, .png, .webp.";

            if (string.IsNullOrWhiteSpace(image.ContentType) || !image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return "File tải lên phải là hình ảnh hợp lệ.";
        }

        return null;
    }

    public abstract class RoomLocationRequestBase
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
        public double? GeocodedLatitude { get; set; }
        public double? GeocodedLongitude { get; set; }
    }

    public sealed class CreateRoomRequest : RoomLocationRequestBase
    {
        public int CategoryId { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Area { get; set; }
        public string? Description { get; set; }
        public string? ContactPhone { get; set; }
        public string? RoomStatus { get; set; }
    }

    public sealed class UpdateRoomRequest : RoomLocationRequestBase
    {
        public int CategoryId { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Area { get; set; }
        public string? Description { get; set; }
        public string? ContactPhone { get; set; }
        public string? RoomStatus { get; set; }
    }

    public sealed class CreateRoomWithImagesRequest : RoomLocationRequestBase
    {
        public int CategoryId { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Area { get; set; }
        public string? Description { get; set; }
        public string? ContactPhone { get; set; }
        public string? RoomStatus { get; set; }
        public List<IFormFile> Images { get; set; } = new();
    }
}
