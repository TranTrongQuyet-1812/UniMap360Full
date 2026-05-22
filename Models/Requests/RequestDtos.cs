using System.ComponentModel.DataAnnotations;

namespace UniMap360.Models.Requests;

// ── Auth ────────────────────────────────────────────────

public sealed class GoogleLoginRequest
{
    [Required(ErrorMessage = "Credential là bắt buộc.")]
    public string Credential { get; set; } = string.Empty;
    public string? Role { get; set; }
}

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã OTP là bắt buộc.")]
    public string Otp { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(200, ErrorMessage = "Email không được vượt quá 200 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu từ 6–100 ký tự.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(200, ErrorMessage = "Email không được vượt quá 200 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu từ 6–100 ký tự.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role là bắt buộc.")]
    [RegularExpression("^(Student|Host|Employer)$", ErrorMessage = "Role phải là Student, Host hoặc Employer.")]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã xác thực (OTP) là bắt buộc.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải có đúng 6 chữ số.")]
    public string Otp { get; set; } = string.Empty;
}

public sealed class SendRegisterOtpRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "Mật khẩu hiện tại là bắt buộc.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu mới từ 6–100 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;
}

// ── Room Appointments ───────────────────────────────────

public sealed class CreateAppointmentRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "RoomId không hợp lệ.")]
    public int RoomId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn thời gian hẹn.")]
    public DateTime ScheduledAt { get; set; }

    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    public string? ContactPhone { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? Note { get; set; }
}

public sealed class HostUpdateAppointmentRequest
{
    [StringLength(500, ErrorMessage = "Phản hồi không được vượt quá 500 ký tự.")]
    public string? Response { get; set; }

    public DateTime? SuggestedAt { get; set; }
}

// ── Job Applications ────────────────────────────────────

public sealed class CreateJobAppJsonRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "JobId không hợp lệ.")]
    public int JobId { get; set; }

    [EmailAddress(ErrorMessage = "Email liên hệ không đúng định dạng.")]
    [StringLength(200, ErrorMessage = "Email không được vượt quá 200 ký tự.")]
    public string? ContactEmail { get; set; }

    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    public string? ContactPhone { get; set; }

    [Url(ErrorMessage = "Link CV không hợp lệ.")]
    [StringLength(500, ErrorMessage = "Link CV không được vượt quá 500 ký tự.")]
    public string? CvUrl { get; set; }
}

public sealed class CreateJobAppFormRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "JobId không hợp lệ.")]
    public int JobId { get; set; }

    [EmailAddress(ErrorMessage = "Email liên hệ không đúng định dạng.")]
    [StringLength(200, ErrorMessage = "Email không được vượt quá 200 ký tự.")]
    public string? ContactEmail { get; set; }

    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    public string? ContactPhone { get; set; }

    [Url(ErrorMessage = "Link CV không hợp lệ.")]
    [StringLength(500, ErrorMessage = "Link CV không được vượt quá 500 ký tự.")]
    public string? CvUrl { get; set; }

    public IFormFile? CvFile { get; set; }
}

// ── Reviews ─────────────────────────────────────────────

public sealed class UpsertReviewRequest
{
    [Required(ErrorMessage = "TargetType là bắt buộc.")]
    [RegularExpression("^(room|job)$", ErrorMessage = "TargetType phải là 'room' hoặc 'job'.")]
    public string TargetType { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "TargetId không hợp lệ.")]
    public int TargetId { get; set; }

    [Range(1, 5, ErrorMessage = "Rating phải từ 1 đến 5.")]
    public int Rating { get; set; }

    [StringLength(1000, ErrorMessage = "Bình luận không được vượt quá 1000 ký tự.")]
    public string? Comment { get; set; }
}

// ── Chat ────────────────────────────────────────────────

public sealed class CreateDirectConversationRequest
{
    public int? TargetAccountId { get; set; }

    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(200, ErrorMessage = "Email không được vượt quá 200 ký tự.")]
    public string? TargetEmail { get; set; }
}

public sealed class SendMessageRequest
{
    [StringLength(2000, ErrorMessage = "Tin nhắn không được vượt quá 2000 ký tự.")]
    public string? Content { get; set; }
}

public sealed class MarkConversationReadRequest
{
    public long? LastReadMessageId { get; set; }
}

// ── Admin Users ─────────────────────────────────────────

public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu từ 6–100 ký tự.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role là bắt buộc.")]
    public string Role { get; set; } = string.Empty;

    [StringLength(200)]
    public string? FullName { get; set; }
    [StringLength(200)]
    public string? CompanyName { get; set; }
    [StringLength(200)]
    public string? University { get; set; }
}

public sealed class UpdateUserRequest
{
    [StringLength(200)]
    public string? FullName { get; set; }
    [StringLength(200)]
    public string? CompanyName { get; set; }
    [StringLength(200)]
    public string? University { get; set; }
    [StringLength(200)]
    public string? Major { get; set; }
}

public sealed class ChangeRoleRequest
{
    [Required(ErrorMessage = "Role mới là bắt buộc.")]
    public string NewRole { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed class ActionReasonRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

// ── Admin Moderation ────────────────────────────────────

public sealed class ModerationActionRequest
{
    [StringLength(1000, ErrorMessage = "Lý do không được vượt quá 1000 ký tự.")]
    public string? Reason { get; set; }
}
