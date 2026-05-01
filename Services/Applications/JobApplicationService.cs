using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using System.Net.Mail;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Options;

namespace UniMap360.Services.Applications;

public interface IJobApplicationService
{
    Task<JobAppResult> CreateApplicationAsync(int accountId, JobAppPayload payload, IFormFile? cvFile, CancellationToken ct = default);
    Task<JobAppListResult> GetStudentApplicationsAsync(int accountId, string? status, int limit, CancellationToken ct = default);
    Task<JobAppListResult> GetEmployerApplicationsAsync(int accountId, string? status, int limit, CancellationToken ct = default);
    Task<JobAppResult> UpdateStatusAsync(int accountId, int applicationId, string targetStatus, CancellationToken ct = default);
    Task<CvStreamResult> GetCvStreamAsync(int accountId, string role, int applicationId, bool asAttachment, CancellationToken ct = default);
}

public sealed class JobAppPayload
{
    public int JobId { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? CvUrl { get; set; }
}

public sealed class JobAppResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Message { get; init; }
    public int? ApplicationId { get; init; }
    public string? Status { get; init; }
}

public sealed class JobAppListResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Message { get; init; }
    public int Total { get; init; }
    public object[] Items { get; init; } = Array.Empty<object>();
}

public sealed class CvStreamResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Message { get; init; }
    public byte[]? FileBytes { get; init; }
    public string? ContentType { get; init; }
    public string? FileName { get; init; }
    public bool Forbidden { get; init; }
}

public sealed class JobApplicationService : IJobApplicationService
{
    private readonly UniMap360ProContext _context;
    private readonly CloudinarySettings _cloudinarySettings;
    private readonly ILogger<JobApplicationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly HashSet<string> AllowedCvExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx" };
    private const long MaxCvFileSizeBytes = 8 * 1024 * 1024;

    public JobApplicationService(
        UniMap360ProContext context,
        IOptions<CloudinarySettings> cloudinaryOptions,
        ILogger<JobApplicationService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _cloudinarySettings = cloudinaryOptions.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<JobAppResult> CreateApplicationAsync(int accountId, JobAppPayload request, IFormFile? cvFile, CancellationToken ct)
    {
        if (request.JobId <= 0)
            return Fail(400, "JobId khong hop le.");

        var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == accountId, ct);
        if (account is null)
            return Fail(401, "Khong tim thay tai khoan hien tai.");

        var job = await _context.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == request.JobId, ct);
        if (job is null)
            return Fail(404, "Khong tim thay viec lam.");

        if (!string.Equals(job.JobStatus, "Open", StringComparison.OrdinalIgnoreCase))
            return Fail(400, "Viec lam hien khong o trang thai dang tuyen.");

        var student = await GetOrCreateStudentProfileAsync(accountId, ct);

        var existingPending = await _context.JobApplications.AsNoTracking()
            .AnyAsync(a => a.JobId == job.JobId && a.StudentId == student.StudentId && a.Status == "Pending", ct);
        if (existingPending)
            return Fail(400, "Ban da co ho so ung tuyen dang cho cho cong viec nay.");

        var contactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? account.Email?.Trim() : request.ContactEmail.Trim();
        if (string.IsNullOrWhiteSpace(contactEmail)) return Fail(400, "Vui long nhap email lien he.");
        if (!IsValidEmail(contactEmail)) return Fail(400, "Email lien he khong dung dinh dang.");

        var contactPhone = request.ContactPhone?.Trim();
        if (string.IsNullOrWhiteSpace(contactPhone)) return Fail(400, "Vui long nhap so dien thoai lien he.");
        if (contactPhone.Length > 20) return Fail(400, "So dien thoai khong duoc vuot qua 20 ky tu.");

