using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Job
{
    public int JobId { get; set; }

    public int EmployerId { get; set; }

    public int LocationId { get; set; }

    public int CategoryId { get; set; }

    public string JobTitle { get; set; } = null!;

    public string? SalaryRange { get; set; }

    public string? Description { get; set; }

    public string? ContactPhone { get; set; }

    public string? JobType { get; set; }

    public string? JobStatus { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool? IsExternal { get; set; }

    public string? SourceUrl { get; set; }

    public virtual Category Category { get; set; } = null!;

    public virtual EmployerProfile Employer { get; set; } = null!;

    public virtual ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();

    public virtual Location Location { get; set; } = null!;
}
