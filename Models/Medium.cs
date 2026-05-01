using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Medium
{
    public int MediaId { get; set; }

    public int TargetId { get; set; }

    public string? TargetType { get; set; }

    public string MediaUrl { get; set; } = null!;

    public bool? IsThumbnail { get; set; }
}
