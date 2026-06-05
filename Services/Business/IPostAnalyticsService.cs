using System;
using System.Threading.Tasks;
using UniMap360.Models.Api;

namespace UniMap360.Services.Business;

public interface IPostAnalyticsService
{
    Task TrackEventAsync(
        string targetType, 
        int targetId, 
        int ownerAccountId, 
        int? actorAccountId, 
        string eventType, 
        string? sourcePage = null, 
        string? metadataJson = null);

    Task<PostAnalyticsSummaryDto> GetPostAnalyticsSummaryAsync(
        string targetType, 
        int targetId, 
        int ownerAccountId, 
        DateTime startDate, 
        DateTime endDate);

    Task<OwnerAnalyticsOverviewDto> GetOwnerOverviewAsync(
        int ownerAccountId, 
        string role, 
        DateTime startDate, 
        DateTime endDate);
}
