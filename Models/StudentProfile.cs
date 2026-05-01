using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class StudentProfile
{
    public int StudentId { get; set; }

    public int AccountId { get; set; }

    public string FullName { get; set; } = null!;

    public string? University { get; set; }

    public string? Major { get; set; }

    public string? Cvlink { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();

    public virtual ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual ICollection<RoommatePost> RoommatePosts { get; set; } = new List<RoommatePost>();
}
