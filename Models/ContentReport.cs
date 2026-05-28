using System;

namespace UniMap360.Models;

public partial class ContentReport
{
    public int ReportId { get; set; }
    public string TargetType { get; set; } = null!;
    public int TargetId { get; set; }
    public int ReporterAccountId { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public string? ResolutionAction { get; set; }
    public string? ResolutionNote { get; set; }
    public int? ReviewedByAdminAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? TargetTitleSnapshot { get; set; }
    public int? OwnerAccountIdSnapshot { get; set; }

    public virtual Account ReporterAccount { get; set; } = null!;
    public virtual Account? ReviewedByAdminAccount { get; set; }
}
