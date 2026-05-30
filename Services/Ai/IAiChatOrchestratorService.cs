using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace UniMap360.Services.Ai
{
    public interface IAiChatOrchestratorService
    {
        Task<AiChatResponse> ProcessQueryAsync(AiChatQueryRequest request, CancellationToken ct = default);
    }

    public class AiChatQueryRequest
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("state")]
        public string State { get; set; } = "waiting_location";

        [JsonPropertyName("userLat")]
        public double? UserLat { get; set; }

        [JsonPropertyName("userLng")]
        public double? UserLng { get; set; }

        [JsonPropertyName("selectedService")]
        public string? SelectedService { get; set; }

        [JsonPropertyName("selectedRadius")]
        public double? SelectedRadius { get; set; }

        [JsonPropertyName("searchLat")]
        public double? SearchLat { get; set; }

        [JsonPropertyName("searchLng")]
        public double? SearchLng { get; set; }

        [JsonPropertyName("searchLocationName")]
        public string? SearchLocationName { get; set; }

        [JsonPropertyName("searchLocationSource")]
        public string? SearchLocationSource { get; set; }
    }

    public class AiChatResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("response")]
        public string Response { get; set; } = "";

        [JsonPropertyName("newState")]
        public string NewState { get; set; } = "";

        [JsonPropertyName("userLat")]
        public double? UserLat { get; set; }

        [JsonPropertyName("userLng")]
        public double? UserLng { get; set; }

        [JsonPropertyName("detectedLat")]
        public double? DetectedLat { get; set; }

        [JsonPropertyName("detectedLng")]
        public double? DetectedLng { get; set; }

        [JsonPropertyName("detectedLocationName")]
        public string? DetectedLocationName { get; set; }

        [JsonPropertyName("selectedService")]
        public string? SelectedService { get; set; }

        [JsonPropertyName("selectedRadius")]
        public double? SelectedRadius { get; set; }

        [JsonPropertyName("searchLat")]
        public double? SearchLat { get; set; }

        [JsonPropertyName("searchLng")]
        public double? SearchLng { get; set; }

        [JsonPropertyName("searchLocationName")]
        public string? SearchLocationName { get; set; }

        [JsonPropertyName("searchLocationSource")]
        public string? SearchLocationSource { get; set; }

        [JsonPropertyName("mapAction")]
        public MapActionDto? MapAction { get; set; }

        [JsonPropertyName("results")]
        public List<ProximityListingItem> Results { get; set; } = new();

        [JsonPropertyName("nextSuggestions")]
        public List<string> NextSuggestions { get; set; } = new();

        [JsonPropertyName("isAiResponse")]
        public bool IsAiResponse { get; set; }

        [JsonPropertyName("isFallback")]
        public bool IsFallback { get; set; }
    }

    public class MapActionDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "spotlight";

        [JsonPropertyName("center")]
        public MapCenterDto Center { get; set; } = new();

        [JsonPropertyName("radiusKm")]
        public double RadiusKm { get; set; }

        [JsonPropertyName("serviceType")]
        public string ServiceType { get; set; } = "both";

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = new();
    }

    public class MapCenterDto
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = ""; // "gps" or "geocoded"
    }

    // Cloudflare Worker Planning Response
    public class CloudflarePlanningPlan
    {
        [JsonPropertyName("intent")]
        public string Intent { get; set; } = ""; // "search_nearby_listings", "ask_missing_info", "general_help"

        [JsonPropertyName("missingFields")]
        public List<string> MissingFields { get; set; } = new();

        [JsonPropertyName("question")]
        public string? Question { get; set; }

        [JsonPropertyName("locationText")]
        public string? LocationText { get; set; }

        [JsonPropertyName("useCurrentLocation")]
        public bool UseCurrentLocation { get; set; }

        [JsonPropertyName("radiusKm")]
        public double? RadiusKm { get; set; }

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = new();

        [JsonPropertyName("needsTools")]
        public List<string> NeedsTools { get; set; } = new();
    }
}
