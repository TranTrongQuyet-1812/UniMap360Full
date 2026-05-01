using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniMap360.Constants;
using UniMap360.Models.Api;
using UniMap360.Models.Requests;
using UniMap360.Services.Appointments;

namespace UniMap360.Controllers.Api;

/// <summary>
/// Controller mỏng cho đặt lịch xem phòng — chỉ validate token + gọi service + trả response.
/// </summary>
[ApiController]
public sealed class RoomAppointmentsController : ControllerBase
{
    private readonly IAppointmentService _service;

    public RoomAppointmentsController(IAppointmentService service)
    {
        _service = service;
    }

    [Authorize(Roles = AppRoles.Student)]
    [HttpPost("api/room-appointments")]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Token không hợp lệ.");

        var result = await _service.CreateAppointmentAsync(
            accountId.Value, request.RoomId, request.ScheduledAt,
            request.ContactPhone, request.Note, cancellationToken);

        return ToActionResult(result.Success, result.StatusCode, result.Message,
            new { message = result.Message, appointmentId = result.AppointmentId, status = result.Status });
    }

    [Authorize(Roles = AppRoles.Student)]
    [HttpGet("api/room-appointments/my")]
    public async Task<IActionResult> GetMyAppointments(
        [FromQuery] string? status,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Token không hợp lệ.");

        var result = await _service.GetStudentAppointmentsAsync(accountId.Value, status, limit, cancellationToken);
        if (!result.Success)
            return ToActionResult(false, result.StatusCode, result.Message, null);

        return this.ApiOk(new { total = result.Total, items = result.Items });
    }

    [Authorize(Roles = AppRoles.Host)]
    [HttpGet("api/host/room-appointments")]
    public async Task<IActionResult> GetHostAppointments(
        [FromQuery] string? status,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Token không hợp lệ.");

        var result = await _service.GetHostAppointmentsAsync(accountId.Value, status, limit, cancellationToken);
        if (!result.Success)
            return ToActionResult(false, result.StatusCode, result.Message, null);

        return this.ApiOk(new { total = result.Total, items = result.Items });
    }

    [Authorize(Roles = AppRoles.Host)]
    [HttpPost("api/host/room-appointments/{appointmentId:int}/accept")]
    public Task<IActionResult> AcceptAppointment(int appointmentId, [FromBody] HostUpdateAppointmentRequest? request, CancellationToken cancellationToken = default)
        => HandleUpdateAsync(appointmentId, "Confirmed", request, cancellationToken);

    [Authorize(Roles = AppRoles.Host)]
    [HttpPost("api/host/room-appointments/{appointmentId:int}/reject")]
    public Task<IActionResult> RejectAppointment(int appointmentId, [FromBody] HostUpdateAppointmentRequest? request, CancellationToken cancellationToken = default)
        => HandleUpdateAsync(appointmentId, "Rejected", request, cancellationToken);

    [Authorize(Roles = AppRoles.Host)]
    [HttpPost("api/host/room-appointments/{appointmentId:int}/suggest")]
    public Task<IActionResult> SuggestAnotherTime(int appointmentId, [FromBody] HostUpdateAppointmentRequest? request, CancellationToken cancellationToken = default)
        => HandleUpdateAsync(appointmentId, "Rescheduled", request, cancellationToken);

    // ── Private ──────────────────────────────────────────

    private async Task<IActionResult> HandleUpdateAsync(
        int appointmentId, string targetStatus,
        HostUpdateAppointmentRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new HostUpdateAppointmentRequest();

        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Token không hợp lệ.");

        var result = await _service.UpdateAppointmentStatusAsync(
            accountId.Value, appointmentId, targetStatus,
            request.Response, request.SuggestedAt, cancellationToken);

        return ToActionResult(result.Success, result.StatusCode, result.Message,
            new { message = result.Message, appointmentId = result.AppointmentId, status = result.Status });
    }

    private IActionResult ToActionResult(bool success, int statusCode, string? message, object? data)
    {
        if (success)
            return this.ApiOk(data);

        return statusCode switch
        {
            400 => this.ApiBadRequest(message ?? "Bad request.", "VALIDATION_ERROR"),
            401 => this.ApiUnauthorized(message ?? "Unauthorized."),
            404 => this.ApiNotFound(message ?? "Not found."),
            409 => this.ApiConflict(message ?? "Conflict."),
            _ => StatusCode(statusCode, ApiResponse<object>.Fail(
                new ApiError(message ?? "Internal error.", "INTERNAL_SERVER_ERROR"),
                HttpContext.TraceIdentifier))
        };
    }

    private int? GetCurrentAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }
}

