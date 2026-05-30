using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UniMap360.Models;

namespace UniMap360.Services.Ai
{
    public sealed class AiChatOrchestratorService : IAiChatOrchestratorService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IAiMapToolService _mapToolService;
        private readonly ILogger<AiChatOrchestratorService> _logger;
        private readonly UniMap360ProContext _context;

        public AiChatOrchestratorService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IAiMapToolService mapToolService,
            ILogger<AiChatOrchestratorService> logger,
            UniMap360ProContext context)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _mapToolService = mapToolService;
            _logger = logger;
            _context = context;
        }

        public async Task<AiChatResponse> ProcessQueryAsync(AiChatQueryRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return new AiChatResponse { Success = false, Response = "Tin nhắn không được để trống." };
            }

            var cleanMsg = request.Message.Trim();
            var cleanMsgLower = cleanMsg.ToLowerInvariant();

            // 1. Chặn lệnh đổi vị trí deterministic
            if (cleanMsgLower.Contains("đổi vị trí") || cleanMsgLower.Contains("doi vi tri"))
            {
                return new AiChatResponse
                {
                    Success = true,
                    Response = "Được chứ! Bạn muốn đổi sang vị trí nào? Hãy nhập địa điểm mới cụ thể nhé.",
                    NewState = "waiting_location",
                    UserLat = null,
                    UserLng = null,
                    SelectedRadius = null,
                    SelectedService = null,
                    SearchLat = null,
                    SearchLng = null,
                    SearchLocationName = null,
                    SearchLocationSource = null,
                    NextSuggestions = new List<string> { "📍 Chia sẻ Vị trí GPS", "Đại học Lạc Hồng", "Đại học Đồng Nai", "Làng Đại học Thủ Đức" },
                    IsAiResponse = false,
                    IsFallback = false
                };
            }

            // 2. Chặn thay đổi dịch vụ nhanh ở trạng thái ready
            if (request.State == "ready")
            {
                var msgNoEmoji = Regex.Replace(cleanMsgLower, @"[^\w\s]", "").Trim();
                if (msgNoEmoji == "phòng trọ" || msgNoEmoji == "phong tro" || 
                    msgNoEmoji == "việc làm" || msgNoEmoji == "viec lam" || 
                    msgNoEmoji == "cả hai" || msgNoEmoji == "ca hai" ||
                    msgNoEmoji == "chỉ xem phòng trọ" || msgNoEmoji == "chi xem phong tro" ||
                    msgNoEmoji == "tìm phòng trọ" || msgNoEmoji == "tim phong tro" ||
                    msgNoEmoji == "chỉ xem việc làm" || msgNoEmoji == "chi xem viec lam" ||
                    msgNoEmoji == "tìm việc làm" || msgNoEmoji == "tim viec lam" ||
                    msgNoEmoji == "xem cả hai" || msgNoEmoji == "xem ca hai" ||
                    msgNoEmoji == "quét cả hai" || msgNoEmoji == "quet ca hai")
                {
                    string service = "both";
                    if (msgNoEmoji.Contains("phòng") || msgNoEmoji.Contains("phong")) service = "room";
                    else if (msgNoEmoji.Contains("việc") || msgNoEmoji.Contains("viec")) service = "job";
                    else service = "both";

                    double? latVal = request.SearchLat ?? request.UserLat;
                    double? lngVal = request.SearchLng ?? request.UserLng;
                    if (!latVal.HasValue || !lngVal.HasValue)
                    {
                        return new AiChatResponse
                        {
                            Success = true,
                            Response = "Mình chưa xác định được vị trí tìm kiếm trước đó. Bạn vui lòng nhập địa điểm trước (ví dụ: 'quanh Đại học Đồng Nai') nhé.",
                            NewState = "waiting_location",
                            UserLat = request.UserLat,
                            UserLng = request.UserLng,
                            SelectedService = service,
                            SelectedRadius = request.SelectedRadius ?? 5.0,
                            NextSuggestions = new List<string> { "📍 Chia sẻ Vị trí GPS", "Đại học Lạc Hồng", "Đại học Đồng Nai", "Làng Đại học Thủ Đức" },
                            IsAiResponse = false,
                            IsFallback = false
                        };
                    }

                    double lat = latVal.Value;
                    double lng = lngVal.Value;
                    double radius = request.SelectedRadius ?? 5.0;
                    string source = request.SearchLocationSource ?? (request.SearchLat.HasValue ? "geocoded" : "gps");
                    string label = request.SearchLocationName ?? (source == "gps" ? "Vị trí của bạn" : "Tâm tìm kiếm");

                    var types = new List<string>();
                    if (service == "room") types.Add("room");
                    else if (service == "job") types.Add("job");
                    else { types.Add("room"); types.Add("job"); }

                    var searchResult = await _mapToolService.SearchNearbyListingsAsync(lat, lng, radius, types, ct);
                    if (searchResult.Error != null)
                    {
                        return new AiChatResponse
                        {
                            Success = true,
                            Response = searchResult.Error,
                            NewState = "ready",
                            UserLat = request.UserLat,
                            UserLng = request.UserLng,
                            SelectedService = service,
                            SelectedRadius = radius,
                            SearchLat = lat,
                            SearchLng = lng,
                            SearchLocationName = label,
                            SearchLocationSource = source,
                            NextSuggestions = BuildSuggestions(service, radius),
                            IsAiResponse = false,
                            IsFallback = true
                        };
                    }

                    var results = searchResult.Items;
                    string finalResponse = "";
                    bool isAiResponseFromWorker = false;
                    bool isFallbackFromWorker = false;

                    var readyWorkerUrl = _configuration?["Cloudflare:WorkerUrl"];
                    if (!string.IsNullOrWhiteSpace(readyWorkerUrl))
                    {
                        var readyPlan = new CloudflarePlanningPlan
                        {
                            Intent = "search_nearby_listings",
                            Types = types,
                            RadiusKm = radius,
                            UseCurrentLocation = (source == "gps"),
                            LocationText = label
                        };

                        try
                        {
                            var client = _httpClientFactory.CreateClient();
                            client.Timeout = TimeSpan.FromSeconds(15);

                            var finalPayload = new
                            {
                                mode = "final",
                                message = cleanMsg,
                                plan = readyPlan,
                                toolResult = new
                                {
                                    searchLocationName = label,
                                    radiusKm = radius,
                                    results = results.Select(r => new
                                    {
                                        id = r.Id,
                                        type = r.Type,
                                        title = r.Title,
                                        price = r.Price,
                                        distanceKm = r.DistanceKm,
                                        address = r.Address
                                    }).ToList(),
                                    mapAction = new
                                    {
                                        type = "spotlight",
                                        center = new { lat = lat, lng = lng, label = label, source = source },
                                        radiusKm = radius,
                                        serviceType = service,
                                        types = types
                                    }
                                }
                            };

                            var finalJsonStr = JsonSerializer.Serialize(finalPayload);
                            var finalContent = new StringContent(finalJsonStr, Encoding.UTF8, "application/json");

                            using var resp = await client.PostAsync(readyWorkerUrl, finalContent, ct);
                            if (resp.IsSuccessStatusCode)
                            {
                                var finalResponseString = await resp.Content.ReadAsStringAsync(ct);
                                using var finalDoc = JsonDocument.Parse(finalResponseString);
                                if (finalDoc.RootElement.TryGetProperty("response", out var respProp))
                                {
                                    finalResponse = respProp.GetString() ?? "";
                                }
                                if (finalDoc.RootElement.TryGetProperty("isAiResponse", out var aiProp))
                                {
                                    isAiResponseFromWorker = aiProp.GetBoolean();
                                }
                                else
                                {
                                    isAiResponseFromWorker = !string.IsNullOrWhiteSpace(finalResponse);
                                }
                                if (finalDoc.RootElement.TryGetProperty("isFallback", out var fbProp))
                                {
                                    isFallbackFromWorker = fbProp.GetBoolean();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error calling Cloudflare final mode for ready-state service switch: {Message}", ex.Message);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(finalResponse))
                    {
                        var localResponse = BuildDeterministicResponse(results, label, radius, service);
                        finalResponse = $"⚠️ **AI UniMap360 đang gặp sự cố, hệ thống tạm hiển thị kết quả tìm kiếm phù hợp:**\n\n{localResponse}";
                        isAiResponseFromWorker = false;
                        isFallbackFromWorker = true;
                    }

                    return new AiChatResponse
                    {
                        Success = true,
                        Response = finalResponse,
                        NewState = "ready",
                        UserLat = request.UserLat,
                        UserLng = request.UserLng,
                        SelectedService = service,
                        SelectedRadius = radius,
                        SearchLat = lat,
                        SearchLng = lng,
                        SearchLocationName = label,
                        SearchLocationSource = source,
                        MapAction = new MapActionDto
                        {
                            Type = "spotlight",
                            Center = new MapCenterDto { Lat = lat, Lng = lng, Label = label, Source = source },
                            RadiusKm = radius,
                            ServiceType = service,
                            Types = types
                        },
                        Results = results,
                        NextSuggestions = BuildSuggestions(service, radius),
                        IsAiResponse = isAiResponseFromWorker,
                        IsFallback = isFallbackFromWorker
                    };
                }
            }



            // 3. Gọi Cloudflare Worker ở PLANNING mode
            var workerUrl = _configuration?["Cloudflare:WorkerUrl"];
            if (string.IsNullOrWhiteSpace(workerUrl))
            {
                _logger?.LogWarning("Cloudflare WorkerUrl is missing in configuration. Falling back to local state-machine.");
                return await ProcessLegacyFallbackAsync(request, ct);
            }

            CloudflarePlanningPlan? plan = null;
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                var planningPayload = new
                {
                    mode = "planning",
                    message = cleanMsg,
                    context = new
                    {
                        userLat = request.UserLat,
                        userLng = request.UserLng,
                        state = request.State,
                        selectedService = request.SelectedService,
                        selectedRadius = request.SelectedRadius,
                        searchLat = request.SearchLat,
                        searchLng = request.SearchLng,
                        searchLocationName = request.SearchLocationName,
                        searchLocationSource = request.SearchLocationSource
                    }
                };

                var jsonStr = JsonSerializer.Serialize(planningPayload);
                var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                using var resp = await client.PostAsync(workerUrl, content, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var responseString = await resp.Content.ReadAsStringAsync(ct);
                    var extractedJson = ExtractJson(responseString);
                    if (!string.IsNullOrWhiteSpace(extractedJson))
                    {
                        plan = JsonSerializer.Deserialize<CloudflarePlanningPlan>(extractedJson);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Cloudflare planning mode: {Message}", ex.Message);
            }

            // 4. Nếu không lấy được plan AI, thực hiện fallback về state machine
            if (plan == null || string.IsNullOrWhiteSpace(plan.Intent))
            {
                _logger.LogWarning("Failed to obtain valid AI plan. Executing legacy state-machine fallback.");
                return await ProcessLegacyFallbackAsync(request, ct);
            }

            // 5. Điều hướng theo Plan Intent
            var isRadiusOnlyMessage = Regex.IsMatch(cleanMsg, @"^\s*\d+([.,]\d+)?\s*(km|kilomet|kilometer)?\s*$", RegexOptions.IgnoreCase);
            if (isRadiusOnlyMessage)
            {
                var hasSearchContext = request.SearchLat.HasValue && request.SearchLng.HasValue;
                var hasGpsRadiusContext =
                    request.UserLat.HasValue &&
                    request.UserLng.HasValue &&
                    string.Equals(request.SearchLocationSource, "gps", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(request.State, "waiting_radius", StringComparison.OrdinalIgnoreCase);

                if (!hasSearchContext && !hasGpsRadiusContext)
                {
                    return new AiChatResponse
                    {
                        Success = true,
                        Response = "Bạn muốn tìm quanh địa điểm nào? Hãy nhập địa điểm cụ thể trước, ví dụ: 'quanh Đại học Đồng Nai 10km'.",
                        NewState = "waiting_location",
                        UserLat = request.UserLat,
                        UserLng = request.UserLng,
                        SelectedService = request.SelectedService,
                        SelectedRadius = null,
                        SearchLat = null,
                        SearchLng = null,
                        SearchLocationName = null,
                        SearchLocationSource = null,
                        NextSuggestions = new List<string> { "Đại học Lạc Hồng", "Đại học Đồng Nai", "Làng Đại học Thủ Đức" },
                        IsAiResponse = false,
                        IsFallback = false
                    };
                }
            }

            if (plan.Intent == "ask_missing_info")
            {
                string nextState = request.State;
                if (plan.MissingFields.Contains("location")) nextState = "waiting_location";
                else if (plan.MissingFields.Contains("types")) nextState = "waiting_service";
                else if (plan.MissingFields.Contains("radius")) nextState = "waiting_radius";

                string? service = request.SelectedService;
                if (plan.Types != null && plan.Types.Count > 0)
                {
                    service = plan.Types.Count == 1 ? plan.Types[0] : "both";
                }

                double? searchLat = request.SearchLat;
                double? searchLng = request.SearchLng;
                string? searchLocationName = request.SearchLocationName;
                string? searchLocationSource = request.SearchLocationSource;
                string responseQuestion = plan.Question ?? "Vui lòng cung cấp thêm thông tin.";

                if (plan.MissingFields.Contains("radius"))
                {
                    if (!string.IsNullOrWhiteSpace(plan.LocationText))
                    {
                        var geocodeResult = await _mapToolService.GeocodeLocationAsync(plan.LocationText, ct);
                        if (geocodeResult.Lat.HasValue && geocodeResult.Lng.HasValue)
                        {
                            searchLat = geocodeResult.Lat.Value;
                            searchLng = geocodeResult.Lng.Value;
                            searchLocationName = geocodeResult.DisplayName ?? plan.LocationText;
                            searchLocationSource = "geocoded";
                        }
                        else
                        {
                            nextState = "waiting_location";
                            responseQuestion = geocodeResult.Error ?? $"Mình chưa xác định được vị trí địa lý của '{plan.LocationText}'. Bạn có thể gõ rõ tên đường, quận/huyện, thành phố hơn không?";
                            searchLat = null;
                            searchLng = null;
                            searchLocationName = null;
                            searchLocationSource = null;
                        }
                    }
                    else
                    {
                        if (!(request.SearchLat.HasValue && request.SearchLng.HasValue))
                        {
                            nextState = "waiting_location";
                            responseQuestion = "Bạn muốn tìm quanh địa điểm nào? Hãy nhập địa điểm cụ thể nhé.";
                            searchLat = null;
                            searchLng = null;
                            searchLocationName = null;
                            searchLocationSource = null;
                        }
                    }
                }
                else
                {
                    // Lọc địa điểm thông thường nếu intent không phải thiếu bán kính hoặc có locationText
                    if (!plan.UseCurrentLocation && !string.IsNullOrWhiteSpace(plan.LocationText))
                    {
                        var geocodeResult = await _mapToolService.GeocodeLocationAsync(plan.LocationText, ct);
                        if (geocodeResult.Lat.HasValue && geocodeResult.Lng.HasValue)
                        {
                            searchLat = geocodeResult.Lat.Value;
                            searchLng = geocodeResult.Lng.Value;
                            searchLocationName = geocodeResult.DisplayName ?? plan.LocationText;
                            searchLocationSource = "geocoded";
                        }
                        else
                        {
                            searchLocationName = plan.LocationText;
                            searchLocationSource = "geocoded";
                        }
                    }
                    else if (plan.UseCurrentLocation && request.UserLat.HasValue && request.UserLng.HasValue)
                    {
                        searchLat = request.UserLat.Value;
                        searchLng = request.UserLng.Value;
                        searchLocationName = "Vị trí của bạn";
                        searchLocationSource = "gps";
                    }
                }

                return new AiChatResponse
                {
                    Success = true,
                    Response = responseQuestion,
                    NewState = nextState,
                    UserLat = request.UserLat,
                    UserLng = request.UserLng,
                    SelectedService = service,
                    SelectedRadius = plan.RadiusKm ?? request.SelectedRadius,
                    SearchLat = searchLat,
                    SearchLng = searchLng,
                    SearchLocationName = searchLocationName,
                    SearchLocationSource = searchLocationSource,
                    NextSuggestions = BuildSuggestions(service ?? "both", plan.RadiusKm ?? request.SelectedRadius),
                    IsAiResponse = true,
                    IsFallback = false
                };
            }
            else if (plan.Intent == "search_nearby_listings")
            {
                double searchLat = 0;
                double searchLng = 0;
                string searchLabel = "";
                string searchSource = "";

                // Phân giải tọa độ tâm tìm kiếm
                if (plan.UseCurrentLocation)
                {
                    if (!request.UserLat.HasValue || !request.UserLng.HasValue)
                    {
                        return new AiChatResponse
                        {
                            Success = true,
                            Response = "Mình không truy cập được GPS của bạn. Hãy chia sẻ vị trí hoặc gõ địa điểm cụ thể (ví dụ: 'quanh Đại học Đồng Nai 10km') nhé.",
                            NewState = "waiting_location",
                            UserLat = request.UserLat,
                            UserLng = request.UserLng,
                            SelectedService = request.SelectedService,
                            SelectedRadius = request.SelectedRadius,
                            NextSuggestions = new List<string> { "📍 Chia sẻ Vị trí GPS", "Đại học Lạc Hồng", "Đại học Đồng Nai", "Làng Đại học Thủ Đức" },
                            IsAiResponse = false,
                            IsFallback = false
                        };
                    }
                    searchLat = request.UserLat.Value;
                    searchLng = request.UserLng.Value;
                    searchLabel = "Vị trí của bạn";
                    searchSource = "gps";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(plan.LocationText))
                    {
                        if (request.SearchLat.HasValue && request.SearchLng.HasValue)
                        {
                            searchLat = request.SearchLat.Value;
                            searchLng = request.SearchLng.Value;
                            searchLabel = request.SearchLocationName ?? "Tâm tìm kiếm";
                            searchSource = request.SearchLocationSource ?? "geocoded";
                        }
                        else
                        {
                            return new AiChatResponse
                            {
                                Success = true,
                                Response = "Bạn muốn tìm quanh địa điểm nào? Hãy nhập tên địa danh cụ thể nhé.",
                                NewState = "waiting_location",
                                UserLat = request.UserLat,
                                UserLng = request.UserLng,
                                IsAiResponse = false,
                                IsFallback = false
                            };
                        }
                    }
                    else
                    {
                        var geocodeResult = await _mapToolService.GeocodeLocationAsync(plan.LocationText, ct);
                        if (!geocodeResult.Lat.HasValue || !geocodeResult.Lng.HasValue)
                        {
                            return new AiChatResponse
                            {
                                Success = true,
                                Response = geocodeResult.Error ?? $"Mình chưa xác định được vị trí địa lý của '{plan.LocationText}'. Bạn có thể gõ rõ tên đường, quận/huyện, thành phố hơn không?",
                                NewState = "waiting_location",
                                UserLat = request.UserLat,
                                UserLng = request.UserLng,
                                IsAiResponse = false,
                                IsFallback = false
                            };
                        }
                        searchLat = geocodeResult.Lat.Value;
                        searchLng = geocodeResult.Lng.Value;
                        searchLabel = geocodeResult.DisplayName ?? plan.LocationText;
                        searchSource = "geocoded";
                    }
                }

                // Validate Radius
                if (!plan.RadiusKm.HasValue)
                {
                    string serviceText = plan.Types.Contains("room") && plan.Types.Contains("job")
                        ? "phòng trọ và việc làm"
                        : plan.Types.Contains("room")
                            ? "phòng trọ"
                            : "việc làm";
                    return new AiChatResponse
                    {
                        Success = true,
                        Response = $"Bạn muốn tìm {serviceText} quanh {searchLabel} trong bán kính mấy km?",
                        NewState = "waiting_radius",
                        UserLat = request.UserLat,
                        UserLng = request.UserLng,
                        SelectedService = plan.Types.Count == 1 ? plan.Types[0] : "both",
                        SearchLat = searchLat,
                        SearchLng = searchLng,
                        SearchLocationName = searchLabel,
                        SearchLocationSource = searchSource
                    };
                }

                double radius = plan.RadiusKm.Value;
                bool wasRadiusCapped = false;
                if (radius > 50.0)
                {
                    radius = 50.0;
                    wasRadiusCapped = true;
                }
                else if (radius < 0.5)
                {
                    radius = 0.5;
                }

                // Tìm kiếm dữ liệu thật trong DB
                var searchResult = await _mapToolService.SearchNearbyListingsAsync(searchLat, searchLng, radius, plan.Types, ct);
                if (searchResult.Error != null)
                {
                    string serviceStr = plan.Types.Count == 1 ? plan.Types[0] : "both";
                    return new AiChatResponse
                    {
                        Success = true,
                        Response = searchResult.Error,
                        NewState = "ready",
                        UserLat = request.UserLat,
                        UserLng = request.UserLng,
                        SelectedService = serviceStr,
                        SelectedRadius = radius,
                        SearchLat = searchLat,
                        SearchLng = searchLng,
                        SearchLocationName = searchLabel,
                        SearchLocationSource = searchSource,
                        NextSuggestions = BuildSuggestions(serviceStr, radius),
                        IsAiResponse = false,
                        IsFallback = true
                    };
                }
                var results = searchResult.Items;

                // Gọi Cloudflare Worker ở mode FINAL
                string finalResponse = "";
                bool isAiResponseFromWorker = false;
                bool isFallbackFromWorker = false;

                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(15);

                    var finalPayload = new
                    {
                        mode = "final",
                        message = cleanMsg,
                        plan = plan,
                        toolResult = new
                        {
                            searchLocationName = searchLabel,
                            radiusKm = radius,
                            results = results.Select(r => new
                            {
                                id = r.Id,
                                type = r.Type,
                                title = r.Title,
                                price = r.Price,
                                distanceKm = r.DistanceKm,
                                address = r.Address
                            }).ToList(),
                            mapAction = new
                            {
                                type = "spotlight",
                                center = new { lat = searchLat, lng = searchLng, label = searchLabel, source = searchSource },
                                radiusKm = radius,
                                serviceType = plan.Types.Count == 1 ? plan.Types[0] : "both",
                                types = plan.Types
                            }
                        }
                    };

                    var finalJsonStr = JsonSerializer.Serialize(finalPayload);
                    var finalContent = new StringContent(finalJsonStr, Encoding.UTF8, "application/json");

                    using var resp = await client.PostAsync(workerUrl, finalContent, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        var finalResponseString = await resp.Content.ReadAsStringAsync(ct);
                        using var finalDoc = JsonDocument.Parse(finalResponseString);
                        if (finalDoc.RootElement.TryGetProperty("response", out var respProp))
                        {
                            finalResponse = respProp.GetString() ?? "";
                        }
                        if (finalDoc.RootElement.TryGetProperty("isAiResponse", out var aiProp))
                        {
                            isAiResponseFromWorker = aiProp.GetBoolean();
                        }
                        else
                        {
                            isAiResponseFromWorker = !string.IsNullOrWhiteSpace(finalResponse);
                        }
                        if (finalDoc.RootElement.TryGetProperty("isFallback", out var fbProp))
                        {
                            isFallbackFromWorker = fbProp.GetBoolean();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling Cloudflare final mode: {Message}", ex.Message);
                }

                // Nếu mode final lỗi, fallback về deterministic response
                if (string.IsNullOrWhiteSpace(finalResponse))
                {
                    string serviceStr = plan.Types.Count == 1 ? plan.Types[0] : "both";
                    var localResponse = BuildDeterministicResponse(results, searchLabel, radius, serviceStr);
                    finalResponse = $"⚠️ **AI UniMap360 đang gặp sự cố, hệ thống tạm hiển thị kết quả tìm kiếm phù hợp:**\n\n{localResponse}";
                    isAiResponseFromWorker = false;
                    isFallbackFromWorker = true;
                }

                if (wasRadiusCapped)
                {
                    finalResponse += "\n\n*(Lưu ý: Bán kính đã được giới hạn tối đa 50km để đảm bảo hiệu suất tìm kiếm.)*";
                }

                string finalService = plan.Types.Count == 1 ? plan.Types[0] : "both";

                return new AiChatResponse
                {
                    Success = true,
                    Response = finalResponse,
                    NewState = "ready",
                    UserLat = request.UserLat,
                    UserLng = request.UserLng,
                    SelectedService = finalService,
                    SelectedRadius = radius,
                    SearchLat = searchLat,
                    SearchLng = searchLng,
                    SearchLocationName = searchLabel,
                    SearchLocationSource = searchSource,
                    MapAction = new MapActionDto
                    {
                        Type = "spotlight",
                        Center = new MapCenterDto { Lat = searchLat, Lng = searchLng, Label = searchLabel, Source = searchSource },
                        RadiusKm = radius,
                        ServiceType = finalService,
                        Types = plan.Types
                    },
                    Results = results,
                    NextSuggestions = BuildSuggestions(finalService, radius),
                    IsAiResponse = isAiResponseFromWorker,
                    IsFallback = isFallbackFromWorker
                };
            }
            else // general_help hoặc các intent ngoài phạm vi
            {
                string helpText = "";
                bool isAiHelp = true;
                bool isFallbackHelp = false;
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(15);
                    var payload = new { message = cleanMsg };
                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var resp = await client.PostAsync(workerUrl, content, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        var responseString = await resp.Content.ReadAsStringAsync(ct);
                        using var doc = JsonDocument.Parse(responseString);
                        if (doc.RootElement.TryGetProperty("response", out var respProp))
                        {
                            helpText = respProp.GetString() ?? "";
                        }
                        if (doc.RootElement.TryGetProperty("isAiResponse", out var aiProp))
                        {
                            isAiHelp = aiProp.GetBoolean();
                        }
                        if (doc.RootElement.TryGetProperty("isFallback", out var fbProp))
                        {
                            isFallbackHelp = fbProp.GetBoolean();
                        }
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(helpText))
                {
                    helpText = $"⚠️ **AI UniMap360 đang gặp sự cố, hệ thống tạm hiển thị hướng dẫn:**\n\n{GetLocalBotResponseFallback(cleanMsg)}";
                    isAiHelp = false;
                    isFallbackHelp = true;
                }

                return new AiChatResponse
                {
                    Success = true,
                    Response = helpText,
                    NewState = request.State,
                    UserLat = request.UserLat,
                    UserLng = request.UserLng,
                    SelectedService = request.SelectedService,
                    SelectedRadius = request.SelectedRadius,
                    SearchLat = request.SearchLat,
                    SearchLng = request.SearchLng,
                    SearchLocationName = request.SearchLocationName,
                    SearchLocationSource = request.SearchLocationSource,
                    IsAiResponse = isAiHelp,
                    IsFallback = isFallbackHelp
                };
            }
        }

        private async Task<AiChatResponse> ProcessLegacyFallbackAsync(AiChatQueryRequest request, CancellationToken ct)
        {
            string state = request.State.ToLowerInvariant();
            string responseText = "";
            string nextState = state;
            
            double? lat = null;
            double? lng = null;

            if (request.SearchLat.HasValue && request.SearchLng.HasValue)
            {
                lat = request.SearchLat.Value;
                lng = request.SearchLng.Value;
            }
            else if (request.UserLat.HasValue && request.UserLng.HasValue && request.SearchLocationSource == "gps")
            {
                lat = request.UserLat.Value;
                lng = request.UserLng.Value;
            }
            double? detectedLat = null;
            double? detectedLng = null;
            string? detectedLocationName = null;

            string? selectedService = request.SelectedService;
            double? selectedRadius = request.SelectedRadius;

            string cleanMsg = request.Message.Trim();

            if (lat == null || lng == null)
            {
                if (state != "waiting_location")
                {
                    responseText = "Mình chưa xác định được vị trí tìm kiếm trước đó. Bạn vui lòng nhập địa điểm trước (ví dụ: 'quanh Đại học Đồng Nai') nhé.";
                    nextState = "waiting_location";
                }
                else
                {
                    var geocodeResult = await _mapToolService.GeocodeLocationAsync(cleanMsg, ct);
                    if (geocodeResult.Lat.HasValue && geocodeResult.Lng.HasValue)
                    {
                        lat = geocodeResult.Lat.Value;
                        lng = geocodeResult.Lng.Value;
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
                        responseText = GetLocalBotResponseFallback(cleanMsg) + "\n\n*(💡 Nhắc nhỏ: Hãy nhập địa chỉ cụ thể như **Đại học Đồng Nai**, **Phường Quyết Thắng Biên Hòa**... để tôi quét bản đồ nhé!)*";
                        nextState = "waiting_location";
                    }
                }
            }
            else if (state == "waiting_service")
            {
                string normMsg = cleanMsg.ToLowerInvariant();
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
                    responseText = GetLocalBotResponseFallback(cleanMsg) + "\n\n*(💡 Hãy chọn **Phòng Trọ**, **Việc Làm** hoặc **Cả Hai** để tôi lọc danh sách giúp bạn nhé!)*";
                    nextState = "waiting_service";
                }
            }
            else if (state == "waiting_radius")
            {
                var match = Regex.Match(cleanMsg, @"[0-9]+(?:\.[0-9]+)?");
                if (match.Success && double.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double km))
                {
                    var hasSearchPoint = request.SearchLat.HasValue && request.SearchLng.HasValue;
                    var hasUserPoint = request.UserLat.HasValue && request.UserLng.HasValue;
                    bool canUseLocation = hasSearchPoint || (hasUserPoint && request.SearchLocationSource == "gps");

                    if (canUseLocation)
                    {
                        if (km > 50.0) km = 50.0;
                        else if (km < 0.5) km = 0.5;
                        selectedRadius = km;
                        nextState = "ready";
                    }
                    else
                    {
                        responseText = "Mình chưa xác định được vị trí tìm kiếm trước đó. Bạn vui lòng nhập địa điểm trước (ví dụ: 'quanh Đại học Đồng Nai') nhé.";
                        nextState = "waiting_location";
                    }
                }
                else
                {
                    responseText = GetLocalBotResponseFallback(cleanMsg) + "\n\n*(💡 Bạn hãy nhập một số km cụ thể, ví dụ: **1.5**, **3**, **5**...)*";
                    nextState = "waiting_radius";
                }
            }
            else if (state == "ready")
            {
                string normMsg = cleanMsg.ToLowerInvariant();
                bool isRoom = normMsg.Contains("phòng") || normMsg.Contains("trọ") || normMsg.Contains("phong") || normMsg.Contains("tro") || normMsg.Contains("room") || normMsg.Contains("nhà");
                bool isJob = normMsg.Contains("việc") || normMsg.Contains("làm") || normMsg.Contains("viec") || normMsg.Contains("lam") || normMsg.Contains("job") || normMsg.Contains("tuyển");
                bool isBoth = normMsg.Contains("cả hai") || normMsg.Contains("ca hai") || normMsg.Contains("both") || normMsg.Contains("tất cả") || normMsg.Contains("tat ca");

                if (isBoth || isRoom || isJob)
                {
                    if (isBoth) selectedService = "both";
                    else if (isRoom) selectedService = "room";
                    else if (isJob) selectedService = "job";

                    responseText = ""; 
                    nextState = "ready";
                }
                else
                {
                    responseText = GetLocalBotResponseFallback(cleanMsg);
                    nextState = "ready";
                }
            }

            double? targetLat = request.SearchLat ?? lat;
            double? targetLng = request.SearchLng ?? lng;
            var results = new List<ProximityListingItem>();
            MapActionDto? mapAction = null;
            double? searchLat = targetLat;
            double? searchLng = targetLng;
            string? searchLocationName = request.SearchLocationName ?? detectedLocationName ?? (targetLat.HasValue ? (request.SearchLat.HasValue ? request.SearchLocationName : "Vị trí của bạn") : null);
            string? searchLocationSource = request.SearchLocationSource ?? (detectedLat.HasValue ? "geocoded" : (request.SearchLat.HasValue ? "geocoded" : "gps"));

            if (nextState == "ready" && targetLat != null && targetLng != null && selectedRadius != null && responseText == "")
            {
                searchLat = targetLat.Value;
                searchLng = targetLng.Value;
                searchLocationName = searchLocationName ?? (request.SearchLat.HasValue ? "Tâm tìm kiếm" : "Vị trí của bạn");
                searchLocationSource = searchLocationSource ?? (request.SearchLat.HasValue ? "geocoded" : "gps");

                var types = new List<string>();
                if (selectedService == "room") types.Add("room");
                else if (selectedService == "job") types.Add("job");
                else { types.Add("room"); types.Add("job"); }

                var searchResult = await _mapToolService.SearchNearbyListingsAsync(targetLat.Value, targetLng.Value, selectedRadius.Value, types, ct);
                if (searchResult.Error != null)
                {
                    responseText = searchResult.Error;
                }
                else
                {
                    results = searchResult.Items;
                    responseText = BuildDeterministicResponse(results, searchLocationName, selectedRadius.Value, selectedService ?? "both");
                }
                
                mapAction = new MapActionDto
                {
                    Type = "spotlight",
                    Center = new MapCenterDto { Lat = targetLat.Value, Lng = targetLng.Value, Label = searchLocationName, Source = searchLocationSource },
                    RadiusKm = selectedRadius.Value,
                    ServiceType = selectedService ?? "both",
                    Types = types
                };
            }

            return new AiChatResponse
            {
                Success = true,
                Response = $"⚠️ **AI UniMap360 đang gặp sự cố, hệ thống tạm hiển thị kết quả tìm kiếm phù hợp:**\n\n{responseText}",
                NewState = nextState,
                UserLat = request.UserLat ?? lat,
                UserLng = request.UserLng ?? lng,
                DetectedLat = detectedLat,
                DetectedLng = detectedLng,
                DetectedLocationName = detectedLocationName,
                SelectedService = selectedService,
                SelectedRadius = selectedRadius,
                SearchLat = searchLat,
                SearchLng = searchLng,
                SearchLocationName = searchLocationName,
                SearchLocationSource = searchLocationSource,
                MapAction = mapAction,
                Results = results,
                NextSuggestions = BuildSuggestions(selectedService ?? "both", selectedRadius),
                IsAiResponse = false,
                IsFallback = true
            };
        }

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return text;
        }

        private static string BuildDeterministicResponse(List<ProximityListingItem> results, string locationLabel, double radiusKm, string serviceType)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🎉 **KẾT QUẢ ĐỀ XUẤT CHO BẠN**");
            sb.AppendLine($"📍 Vị trí tìm kiếm: **{locationLabel}**");
            sb.AppendLine($"⚡ Bán kính quét: **{radiusKm} km**");
            sb.AppendLine();

            if (results.Count == 0)
            {
                sb.AppendLine("😔 Hiện tại không có phòng trọ hoặc việc làm nào phù hợp trong bán kính yêu cầu.");
                sb.AppendLine("💡 Bạn có thể thử tăng bán kính tìm kiếm lên lớn hơn (ví dụ: *5 km*, *10 km*).");
            }
            else
            {
                sb.AppendLine($"🔍 Tìm thấy **{results.Count}** địa điểm phù hợp trong vùng spotlight:");
                sb.AppendLine();

                int roomsCount = results.Count(x => x.Type == "room");
                int jobsCount = results.Count(x => x.Type == "job");

                if (roomsCount > 0)
                {
                    sb.AppendLine($"🏠 **Phòng trọ ({roomsCount}):**");
                    foreach (var item in results.Where(x => x.Type == "room").Take(3))
                    {
                        sb.AppendLine($"- **{item.Title}**");
                        sb.AppendLine($"  📍 Cách {locationLabel} **{item.DistanceKm:F1} km** | Giá: `{item.Price}`");
                    }
                    if (roomsCount > 3) sb.AppendLine($"- *Và {roomsCount - 3} phòng trọ khác đang hiển thị trên bản đồ...*");
                    sb.AppendLine();
                }

                if (jobsCount > 0)
                {
                    sb.AppendLine($"💼 **Việc làm ({jobsCount}):**");
                    foreach (var item in results.Where(x => x.Type == "job").Take(3))
                    {
                        sb.AppendLine($"- **{item.Title}**");
                        sb.AppendLine($"  📍 Cách {locationLabel} **{item.DistanceKm:F1} km** | Lương: `{item.Price}`");
                    }
                    if (jobsCount > 3) sb.AppendLine($"- *Và {jobsCount - 3} việc làm khác đang hiển thị trên bản đồ...*");
                    sb.AppendLine();
                }

                sb.AppendLine("🎯 *Bản đồ đã tự động zoom vào khu vực và vẽ vòng tròn bán kính spotlight. Các địa điểm nằm ngoài vùng đã được làm mờ đi để bạn dễ quan sát.*");
            }

            return sb.ToString();
        }

        private static List<string> BuildSuggestions(string service, double? radius)
        {
            var suggestions = new List<string>();
            if (service == "room")
            {
                suggestions.Add("💼 Chỉ xem việc làm");
                suggestions.Add("✨ Xem cả hai");
            }
            else if (service == "job")
            {
                suggestions.Add("🏠 Chỉ xem phòng trọ");
                suggestions.Add("✨ Xem cả hai");
            }
            else
            {
                suggestions.Add("🏠 Chỉ xem phòng trọ");
                suggestions.Add("💼 Chỉ xem việc làm");
            }

            double currentRadius = radius ?? 5.0;
            if (currentRadius < 5.0)
            {
                suggestions.Add("⚡ Quét 5 km");
            }
            else if (currentRadius < 10.0)
            {
                suggestions.Add("⚡ Quét 10 km");
            }
            else
            {
                suggestions.Add("⚡ Quét 20 km");
            }

            suggestions.Add("🔄 Đổi Vị Trí");
            return suggestions;
        }

        private static string GetLocalBotResponseFallback(string message)
        {
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
                return "Tôi là **UniMap360 AI Assistant** - Trợ lý bản đồ thông minh dành cho sinh viên!\n\n" +
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

            return "💡 Tôi đã ghi nhận tin nhắn của bạn. Để tìm kiếm tối ưu, bạn hãy **nhập địa chỉ cụ thể** (ví dụ: *Đại học Công nghệ Đồng Nai*) hoặc **chọn các gợi ý nhanh** ở khung chat bên dưới để tôi hỗ trợ nhé!";
        }
    }
}
