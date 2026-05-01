using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Room
{
    public int RoomId { get; set; }

    public int HostId { get; set; }

    public int LocationId { get; set; }

    public int CategoryId { get; set; }

    public string Title { get; set; } = null!;

    public decimal Price { get; set; }

    public double Area { get; set; }

    public string? Description { get; set; }

    public string? ContactPhone { get; set; }

    public string? RoomStatus { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool? IsExternal { get; set; }

    public string? SourceUrl { get; set; }

    public virtual Category Category { get; set; } = null!;

    public virtual HostProfile Host { get; set; } = null!;

    public virtual Location Location { get; set; } = null!;
}
