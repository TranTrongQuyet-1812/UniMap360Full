using System;

namespace UniMap360.Models;

public partial class RoomViewingAppointment
{
    public int AppointmentId { get; set; }

    public int RoomId { get; set; }

    public int StudentId { get; set; }

    public int HostId { get; set; }

    public DateTime ScheduledAt { get; set; }

    public string Status { get; set; } = null!;

    public string? ContactPhone { get; set; }

    public string? StudentNote { get; set; }

    public string? HostResponse { get; set; }

    public DateTime? SuggestedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual HostProfile Host { get; set; } = null!;

    public virtual Room Room { get; set; } = null!;

    public virtual StudentProfile Student { get; set; } = null!;
}

