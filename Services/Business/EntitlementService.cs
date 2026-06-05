using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniMap360.Models;

namespace UniMap360.Services.Business;

public class EntitlementService : IEntitlementService
{
    private readonly UniMap360ProContext _context;
    private readonly IBillingSettingsService _billingSettingsService;

    public EntitlementService(UniMap360ProContext context, IBillingSettingsService billingSettingsService)
    {
        _context = context;
        _billingSettingsService = billingSettingsService;
    }

    public async Task<bool> CanUseFeatureAsync(int accountId, string featureCode)
    {
        // 1. Kiểm tra Whitelist các tính năng hợp lệ
        var businessAllowedFeatures = new System.Collections.Generic.HashSet<string>
        {
            "MapPinned",
            "ExplorePriority",
            "AnalyticsDashboard",
            "VerifiedBadge"
        };

        if (!businessAllowedFeatures.Contains(featureCode))
        {
            return false;
        }

        // 2. Lấy thông tin tài khoản
        var account = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == accountId);

        if (account is null) return false;

        var role = account.UserRole;

        // 3. Admin, SuperAdmin được bypass miễn phí hoàn toàn để test/quản trị
        if (role == "Admin" || role == "SuperAdmin")
        {
            return true;
        }

        // 4. Kiểm tra phân quyền tính năng theo Vai trò tài khoản (Role-based Feature Authorization)
        if (role != "Host" && role != "Employer")
        {
            // Student và các role khác không được dùng nhóm business paid features
            return false;
        }

        // 5. Kiểm tra cấu hình bắt buộc thu phí
        var isBillingEnabled = await _billingSettingsService.IsBillingEnforcedAsync();
        if (!isBillingEnabled)
        {
            return true; // Nếu chưa kích hoạt thu phí, cho phép Host/Employer dùng tất cả tính năng trên
        }

        // 6. Kiểm tra xem tài khoản có gói cước hoạt động và khớp RoleScope hay không
        var subscription = await _context.AccountSubscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.AccountId == accountId && s.ExpiresAt > DateTime.UtcNow && (s.Status == "Active" || s.Status == "Trialing"));

        if (subscription is null)
        {
            return false; // Không có gói cước nào còn hạn hoặc không hoạt động
        }

        // Kiểm tra Scope của gói cước phải khớp với Role của tài khoản
        var planScope = subscription.Plan.RoleScope;
        if (role == "Host")
        {
            if (planScope != "Landlord" && planScope != "Host" && planScope != "Business")
            {
                return false;
            }
        }
        else if (role == "Employer")
        {
            if (planScope != "Employer" && planScope != "Business")
            {
                return false;
            }
        }

        return true;
    }

    public async Task CheckFeatureAccessAsync(int accountId, string featureCode)
    {
        var hasAccess = await CanUseFeatureAsync(accountId, featureCode);
        if (!hasAccess)
        {
            throw new EntitlementLockedException(featureCode);
        }
    }
}
