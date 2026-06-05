using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UniMap360.Models;

namespace UniMap360.Services.Business;

public class FeaturedListingService : IFeaturedListingService
{
    private readonly UniMap360ProContext _context;
    private readonly IMemoryCache _cache;
    private readonly IEntitlementService _entitlementService;
    private const string MapFeedCacheKey = "GlobalMapFeed";

    public FeaturedListingService(
        UniMap360ProContext context, 
        IMemoryCache cache, 
        IEntitlementService entitlementService)
    {
        _context = context;
        _cache = cache;
        _entitlementService = entitlementService;
    }

    public async Task<(bool Success, string Message)> PinListingAsync(
        int ownerAccountId, 
        string targetType, 
        int targetId, 
        string featureType, 
        int durationDays = 30)
    {
        var normalizedTarget = targetType.Trim();
        var normalizedFeature = featureType.Trim();

        // 1. Kiểm tra Entitlement nâng cao trước
        var hasEntitlement = await _entitlementService.CanUseFeatureAsync(ownerAccountId, normalizedFeature);
        if (!hasEntitlement)
        {
            return (false, "Tài khoản của bạn chưa đăng ký hoặc gói dịch vụ đã hết hạn.");
        }

        // 2. Kiểm tra quyền sở hữu và trạng thái hoạt động của bài đăng
        bool isOwner = false;
        bool isStatusActive = false;
        if (normalizedTarget == "Room")
        {
            var room = await _context.Rooms
                .Include(r => r.Host)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoomId == targetId);

            if (room != null)
            {
                isOwner = (room.Host.AccountId == ownerAccountId);
                isStatusActive = (room.RoomStatus == "Available");
            }
        }
        else if (normalizedTarget == "Job")
        {
            var job = await _context.Jobs
                .Include(j => j.Employer)
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.JobId == targetId);

            if (job != null)
            {
                isOwner = (job.Employer.AccountId == ownerAccountId);
                isStatusActive = (job.JobStatus == "Open");
            }
        }

        if (!isOwner)
        {
            return (false, "Bài đăng này không tồn tại hoặc không thuộc quyền sở hữu của bạn.");
        }

        if (!isStatusActive)
        {
            return (false, "Không thể ghim bài đăng đang bị ẩn, từ chối hoặc đã đóng.");
        }

        // 3. Thực thi transaction với IsolationLevel Serializable để chống Race Condition (double-click)
        using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // Kiểm tra xem bài đăng này đã được ghim trước đó chưa
            var alreadyPinned = await _context.FeaturedListings
                .AnyAsync(f => f.TargetType == normalizedTarget 
                            && f.TargetId == targetId 
                            && f.FeatureType == normalizedFeature 
                            && f.Status == "Active" 
                            && f.EndsAt > DateTime.UtcNow);

            if (alreadyPinned)
            {
                return (false, "Bài đăng này đã được ghim nổi bật rồi.");
            }

            // Đếm số lượng bài ghim hiện tại (Enforce Max 2 Pins)
            if (normalizedFeature == "MapPinned")
            {
                var activePinsCount = await _context.FeaturedListings
                    .CountAsync(f => f.OwnerAccountId == ownerAccountId 
                                && f.FeatureType == "MapPinned" 
                                && f.Status == "Active" 
                                && f.EndsAt > DateTime.UtcNow);

                if (activePinsCount >= 2)
                {
                    return (false, "Bạn chỉ được ghim tối đa 2 bài đăng nổi bật trên bản đồ cùng một lúc. Vui lòng hủy ghim bài đăng cũ trước.");
                }
            }
            else if (normalizedFeature == "ExplorePriority")
            {
                var activePinsCount = await _context.FeaturedListings
                    .CountAsync(f => f.OwnerAccountId == ownerAccountId 
                                && f.FeatureType == "ExplorePriority" 
                                && f.Status == "Active" 
                                && f.EndsAt > DateTime.UtcNow);

                if (activePinsCount >= 2)
                {
                    return (false, "Bạn chỉ được ghim tối đa 2 bài đăng ưu tiên khám phá cùng một lúc. Vui lòng hủy ghim bài đăng cũ trước.");
                }
            }

            var featured = new FeaturedListing
            {
                OwnerAccountId = ownerAccountId,
                TargetType = normalizedTarget,
                TargetId = targetId,
                FeatureType = normalizedFeature,
                Status = "Active",
                StartsAt = DateTime.UtcNow,
                EndsAt = DateTime.UtcNow.AddDays(durationDays)
            };

            _context.FeaturedListings.Add(featured);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 4. Invalidate Cache Map ngay lập tức
            _cache.Remove(MapFeedCacheKey);

            return (true, "Ghim bài đăng nổi bật thành công!");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return (false, $"Lỗi hệ thống: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UnpinListingAsync(
        int ownerAccountId, 
        string targetType, 
        int targetId, 
        string featureType)
    {
        var normalizedTarget = targetType.Trim();
        var normalizedFeature = featureType.Trim();

        var featured = await _context.FeaturedListings
            .FirstOrDefaultAsync(f => f.OwnerAccountId == ownerAccountId 
                                   && f.TargetType == normalizedTarget 
                                   && f.TargetId == targetId 
                                   && f.FeatureType == normalizedFeature 
                                   && f.Status == "Active");

        if (featured is null)
        {
            return (false, "Không tìm thấy bài ghim nổi bật nào đang hoạt động.");
        }

        featured.Status = "Cancelled";
        featured.CancelledAt = DateTime.UtcNow;
        featured.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Invalidate Cache Map ngay lập tức
        _cache.Remove(MapFeedCacheKey);

        return (true, "Hủy ghim bài đăng thành công.");
    }

    public async Task<bool> IsFeaturedAsync(string targetType, int targetId, string featureType)
    {
        var normalizedTarget = targetType.Trim();
        var normalizedFeature = featureType.Trim();

        return await _context.FeaturedListings
            .AsNoTracking()
            .AnyAsync(f => f.TargetType == normalizedTarget 
                        && f.TargetId == targetId 
                        && f.FeatureType == normalizedFeature 
                        && f.Status == "Active" 
                        && f.EndsAt > DateTime.UtcNow);
    }
}
