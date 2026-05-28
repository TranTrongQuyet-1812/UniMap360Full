using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMap360.Models;

public partial class RoommatePost
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [Required]
    [StringLength(255)]
    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    [StringLength(50)]
    public string? TargetGender { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? BudgetPerMonth { get; set; }

    [StringLength(500)]
    public string? Habits { get; set; }

    [StringLength(255)]
    public string? AreaPreference { get; set; }

    public bool IsActive { get; set; } = true;

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual StudentProfile Student { get; set; } = null!;
}
