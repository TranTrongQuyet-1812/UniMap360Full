using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UniMap360.Models;

namespace UniMap360.Services.Business;

public sealed class BillingSettingsService : IBillingSettingsService
{
    private const string CacheKey = "Billing.EnforcementEnabled";
    private const string SettingKey = "Billing.EnforcementEnabled";

    private readonly UniMap360ProContext _context;
    private readonly IMemoryCache _cache;

    public BillingSettingsService(UniMap360ProContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<bool> IsBillingEnforcedAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);

            var setting = await _context.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == SettingKey, cancellationToken);

            if (setting is null)
            {
                return true;
            }

            return string.Equals(setting.Value, "true", StringComparison.OrdinalIgnoreCase);
        });
    }

    public async Task SetBillingEnforcedAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var setting = await _context.AppSettings
            .FirstOrDefaultAsync(x => x.Key == SettingKey, cancellationToken);

        var valStr = enabled ? "true" : "false";

        if (setting is null)
        {
            _context.AppSettings.Add(new AppSetting
            {
                Key = SettingKey,
                Value = valStr
            });
        }
        else
        {
            setting.Value = valStr;
            _context.AppSettings.Update(setting);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cache
        _cache.Remove(CacheKey);
    }
}
