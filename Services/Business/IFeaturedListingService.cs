using System.Threading.Tasks;

namespace UniMap360.Services.Business;

public interface IFeaturedListingService
{
    Task<(bool Success, string Message)> PinListingAsync(
        int ownerAccountId, 
        string targetType, 
        int targetId, 
        string featureType, 
        int durationDays = 30);

    Task<(bool Success, string Message)> UnpinListingAsync(
        int ownerAccountId, 
        string targetType, 
        int targetId, 
        string featureType);

    Task<bool> IsFeaturedAsync(string targetType, int targetId, string featureType);
}
