using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniMap360.Constants;
using UniMap360.Models.Api;
using UniMap360.Models.Requests;
using UniMap360.Services.Applications;

namespace UniMap360.Controllers.Api;

/// <summary>
/// Controller mỏng cho ứng tuyển việc làm — validate token + gọi service + trả response.
/// </summary>
[ApiController]
public sealed class JobApplicationsController : ControllerBase
{
    private readonly IJobApplicationService _service;

    public JobApplicationsController(IJobApplicationService service)
    {
        _service = service;
    }

    [Authorize(Roles = AppRoles.Student)]
    [HttpPost("api/job-applications")]
    [Consumes("application/json")]
    public Task<IActionResult> CreateFromJson([FromBody] CreateJobAppJsonRequest req, CancellationToken ct = default)
        => CreateCoreAsync(new JobAppPayload { JobId = req.JobId, ContactEmail = req.ContactEmail, ContactPhone = req.ContactPhone, CvUrl = req.CvUrl }, null, ct);

    [Authorize(Roles = AppRoles.Student)]
    [HttpPost("api/job-applications")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> CreateFromForm([FromForm] CreateJobAppFormRequest req, CancellationToken ct = default)
        => CreateCoreAsync(new JobAppPayload { JobId = req.JobId, ContactEmail = req.ContactEmail, ContactPhone = req.ContactPhone, CvUrl = req.CvUrl }, req.CvFile, ct);

    private async Task<IActionResult> CreateCoreAsync(JobAppPayload payload, IFormFile? cvFile, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var r = await _service.CreateApplicationAsync(accountId.Value, payload, cvFile, ct);
        return ToResult(r.Success, r.StatusCode, r.Message,
            new { message = r.Message, applicationId = r.ApplicationId, status = r.Status });
    }

    [Authorize(Roles = AppRoles.Student)]
    [HttpGet("api/job-applications/my")]
    public async Task<IActionResult> GetMy([FromQuery] string? status, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var r = await _service.GetStudentApplicationsAsync(accountId.Value, status, limit, ct);
        return this.ApiOk(new { total = r.Total, items = r.Items });
    }

    [Authorize(Roles = AppRoles.Employer)]
    [HttpGet("api/employer/job-applications")]
    public async Task<IActionResult> GetEmployer([FromQuery] string? status, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var r = await _service.GetEmployerApplicationsAsync(accountId.Value, status, limit, ct);
        if (!r.Success) return ToResult(false, r.StatusCode, r.Message, null);
        return this.ApiOk(new { total = r.Total, items = r.Items });
    }

    [Authorize(Roles = AppRoles.Employer)]
    [HttpPost("api/employer/job-applications/{applicationId:int}/accept")]
    public Task<IActionResult> Accept(int applicationId, CancellationToken ct) => HandleStatusAsync(applicationId, "Accepted", ct);

    [Authorize(Roles = AppRoles.Employer)]
    [HttpPost("api/employer/job-applications/{applicationId:int}/reject")]
    public Task<IActionResult> Reject(int applicationId, CancellationToken ct) => HandleStatusAsync(applicationId, "Rejected", ct);

    private async Task<IActionResult> HandleStatusAsync(int applicationId, string status, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var r = await _service.UpdateStatusAsync(accountId.Value, applicationId, status, ct);
        return ToResult(r.Success, r.StatusCode, r.Message,
            new { message = r.Message, applicationId = r.ApplicationId, status = r.Status });
    }

    [Authorize(Roles = $"{AppRoles.Employer},{AppRoles.Student}")]
    [HttpGet("api/job-applications/{applicationId:int}/cv")]
    public Task<IActionResult> ViewCv(int applicationId, CancellationToken ct) => StreamCvAsync(applicationId, false, ct);

    [Authorize(Roles = $"{AppRoles.Employer},{AppRoles.Student}")]
    [HttpGet("api/job-applications/{applicationId:int}/cv/download")]
    public Task<IActionResult> DownloadCv(int applicationId, CancellationToken ct) => StreamCvAsync(applicationId, true, ct);

    private async Task<IActionResult> StreamCvAsync(int applicationId, bool asAttachment, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return this.ApiUnauthorized("Token không hợp lệ.");

        var role = (User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
            ?? string.Empty).Trim().ToLowerInvariant();

        var r = await _service.GetCvStreamAsync(accountId.Value, role, applicationId, asAttachment, ct);
        if (r.Forbidden) return Forbid();
        if (!r.Success) return ToResult(false, r.StatusCode, r.Message, null);
        if (asAttachment) return File(r.FileBytes!, r.ContentType!, r.FileName!);
        return File(r.FileBytes!, r.ContentType!);
    }

    // ── Helpers ──

    private IActionResult ToResult(bool ok, int code, string? msg, object? data)
    {
        if (ok) return this.ApiOk(data);
        return code switch
        {
            400 => this.ApiBadRequest(msg ?? "Bad request.", "VALIDATION_ERROR"),
            401 => this.ApiUnauthorized(msg ?? "Unauthorized."),
            404 => this.ApiNotFound(msg ?? "Not found."),
            409 => this.ApiConflict(msg ?? "Conflict."),
            _ => StatusCode(code, ApiResponse<object>.Fail(new ApiError(msg ?? "Error.", "INTERNAL_SERVER_ERROR"), HttpContext.TraceIdentifier))
        };
    }

    private int? GetCurrentAccountId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}

