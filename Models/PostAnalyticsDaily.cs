using System;

namespace UniMap360.Models;

public partial class PostAnalyticsDaily
{
    public int Id { get; set; }

    public string TargetType { get; set; } = null!; // "Room", "Job"

    public int TargetId { get; set; }

    public DateTime Date { get; set; }

    public int ViewCount { get; set; }

    public int ListingImpressionCount { get; set; }

    public int MapImpressionCount { get; set; }

    public int ChatStartedCount { get; set; }

    public int FavoriteCount { get; set; }

    public int AppointmentCount { get; set; }

    public int ApplicationCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
