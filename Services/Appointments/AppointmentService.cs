using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;

namespace UniMap360.Services.Appointments;

public interface IAppointmentService
{
    Task<AppointmentResult> CreateAppointmentAsync(
        int accountId,
        int roomId,
        DateTime scheduledAt,
        string? contactPhone,
        string? note,
        CancellationToken cancellationToken = default);

    Task<AppointmentListResult> GetStudentAppointmentsAsync(
        int accountId,
        string? statusFilter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<AppointmentListResult> GetHostAppointmentsAsync(
        int accountId,
        string? statusFilter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<AppointmentUpdateResult> UpdateAppointmentStatusAsync(
        int accountId,
        int appointmentId,
        string targetStatus,
        string? hostResponse,
        DateTime? suggestedAt,
        CancellationToken cancellationToken = default);
}

public sealed class AppointmentResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Message { get; init; }
    public int? AppointmentId { get; init; }
    public string? Status { get; init; }
}

public sealed class AppointmentListResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Message { get; init; }
    public int Total { get; init; }
    public object[] Items { get; init; } = Array.Empty<object>();
}

public sealed class AppointmentUpdateResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Message { get; init; }
    public int? AppointmentId { get; init; }
    public string? Status { get; init; }
}

public sealed class AppointmentService : IAppointmentService
{
    private readonly UniMap360ProContext _context;
    private readonly ILogger<AppointmentService> _logger;

    public AppointmentService(UniMap360ProContext context, ILogger<AppointmentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<AppointmentResult> CreateAppointmentAsync(
        int accountId, int roomId, DateTime scheduledAt,
        string? contactPhone, string? note,
        CancellationToken cancellationToken)
    {
        if (roomId <= 0)
            return new AppointmentResult { Success = false, StatusCode = 400, Message = "RoomId khong hop le." };

        if (scheduledAt <= DateTime.UtcNow.AddMinutes(15))
            return new AppointmentResult { Success = false, StatusCode = 400, Message = "Vui long chon thoi gian hen sau it nhat 15 phut." };

        var room = await _context.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoomId == roomId, cancellationToken);
        if (room is null)
            return new AppointmentResult { Success = false, StatusCode = 404, Message = "Khong tim thay phong." };

        if (!string.Equals(room.RoomStatus, "Available", StringComparison.OrdinalIgnoreCase))
            return new AppointmentResult { Success = false, StatusCode = 400, Message = "Phong hien khong o trang thai co the dat lich xem." };

        var student = await GetOrCreateStudentProfileAsync(accountId, cancellationToken);

        var appointment = new RoomViewingAppointment
        {
            RoomId = room.RoomId,
            StudentId = student.StudentId,
            HostId = room.HostId,
            ScheduledAt = scheduledAt,
            Status = "Pending",
            ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim(),
            StudentNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        _context.RoomViewingAppointments.Add(appointment);
        await _context.SaveChangesAsync(cancellationToken);

        var hostAccountId = await _context.HostProfiles
            .Where(h => h.HostId == room.HostId)
            .Select(h => (int?)h.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        if (hostAccountId.HasValue)
        {
            _context.Notifications.Add(new Notification
            {
                RecipientAccountId = hostAccountId.Value,
                Type = "room_viewing_request",
                Title = "Co yeu cau dat lich xem phong moi",
                Message = $"Co nguoi dung vua gui yeu cau xem phong \"{room.Title}\".",
                TargetType = ContentTargetTypes.Room,
                TargetId = room.RoomId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                MetaJson = JsonSerializer.Serialize(new
                {
                    appointmentId = appointment.AppointmentId,
                    scheduledAt,
                    contactPhone
                })
            });
            await _context.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Appointment created. AppointmentId={AppointmentId}, RoomId={RoomId}, StudentId={StudentId}",
            appointment.AppointmentId, room.RoomId, student.StudentId);

        return new AppointmentResult
        {
            Success = true,
            Message = "Dat lich xem phong thanh cong.",
            AppointmentId = appointment.AppointmentId,
            Status = appointment.Status
        };
    }

    public async Task<AppointmentListResult> GetStudentAppointmentsAsync(
        int accountId, string? statusFilter, int limit,
        CancellationToken cancellationToken)
    {
        var student = await _context.StudentProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AccountId == accountId, cancellationToken);
        if (student is null)
            return new AppointmentListResult { Success = true, Total = 0 };

        limit = limit <= 0 ? 50 : Math.Min(limit, 200);

        var query = _context.RoomViewingAppointments
            .AsNoTracking()
            .Where(a => a.StudentId == student.StudentId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
            query = query.Where(a => a.Status == statusFilter.Trim());

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new
            {
                a.AppointmentId,
                a.RoomId,
                roomTitle = a.Room.Title,
                a.ScheduledAt,
                a.Status,
                a.ContactPhone,
                a.StudentNote,
                a.HostResponse,
                a.SuggestedAt,
                a.CreatedAt,
                hostName = a.Host.FullName,
                hostPhone = a.Host.Phone
            })
            .ToListAsync(cancellationToken);

        return new AppointmentListResult
        {
            Success = true,
            Total = items.Count,
            Items = items.Cast<object>().ToArray()
        };
    }

    public async Task<AppointmentListResult> GetHostAppointmentsAsync(
        int accountId, string? statusFilter, int limit,
        CancellationToken cancellationToken)
    {
        var host = await _context.HostProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.AccountId == accountId, cancellationToken);
        if (host is null)
            return new AppointmentListResult { Success = false, StatusCode = 404, Message = "Khong tim thay ho so chu tro." };

        limit = limit <= 0 ? 100 : Math.Min(limit, 300);

        var query = _context.RoomViewingAppointments
            .AsNoTracking()
            .Where(a => a.HostId == host.HostId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
            query = query.Where(a => a.Status == statusFilter.Trim());

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new
            {
                a.AppointmentId,
                a.RoomId,
                roomTitle = a.Room.Title,
                a.ScheduledAt,
                a.Status,
                a.ContactPhone,
                a.StudentNote,
                a.HostResponse,
                a.SuggestedAt,
                a.CreatedAt,
                studentId = a.StudentId,
                studentName = a.Student.FullName
            })
            .ToListAsync(cancellationToken);

        return new AppointmentListResult
        {
            Success = true,
            Total = items.Count,
            Items = items.Cast<object>().ToArray()
        };
    }

