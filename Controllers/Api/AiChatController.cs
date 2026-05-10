using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniMap360.Models;

namespace UniMap360.Controllers.Api
{
    [Route("api/ai-chat")]
    [ApiController]
    [AllowAnonymous]
    public class AiChatController : ControllerBase
    {
        private readonly UniMap360ProContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public AiChatController(UniMap360ProContext context, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] JsonElement payload)
        {
            // Parse case-insensitive and cache-resilient manually to prevent ASP.NET 400 Bad Request
            string? userMsg = payload.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : 
                              (payload.TryGetProperty("Message", out var msgEl2) ? msgEl2.GetString() : null);

            if (string.IsNullOrWhiteSpace(userMsg))
            {
                return BadRequest(new { success = false, message = "Tin nhắn không được để trống." });
            }

            string state = "waiting_location";
            if (payload.TryGetProperty("state", out var stateEl) && stateEl.ValueKind == JsonValueKind.String) state = stateEl.GetString() ?? "waiting_location";
            else if (payload.TryGetProperty("State", out var stateEl2) && stateEl2.ValueKind == JsonValueKind.String) state = stateEl2.GetString() ?? "waiting_location";
            
            state = state.ToLowerInvariant();

            double? lat = null;
            if (payload.TryGetProperty("userLat", out var latEl) && latEl.ValueKind == JsonValueKind.Number) lat = latEl.GetDouble();
            else if (payload.TryGetProperty("UserLat", out var latEl2) && latEl2.ValueKind == JsonValueKind.Number) lat = latEl2.GetDouble();

            double? lng = null;
            if (payload.TryGetProperty("userLng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number) lng = lngEl.GetDouble();
            else if (payload.TryGetProperty("UserLng", out var lngEl2) && lngEl2.ValueKind == JsonValueKind.Number) lng = lngEl2.GetDouble();

            string? selectedService = null;
            if (payload.TryGetProperty("selectedService", out var srvEl) && srvEl.ValueKind == JsonValueKind.String) selectedService = srvEl.GetString();
            else if (payload.TryGetProperty("SelectedService", out var srvEl2) && srvEl2.ValueKind == JsonValueKind.String) selectedService = srvEl2.GetString();

            double? selectedRadius = null;
            if (payload.TryGetProperty("selectedRadius", out var radEl) && radEl.ValueKind == JsonValueKind.Number) selectedRadius = radEl.GetDouble();
            else if (payload.TryGetProperty("SelectedRadius", out var radEl2) && radEl2.ValueKind == JsonValueKind.Number) selectedRadius = radEl2.GetDouble();

            userMsg = userMsg.Trim();

            string responseText = "";
            string nextState = state;
            double? detectedLat = null;
            double? detectedLng = null;
            string? detectedLocationName = null;

            // ==========================================
            // CƠ CHẾ STATE MACHINE CHO CHATBOT AI
            // ==========================================

            // Khởi tạo/Trạng thái 1: Chưa có tọa độ, cần lấy vị trí của người dùng
            if (lat == null || lng == null)
            {
                // Thử Geocode tin nhắn của người dùng xem có phải địa chỉ không
                var geocodeResult = await TryGeocodeAddressAsync(userMsg);
                if (geocodeResult != null)
                {
                    lat = geocodeResult.Lat;
                    lng = geocodeResult.Lng;
                    detectedLat = lat;
                    detectedLng = lng;
                    detectedLocationName = geocodeResult.DisplayName;

                    responseText = $"📍 Tôi đã xác định được vị trí của bạn tại **{geocodeResult.DisplayName}**.\n\n" +
                                   $"Bạn muốn tìm gì xung quanh đây?\n" +
                                   $"*(Vui lòng chọn hoặc gõ: **Phòng Trọ**, **Việc Làm** hoặc **Cả Hai**)*";
                    nextState = "waiting_service";
                }
                else
                {
                    // FALLBACK THÔNG MINH: Nếu không nhận diện được địa chỉ, gửi câu hỏi lên Cloudflare Workers AI để trả lời thông minh!
                    // Giúp sinh viên chém gió, hỏi han thoải mái mà không bị gò bó.
                    var aiResponse = await CallCloudflareWorkerAiAsync(userMsg);
                    responseText = aiResponse + "\n\n*(💡 Nhắc nhỏ: Hãy nhập địa chỉ cụ thể như **Đại học Đồng Nai**, **Phường Quyết Thắng Biên Hòa**... để tôi quét bản đồ cận lộ nhé!)*";
                    nextState = "waiting_location";
                }
            }
            // Trạng thái 2: Đang chờ chọn loại hình (Phòng trọ / Việc làm / Cả hai)
            else if (state == "waiting_service")
            {
                string normMsg = userMsg.ToLowerInvariant();
                
                // Hỗ trợ kiểm tra cả chuỗi có dấu và không dấu
                bool isRoom = normMsg.Contains("phòng") || normMsg.Contains("trọ") || normMsg.Contains("phong") || normMsg.Contains("tro") || normMsg.Contains("room") || normMsg.Contains("nhà");
                bool isJob = normMsg.Contains("việc") || normMsg.Contains("làm") || normMsg.Contains("viec") || normMsg.Contains("lam") || normMsg.Contains("job") || normMsg.Contains("tuyển");
                bool isBoth = normMsg.Contains("cả hai") || normMsg.Contains("ca hai") || normMsg.Contains("both") || normMsg.Contains("tất cả") || normMsg.Contains("tat ca");

                if (isBoth)
                {
                    selectedService = "both";
                    responseText = "✨ Bạn đã chọn tìm **Cả Phòng Trọ & Việc Làm**.\n\n" +
                                   "Tiếp theo, bạn muốn tìm trong **bán kính bao nhiêu km**?\n" +
                                   "*(Vui lòng chọn các gợi ý bên dưới hoặc tự nhập số km mong muốn)*";
                    nextState = "waiting_radius";
                }
                else if (isRoom)
                {
                    selectedService = "room";
                    responseText = "🏠 Bạn đã chọn tìm **Phòng Trọ**.\n\n" +
                                   "Tiếp theo, bạn muốn tìm trong **bán kính bao nhiêu km**?\n" +
                                   "*(Vui lòng chọn các gợi ý bên dưới hoặc tự nhập số km mong muốn)*";
                    nextState = "waiting_radius";
                }
                else if (isJob)
                {
                    selectedService = "job";
                    responseText = "💼 Bạn đã chọn tìm **Việc Làm**.\n\n" +
                                   "Tiếp theo, bạn muốn tìm trong **bán kính bao nhiêu km**?\n" +
                                   "*(Vui lòng chọn các gợi ý bên dưới hoặc tự nhập số km mong muốn)*";
                    nextState = "waiting_radius";
                }
                else
                {
                    // Nhận diện chém gió khi đang ở bước chọn dịch vụ
                    var aiResponse = await CallCloudflareWorkerAiAsync(userMsg);
                    responseText = aiResponse + "\n\n*(💡 Hãy chọn **Phòng Trọ**, **Việc Làm** hoặc **Cả Hai** để tôi lọc danh sách giúp bạn nhé!)*";
                    nextState = "waiting_service";
                }
            }
            // Trạng thái 3: Đang chờ chọn bán kính
            else if (state == "waiting_radius")
            {
                var match = Regex.Match(userMsg, @"[0-9]+(?:\.[0-9]+)?");
                if (match.Success && double.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double km))
                {
                    selectedRadius = km;
                    nextState = "ready";
                }
                else
                {
                    // Nhận diện chém gió khi đang ở bước chọn bán kính
                    var aiResponse = await CallCloudflareWorkerAiAsync(userMsg);
                    responseText = aiResponse + "\n\n*(💡 Bạn hãy nhập một số km cụ thể, ví dụ: **1.5**, **3**, **5**...)*";
                    nextState = "waiting_radius";
                }
            }
            // Trạng thái 4: Đã hoàn thành quét hoặc chat tự do tiếp theo
            else if (state == "ready")
            {
                string normMsg = userMsg.ToLowerInvariant();
                bool isRoom = normMsg.Contains("phòng") || normMsg.Contains("trọ") || normMsg.Contains("phong") || normMsg.Contains("tro") || normMsg.Contains("room") || normMsg.Contains("nhà");
                bool isJob = normMsg.Contains("việc") || normMsg.Contains("làm") || normMsg.Contains("viec") || normMsg.Contains("lam") || normMsg.Contains("job") || normMsg.Contains("tuyển");
                bool isBoth = normMsg.Contains("cả hai") || normMsg.Contains("ca hai") || normMsg.Contains("both") || normMsg.Contains("tất cả") || normMsg.Contains("tat ca");

                if (isBoth || isRoom || isJob)
                {
                    if (isBoth) selectedService = "both";
                    else if (isRoom) selectedService = "room";
                    else if (isJob) selectedService = "job";

                    // Đã có sẵn lat, lng, radius từ state cũ -> Chạy lại tìm kiếm ngay lập tức!
                    responseText = ""; 
                    nextState = "ready";
                }
                else
                {
                    // Khi đã hoàn thành hoặc người dùng tiếp tục chat chém gió, gọi thẳng lên Cloudflare AI
                    responseText = await CallCloudflareWorkerAiAsync(userMsg);
                    nextState = "ready";
                }
            }

            // Trạng thái 4 thực tế (Kích hoạt quét bản đồ spotlight khi đầy đủ tham số)
            if (nextState == "ready" && lat != null && lng != null && selectedRadius != null && responseText == "")
            {
                var searchResults = await GetProximityItemsAsync(lat.Value, lng.Value, selectedRadius.Value, selectedService ?? "both");

                var sb = new StringBuilder();
                sb.AppendLine($"🎉 **KẾT QUẢ ĐỀ XUẤT CHO BẠN**");
                sb.AppendLine($"📍 Vị trí của bạn: `[{lat.Value:F5}, {lng.Value:F5}]`");
                sb.AppendLine($"⚡ Bán kính tìm kiếm: **{selectedRadius.Value} km**");
                sb.AppendLine();

                if (searchResults.Count == 0)
                {
                    sb.AppendLine("😔 Hiện tại không có phòng trọ hoặc việc làm nào phù hợp trong bán kính yêu cầu.");
                    sb.AppendLine("💡 Bạn có thể thử tăng bán kính tìm kiếm lên lớn hơn (ví dụ: *5 km*, *10 km*).");
                }
                else
                {
                    sb.AppendLine($"🔍 Tìm thấy **{searchResults.Count}** địa điểm phù hợp trong vùng spotlight:");
                    sb.AppendLine();

                    int roomsCount = searchResults.Count(x => x.Type == "room");
                    int jobsCount = searchResults.Count(x => x.Type == "job");

                    if (roomsCount > 0)
                    {
                        sb.AppendLine($"🏠 **Phòng trọ gần bạn ({roomsCount}):**");
                        foreach (var item in searchResults.Where(x => x.Type == "room").Take(3))
                        {
                            sb.AppendLine($"- **{item.Title}**");
                            sb.AppendLine($"  📍 Cách bạn **{item.Distance:F1} km** | Giá: `{item.Price}`");
                        }
                        if (roomsCount > 3) sb.AppendLine($"- *Và {roomsCount - 3} phòng trọ khác đang hiển thị trên bản đồ...*");
                        sb.AppendLine();
                    }

                    if (jobsCount > 0)
                    {
                        sb.AppendLine($"💼 **Việc làm gần bạn ({jobsCount}):**");
                        foreach (var item in searchResults.Where(x => x.Type == "job").Take(3))
                        {
                            sb.AppendLine($"- **{item.Title}**");
                            sb.AppendLine($"  📍 Cách bạn **{item.Distance:F1} km** | Lương: `{item.Price}`");
                        }
                        if (jobsCount > 3) sb.AppendLine($"- *Và {jobsCount - 3} việc làm khác đang hiển thị trên bản đồ...*");
                        sb.AppendLine();
                    }

                    sb.AppendLine("🎯 *Bản đồ đã tự động zoom vào khu vực và vẽ vòng tròn bán kính spotlight xung quanh bạn. Các địa điểm nằm ngoài vùng đã được làm mờ đi để bạn dễ quan sát.*");
                }

                responseText = sb.ToString();
                nextState = "ready";
            }

            // HỖ TRỢ ĐỔI LẠI VỊ TRÍ NẾU HỌ MUỐN
            if (userMsg.ToLowerInvariant().Contains("đổi vị trí") || userMsg.ToLowerInvariant().Contains("doi vi tri"))
            {
                responseText = "🤖 Được chứ! Bạn muốn đổi sang vị trí nào? Hãy nhập địa điểm mới cụ thể nhé.";
                nextState = "waiting_location";
                lat = null;
                lng = null;
                selectedRadius = null;
                selectedService = null;
            }

            return Ok(new
            {
                success = true,
                response = responseText,
                newState = nextState,
                userLat = lat,
                userLng = lng,
                detectedLat = detectedLat,
                detectedLng = detectedLng,
                detectedLocationName = detectedLocationName,
                selectedService = selectedService,
                selectedRadius = selectedRadius
            });
        }

        private async Task<string> CallCloudflareWorkerAiAsync(string message)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5); // Shorter timeout for much snappier UX fallback

