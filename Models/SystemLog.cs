using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class SystemLog
{
    public int LogId { get; set; }

    public int? AccountId { get; set; }

    public string? LogAction { get; set; }

    public DateTime? ActionTime { get; set; }

    public virtual Account? Account { get; set; }
}
