using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniMap360.Options;

namespace UniMap360.Services.Moderation;

public class NvidiaNimModerationService : IAiModerationService
{
    private readonly HttpClient _httpClient;
    private readonly NvidiaSettings _settings;
    private readonly ILogger<NvidiaNimModerationService> _logger;

    public NvidiaNimModerationService(
        HttpClient httpClient,
        IOptions<NvidiaSettings> settings,
        ILogger<NvidiaNimModerationService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ModerationResult> ModerateContentAsync(
        string title, 
        string? description, 
        string contentType, 
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning("Nvidia AI Moderation is disabled or ApiKey is missing. Content approved by default.");
            return new ModerationResult { IsApproved = true };
        }

        try
        {
            var systemPrompt = "Bạn là AI kiểm duyệt nội dung của UniMap360. Đánh giá tin đăng và trả về JSON thô duy nhất. Không thêm mở đầu, kết luận hay thẻ markdown code block:\n" +
                               "{\n" +
                               "  \"isApproved\": true/false,\n" +
                               "  \"reason\": \"Lý do chi tiết bằng tiếng Việt nếu từ chối, hoặc null\",\n" +
                               "  \"flaggedCategory\": \"Spam/Vulgarity/Off-Topic/Sensitive hoặc null\"\n" +
                               "}";

            var userContent = $"Loại tin: {contentType}\nTiêu đề: {title}\nMô tả: {description ?? "(Không có mô tả)"}";

            var requestBody = new
            {
                model = _settings.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.1,
                max_tokens = 500,
                response_format = new { type = "json_object" }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody), 
                Encoding.UTF8, 
                "application/json"
            );

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Nvidia API error: {Status} - {Error}", response.StatusCode, errContent);
                return new ModerationResult { IsApproved = true }; // Safe fallback
            }

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonString);
            var contentResult = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(contentResult))
            {
                return new ModerationResult { IsApproved = true };
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<ModerationResultJson>(contentResult, options);

            return new ModerationResult
            {
                IsApproved = result?.IsApproved ?? true,
                Reason = result?.Reason,
                FlaggedCategory = result?.FlaggedCategory
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred during content moderation scanning.");
            return new ModerationResult { IsApproved = true }; // Safe fallback
        }
    }

    private class ModerationResultJson
    {
        public bool IsApproved { get; set; }
        public string? Reason { get; set; }
        public string? FlaggedCategory { get; set; }
    }
}
