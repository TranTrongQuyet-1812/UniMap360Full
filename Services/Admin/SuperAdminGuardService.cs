using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniMap360.Models;
using UniMap360.Options;

namespace UniMap360.Services.Admin;

public sealed class SuperAdminGuardService : ISuperAdminGuardService
{
    private const string OwnerSettingKey = "Security.SuperAdminAccountId";

    private readonly UniMap360ProContext _context;
    private readonly AdminSecurityOptions _options;
    private readonly ILogger<SuperAdminGuardService> _logger;

    public SuperAdminGuardService(
        UniMap360ProContext context,
        IOptions<AdminSecurityOptions> options,
        ILogger<SuperAdminGuardService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int?> GetOwnerAdminAccountIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _context.AppSettings
                .AsNoTracking()
                .Where(x => x.Key == OwnerSettingKey)
                .Select(x => x.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (int.TryParse(value, out var idFromDb) && idFromDb > 0)
                return idFromDb;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read AppSettings owner admin key. Falling back to configuration.");
        }

        return _options.OwnerAccountId > 0 ? _options.OwnerAccountId : null;
    }

    public async Task<bool> IsOwnerAdminAsync(int accountId, CancellationToken cancellationToken = default)
    {
        var ownerId = await GetOwnerAdminAccountIdAsync(cancellationToken);
        return ownerId.HasValue && ownerId.Value == accountId;
    }
}