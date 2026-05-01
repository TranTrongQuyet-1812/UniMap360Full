using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;
using UniMap360.Models.Requests;

namespace UniMap360.Controllers.Api;

[Route("api/reviews")]
[ApiController]
public class ReviewsController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public ReviewsController(UniMap360ProContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet("{targetType}/{targetId:int}")]
    public async Task<IActionResult> GetReviews(string targetType, int targetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 5)
    {
        var normalizedType = NormalizeTargetType(targetType);
        if (normalizedType is null)
            return this.ApiBadRequest("targetType không hợp lệ. Chỉ chấp nhận: room hoặc job.", "VALIDATION_ERROR");

        if (!await TargetExistsAsync(normalizedType, targetId))
            return this.ApiNotFound("Không tìm thấy tin đăng cần xem đánh giá.");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 20);

        var query = _context.Reviews
            .AsNoTracking()
            .Include(r => r.Student)
            .Where(r => r.TargetType == normalizedType && r.TargetId == targetId);

        var totalReviews = await query.CountAsync();
        var avgRatingRaw = await query
            .Where(r => r.Rating.HasValue)
            .Select(r => (double?)r.Rating)
            .AverageAsync();

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalReviews / (double)pageSize));
        if (page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ReviewId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.ReviewId,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                reviewerName = string.IsNullOrWhiteSpace(r.Student.FullName) ? "Sinh viên" : r.Student.FullName
            })
            .ToListAsync();

        return this.ApiOk(new
        {
            targetType = normalizedType,
            targetId,
            totalReviews,
            avgRating = avgRatingRaw.HasValue ? Math.Round(avgRatingRaw.Value, 2) : (double?)null,
            page,
            pageSize,
            totalPages,
            items
        });
    }

    [Authorize(Roles = "Student")]
    [HttpPost]
    public async Task<IActionResult> UpsertReview([FromBody] UpsertReviewRequest request)
    {
        var normalizedType = NormalizeTargetType(request.TargetType);
        if (normalizedType is null)
            return this.ApiBadRequest("targetType không hợp lệ. Chỉ chấp nhận: room hoặc job.", "VALIDATION_ERROR");

        if (request.TargetId <= 0)
            return this.ApiBadRequest("targetId phải lớn hơn 0.", "VALIDATION_ERROR");

        if (request.Rating < 1 || request.Rating > 5)
            return this.ApiBadRequest("rating phải trong khoảng từ 1 đến 5.", "VALIDATION_ERROR");

        if (!await TargetExistsAsync(normalizedType, request.TargetId))
            return this.ApiNotFound("Không tìm thấy tin đăng cần đánh giá.");

        var student = await EnsureCurrentStudentAsync();
        if (student is null)
            return this.ApiNotFound("Không tìm thấy hồ sơ sinh viên cho tài khoản hiện tại.");

        var trimmedComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (trimmedComment?.Length > 1000)
            return this.ApiBadRequest("comment không được vượt quá 1000 ký tự.", "VALIDATION_ERROR");

        var review = await _context.Reviews
            .FirstOrDefaultAsync(r =>
                r.StudentId == student.StudentId &&
                r.TargetType == normalizedType &&
                r.TargetId == request.TargetId);

        var isNew = review is null;
        if (isNew)
        {
            review = new Review
            {
                StudentId = student.StudentId,
                TargetType = normalizedType,
                TargetId = request.TargetId,
                Rating = request.Rating,
                Comment = trimmedComment,
                CreatedAt = DateTime.UtcNow
            };
            _context.Reviews.Add(review);
        }
        else
        {
            review!.Rating = request.Rating;
            review.Comment = trimmedComment;
            review.CreatedAt ??= DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return this.ApiOk(new
        {
            message = isNew ? "Gửi đánh giá thành công." : "Cập nhật đánh giá thành công.",
            reviewId = review!.ReviewId,
            targetType = review.TargetType,
            targetId = review.TargetId,
            rating = review.Rating,
            comment = review.Comment,
            createdAt = review.CreatedAt
        });
    }

    private async Task<StudentProfile?> EnsureCurrentStudentAsync()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(accountIdClaim, out var accountId)) return null;

        var student = await _context.StudentProfiles.FirstOrDefaultAsync(s => s.AccountId == accountId);
        if (student is not null) return student;

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
        if (account is null) return null;

        var fallbackName = !string.IsNullOrWhiteSpace(account.Email)
            ? account.Email.Split('@')[0]
            : $"student-{accountId}";

        student = new StudentProfile
        {
            AccountId = accountId,
            FullName = fallbackName
        };

        _context.StudentProfiles.Add(student);
        await _context.SaveChangesAsync();
        return student;
    }

    private async Task<bool> TargetExistsAsync(string targetType, int targetId)
    {
        if (targetType == "Room")
            return await _context.Rooms.AnyAsync(r => r.RoomId == targetId);

        return await _context.Jobs.AnyAsync(j => j.JobId == targetId);
    }

    private static string? NormalizeTargetType(string? targetType)
    {
        if (string.IsNullOrWhiteSpace(targetType)) return null;
        return targetType.Trim().ToLowerInvariant() switch
        {
            "room" => "Room",
            "job" => "Job",
            _ => null
        };
    }
}