                // Ép buộc AI chỉ trả lời trong phạm vi dự án
                string strictPrompt = $"Chỉ đóng vai là Trợ lý bản đồ UniMap360. Tuyệt đối KHÔNG trả lời kiến thức chung (như code, toán học, lịch sử...). Nếu người dùng hỏi ngoài lề, hãy trả lời: 'Xin lỗi, tôi chỉ hỗ trợ tìm kiếm Phòng Trọ và Việc Làm trên UniMap360.'. Câu hỏi của người dùng: {message}";

                // Gửi JSON chuẩn REST API cực kỳ sạch sẽ
                var payload = new { message = strictPrompt };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var workerUrl = _configuration["Cloudflare:WorkerUrl"];
                if (string.IsNullOrWhiteSpace(workerUrl))
                {
                    return GetLocalBotResponse(message);
                }

                using var resp = await client.PostAsync(workerUrl, content);
                if (resp.IsSuccessStatusCode)
                {
                    var responseString = await resp.Content.ReadAsStringAsync();
                    
                    // Đọc trực tiếp trường "response" từ JSON nhận về từ Cloudflare
                    using var doc = JsonDocument.Parse(responseString);
                    if (doc.RootElement.TryGetProperty("response", out var respProp))
                    {
                        return respProp.GetString() ?? "Xin lỗi, tôi gặp sự cố khi phản hồi câu hỏi của bạn.";
                    }
                    return responseString;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Cloudflare Worker Error: " + ex.Message);
            }
            