        var cvUrlInput = request.CvUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(cvUrlInput) && !IsValidAbsoluteUrl(cvUrlInput))
            return Fail(400, "Link CV khong hop le.");

        var cvUpload = await ResolveCvSourceAsync(accountId, cvFile, cvUrlInput, student.Cvlink, ct);
        if (cvUpload.ErrorMessage is not null)
            return Fail(400, cvUpload.ErrorMessage);

        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        var application = new JobApplication
        {
            JobId = job.JobId,
            StudentId = student.StudentId,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            CvUrl = cvUpload.CvUrl,
            CvPublicId = cvUpload.CvPublicId,
            Status = "Pending"
        };
        _context.JobApplications.Add(application);

        if (!string.IsNullOrWhiteSpace(cvUpload.CvUrl) && !string.Equals(student.Cvlink, cvUpload.CvUrl, StringComparison.Ordinal))
            student.Cvlink = cvUpload.CvUrl;

        await _context.SaveChangesAsync(ct);

        var employerAccountId = await _context.EmployerProfiles
            .Where(e => e.EmployerId == job.EmployerId)
            .Select(e => (int?)e.AccountId)
            .FirstOrDefaultAsync(ct);
        if (employerAccountId.HasValue)
        {
            _context.Notifications.Add(new Notification
            {
                RecipientAccountId = employerAccountId.Value,
                Type = "job_application_request",
                Title = "Co ho so ung tuyen moi",
                Message = $"Co ung vien vua ung tuyen cong viec \"{job.JobTitle}\".",
                TargetType = ContentTargetTypes.Job,
                TargetId = job.JobId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation(
            "Job application created. ApplicationId={ApplicationId}, JobId={JobId}, StudentId={StudentId}",
            application.ApplicationId, job.JobId, student.StudentId);

        return new JobAppResult
        {
            Success = true,
            Message = "Ung tuyen thanh cong.",
            ApplicationId = application.ApplicationId,
            Status = application.Status
        };
    }

    public async Task<JobAppListResult> GetStudentApplicationsAsync(int accountId, string? status, int limit, CancellationToken ct)
    {
        var student = await _context.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.AccountId == accountId, ct);
        if (student is null) return new JobAppListResult { Success = true };

        limit = limit <= 0 ? 100 : Math.Min(limit, 300);
        var query = _context.JobApplications.AsNoTracking().Where(a => a.StudentId == student.StudentId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status.Trim());

        var items = await query.OrderByDescending(a => a.ApplicationId).Take(limit)
            .Select(a => new
            {
                a.ApplicationId,
                a.JobId,
                jobTitle = a.Job.JobTitle,
                companyName = a.Job.Employer.CompanyName,
                a.ContactEmail,
                a.ContactPhone,
                a.CvUrl,
                a.Status
            })
            .ToListAsync(ct);
        return new JobAppListResult { Success = true, Total = items.Count, Items = items.Cast<object>().ToArray() };
    }

    public async Task<JobAppListResult> GetEmployerApplicationsAsync(int accountId, string? status, int limit, CancellationToken ct)
    {
        var employer = await _context.EmployerProfiles.AsNoTracking().FirstOrDefaultAsync(e => e.AccountId == accountId, ct);
        if (employer is null) return new JobAppListResult { Success = false, StatusCode = 404, Message = "Khong tim thay ho so nha tuyen dung." };

        limit = limit <= 0 ? 200 : Math.Min(limit, 400);
        var query = _context.JobApplications.AsNoTracking().Where(a => a.Job.EmployerId == employer.EmployerId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status.Trim());

        var items = await query.OrderByDescending(a => a.ApplicationId).Take(limit)
            .Select(a => new
            {
                a.ApplicationId,
                a.JobId,
                jobTitle = a.Job.JobTitle,
                a.Status,
                studentId = a.StudentId,
                studentName = a.Student.FullName,
                studentEmail = a.Student.Account.Email,
                studentUniversity = a.Student.University,
                studentMajor = a.Student.Major,
                contactEmail = a.ContactEmail,
                contactPhone = a.ContactPhone,
                cvUrl = a.CvUrl ?? a.Student.Cvlink
            })
            .ToListAsync(ct);
        return new JobAppListResult { Success = true, Total = items.Count, Items = items.Cast<object>().ToArray() };
    }

    public async Task<JobAppResult> UpdateStatusAsync(int accountId, int applicationId, string targetStatus, CancellationToken ct)
    {
        var employer = await _context.EmployerProfiles.AsNoTracking().FirstOrDefaultAsync(e => e.AccountId == accountId, ct);
        if (employer is null) return Fail(404, "Khong tim thay ho so nha tuyen dung.");

        var normalizedStatus = NormalizeJobApplicationStatus(targetStatus);
        if (normalizedStatus is null) return Fail(400, "Trang thai cap nhat khong hop le.");

        try
        {
            var application = await _context.JobApplications
                .Include(a => a.Job)
                .FirstOrDefaultAsync(a => a.ApplicationId == applicationId, ct);

            if (application is null)
                return Fail(404, "Khong tim thay ho so ung tuyen.");

            if (application.Job.EmployerId != employer.EmployerId)
                return Fail(403, "Ban khong co quyen cap nhat ho so nay.");

            if (!string.Equals(application.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                return Fail(409, "Ho so nay da duoc xu ly truoc do.");

            var now = DateTime.UtcNow;
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            application.Status = normalizedStatus;
            await _context.SaveChangesAsync(ct);

            var studentAccountId = await _context.StudentProfiles
                .Where(s => s.StudentId == application.StudentId)
                .Select(s => (int?)s.AccountId)
                .FirstOrDefaultAsync(ct);

            if (studentAccountId.HasValue)
            {
                _context.Notifications.Add(new Notification
                {
                    RecipientAccountId = studentAccountId.Value,
                    Type = "job_application_update",
                    Title = BuildEmployerDecisionTitle(normalizedStatus),
                    Message = BuildEmployerDecisionMessage(normalizedStatus, application.Job.JobTitle ?? $"Job #{application.JobId}"),
                    TargetType = ContentTargetTypes.Job,
                    TargetId = application.JobId,
                    IsRead = false,
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Job application updated. ApplicationId={ApplicationId}, EmployerId={EmployerId}, TargetStatus={TargetStatus}",
                applicationId, employer.EmployerId, normalizedStatus);

            return new JobAppResult
            {
                Success = true,
                Message = "Cap nhat ho so ung tuyen thanh cong.",
                ApplicationId = applicationId,
                Status = normalizedStatus
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Update job application failed. ApplicationId={ApplicationId}, EmployerId={EmployerId}",
                applicationId, employer.EmployerId);
            return Fail(500, "Khong the cap nhat ho so ung tuyen. Vui long thu lai.");
        }
    }

    public async Task<CvStreamResult> GetCvStreamAsync(int accountId, string role, int applicationId, bool asAttachment, CancellationToken ct)
    {
        var application = await _context.JobApplications.AsNoTracking()
            .Where(a => a.ApplicationId == applicationId)
            .Select(a => new
            {
                a.ApplicationId,
                a.CvUrl,
                a.CvPublicId,
                StudentAccountId = a.Student.AccountId,
                EmployerAccountId = a.Job.Employer.AccountId
            })
            .FirstOrDefaultAsync(ct);
        if (application is null) return new CvStreamResult { StatusCode = 404, Message = "Khong tim thay ho so ung tuyen." };

        var canAccess = role switch
        {
            "employer" => application.EmployerAccountId == accountId,
            "student" => application.StudentAccountId == accountId,
            _ => false
        };
        if (!canAccess) return new CvStreamResult { Forbidden = true, StatusCode = 403 };

        if (string.IsNullOrWhiteSpace(application.CvUrl))
            return new CvStreamResult { StatusCode = 404, Message = "Ho so nay chua co CV." };

        var sourceUrls = BuildCvFetchUrls(application.CvPublicId, application.CvUrl);
        var client = _httpClientFactory.CreateClient();
        foreach (var sourceUrl in sourceUrls)
        {
            try
            {
                using var response = await client.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) continue;
                var fileBytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (fileBytes.Length == 0) continue;
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                var fileName = BuildCvFileName(application.CvUrl, application.ApplicationId);
                if (!asAttachment && string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
                    mediaType = GuessMediaTypeFromFileName(fileName);
                return new CvStreamResult { Success = true, FileBytes = fileBytes, ContentType = mediaType, FileName = asAttachment ? fileName : null };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fetch CV failed. Url={Url}", sourceUrl);
            }
        }
        return new CvStreamResult { StatusCode = 502, Message = "Khong the tai CV tu Cloudinary." };
    }

    private static JobAppResult Fail(int code, string msg) => new() { Success = false, StatusCode = code, Message = msg };

    private async Task<StudentProfile> GetOrCreateStudentProfileAsync(int accountId, CancellationToken ct)
    {
        var student = await _context.StudentProfiles.FirstOrDefaultAsync(s => s.AccountId == accountId, ct);
        if (student is not null) return student;
        var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == accountId, ct);
        var name = account?.Email?.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? $"Student {accountId}";
        student = new StudentProfile { AccountId = accountId, FullName = name };
        _context.StudentProfiles.Add(student);
        await _context.SaveChangesAsync(ct);
        return student;
    }

    private async Task<(string? CvUrl, string? CvPublicId, string? ErrorMessage)> ResolveCvSourceAsync(
        int accountId,
        IFormFile? cvFile,
        string? cvUrlInput,
        string? studentCvUrl,
        CancellationToken ct)
    {
        if (cvFile is not null && cvFile.Length > 0)
        {
            var err = ValidateCvFile(cvFile);
            if (err is not null) return (null, null, err);
            try
            {
                var r = await UploadCvToCloudinaryAsync(cvFile, accountId, ct);
                return (r.CvUrl, r.CvPublicId, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Upload CV failed. AccountId={AccountId}", accountId);
                return (null, null, "Tai CV len that bai.");
            }
        }

        if (!string.IsNullOrWhiteSpace(cvUrlInput)) return (cvUrlInput, null, null);
        if (!string.IsNullOrWhiteSpace(studentCvUrl)) return (studentCvUrl.Trim(), null, null);
        return (null, null, "Vui long cung cap CV (link hoac file).");
    }

    private async Task<(string CvUrl, string CvPublicId)> UploadCvToCloudinaryAsync(IFormFile cvFile, int accountId, CancellationToken ct)
    {
        var creds = ResolveCloudinaryCredentials();
        if (string.IsNullOrWhiteSpace(creds.CloudName) || string.IsNullOrWhiteSpace(creds.ApiKey) || string.IsNullOrWhiteSpace(creds.ApiSecret))
            throw new InvalidOperationException("Cloudinary chua duoc cau hinh.");

        var folder = $"CV-DuAn/{accountId}";
        var ext = Path.GetExtension(cvFile.FileName)?.ToLowerInvariant();
        var publicId = $"cv_{accountId}_{Guid.NewGuid():N}{ext}";
        var cloudinary = new Cloudinary(new CloudinaryDotNet.Account(creds.CloudName, creds.ApiKey, creds.ApiSecret))
        {
            Api = { Secure = true }
        };
        await using var stream = cvFile.OpenReadStream();
        var result = await cloudinary.UploadLargeRawAsync(new RawUploadParams
        {
            File = new FileDescription(cvFile.FileName, stream),
            Folder = folder,
            PublicId = publicId,
            Type = "upload",
            Overwrite = true,
            UniqueFilename = false,
            UseFilename = false
        });

        if (result?.Error is not null) throw new InvalidOperationException(result.Error.Message);
        var url = result?.SecureUrl?.ToString() ?? throw new InvalidOperationException("Cloudinary upload succeeded but URL missing.");
        return (url, $"{folder}/{publicId}");
    }

    private List<string> BuildCvFetchUrls(string? cvPublicId, string cvUrl)
    {
        var publicId = string.IsNullOrWhiteSpace(cvPublicId) ? TryExtractCloudinaryRawPublicIdFromUrl(cvUrl) : cvPublicId.Trim();
        var creds = ResolveCloudinaryCredentials();
        if (string.IsNullOrWhiteSpace(publicId) || string.IsNullOrWhiteSpace(creds.CloudName) || string.IsNullOrWhiteSpace(creds.ApiKey) || string.IsNullOrWhiteSpace(creds.ApiSecret))
            return new List<string> { cvUrl };

        try
        {
            var cloudinary = new Cloudinary(new CloudinaryDotNet.Account(creds.CloudName, creds.ApiKey, creds.ApiSecret)) { Api = { Secure = true } };
            var signed = cloudinary.Api.UrlImgUp.ResourceType("raw").Type("upload").Secure(true).Signed(true).BuildUrl(publicId);
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(cvUrl)) urls.Add(cvUrl);
            if (!string.IsNullOrWhiteSpace(signed) && !urls.Contains(signed, StringComparer.OrdinalIgnoreCase)) urls.Add(signed);
            return urls;
        }
        catch
        {
            return new List<string> { cvUrl };
        }
    }

    private (string? CloudName, string? ApiKey, string? ApiSecret) ResolveCloudinaryCredentials()
    {
        if (!string.IsNullOrWhiteSpace(_cloudinarySettings.CloudinaryUrl))
        {
            var raw = _cloudinarySettings.CloudinaryUrl.Trim();
            if (raw.StartsWith("CLOUDINARY_URL=", StringComparison.OrdinalIgnoreCase)) raw = raw["CLOUDINARY_URL=".Length..].Trim();
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && string.Equals(uri.Scheme, "cloudinary", StringComparison.OrdinalIgnoreCase))
            {
                var userInfo = uri.UserInfo ?? string.Empty;
                var sep = userInfo.IndexOf(':');
                if (sep > 0)
                    return (
                        uri.Host?.Trim(),
                        Uri.UnescapeDataString(userInfo[..sep]).Trim(),
                        Uri.UnescapeDataString(userInfo[(sep + 1)..]).Trim()
                    );
            }
        }
        return (_cloudinarySettings.CloudName?.Trim(), _cloudinarySettings.ApiKey?.Trim(), _cloudinarySettings.ApiSecret?.Trim());
    }

    private static string? TryExtractCloudinaryRawPublicIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var path = uri.AbsolutePath;
        var idx = path.IndexOf("/raw/upload/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var remainder = path[(idx + "/raw/upload/".Length)..];
        if (remainder.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            var si = remainder.IndexOf('/');
            if (si > 1 && remainder[1..si].All(char.IsDigit)) remainder = remainder[(si + 1)..];
        }
        remainder = Uri.UnescapeDataString(remainder);
        return string.IsNullOrWhiteSpace(remainder) ? null : remainder;
    }

    private static string? ValidateCvFile(IFormFile f)
    {
        if (f.Length <= 0) return "File CV khong hop le.";
        if (f.Length > MaxCvFileSizeBytes) return "File CV khong duoc vuot qua 8MB.";
        var ext = Path.GetExtension(f.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedCvExtensions.Contains(ext))
            return "Dinh dang CV khong ho tro. Chi chap nhan: .pdf, .doc, .docx.";
        return null;
    }

    private static string BuildCvFileName(string cvUrl, int appId)
    {
        try
        {
            var n = Path.GetFileName(new Uri(cvUrl).AbsolutePath);
            if (!string.IsNullOrWhiteSpace(n)) return n;
        }
        catch
        {
            // ignored
        }
        return $"cv-application-{appId}.pdf";
    }

    private static string GuessMediaTypeFromFileName(string f) => Path.GetExtension(f)?.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    private static bool IsValidEmail(string v)
    {
        try
        {
            var m = new MailAddress(v);
            return string.Equals(m.Address, v, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidAbsoluteUrl(string v)
        => Uri.TryCreate(v, UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static string? NormalizeJobApplicationStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var value = status.Trim();
        if (value.Equals("Accepted", StringComparison.OrdinalIgnoreCase)) return "Accepted";
        if (value.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        return null;
    }

    private static string BuildEmployerDecisionTitle(string status) => status switch
    {
        "Accepted" => "Ho so ung tuyen da duoc chap nhan",
        "Rejected" => "Ho so ung tuyen da bi tu choi",
        _ => "Ho so ung tuyen da duoc cap nhat"
    };

    private static string BuildEmployerDecisionMessage(string status, string jobTitle) => status switch
    {
        "Accepted" => $"Ho so ung tuyen cho cong viec \"{jobTitle}\" da duoc nha tuyen dung chap nhan.",
        "Rejected" => $"Ho so ung tuyen cho cong viec \"{jobTitle}\" da bi nha tuyen dung tu choi.",
        _ => $"Ho so ung tuyen cho cong viec \"{jobTitle}\" da duoc cap nhat."
    };
}
