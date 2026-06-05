using System;
using System.Collections.Generic;

namespace UniMap360.Models.Api;

public class PostAnalyticsSummaryDto
{
    public int TargetId { get; set; }
    public string TargetType { get; set; } = null!;
    public int TotalViews { get; set; }
    public int TotalListingImpressions { get; set; }
    public int TotalMapImpressions { get; set; }
    public int TotalChats { get; set; }
    public int TotalFavorites { get; set; }
    public int TotalAppointments { get; set; } // for Rooms
    public int TotalApplications { get; set; }  // for Jobs
    public List<DailyMetricDto> DailyMetrics { get; set; } = new();
}

public class DailyMetricDto
{
    public DateTime Date { get; set; }
    public int Views { get; set; }
    public int ListingImpressions { get; set; }
    public int MapImpressions { get; set; }
    public int Chats { get; set; }
    public int Favorites { get; set; }
    public int Appointments { get; set; }
    public int Applications { get; set; }
}

public class OwnerAnalyticsOverviewDto
{
    public int OwnerAccountId { get; set; }
    public int TotalPostsCount { get; set; }
    public int TotalViews { get; set; }
    public int TotalChats { get; set; }
    public int TotalFavorites { get; set; }
    public int TotalAppointments { get; set; }
    public int TotalApplications { get; set; }
    public List<PostMetricSummaryDto> TopPerformingPosts { get; set; } = new();
    public List<DailyMetricDto> DailyMetrics { get; set; } = new();
}

public class PostMetricSummaryDto
{
    public int TargetId { get; set; }
    public string TargetType { get; set; } = null!;
    public string Title { get; set; } = null!;
    public int Views { get; set; }
    public int Chats { get; set; }
    public int Favorites { get; set; }
}
