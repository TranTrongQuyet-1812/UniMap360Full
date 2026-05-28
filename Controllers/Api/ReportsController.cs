using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniMap360.Models.Api;
using UniMap360.Models.Requests;
using UniMap360.Services.Reports;

namespace UniMap360.Controllers.Api;

[Route("api/reports")]
[ApiController]
public class ReportsController : ControllerBase
{
    private readonly IContentReportService _reportService;

    public ReportsController(IContentReportService reportService)
    {
        _reportService = reportService;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> ReportListing(
        [FromBody] CreateReportRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetType) || request.TargetId <= 0)
            return this.ApiBadRequest("Thiếu thông tin bài đăng.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return this.ApiBadRequest("Vui lòng nhập lý do báo cáo.");

        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(accountIdClaim, out var reporterAccountId))
            return this.ApiUnauthorized("Vui lòng đăng nhập để gửi báo cáo.");

        try
        {
            await _reportService.CreateReportAsync(
                reporterAccountId,
                request.TargetType,
                request.TargetId,
                request.Reason,
                cancellationToken);

            return this.ApiOk(new { message = "Cảm ơn bạn đã báo cáo. Quản trị viên sẽ xem xét trong thời gian sớm nhất." });
        }
        catch (KeyNotFoundException ex)
        {
            return this.ApiNotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return this.ApiBadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return this.ApiBadRequest(ex.Message);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            var msg = ex.InnerException?.Message ?? "";
            if (msg.Contains("UQ_ContentReports_Reporter_Target_Active") || msg.Contains("23505"))
            {
                return this.ApiBadRequest("Bạn đã gửi báo cáo cho bài viết này và đang chờ quản trị viên xem xét.");
            }
            return StatusCode(500, new { message = "Lỗi hệ thống khi gửi báo cáo.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hệ thống khi gửi báo cáo.", detail = ex.Message });
        }
    }
}
