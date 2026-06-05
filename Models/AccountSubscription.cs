using System;

namespace UniMap360.Models;

public partial class AccountSubscription
{
    public int SubscriptionId { get; set; }

    public int AccountId { get; set; }

    public int PlanId { get; set; }

    public string Status { get; set; } = null!; // "Trialing", "Active", "PastDue", "Expired", "Cancelled"

    public DateTime StartedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Account Account { get; set; } = null!;

    public virtual SubscriptionPlan Plan { get; set; } = null!;
}
