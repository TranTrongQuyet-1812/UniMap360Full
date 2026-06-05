using System.Threading.Tasks;
using UniMap360.Models;

namespace UniMap360.Services.Business;

public interface ISubscriptionService
{
    Task<AccountSubscription?> GetActiveSubscriptionAsync(int accountId);
    Task<AccountSubscription> CreatePaidSubscriptionAsync(int accountId, string planCode);
    Task<AccountSubscription> CreateTrialSubscriptionAsync(int accountId);
    Task<int> CheckAndDeactivateExpiredSubscriptionsAsync();
}
