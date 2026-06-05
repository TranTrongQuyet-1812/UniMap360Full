using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class SubscriptionPlan
{
    public int PlanId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string RoleScope { get; set; } = null!; // "Landlord", "Employer", "Business"

    public decimal PriceVnd { get; set; } = 36000;

    public string BillingCycle { get; set; } = "Monthly";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<AccountSubscription> AccountSubscriptions { get; set; } = new List<AccountSubscription>();
}
