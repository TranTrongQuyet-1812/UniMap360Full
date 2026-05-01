using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class JobApplication
{
    public int ApplicationId { get; set; }

    public int StudentId { get; set; }

    public int JobId { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? CvUrl { get; set; }

    public string? CvPublicId { get; set; }

    public string? Status { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual StudentProfile Student { get; set; } = null!;
}
