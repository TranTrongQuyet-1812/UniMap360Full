using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using UniMap360.Models;
using UniMap360.Services.Ai;

namespace UniMap360.Controllers.Api
{
    [Route("api/ai-chat")]
    [ApiController]
    [Authorize]
    public class AiChatController : ControllerBase
    {
        private readonly IAiChatOrchestratorService _orchestratorService;

        // Constructor chính sử dụng Dependency Injection
        [ActivatorUtilitiesConstructor]
        public AiChatController(IAiChatOrchestratorService orchestratorService)
        {
            _orchestratorService = orchestratorService;
        }

        // Constructor phụ để duy trì tương thích ngược với các bộ Unit/Integration Tests hiện tại
        public AiChatController(UniMap360ProContext context, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            var toolLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AiMapToolService>.Instance;
            var orchLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AiChatOrchestratorService>.Instance;
            
            var toolService = new AiMapToolService(context, httpClientFactory, toolLogger);
            _orchestratorService = new AiChatOrchestratorService(httpClientFactory, configuration, toolService, orchLogger, context);
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] JsonElement payload)
        {
            // Trích xuất tin nhắn người dùng (hỗ trợ cả hoa/thường)
            string? userMsg = payload.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : 
                              (payload.TryGetProperty("Message", out var msgEl2) ? msgEl2.GetString() : null);

            if (string.IsNullOrWhiteSpace(userMsg))
            {
                return BadRequest(new { success = false, message = "Tin nhắn không được để trống." });
            }

            // Trích xuất trạng thái hiện tại
            string state = "waiting_location";
            if (payload.TryGetProperty("state", out var stateEl) && stateEl.ValueKind == JsonValueKind.String) 
                state = stateEl.GetString() ?? "waiting_location";
            else if (payload.TryGetProperty("State", out var stateEl2) && stateEl2.ValueKind == JsonValueKind.String) 
                state = stateEl2.GetString() ?? "waiting_location";

            // Trích xuất tọa độ GPS người dùng
            double? lat = null;
            if (payload.TryGetProperty("userLat", out var latEl) && latEl.ValueKind == JsonValueKind.Number) lat = latEl.GetDouble();
            else if (payload.TryGetProperty("UserLat", out var latEl2) && latEl2.ValueKind == JsonValueKind.Number) lat = latEl2.GetDouble();

            double? lng = null;
            if (payload.TryGetProperty("userLng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number) lng = lngEl.GetDouble();
            else if (payload.TryGetProperty("UserLng", out var lngEl2) && lngEl2.ValueKind == JsonValueKind.Number) lng = lngEl2.GetDouble();

            // Trích xuất loại hình dịch vụ đang chọn
            string? selectedService = null;
            if (payload.TryGetProperty("selectedService", out var srvEl) && srvEl.ValueKind == JsonValueKind.String) selectedService = srvEl.GetString();
            else if (payload.TryGetProperty("SelectedService", out var srvEl2) && srvEl2.ValueKind == JsonValueKind.String) selectedService = srvEl2.GetString();

            // Trích xuất bán kính tìm kiếm
            double? selectedRadius = null;
            if (payload.TryGetProperty("selectedRadius", out var radEl) && radEl.ValueKind == JsonValueKind.Number) selectedRadius = radEl.GetDouble();
            else if (payload.TryGetProperty("SelectedRadius", out var radEl2) && radEl2.ValueKind == JsonValueKind.Number) selectedRadius = radEl2.GetDouble();

            // Trích xuất tọa độ tâm tìm kiếm từ phía client (phục vụ đồng bộ)
            double? searchLat = null;
            if (payload.TryGetProperty("searchLat", out var sLatEl) && sLatEl.ValueKind == JsonValueKind.Number) searchLat = sLatEl.GetDouble();
            else if (payload.TryGetProperty("SearchLat", out var sLatEl2) && sLatEl2.ValueKind == JsonValueKind.Number) searchLat = sLatEl2.GetDouble();

            double? searchLng = null;
            if (payload.TryGetProperty("searchLng", out var sLngEl) && sLngEl.ValueKind == JsonValueKind.Number) searchLng = sLngEl.GetDouble();
            else if (payload.TryGetProperty("SearchLng", out var sLngEl2) && sLngEl2.ValueKind == JsonValueKind.Number) searchLng = sLngEl2.GetDouble();

            string? searchLocationName = null;
            if (payload.TryGetProperty("searchLocationName", out var sNameEl) && sNameEl.ValueKind == JsonValueKind.String) searchLocationName = sNameEl.GetString();
            else if (payload.TryGetProperty("SearchLocationName", out var sNameEl2) && sNameEl2.ValueKind == JsonValueKind.String) searchLocationName = sNameEl2.GetString();

            string? searchLocationSource = null;
            if (payload.TryGetProperty("searchLocationSource", out var sSrcEl) && sSrcEl.ValueKind == JsonValueKind.String) searchLocationSource = sSrcEl.GetString();
            else if (payload.TryGetProperty("SearchLocationSource", out var sSrcEl2) && sSrcEl2.ValueKind == JsonValueKind.String) searchLocationSource = sSrcEl2.GetString();

            var requestDto = new AiChatQueryRequest
            {
                Message = userMsg,
                State = state,
                UserLat = lat,
                UserLng = lng,
                SelectedService = selectedService,
                SelectedRadius = selectedRadius,
                SearchLat = searchLat,
                SearchLng = searchLng,
                SearchLocationName = searchLocationName,
                SearchLocationSource = searchLocationSource
            };

            var responseDto = await _orchestratorService.ProcessQueryAsync(requestDto);
            if (!responseDto.Success)
            {
                return BadRequest(new { success = false, message = responseDto.Response });
            }

            return Ok(responseDto);
        }
    }
}
