using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class EmployerProfile
{
    public int EmployerId { get; set; }

    public int AccountId { get; set; }

    public string CompanyName { get; set; } = null!;

    public string? TaxCode { get; set; }

    public string? Website { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
