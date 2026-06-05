using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Requests;
using UniMap360.Models.Api;

namespace UniMap360.Controllers.Api;

[Route("api/favorites")]
[ApiController]
[Authorize(Roles = "Student")]
public class FavoritesController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public FavoritesController(UniMap360ProContext context)
    {
        _context = context;
    }

    [HttpGet("status/{targetType}/{targetId:int}")]
    public async Task<IActionResult> GetFavoriteStatus(string targetType, int targetId)
    {
        var normalizedType = NormalizeTargetType(targetType);
        if (normalizedType is null)
            return this.ApiBadRequest("targetType không hợp lệ. Chỉ chấp nhận: room hoặc job.", "VALIDATION_ERROR");

        var student = await EnsureCurrentStudentAsync();
        if (student is null)
            return this.ApiNotFound("Không tìm thấy hồ sơ sinh viên cho tài khoản hiện tại.");

        var isFavorite = await _context.Favorites.AnyAsync(f =>
            f.StudentId == student.StudentId &&
            f.TargetType == normalizedType &&
            f.TargetId == targetId);

        return this.ApiOk(new { isFavorite });
    }

    [HttpPost("toggle")]
    public async Task<IActionResult> ToggleFavorite([FromBody] ToggleFavoriteRequest request)
    {
        var normalizedType = NormalizeTargetType(request.TargetType);
        if (normalizedType is null)
            return this.ApiBadRequest("targetType không hợp lệ. Chỉ chấp nhận: room hoặc job.", "VALIDATION_ERROR");

        if (request.TargetId <= 0)
            return this.ApiBadRequest("targetId phải lớn hơn 0.", "VALIDATION_ERROR");

        if (!await TargetExistsAsync(normalizedType, request.TargetId))
            return this.ApiNotFound("Không tìm thấy tin đăng cần yêu thích.");

        var student = await EnsureCurrentStudentAsync();
        if (student is null)
            return this.ApiNotFound("Không tìm thấy hồ sơ sinh viên cho tài khoản hiện tại.");

        var favorite = await _context.Favorites.FirstOrDefaultAsync(f =>
            f.StudentId == student.StudentId &&
            f.TargetType == normalizedType &&
            f.TargetId == request.TargetId);

        bool isFavorite;
        if (favorite is null)
        {
            favorite = new Favorite
            {
                StudentId = student.StudentId,
                TargetType = normalizedType,
                TargetId = request.TargetId
            };
            _context.Favorites.Add(favorite);
            isFavorite = true;
        }
        else
        {
            _context.Favorites.Remove(favorite);
            isFavorite = false;
        }

        await _context.SaveChangesAsync();

        return this.ApiOk(new
        {
            isFavorite,
            message = isFavorite ? "Đã lưu vào danh sách yêu thích." : "Đã xóa khỏi danh sách yêu thích."
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
