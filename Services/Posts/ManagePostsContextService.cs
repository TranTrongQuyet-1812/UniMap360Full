using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using UniMap360.Constants;
using UniMap360.Models;

namespace UniMap360.Services.Posts;

public interface IManagePostsContextService
{
    string? GetCurrentRole(ClaimsPrincipal user);
    Task<HostProfile?> GetCurrentHostAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<EmployerProfile?> GetCurrentEmployerAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<Dictionary<int, object>> LoadLocationMapAsync(IEnumerable<int> locationIds, CancellationToken cancellationToken = default);
}

public sealed class ManagePostsContextService : IManagePostsContextService
{
    private readonly UniMap360ProContext _context;

    public ManagePostsContextService(UniMap360ProContext context)
    {
        _context = context;
    }

    public string? GetCurrentRole(ClaimsPrincipal user)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return role.Trim().ToLowerInvariant() switch
        {
            "host" => AppRoles.Host,
            "employer" => AppRoles.Employer,
            _ => null
        };
    }

    public async Task<HostProfile?> GetCurrentHostAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (!TryGetAccountId(user, out var accountId))
        {
            return null;
        }

        return await _context.HostProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.AccountId == accountId, cancellationToken);
    }

    public async Task<EmployerProfile?> GetCurrentEmployerAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (!TryGetAccountId(user, out var accountId))
        {
            return null;
        }

        var existing = await _context.EmployerProfiles
            .FirstOrDefaultAsync(e => e.AccountId == accountId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);
        if (account is null || !string.Equals(account.UserRole, AppRoles.Employer, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var newProfile = new EmployerProfile
        {
            AccountId = accountId,
            CompanyName = BuildDefaultEmployerCompanyName(account.Email, accountId),
            TaxCode = BuildTemporaryEmployerTaxCode(accountId)
        };

        _context.EmployerProfiles.Add(newProfile);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return newProfile;
        }
        catch (DbUpdateException)
        {
            return await _context.EmployerProfiles.FirstOrDefaultAsync(e => e.AccountId == accountId, cancellationToken);
        }
    }

    public async Task<Dictionary<int, object>> LoadLocationMapAsync(IEnumerable<int> locationIds, CancellationToken cancellationToken = default)
    {
        var ids = locationIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, object>();
        }

        var locations = await _context.Locations
            .AsNoTracking()
            .Where(l => ids.Contains(l.LocationId))
            .Select(l => new
            {
                l.LocationId,
                l.AddressText,
                l.ProvinceCode,
                l.ProvinceName,
                l.DistrictCode,
                l.DistrictName,
                l.WardCode,
                l.WardName,
                l.HouseNumber,
                l.Street,
                l.Coordinates
            })
            .ToListAsync(cancellationToken);

        return locations.ToDictionary(
            x => x.LocationId,
            x =>
            {
                var point = x.Coordinates as Point;
                return (object)new
                {
                    x.LocationId,
                    x.AddressText,
                    x.ProvinceCode,
                    x.ProvinceName,
                    x.DistrictCode,
                    x.DistrictName,
                    x.WardCode,
                    x.WardName,
                    x.HouseNumber,
                    x.Street,
                    latitude = point?.Y,
                    longitude = point?.X,
                    displayName = BuildLocationDisplayName(new UniMap360.Models.Location
                    {
                        LocationId = x.LocationId,
                        AddressText = x.AddressText,
                        ProvinceName = x.ProvinceName,
                        DistrictName = x.DistrictName,
                        WardName = x.WardName,
                        HouseNumber = x.HouseNumber,
                        Street = x.Street
                    })
                };
            });
    }

    private static bool TryGetAccountId(ClaimsPrincipal user, out int accountId)
    {
        var accountIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out accountId);
    }

    private static string BuildDefaultEmployerCompanyName(string email, int accountId)
    {
        var alias = email.Split('@', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = $"Employer {accountId}";
        }

        var companyName = $"Doanh nghiệp {alias}";
        return companyName.Length > 255 ? companyName[..255] : companyName;
    }

    private static string BuildTemporaryEmployerTaxCode(int accountId) => $"EMP{accountId:D10}";

    private static string BuildLocationDisplayName(UniMap360.Models.Location location)
    {
        var streetPart = string.Join(' ', new[]
        {
            location.HouseNumber,
            location.Street
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim()));

        var parts = new[]
            {
                string.IsNullOrWhiteSpace(streetPart) ? null : streetPart,
                location.WardName,
                location.DistrictName,
                location.ProvinceName
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parts.Count > 0)
        {
            return string.Join(", ", parts);
        }

        if (!string.IsNullOrWhiteSpace(location.District))
        {
            return location.District.Trim();
        }

        return string.IsNullOrWhiteSpace(location.AddressText)
            ? $"Location #{location.LocationId}"
            : location.AddressText.Trim();
    }
}

