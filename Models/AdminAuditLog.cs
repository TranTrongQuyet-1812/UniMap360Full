using System;

namespace UniMap360.Models;

public partial class AdminAuditLog
{
    public int AuditId { get; set; }

    public int AdminAccountId { get; set; }

    public string Action { get; set; } = null!;

    public string TargetType { get; set; } = null!;

    public int? TargetId { get; set; }

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public string? Reason { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }
}