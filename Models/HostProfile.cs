using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class HostProfile
{
    public int HostId { get; set; }

    public int AccountId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Idcard { get; set; }

    public bool? IsVerified { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}
