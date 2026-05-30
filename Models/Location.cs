using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace UniMap360.Models;

public partial class Location
{
    public int LocationId { get; set; }

    public string AddressText { get; set; } = null!;

    public string? ProvinceCode { get; set; }

    public string? ProvinceName { get; set; }

    public string? DistrictCode { get; set; }

    public string? DistrictName { get; set; }

    public string? WardCode { get; set; }

    public string? WardName { get; set; }

    public string? HouseNumber { get; set; }

    public string? Street { get; set; }

    public string? FullAddressNormalized { get; set; }

    public Geometry Coordinates { get; set; } = null!;

    public string? District { get; set; }

    public double? GeocodedLatitude { get; set; }

    public double? GeocodedLongitude { get; set; }

    public double? LocationDistanceMeters { get; set; }

    public string? LocationConfidence { get; set; }

    public bool? LocationSuspicious { get; set; }

    public string? GeocodeSource { get; set; }

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}
