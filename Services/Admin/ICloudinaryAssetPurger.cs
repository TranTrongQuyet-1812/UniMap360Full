namespace UniMap360.Services.Admin;

public interface ICloudinaryAssetPurger
{
    Task<bool> TryPurgeByPublicIdAsync(string publicId, CancellationToken cancellationToken = default);
    string? TryExtractPublicIdFromUrl(string? mediaUrl);
}