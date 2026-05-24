using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace UniMap360.Services.Email;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string body);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private static readonly HttpClient _httpClient = new();

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        var scriptUrl = _configuration["Email:GoogleScriptUrl"];
        if (string.IsNullOrEmpty(scriptUrl))
        {
            throw new Exception("Cấu hình Email:GoogleScriptUrl bị thiếu hoặc để trống.");
        }

        try
        {
            var token = _configuration["Email:GoogleScriptToken"] ?? "UniMap360_Secret_2026";
            var payload = new
            {
                token = token,
                to = toEmail,
                subject = subject,
                body = body,
                name = "UniMap360 Support"
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(scriptUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Không thể gửi email qua Google Script API. Status: {response.StatusCode}, Response: {errContent}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
            {
                var errorMsg = root.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "Unknown error";
                throw new Exception($"Google Script báo lỗi: {errorMsg}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception("Gửi email qua Google Script thất bại.", ex);
        }
    }
}
