using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UniMap360.Models;

namespace UniMap360.Services.Business;

public class SubscriptionService : ISubscriptionService
{
    private readonly UniMap360ProContext _context;
    private readonly IMemoryCache _cache;
    private const string MapFeedCacheKey = "GlobalMapFeed";

    public SubscriptionService(UniMap360ProContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<AccountSubscription?> GetActiveSubscriptionAsync(int accountId)
    {
        return await _context.AccountSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.AccountId == accountId && s.ExpiresAt > DateTime.UtcNow && (s.Status == "Active" || s.Status == "Trialing"));
    }

    public async Task<AccountSubscription> CreateTrialSubscriptionAsync(int accountId)
    {
        var planCode = "LANDLORD_TRIAL";
        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == planCode);
        if (plan is null)
        {
            plan = new SubscriptionPlan
            {
                Code = planCode,
                Name = "Gói dùng thử Chủ trọ",
                RoleScope = "Host",
                PriceVnd = 0,
                BillingCycle = "Monthly",
                IsActive = true
            };
            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync();
        }

        var oldSubscriptions = await _context.AccountSubscriptions
            .Where(s => s.AccountId == accountId && (s.Status == "Active" || s.Status == "Trialing"))
            .ToListAsync();

        foreach (var oldSub in oldSubscriptions)
        {
            oldSub.Status = "Cancelled";
            oldSub.UpdatedAt = DateTime.UtcNow;
        }

        var subscription = new AccountSubscription
        {
            AccountId = accountId,
            PlanId = plan.PlanId,
            Status = "Trialing",
            StartedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _context.AccountSubscriptions.Add(subscription);
        await _context.SaveChangesAsync();

        return subscription;
    }

    public async Task<AccountSubscription> CreatePaidSubscriptionAsync(int accountId, string planCode)
    {
        var hasActiveTransaction = _context.Database.CurrentTransaction != null;
        var tx = hasActiveTransaction ? null : await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // 1. Tìm gói cước, nếu chưa có thì khởi tạo mặc định (Seeding tự động)
            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == planCode);
            if (plan is null)
            {
                plan = new SubscriptionPlan
                {
                    Code = planCode,
                    Name = planCode.Contains("Host") ? "Gói VIP Chủ Trọ" : "Gói VIP Nhà Tuyển Dụng",
                    RoleScope = planCode.Contains("Host") ? "Host" : "Employer",
                    PriceVnd = 36000,
                    BillingCycle = "Monthly",
                    IsActive = true
                };
                _context.SubscriptionPlans.Add(plan);
                await _context.SaveChangesAsync();
            }

            // Vô hiệu hoá các gói cũ (nếu có)
            var oldSubscriptions = await _context.AccountSubscriptions
                .Where(s => s.AccountId == accountId && s.Status == "Active")
                .ToListAsync();

            foreach (var oldSub in oldSubscriptions)
            {
                oldSub.Status = "Cancelled";
                oldSub.UpdatedAt = DateTime.UtcNow;
            }

            var subscription = new AccountSubscription
            {
                AccountId = accountId,
                PlanId = plan.PlanId,
                Status = "Active",
                StartedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };

            _context.AccountSubscriptions.Add(subscription);
            await _context.SaveChangesAsync();
            
            if (tx != null)
            {
                await tx.CommitAsync();
            }

            return subscription;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UQ_SubscriptionPlans_Code") == true)
        {
            if (tx != null) await tx.RollbackAsync();
            throw new InvalidOperationException("Gói cước doanh nghiệp này đang được khởi tạo bởi một tiến trình khác. Vui lòng thử lại sau.");
        }
        catch (Exception)
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<int> CheckAndDeactivateExpiredSubscriptionsAsync()
    {
        var now = DateTime.UtcNow;
        
        // 1. Lấy tất cả các subscription đã hết hạn
        var expiredSubscriptions = await _context.AccountSubscriptions
            .Where(s => s.Status == "Active" && s.ExpiresAt <= now)
            .ToListAsync();

        if (!expiredSubscriptions.Any())
        {
            return 0;
        }

        var expiredAccountIds = expiredSubscriptions.Select(s => s.AccountId).Distinct().ToList();

        // 2. Chuyển trạng thái sang Expired
        foreach (var sub in expiredSubscriptions)
        {
            sub.Status = "Expired";
            sub.UpdatedAt = now;
        }

        // 3. Gỡ các bài ghim tương ứng của tài khoản đã hết hạn
        var activeFeaturedListings = await _context.FeaturedListings
            .Where(f => expiredAccountIds.Contains(f.OwnerAccountId) && f.Status == "Active")
            .ToListAsync();

        bool hasChanges = activeFeaturedListings.Any();
        foreach (var featured in activeFeaturedListings)
        {
            featured.Status = "Expired";
            featured.UpdatedAt = now;
        }

        await _context.SaveChangesAsync();

        // 4. Nếu có gỡ ghim, xóa cache Map Feed ngay lập tức
        if (hasChanges)
        {
            _cache.Remove(MapFeedCacheKey);
        }

        return expiredSubscriptions.Count;
    }
}
