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
        // 1. Try traditional SMTP first if configured
        var smtpServer = _configuration["Email:SmtpServer"] ?? "smtp.gmail.com";
        var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
        var smtpUser = _configuration["Email:SmtpUser"];
        var smtpPass = _configuration["Email:SmtpPass"];
        var fromEmail = _configuration["Email:FromEmail"] ?? smtpUser;

        bool smtpAttempted = false;
        Exception? smtpException = null;

        if (!string.IsNullOrEmpty(smtpUser) && !string.IsNullOrEmpty(smtpPass))
        {
            smtpAttempted = true;
            try
            {
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

                // Thiết lập timeout 5 giây để tránh bị treo lâu khi nhà mạng/cloud chặn cổng 587
                await client.SendMailAsync(mailMessage).WaitAsync(TimeSpan.FromSeconds(5));
                return; // SMTP Success!
            }
            catch (Exception ex)
            {
                smtpException = ex;
                // Log warning and proceed to fallback
                // We'll write to console or debug diagnostics for tracing
                System.Diagnostics.Debug.WriteLine($"SMTP email sending failed: {ex.Message}. Falling back to Google Script...");
            }
        }

        // 2. Fallback to Google Script Web App
        var scriptUrl = _configuration["Email:GoogleScriptUrl"];
        if (!string.IsNullOrEmpty(scriptUrl))
        {
            try
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
                return; // Google Script Success!
            }
            catch (Exception gasEx)
            {
                if (smtpAttempted && smtpException != null)
                {
                    throw new AggregateException("Both SMTP and Google Script email sending methods failed.", smtpException, gasEx);
                }
                throw new Exception("Google Script email sending failed.", gasEx);
            }
        }

        // If we reach here, either SMTP failed and there was no Google Script fallback, or neither was configured.
        if (smtpAttempted && smtpException != null)
        {
            throw new Exception("SMTP email sending failed and no fallback is configured.", smtpException);
        }

        throw new Exception("Email configuration is missing (both SMTP credentials and GoogleScriptUrl are blank).");
    }
}
