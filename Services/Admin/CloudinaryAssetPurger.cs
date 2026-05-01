using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UniMap360.Options;

namespace UniMap360.Services.Admin;

public sealed class CloudinaryAssetPurger : ICloudinaryAssetPurger
{
    private enum DestroyAttemptState
    {
        Deleted,
        NotFound,
        Failed
    }

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudinarySettings _cloudinarySettings;
    private readonly ILogger<CloudinaryAssetPurger> _logger;

    public CloudinaryAssetPurger(
        IHttpClientFactory httpClientFactory,
        IOptions<CloudinarySettings> cloudinaryOptions,
        ILogger<CloudinaryAssetPurger> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cloudinarySettings = cloudinaryOptions.Value;
        _logger = logger;
    }

    public async Task<bool> TryPurgeByPublicIdAsync(string publicId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicId) || !_cloudinarySettings.Enabled)
            return true;

        var creds = ResolveCredentials();
        if (string.IsNullOrWhiteSpace(creds.CloudName)
            || string.IsNullOrWhiteSpace(creds.ApiKey)
            || string.IsNullOrWhiteSpace(creds.ApiSecret))
        {
            return false;
        }

        var resourceTypes = new[] { "image", "video", "raw" };
        var hadFailure = false;

        foreach (var resourceType in resourceTypes)
        {
            var attempt = await TryDestroyByResourceTypeAsync(
                creds.CloudName!,
                creds.ApiKey!,
                creds.ApiSecret!,
                resourceType,
                publicId,
                cancellationToken);

            if (attempt == DestroyAttemptState.Deleted)
                return true;

            if (attempt == DestroyAttemptState.Failed)
                hadFailure = true;
        }

        // All resource types returned "not found" => already absent is considered success.
        return !hadFailure;
    }

    public string? TryExtractPublicIdFromUrl(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl)) return null;
        if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Contains("cloudinary.com", StringComparison.OrdinalIgnoreCase)) return null;

        var absolutePath = uri.AbsolutePath;
        const string uploadMarker = "/upload/";
        var markerIndex = absolutePath.IndexOf(uploadMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return null;

        var rest = absolutePath[(markerIndex + uploadMarker.Length)..].Trim('/');
        if (string.IsNullOrWhiteSpace(rest)) return null;

        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count == 0) return null;

        if (segments.Count > 1 && segments[0].StartsWith("v", StringComparison.OrdinalIgnoreCase)
            && segments[0].Length > 1
            && segments[0][1..].All(char.IsDigit))
        {
            segments.RemoveAt(0);
        }

        if (segments.Count == 0) return null;
        var joined = string.Join('/', segments);
        var dotIndex = joined.LastIndexOf('.');
        if (dotIndex <= 0) return null;

        return Uri.UnescapeDataString(joined[..dotIndex]);
    }

    private (string? CloudName, string? ApiKey, string? ApiSecret) ResolveCredentials()
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

    private static string ComputeDestroySignature(string publicId, long timestamp, string apiSecret)
    {
        var toSign = $"invalidate=true&public_id={publicId}&timestamp={timestamp}{apiSecret}";
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

    private async Task<DestroyAttemptState> TryDestroyByResourceTypeAsync(
        string cloudName,
        string apiKey,
        string apiSecret,
        string resourceType,
        string publicId,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = $"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(cloudName)}/{resourceType}/destroy";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = ComputeDestroySignature(publicId, timestamp, apiSecret);

            var client = _httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["api_key"] = apiKey,
                ["timestamp"] = timestamp.ToString(),
                ["public_id"] = publicId,
                ["invalidate"] = "true",
                ["signature"] = signature
            });

            using var response = await client.PostAsync(endpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cloudinary destroy failed. ResourceType={ResourceType}, PublicId={PublicId}, Status={StatusCode}, Body={Body}",
                    resourceType,
                    publicId,
                    (int)response.StatusCode,
                    body);
                return DestroyAttemptState.Failed;
            }

            var result = TryReadResult(body);
            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                return DestroyAttemptState.Deleted;

            if (string.Equals(result, "not found", StringComparison.OrdinalIgnoreCase))
                return DestroyAttemptState.NotFound;

            // Unrecognized success payload: treat as successful best-effort.
            return DestroyAttemptState.Deleted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Cloudinary destroy exception. ResourceType={ResourceType}, PublicId={PublicId}",
                resourceType,
                publicId);
            return DestroyAttemptState.Failed;
        }
    }

    private static string? TryReadResult(string? jsonBody)
    {
        if (string.IsNullOrWhiteSpace(jsonBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            return doc.RootElement.TryGetProperty("result", out var resultElement)
                ? resultElement.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
