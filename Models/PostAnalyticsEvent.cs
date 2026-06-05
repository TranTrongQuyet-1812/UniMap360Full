using System;

namespace UniMap360.Models;

public partial class PostAnalyticsEvent
{
    public int EventId { get; set; }

    public string TargetType { get; set; } = null!; // "Room", "Job"

    public int TargetId { get; set; }

    public int OwnerAccountId { get; set; }

    public int? ActorAccountId { get; set; }

    public string EventType { get; set; } = null!; // "ChatStarted", "AppointmentCreated", "JobApplied"

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public string? SourcePage { get; set; }

    public string? MetadataJson { get; set; }

    public virtual Account OwnerAccount { get; set; } = null!;

    public virtual Account? ActorAccount { get; set; }
}
