using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Review
{
    public int ReviewId { get; set; }

    public int StudentId { get; set; }

    public int TargetId { get; set; }

    public string? TargetType { get; set; }

    public int? Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual StudentProfile Student { get; set; } = null!;
}