            // FALLBACK LÊN KỊCH BẢN CHATBOT NỘI BỘ THÔNG MINH
            return GetLocalBotResponse(message);
        }

        private string GetLocalBotResponse(string message)
        {
            // Remove common punctuation to ensure boundary matching works even with "?", ",", etc.
            var cleanMsg = new string(message.Where(c => !char.IsPunctuation(c)).ToArray());
            string msg = " " + cleanMsg.ToLowerInvariant().Trim() + " ";

            if (msg.Contains(" chào ") || msg.Contains(" hello ") || msg.Contains(" hi ") || msg.Contains(" chao ") || msg.Contains(" xin chào ") || msg.Contains(" hey "))
            {
                return "👋 Xin chào! Tôi là Trợ lý ảo UniMap360 AI. Rất vui được hỗ trợ bạn!\n\n" +
                       "Tôi có thể giúp bạn quét tìm **Phòng Trọ** và **Việc Làm** xung quanh bất kỳ địa điểm nào.\n\n" +
                       "👉 Để bắt đầu, bạn hãy **nhập địa điểm** muốn tìm kiếm (ví dụ: *Đại học Đồng Nai*, *Biên Hòa*) hoặc nhấn nút **Định vị GPS** nhé!";
            }

            if (msg.Contains(" bạn là ai ") || msg.Contains(" ban la ai ") || msg.Contains(" chức năng ") || msg.Contains(" chuc nang ") || msg.Contains(" giúp gì ") || msg.Contains(" giup gi ") || msg.Contains(" làm được gì ") || msg.Contains(" lam duoc gi "))
            {
                return "🤖 Tôi là **UniMap360 AI Assistant** - Trợ lý bản đồ thông minh dành cho sinh viên!\n\n" +
                       "**Chức năng chính của tôi:**\n" +
                       "1. 📍 Nhận diện vị trí qua định vị GPS hoặc tự động tìm địa chỉ thủ công.\n" +
                       "2. 🔍 Lọc nhanh Phòng trọ / Việc làm trong bán kính tùy chọn (1km - 10km...).\n" +
                       "3. 🔦 Kích hoạt hiệu ứng **Spotlight bản đồ** để làm mờ các vị trí ngoài vùng và đo khoảng cách chính xác đến từng mét.\n\n" +
                       "Hãy nhập địa điểm để bắt đầu thử nghiệm nhé!";
            }

            if (msg.Contains(" đổi vị trí ") || msg.Contains(" doi vi tri ") || msg.Contains(" vị trí ") || msg.Contains(" vi tri ") || msg.Contains(" địa chỉ ") || msg.Contains(" dia chi "))
            {
                return "📍 Bạn có thể thay đổi vị trí tìm kiếm bằng cách gõ địa chỉ mới (ví dụ: *Phường Quyết Thắng, Biên Hòa*) hoặc nhấn nút **Đổi Vị Trí** ở bên dưới.";
            }

            if (msg.Contains(" tác giả ") || msg.Contains(" ai tạo ra ") || msg.Contains(" ai tao ra ") || msg.Contains(" admin ") || msg.Contains(" quyết ") || msg.Contains(" quyet "))
            {
                return "👑 Bản đồ thông minh UniMap360 được thiết kế và phát triển bởi **Trần Trọng Quyết**.\n\n" +
                       "Tôi là Trợ lý AI đồng hành cùng bạn trên bản đồ này! Chúc bạn tìm được phòng trọ và việc làm ưng ý nhất nhé!";
            }

            // General Fallback
            return "💡 Tôi đã ghi nhận tin nhắn của bạn. Để tìm kiếm tối ưu, bạn hãy **nhập địa chỉ cụ thể** (ví dụ: *Đại học Công nghệ Đồng Nai*) hoặc **chọn các gợi ý nhanh** ở khung chat bên dưới để tôi hỗ trợ nhé!";
        }

        private async Task<GeocodeCoords?> TryGeocodeAddressAsync(string address)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                
                // Giới hạn tìm kiếm ở Việt Nam
                var query = address + ", Việt Nam";
                var endpoint = "https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=1&q=" + Uri.EscapeDataString(query);
                var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                req.Headers.UserAgent.ParseAdd("UniMap360/1.0 (contact: support@unimap360.local)");

                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return null;

                await using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    var latStr = first.TryGetProperty("lat", out var latProp) ? latProp.GetString() : null;
                    var lonStr = first.TryGetProperty("lon", out var lonProp) ? lonProp.GetString() : null;
                    var displayName = first.TryGetProperty("display_name", out var dnProp) ? dnProp.GetString() : null;

                    if (latStr != null && lonStr != null &&
                        double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng))
                    {
                        // Rút gọn displayName cho đẹp
                        string? shortName = displayName;
                        if (!string.IsNullOrWhiteSpace(displayName))
                        {
                            var parts = displayName.Split(',');
                            if (parts.Length > 2)
                            {
                                shortName = string.Join(",", parts.Take(3)).Trim();
                            }
                        }
                        return new GeocodeCoords { Lat = lat, Lng = lng, DisplayName = shortName ?? "Địa điểm đã quét" };
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi và trả về null
            }
            return null;
        }

        private async Task<List<ProximityItem>> GetProximityItemsAsync(double lat, double lng, double radiusKm, string serviceType)
        {
            var items = new List<ProximityItem>();

            try
            {
                // Sử dụng view VGlobalMapFeeds
                var feedItems = await _context.VGlobalMapFeeds
                    .AsNoTracking()
                    .Where(x => x.Latitude != null && x.Longitude != null)
                    .ToListAsync();

                // Lấy thông tin lương công việc
                var jobIds = feedItems.Where(x => x.ItemType == "Job").Select(x => x.Id).Distinct().ToList();
                var jobSalaries = await _context.Jobs
                    .AsNoTracking()
                    .Where(j => jobIds.Contains(j.JobId))
                    .ToDictionaryAsync(j => j.JobId, j => j.SalaryRange ?? "Thỏa thuận");

                foreach (var item in feedItems)
                {
                    var normalizedType = item.ItemType.ToLowerInvariant();
                    if (serviceType == "room" && normalizedType != "room") continue;
                    if (serviceType == "job" && normalizedType != "job") continue;

                    double itemLat = item.Latitude!.Value;
                    double itemLng = item.Longitude!.Value;

                    double distance = CalculateDistance(lat, lng, itemLat, itemLng);
                    if (distance <= radiusKm)
                    {
                        string displayPrice = "Liên hệ";
                        if (normalizedType == "room" && item.Value.HasValue)
                        {
                            displayPrice = item.Value.Value >= 1000000
                                ? $"{item.Value.Value / 1000000m:0.#} Triệu"
                                : $"{item.Value.Value / 1000m:0}k";
                        }
                        else if (normalizedType == "job")
                        {
                            displayPrice = jobSalaries.GetValueOrDefault(item.Id, "Thỏa thuận");
                        }

                        items.Add(new ProximityItem
                        {
                            Id = item.Id,
                            Type = normalizedType,
                            Title = item.Title,
                            Price = displayPrice,
                            Lat = itemLat,
                            Lng = itemLng,
                            Address = item.AddressText,
                            Distance = distance
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error calculating proximity search: " + ex.Message);
            }

            return items.OrderBy(x => x.Distance).ToList();
        }

        private double CalculateDistance(double lat1, double lng1, double lat2, double lng2)
        {
            double r = 6371.0; // Bán kính Trái Đất theo km
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLng = (lng2 - lng1) * Math.PI / 180.0;

            double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLng / 2.0) * Math.Sin(dLng / 2.0);

            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return r * c;
        }

        public class AiQueryRequest
        {
            [JsonPropertyName("message")]
            public string Message { get; set; } = "";
            [JsonPropertyName("state")]
            public string State { get; set; } = "";
            [JsonPropertyName("userLat")]
            public double? UserLat { get; set; }
            [JsonPropertyName("userLng")]
            public double? UserLng { get; set; }
            [JsonPropertyName("selectedService")]
            public string SelectedService { get; set; } = "";
            [JsonPropertyName("selectedRadius")]
            public double? SelectedRadius { get; set; }
        }

        private class GeocodeCoords
        {
            public double Lat { get; set; }
            public double Lng { get; set; }
            public string DisplayName { get; set; } = "";
        }

        public class ProximityItem
        {
            public int Id { get; set; }
            public string Type { get; set; } = "";
            public string Title { get; set; } = "";
            public string Price { get; set; } = "";
            public double Lat { get; set; }
            public double Lng { get; set; }
            public string Address { get; set; } = "";
            public double Distance { get; set; }
        }
    }
}
