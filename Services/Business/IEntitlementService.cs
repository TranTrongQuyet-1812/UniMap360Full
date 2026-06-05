using System.Threading.Tasks;

namespace UniMap360.Services.Business;

public interface IEntitlementService
{
    Task<bool> CanUseFeatureAsync(int accountId, string featureCode);
    Task CheckFeatureAccessAsync(int accountId, string featureCode);
}
