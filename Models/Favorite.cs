using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Favorite
{
    public int FavoriteId { get; set; }

    public int StudentId { get; set; }

    public int TargetId { get; set; }

    public string? TargetType { get; set; }

    public virtual StudentProfile Student { get; set; } = null!;
}
