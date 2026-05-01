using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class VGlobalMapFeed
{
    public string ItemType { get; set; } = null!;

    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public decimal? Value { get; set; }

    public string AddressText { get; set; } = null!;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string CategoryName { get; set; } = null!;

    public bool? IsExternal { get; set; }

    public string? SourceUrl { get; set; }
}
