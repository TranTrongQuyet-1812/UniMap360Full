using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniMap360.Options;
using UniMap360.Services.Reports;

namespace UniMap360.Services.Moderation;

public class TelegramBotListenerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramBotListenerService> _logger;
    private int _offset = 0;

    public TelegramBotListenerService(
        IServiceScopeFactory scopeFactory,
        IOptions<TelegramSettings> settings,
        HttpClient httpClient,
        ILogger<TelegramBotListenerService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.BotToken))
        {
            _logger.LogInformation("Telegram Bot Listener is disabled.");
            return;
        }

        _logger.LogInformation("Telegram Bot Listener is starting.");

        // Clear existing updates by calling getUpdates with offset = -1
        try
        {
            await GetUpdatesAsync(-1, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear initial Telegram updates.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updatesJson = await GetUpdatesAsync(_offset, stoppingToken);
                if (updatesJson != null && updatesJson.RootElement.TryGetProperty("result", out var resultElement) && resultElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var update in resultElement.EnumerateArray())
                    {
                        if (update.TryGetProperty("update_id", out var updateIdElement))
                        {
                            _offset = updateIdElement.GetInt32() + 1;
                        }

                        if (update.TryGetProperty("callback_query", out var callbackQuery))
                        {
                            await ProcessCallbackQueryAsync(callbackQuery, stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during Telegram polling getUpdates.");
            }

            await Task.Delay(2000, stoppingToken); // Poll every 2 seconds
        }
    }

    private async Task<JsonDocument?> GetUpdatesAsync(int offset, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{_settings.BotToken}/getUpdates?timeout=10";
        if (offset != 0)
        {
            url += $"&offset={offset}";
        }

        using var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(content);
    }

    private async Task ProcessCallbackQueryAsync(JsonElement callbackQuery, CancellationToken ct)
    {
        try
        {
            string callbackId = callbackQuery.GetProperty("id").GetString()!;
            string fromId = callbackQuery.GetProperty("from").GetProperty("id").GetInt64().ToString();
            string callbackData = callbackQuery.GetProperty("data").GetString()!;

            // Verify security: check if sender is the admin
            if (fromId != _settings.ChatId)
            {
                _logger.LogWarning("Unauthorized callback query attempt from Telegram ID: {TelegramId}", fromId);
                await AnswerCallbackQueryAsync(callbackId, "Bạn không có quyền thực hiện hành động này.", ct);
                return;
            }

            // Parse callback data: "mod:del:123" or "mod:ok:123"
            var parts = callbackData.Split(':');
            if (parts.Length < 3 || parts[0] != "mod") return;

            string actionType = parts[1]; // "del" or "ok"
            if (!int.TryParse(parts[2], out int reportId)) return;

            using var scope = _scopeFactory.CreateScope();
            var reportService = scope.ServiceProvider.GetRequiredService<IContentReportService>();
            var db = scope.ServiceProvider.GetRequiredService<Models.UniMap360ProContext>();

            // Find first active Admin ID to log the action
            var adminAccountId = await db.Accounts
                .Where(a => a.UserRole == "Admin")
                .OrderBy(a => a.AccountId)
                .Select(a => a.AccountId)
                .FirstOrDefaultAsync(ct);

            if (adminAccountId <= 0)
            {
                await AnswerCallbackQueryAsync(callbackId, "Không tìm thấy tài khoản quản trị viên trong hệ thống.", ct);
                return;
            }

            bool success = false;
            string feedbackMsg = "";

            if (actionType == "del")
            {
                success = await reportService.ResolveAsync(adminAccountId, reportId, "Delete", "Đã xử lý xóa qua Telegram", true, null, ct);
                feedbackMsg = success ? "Đã xóa bài đăng vi phạm." : "Không thể xóa bài đăng hoặc đã bị xử lý trước đó.";
            }
            else if (actionType == "ok")
            {
                success = await reportService.DismissAsync(adminAccountId, reportId, "Đã bỏ qua qua Telegram", ct);
                feedbackMsg = success ? "Đã bỏ qua tố cáo." : "Không thể bỏ qua tố cáo hoặc đã bị xử lý trước đó.";
            }

            // Update the telegram message to reflect the action taken
            if (success)
            {
                var messageObj = callbackQuery.GetProperty("message");
                long chatId = messageObj.GetProperty("chat").GetProperty("id").GetInt64();
                int messageId = messageObj.GetProperty("message_id").GetInt32();
                string originalText = messageObj.GetProperty("text").GetString()!;

                string actionLabel = actionType == "del" ? "❌ ĐÃ XÓA BÀI ĐĂNG" : "✅ ĐÃ BỎ QUA BÁO CÁO";
                string updatedText = $"{originalText}\n\n<b>Trạng thái:</b> {actionLabel}";

                await EditMessageTextAsync(chatId, messageId, updatedText, ct);
            }

            await AnswerCallbackQueryAsync(callbackId, feedbackMsg, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing callback query.");
        }
    }

    private async Task AnswerCallbackQueryAsync(string callbackId, string text, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{_settings.BotToken}/answerCallbackQuery";
        var body = new { callback_query_id = callbackId, text = text };
        using var response = await _httpClient.PostAsync(
            url, 
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), 
            ct);
    }

    private async Task EditMessageTextAsync(long chatId, int messageId, string text, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{_settings.BotToken}/editMessageText";
        var body = new
        {
            chat_id = chatId,
            message_id = messageId,
            text = text,
            parse_mode = "HTML"
            // Omit reply_markup to remove buttons
        };
        using var response = await _httpClient.PostAsync(
            url, 
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), 
            ct);
    }
}
