using System;

namespace UniMap360.Models;

public partial class Notification
{
    public long NotificationId { get; set; }

    public int RecipientAccountId { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? TargetType { get; set; }

    public int? TargetId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public string? MetaJson { get; set; }

    public virtual Account RecipientAccount { get; set; } = null!;
}