    public async Task<AppointmentUpdateResult> UpdateAppointmentStatusAsync(
        int accountId, int appointmentId, string targetStatus,
        string? hostResponse, DateTime? suggestedAt,
        CancellationToken cancellationToken)
    {
        var host = await _context.HostProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.AccountId == accountId, cancellationToken);
        if (host is null)
            return new AppointmentUpdateResult { Success = false, StatusCode = 404, Message = "Khong tim thay ho so chu tro." };

        var normalizedStatus = NormalizeAppointmentStatus(targetStatus);
        if (normalizedStatus is null)
            return new AppointmentUpdateResult { Success = false, StatusCode = 400, Message = "Trang thai cap nhat khong hop le." };

        var appointment = await _context.RoomViewingAppointments
            .Include(a => a.Room)
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId, cancellationToken);

        if (appointment is null)
            return new AppointmentUpdateResult { Success = false, StatusCode = 404, Message = "Khong tim thay lich hen." };

        if (appointment.HostId != host.HostId)
            return new AppointmentUpdateResult { Success = false, StatusCode = 403, Message = "Ban khong co quyen cap nhat lich hen nay." };

        if (!string.Equals(appointment.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            return new AppointmentUpdateResult { Success = false, StatusCode = 409, Message = "Lich hen da duoc xu ly truoc do." };

        if (normalizedStatus == "Rescheduled")
        {
            if (!suggestedAt.HasValue)
                return new AppointmentUpdateResult { Success = false, StatusCode = 400, Message = "Vui long chon gio de xuat moi." };

            if (suggestedAt.Value <= DateTime.UtcNow.AddMinutes(15))
                return new AppointmentUpdateResult { Success = false, StatusCode = 400, Message = "Gio de xuat moi phai sau it nhat 15 phut." };
        }

        try
        {
            var now = DateTime.UtcNow;
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            appointment.Status = normalizedStatus;
            appointment.HostResponse = string.IsNullOrWhiteSpace(hostResponse) ? null : hostResponse.Trim();
            appointment.SuggestedAt = normalizedStatus == "Rescheduled" ? suggestedAt : null;
            appointment.UpdatedAt = now;
            await _context.SaveChangesAsync(cancellationToken);

            var studentAccountId = await _context.StudentProfiles
                .Where(s => s.StudentId == appointment.StudentId)
                .Select(s => (int?)s.AccountId)
                .FirstOrDefaultAsync(cancellationToken);

            if (studentAccountId.HasValue)
            {
                _context.Notifications.Add(new Notification
                {
                    RecipientAccountId = studentAccountId.Value,
                    Type = "room_viewing_update",
                    Title = BuildHostDecisionTitle(normalizedStatus),
                    Message = BuildHostDecisionMessage(normalizedStatus, appointment.Room?.Title ?? $"Phong #{appointment.RoomId}"),
                    TargetType = ContentTargetTypes.Room,
                    TargetId = appointment.RoomId,
                    IsRead = false,
                    CreatedAt = now,
                    MetaJson = JsonSerializer.Serialize(new
                    {
                        appointmentId = appointment.AppointmentId,
                        status = normalizedStatus,
                        suggestedAt = appointment.SuggestedAt
                    })
                });
                await _context.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Appointment updated. AppointmentId={AppointmentId}, HostId={HostId}, TargetStatus={TargetStatus}",
                appointmentId, host.HostId, normalizedStatus);

            return new AppointmentUpdateResult
            {
                Success = true,
                Message = "Cap nhat lich hen thanh cong.",
                AppointmentId = appointmentId,
                Status = normalizedStatus
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Update appointment failed. AppointmentId={AppointmentId}, HostId={HostId}",
                appointmentId, host.HostId);
            return new AppointmentUpdateResult { Success = false, StatusCode = 500, Message = "Khong the cap nhat lich hen luc nay. Vui long thu lai." };
        }
    }

    private async Task<StudentProfile> GetOrCreateStudentProfileAsync(int accountId, CancellationToken cancellationToken)
    {
        var student = await _context.StudentProfiles
            .FirstOrDefaultAsync(s => s.AccountId == accountId, cancellationToken);
        if (student is not null) return student;

        var account = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

        var displayName = account?.Email?.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = $"Student {accountId}";

        student = new StudentProfile { AccountId = accountId, FullName = displayName };
        _context.StudentProfiles.Add(student);
        await _context.SaveChangesAsync(cancellationToken);
        return student;
    }

    private static string BuildHostDecisionTitle(string status) => status switch
    {
        "Confirmed" => "Lich xem phong da duoc chap nhan",
        "Rejected" => "Lich xem phong da bi tu choi",
        "Rescheduled" => "Chu tro de xuat gio xem phong moi",
        _ => "Lich xem phong da duoc cap nhat"
    };

    private static string BuildHostDecisionMessage(string status, string roomTitle) => status switch
    {
        "Confirmed" => $"Lich hen xem phong \"{roomTitle}\" da duoc chu tro chap nhan.",
        "Rejected" => $"Lich hen xem phong \"{roomTitle}\" da bi chu tro tu choi.",
        "Rescheduled" => $"Chu tro da de xuat gio moi cho lich xem phong \"{roomTitle}\".",
        _ => $"Lich xem phong \"{roomTitle}\" da duoc cap nhat."
    };

    private static string? NormalizeAppointmentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var value = status.Trim();
        if (value.Equals("Confirmed", StringComparison.OrdinalIgnoreCase)) return "Confirmed";
        if (value.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (value.Equals("Rescheduled", StringComparison.OrdinalIgnoreCase)) return "Rescheduled";
        return null;
    }
}
