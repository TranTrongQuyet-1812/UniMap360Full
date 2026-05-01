using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Category
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string? CategoryType { get; set; }

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}
