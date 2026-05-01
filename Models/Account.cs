using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Account
{
    public int AccountId { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string? UserRole { get; set; }

    public string? AvatarUrl { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public bool IsLocked { get; set; }

    public string? LockedReason { get; set; }

    public DateTime? LockedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual EmployerProfile? EmployerProfile { get; set; }

    public virtual HostProfile? HostProfile { get; set; }

    public virtual StudentProfile? StudentProfile { get; set; }

    public virtual ICollection<ConversationParticipant> ConversationParticipants { get; set; } = new List<ConversationParticipant>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();
}
