using System.Threading;
using System.Threading.Tasks;

namespace UniMap360.Services.Business;

public interface IBillingSettingsService
{
    Task<bool> IsBillingEnforcedAsync(CancellationToken cancellationToken = default);
    Task SetBillingEnforcedAsync(bool enabled, CancellationToken cancellationToken = default);
}
