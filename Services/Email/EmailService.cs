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
        if (!string.IsNullOrEmpty(scriptUrl))
        {
            var token = _configuration["Email:GoogleScriptToken"] ?? "UniMap360_Secret_2026";
            var payload = new
            {
                token = token,
                to = toEmail,
                subject = subject,
                body = body
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(scriptUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to send email via Google Script API. Status: {response.StatusCode}, Response: {errContent}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
            {
                var errorMsg = root.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "Unknown error";
                throw new Exception($"Google Script returned failure: {errorMsg}");
            }
            return;
        }

        // Fallback to traditional SMTP
        var smtpServer = _configuration["Email:SmtpServer"] ?? "smtp.gmail.com";
        var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
        var smtpUser = _configuration["Email:SmtpUser"];
        var smtpPass = _configuration["Email:SmtpPass"];
        var fromEmail = _configuration["Email:FromEmail"] ?? smtpUser;

        if (string.IsNullOrEmpty(smtpUser) || string.IsNullOrEmpty(smtpPass))
        {
            throw new Exception("Email configuration is missing (SmtpUser or SmtpPass).");
        }

        using var client = new SmtpClient(smtpServer, smtpPort)
        {
            Credentials = new NetworkCredential(smtpUser, smtpPass),
            EnableSsl = true
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(fromEmail ?? smtpUser, "UniMap360 Support"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        mailMessage.To.Add(toEmail);

        await client.SendMailAsync(mailMessage);
    }
}
