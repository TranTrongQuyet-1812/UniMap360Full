using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using UniMap360.Models;
using UniMap360.Models.Api;
using UniMap360.Models.Requests;
using Microsoft.Extensions.Caching.Memory;
using UniMap360.Services.Email;

namespace UniMap360.Controllers.Api;

[Route("api/auth")]
[ApiController]
[EnableRateLimiting("AuthRateLimit")]
public class AuthController : ControllerBase
{
    private readonly UniMap360ProContext _context;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IEmailService _emailService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UniMap360ProContext context, IConfiguration configuration, IMemoryCache cache, IEmailService emailService, IHttpClientFactory httpClientFactory, ILogger<AuthController> logger)
    {
        _context = context;
        _configuration = configuration;
        _cache = cache;
        _emailService = emailService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return this.ApiBadRequest("Email và mật khẩu là bắt buộc.", "VALIDATION_ERROR");

        var email = request.Email.Trim().ToLowerInvariant();
        
        if (!email.EndsWith("@gmail.com"))
            return this.ApiBadRequest("Xin lỗi, hiện tại hệ thống chỉ hỗ trợ tài khoản @gmail.com.", "VALIDATION_ERROR");

        // Xác thực mã OTP
        if (!_cache.TryGetValue($"RegisterOTP_{email}", out object? savedOtpObj) || savedOtpObj?.ToString() != request.Otp)
        {
            return this.ApiBadRequest("Mã xác thực (OTP) không hợp lệ hoặc đã hết hạn.");
        }

        var role = NormalizeRole(request.Role);
        if (role is null)
            return this.ApiBadRequest("Role không hợp lệ. Chỉ chấp nhận: Student, Host, Employer.", "VALIDATION_ERROR");

        var exists = await _context.Accounts.AnyAsync(a => a.Email == email);
        if (exists)
            return this.ApiConflict("Email đã tồn tại.");

        var account = new Account
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            UserRole = role,
            IsActive = true,
            IsLocked = false,
            AvatarUrl = $"https://api.dicebear.com/7.x/identicon/svg?seed={Uri.EscapeDataString(email)}",
            CreatedAt = DateTime.UtcNow
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        // Xóa mã OTP khỏi Cache sau khi đăng ký thành công
        _cache.Remove($"RegisterOTP_{email}");

        if (string.Equals(role, "Host", StringComparison.OrdinalIgnoreCase))
        {
            var hostProfile = new HostProfile
            {
                AccountId = account.AccountId,
                FullName = BuildDefaultHostFullName(account.Email, account.AccountId),
                Idcard = BuildTemporaryHostIdCard(account.AccountId),
                IsVerified = false
            };

            _context.HostProfiles.Add(hostProfile);
            await _context.SaveChangesAsync();
        }
        else if (string.Equals(role, "Employer", StringComparison.OrdinalIgnoreCase))
        {
            var employerProfile = new EmployerProfile
            {
                AccountId = account.AccountId,
                CompanyName = BuildDefaultEmployerCompanyName(account.Email, account.AccountId),
                TaxCode = BuildTemporaryEmployerTaxCode(account.AccountId)
            };

            _context.EmployerProfiles.Add(employerProfile);
            await _context.SaveChangesAsync();
        }

        return this.ApiOk(new
        {
            message = "Đăng ký thành công.",
            accountId = account.AccountId,
            email = account.Email,
            role = account.UserRole,
            avatarUrl = account.AvatarUrl
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return this.ApiBadRequest("Email và mật khẩu là bắt buộc.", "VALIDATION_ERROR");

        var email = request.Email.Trim().ToLowerInvariant();
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == email);
        if (account is null)
            return this.ApiUnauthorized("Sai email hoặc mật khẩu.");

        if (!account.IsActive)
            return this.ApiUnauthorized("Tài khoản đã bị vô hiệu hóa.");

        if (account.IsLocked)
            return this.ApiUnauthorized("Tài khoản đang bị khóa.");

        var verified = await VerifyPasswordAsync(account, request.Password);
        if (!verified)
            return this.ApiUnauthorized("Sai email hoặc mật khẩu.");

        account.LastLoginAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var token = GenerateJwt(account);
        SetTokenCookie(token);

        return this.ApiOk(new
        {
            // Vẫn trả về token để tương thích ngược với Frontend, 
            // nhưng Frontend có thể bỏ qua vì Cookie đã tự động xử lý.
            accessToken = token,
            accountId = account.AccountId,
            email = account.Email,
            role = account.UserRole,
            avatarUrl = account.AvatarUrl
        });
    }

    [AllowAnonymous]
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Credential))
            return this.ApiBadRequest("Thiếu token xác thực.");

        try
        {
            // SEC-01 FIX: Validate Google JWT qua Google's OAuth2 tokeninfo API
            // thay vì chỉ parse JWT không verify signature.
            var httpClient = _httpClientFactory.CreateClient();
            var googleResponse = await httpClient.GetAsync(
                $"https://oauth2.googleapis.com/tokeninfo?id_token={request.Credential}");

            if (!googleResponse.IsSuccessStatusCode)
                return this.ApiBadRequest("Token Google không hợp lệ hoặc đã hết hạn.");

            var googleJson = await googleResponse.Content.ReadAsStringAsync();
            var googleData = JsonDocument.Parse(googleJson).RootElement;

            var email = googleData.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
            var name = googleData.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var picture = googleData.TryGetProperty("picture", out var picProp) ? picProp.GetString() : null;

            // Verify audience (client ID) matches our app to prevent token reuse attacks
            var aud = googleData.TryGetProperty("aud", out var audProp) ? audProp.GetString() : null;
            var expectedClientId = _configuration["OAuth:GoogleClientId"];
            if (!string.IsNullOrEmpty(expectedClientId) && aud != expectedClientId)
            {
                _logger.LogWarning("Google JWT audience mismatch: expected {Expected}, got {Actual}", expectedClientId, aud);
                return this.ApiBadRequest("Token Google không hợp lệ.");
            }

            if (string.IsNullOrEmpty(email))
                return this.ApiBadRequest("Không thể lấy email từ Google.");

            email = email.Trim().ToLowerInvariant();

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == email);
            if (account == null)
            {
                if (string.IsNullOrEmpty(request.Role))
                {
                    // Trả về yêu cầu chọn Role thay vì lỗi
                    return this.ApiOk(new
                    {
                        requireRole = true,
                        message = "Vui lòng chọn vai trò để hoàn tất đăng ký."
                    });
                }

                // Kiểm tra Role hợp lệ
                var validRoles = new[] { "Student", "Host", "Employer" };
                if (!validRoles.Contains(request.Role))
                {
                    return this.ApiBadRequest("Vai trò không hợp lệ.");
                }

                // Tạo tài khoản mới với Role được chọn
                account = new Account
                {
                    Email = email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    UserRole = request.Role,
                    IsActive = true,
                    IsLocked = false,
                    AvatarUrl = picture ?? $"https://api.dicebear.com/7.x/identicon/svg?seed={Uri.EscapeDataString(email)}",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Accounts.Add(account);
                await _context.SaveChangesAsync();

                // Tạo Profile tương ứng
                if (request.Role == "Student")
                {
                    _context.StudentProfiles.Add(new StudentProfile { AccountId = account.AccountId, FullName = name ?? "Sinh viên mới" });
                }
                else if (request.Role == "Host")
                {
                    _context.HostProfiles.Add(new HostProfile { AccountId = account.AccountId, FullName = name ?? "Chủ trọ mới" });
                }
                else if (request.Role == "Employer")
                {
                    _context.EmployerProfiles.Add(new EmployerProfile { AccountId = account.AccountId, CompanyName = name ?? "Công ty mới" });
                }
                await _context.SaveChangesAsync();
            }

            account.LastLoginAt = DateTime.UtcNow;
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var token = GenerateJwt(account);
            SetTokenCookie(token);

            return this.ApiOk(new
            {
                accessToken = token,
                accountId = account.AccountId,
                email = account.Email,
                role = account.UserRole,
                avatarUrl = account.AvatarUrl
            });
        }
        catch (Exception)
        {
            return this.ApiBadRequest("Token không hợp lệ.");
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(accountIdClaim, out var accountId))
            return this.ApiUnauthorized("Token không hợp lệ.");

        var account = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == accountId);

        if (account is null)
            return this.ApiNotFound("Không tìm thấy tài khoản.");

        return this.ApiOk(new
        {
            accountId = account.AccountId,
            email = account.Email,
            role = account.UserRole,
            avatarUrl = account.AvatarUrl,
            isActive = account.IsActive,
            isLocked = account.IsLocked,
            createdAt = account.CreatedAt
        });
    }

    [Authorize]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(accountIdClaim, out var accountId))
            return this.ApiUnauthorized("Token không hợp lệ.");

        var account = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == accountId);

        if (account is null)
            return this.ApiNotFound("Không tìm thấy tài khoản.");

        var token = GenerateJwt(account);
        SetTokenCookie(token);

        return this.ApiOk(new
        {
            accessToken = token,
            accountId = account.AccountId,
            email = account.Email,
            role = account.UserRole,
            avatarUrl = account.AvatarUrl
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("unimap360.accessToken", new CookieOptions
        {
            Path = "/",
            Secure = true,
            SameSite = SameSiteMode.Lax
        });
        return this.ApiOk(new { message = "Đã đăng xuất." });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return this.ApiBadRequest("Mật khẩu hiện tại và mật khẩu mới là bắt buộc.", "VALIDATION_ERROR");

        if (request.NewPassword.Length < 8)
            return this.ApiBadRequest("Mật khẩu mới phải có ít nhất 8 ký tự.", "VALIDATION_ERROR");

        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(accountIdClaim, out var accountId))
            return this.ApiUnauthorized("Token không hợp lệ.");

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
        if (account is null)
            return this.ApiNotFound("Không tìm thấy tài khoản.");

        if (!await VerifyPasswordAsync(account, request.CurrentPassword))
            return this.ApiUnauthorized("Mật khẩu hiện tại không đúng.");

        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();

        return this.ApiOk(new { message = "Đổi mật khẩu thành công." });
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == email);
        if (account == null)
            return this.ApiBadRequest("Không tìm thấy tài khoản với Email này.");

        // Tạo OTP 6 số
        // SEC-02 FIX: Dùng RandomNumberGenerator (crypto-safe) thay vì System.Random
        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        // Lưu OTP vào MemoryCache với thời hạn 5 phút
        _cache.Set($"OTP_{email}", otp, TimeSpan.FromMinutes(5));

        try
        {
            // GỬI EMAIL THẬT
            var subject = "[UniMap360] Mã xác thực đặt lại mật khẩu";
            var body = $@"
                <div style='font-family: sans-serif; padding: 20px; border: 1px solid #eee; border-radius: 10px; max-width: 500px;'>
                    <h2 style='color: #780115;'>UniMap360 Security</h2>
                    <p>Chào bạn,</p>
                    <p>Bạn đã yêu cầu đặt lại mật khẩu cho tài khoản UniMap360. Mã xác thực (OTP) của bạn là:</p>
                    <div style='background: #f8f9fa; padding: 15px; text-align: center; font-size: 24px; font-weight: bold; letter-spacing: 5px; color: #780115; border-radius: 5px;'>
                        {otp}
                    </div>
                    <p style='margin-top: 20px;'>Mã này có hiệu lực trong vòng <b>5 phút</b>. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                    <p style='font-size: 12px; color: #999;'>Đây là email tự động, vui lòng không phản hồi.</p>
                </div>";

            await _emailService.SendEmailAsync(email, subject, body);

            return this.ApiOk(new { message = "Mã xác thực đã được gửi tới Email của bạn." });
        }
        catch (Exception ex)
        {
            // Log lỗi nếu cần
            // SEC-07 FIX: Không tiết lộ chi tiết lỗi SMTP cho client
            _logger.LogError(ex, "Không thể gửi email OTP tới {Email}", email);
            return this.ApiBadRequest("Không thể gửi Email. Vui lòng thử lại sau.");
        }
    }

    [AllowAnonymous]
    [HttpPost("send-register-otp")]
    public async Task<IActionResult> SendRegisterOtp([FromBody] SendRegisterOtpRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        
        if (!email.EndsWith("@gmail.com"))
            return this.ApiBadRequest("Xin lỗi, hệ thống chỉ hỗ trợ tài khoản @gmail.com.");

        var exists = await _context.Accounts.AnyAsync(a => a.Email == email);
        if (exists)
            return this.ApiConflict("Email này đã được đăng ký.");

        // Tạo OTP 6 số
        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        // Lưu OTP vào MemoryCache với thời hạn 2 phút (như yêu cầu của bạn)
        _cache.Set($"RegisterOTP_{email}", otp, TimeSpan.FromMinutes(2));

        try
        {
            var subject = "[UniMap360] Mã xác thực đăng ký tài khoản";
            var body = $@"
                <div style='font-family: sans-serif; padding: 20px; border: 1px solid #eee; border-radius: 10px; max-width: 500px;'>
                    <h2 style='color: #780115;'>UniMap360 Registration</h2>
                    <p>Chào bạn,</p>
                    <p>Bạn đang tiến hành đăng ký tài khoản tại UniMap360. Mã xác thực (OTP) của bạn là:</p>
                    <div style='background: #f8f9fa; padding: 15px; text-align: center; font-size: 24px; font-weight: bold; letter-spacing: 5px; color: #780115; border-radius: 5px;'>
                        {otp}
                    </div>
                    <p style='margin-top: 20px;'>Mã này có hiệu lực trong vòng <b>2 phút</b>. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                    <p style='font-size: 12px; color: #999;'>Đây là email tự động, vui lòng không phản hồi.</p>
                </div>";

            await _emailService.SendEmailAsync(email, subject, body);
            return this.ApiOk(new { message = "Mã xác thực đã được gửi tới Email của bạn." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể gửi email đăng ký tới {Email}", email);
            return this.ApiBadRequest("Không thể gửi Email. Vui lòng thử lại sau.");
        }
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        
        // Kiểm tra OTP trong Cache
        if (!_cache.TryGetValue($"OTP_{email}", out object? savedOtpObj) || savedOtpObj?.ToString() != request.Otp)
        {
            return this.ApiBadRequest("Mã xác thực (OTP) không hợp lệ hoặc đã hết hạn.");
        }

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == email);
        if (account == null)
            return this.ApiBadRequest("Không tìm thấy tài khoản.");

        // Hash mật khẩu mới và lưu
        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        account.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        // Xóa OTP khỏi Cache sau khi đã dùng
        _cache.Remove($"OTP_{email}");

        return this.ApiOk(new { message = "Đặt lại mật khẩu thành công. Bạn có thể đăng nhập ngay bây giờ." });
    }

    private async Task<bool> VerifyPasswordAsync(Account account, string password)
    {
        if (string.IsNullOrWhiteSpace(account.PasswordHash))
            return false;

        // Kiểm tra xem mật khẩu đã được mã hóa BCrypt chưa ($2a$, $2b$, $2y$)
        if (account.PasswordHash.StartsWith("$2a$") || 
            account.PasswordHash.StartsWith("$2b$") || 
            account.PasswordHash.StartsWith("$2y$"))
        {
            return BCrypt.Net.BCrypt.Verify(password, account.PasswordHash);
        }
        else
        {
            // Fallback: Kiểm tra mật khẩu plain-text cũ (dành cho các tài khoản tạo lúc làm đồ án)
            if (account.PasswordHash == password)
            {
                // Tự động nâng cấp mật khẩu lên BCrypt để bảo mật hơn
                account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                account.UpdatedAt = DateTime.UtcNow;
                // Lưu lại thay đổi ngay lập tức
                await _context.SaveChangesAsync();
                return true;
            }
        }
        return false;
    }

    private static string BuildDefaultHostFullName(string email, int accountId)
    {
        var alias = email.Split('@', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (string.IsNullOrWhiteSpace(alias)) alias = $"Host {accountId}";
        if (alias.Length > 100) alias = alias[..100];
        return alias;
    }

    private static string BuildTemporaryHostIdCard(int accountId)
    {
        return $"HOST{accountId:D10}";
    }

    private static string BuildDefaultEmployerCompanyName(string email, int accountId)
    {
        var alias = email.Split('@', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (string.IsNullOrWhiteSpace(alias)) alias = $"Employer {accountId}";
        var companyName = $"Doanh nghiệp {alias}";
        if (companyName.Length > 255) companyName = companyName[..255];
        return companyName;
    }

    private static string BuildTemporaryEmployerTaxCode(int accountId)
    {
        return $"EMP{accountId:D10}";
    }

    private string GenerateJwt(Account account)
    {
        var key = _configuration["Jwt:Key"]!;
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];
        var expiresMinutes = int.TryParse(_configuration["Jwt:ExpiresMinutes"], out var minutes) ? minutes : 120;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.AccountId.ToString()),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new(ClaimTypes.Role, account.UserRole ?? "Student"),
            new(ClaimTypes.NameIdentifier, account.AccountId.ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return "Student";
        return role.Trim().ToLowerInvariant() switch
        {
            "student" => "Student",
            "host" => "Host",
            "employer" => "Employer",
            _ => null
        };
    }

    private void SetTokenCookie(string token)
    {
        var expiresMinutes = int.TryParse(_configuration["Jwt:ExpiresMinutes"], out var minutes) ? minutes : 120;
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,       // Chống XSS: JS không thể đọc được
            Secure = true,         // Yêu cầu HTTPS
            SameSite = SameSiteMode.Lax, // Tránh CSRF cơ bản
            Expires = DateTime.UtcNow.AddMinutes(expiresMinutes),
            Path = "/"
        };
        Response.Cookies.Append("unimap360.accessToken", token, cookieOptions);
    }
}