using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniMap360.Options;

namespace UniMap360.Services.Moderation;

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly TelegramSettings _settings;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(
        HttpClient httpClient,
        IOptions<TelegramSettings> settings,
        ILogger<TelegramNotificationService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAlertAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.BotToken) || string.IsNullOrWhiteSpace(_settings.ChatId))
        {
            _logger.LogWarning("Telegram Notification is disabled or configuration is missing.");
            return;
        }

        if (_settings.ChatId == "YOUR_TELEGRAM_CHAT_ID")
        {
            _logger.LogWarning("Telegram Notification ChatId is still set to placeholder. Skipping alert.");
            return;
        }

        try
        {
            // Đảm bảo loại bỏ hoàn toàn các emoji/icon nếu có trong chuỗi đầu vào để bảo toàn băng thông/token
            var cleanMessage = StripEmojis(message);

            var requestUrl = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";
            var requestBody = new
            {
                chat_id = _settings.ChatId,
                text = cleanMessage,
                parse_mode = "HTML"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Telegram API error: {Status} - {Error}", response.StatusCode, errContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred during sending Telegram notification.");
        }
    }

    public async Task SendAlertWithActionsAsync(string message, int reportId, string contentType, int postId, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.BotToken) || string.IsNullOrWhiteSpace(_settings.ChatId))
        {
            _logger.LogWarning("Telegram Notification is disabled or configuration is missing.");
            return;
        }

        if (_settings.ChatId == "YOUR_TELEGRAM_CHAT_ID")
        {
            _logger.LogWarning("Telegram Notification ChatId is still set to placeholder. Skipping alert.");
            return;
        }

        try
        {
            var cleanMessage = StripEmojis(message);

            var requestUrl = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";
            var requestBody = new
            {
                chat_id = _settings.ChatId,
                text = cleanMessage,
                parse_mode = "HTML",
                reply_markup = new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            new { text = "❌ Xóa bài", callback_data = $"mod:del:{reportId}" },
                            new { text = "✅ Bỏ qua", callback_data = $"mod:ok:{reportId}" }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Telegram API error: {Status} - {Error}", response.StatusCode, errContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred during sending Telegram interactive notification.");
        }
    }

    private static string StripEmojis(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogatePair(text, i))
            {
                int codepoint = char.ConvertToUtf32(text, i);
                i++; // Skip the second char of the surrogate pair
                
                // Check if codepoint is an emoji
                bool isEmoji = (codepoint >= 0x1F300 && codepoint <= 0x1F9FF) || 
                               (codepoint >= 0x1F600 && codepoint <= 0x1F64F) || 
                               (codepoint >= 0x1F680 && codepoint <= 0x1F6FF) || 
                               (codepoint >= 0x1F100 && codepoint <= 0x1F1FF) ||
                               (codepoint >= 0x1F200 && codepoint <= 0x1F2FF) ||
                               (codepoint >= 0x1F900 && codepoint <= 0x1F9FF) ||
                               (codepoint >= 0x1FA00 && codepoint <= 0x1FAFF) ||
                               (codepoint >= 0x1F000 && codepoint <= 0x1F0FF);
                               
                if (!isEmoji)
                {
                    sb.Append(char.ConvertFromUtf32(codepoint));
                }
            }
            else
            {
                char c = text[i];
                bool isEmoji = (c >= 0x2600 && c <= 0x26FF) || 
                               (c >= 0x2700 && c <= 0x27BF);
                               
                if (!isEmoji)
                {
                    sb.Append(c);
                }
            }
        }
        return sb.ToString();
    }
}
