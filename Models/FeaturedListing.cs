using System;

namespace UniMap360.Models;

public partial class FeaturedListing
{
    public int FeaturedListingId { get; set; }

    public int OwnerAccountId { get; set; }

    public string TargetType { get; set; } = null!; // "Room", "Job"

    public int TargetId { get; set; }

    public string FeatureType { get; set; } = null!; // "MapPinned", "ExplorePriority"

    public string Status { get; set; } = null!; // "Active", "Cancelled", "Expired", "PaymentLocked"

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CancelledAt { get; set; }

    public virtual Account OwnerAccount { get; set; } = null!;
}
