using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniMap360.Models;
using UniMap360.Models.Api;

namespace UniMap360.Services.Business;

public class PostAnalyticsService : IPostAnalyticsService
{
    private readonly UniMap360ProContext _context;

    public PostAnalyticsService(UniMap360ProContext context)
    {
        _context = context;
    }

    public async Task TrackEventAsync(
        string targetType, 
        int targetId, 
        int ownerAccountId, 
        int? actorAccountId, 
        string eventType, 
        string? sourcePage = null, 
        string? metadataJson = null)
    {
        var normalizedTarget = targetType.Trim();
        var normalizedEvent = eventType.Trim();
        var date = DateTime.UtcNow.Date;

        // 1. Phân loại sự kiện
        bool isCriticalEvent = normalizedEvent == "ChatStarted" 
                            || normalizedEvent == "AppointmentCreated" 
                            || normalizedEvent == "JobApplied";

        // 2. Ghi nhận sự kiện chi tiết (Chỉ ghi nhận sự kiện quan trọng để tránh phình dữ liệu)
        if (isCriticalEvent)
        {
            var rawEvent = new PostAnalyticsEvent
            {
                TargetType = normalizedTarget,
                TargetId = targetId,
                OwnerAccountId = ownerAccountId,
                ActorAccountId = actorAccountId,
                EventType = normalizedEvent,
                OccurredAt = DateTime.UtcNow,
                SourcePage = sourcePage,
                MetadataJson = metadataJson
            };
            _context.PostAnalyticsEvents.Add(rawEvent);
            await _context.SaveChangesAsync();
        }

        // 3. Cộng dồn số liệu tổng hợp hàng ngày
        int viewVal = normalizedEvent == "ViewDetail" ? 1 : 0;
        int listingImpressionVal = normalizedEvent == "ListingImpression" ? 1 : 0;
        int mapImpressionVal = normalizedEvent == "MapImpression" ? 1 : 0;
        int chatVal = normalizedEvent == "ChatStarted" ? 1 : 0;
        int favoriteVal = normalizedEvent == "FavoriteAdded" ? 1 : 0;
        int appointmentVal = normalizedEvent == "AppointmentCreated" ? 1 : 0;
        int applicationVal = normalizedEvent == "JobApplied" ? 1 : 0;

        // Dùng lệnh Native SQL Upsert nếu chạy PostgreSQL
        if (_context.Database.ProviderName?.Contains("Npgsql") == true)
        {
            var sql = @"
                INSERT INTO ""PostAnalyticsDailies"" (
                    ""TargetType"", ""TargetId"", ""Date"", 
                    ""ViewCount"", ""ListingImpressionCount"", ""MapImpressionCount"", 
                    ""ChatStartedCount"", ""FavoriteCount"", ""AppointmentCount"", ""ApplicationCount"", 
                    ""CreatedAt"", ""UpdatedAt""
                )
                VALUES (
                    {0}, {1}, {2}, 
                    {3}, {4}, {5}, 
                    {6}, {7}, {8}, {9}, 
                    NOW(), NOW()
                )
                ON CONFLICT (""TargetType"", ""TargetId"", ""Date"") 
                DO UPDATE SET 
                    ""ViewCount"" = ""PostAnalyticsDailies"".""ViewCount"" + EXCLUDED.""ViewCount"",
                    ""ListingImpressionCount"" = ""PostAnalyticsDailies"".""ListingImpressionCount"" + EXCLUDED.""ListingImpressionCount"",
                    ""MapImpressionCount"" = ""PostAnalyticsDailies"".""MapImpressionCount"" + EXCLUDED.""MapImpressionCount"",
                    ""ChatStartedCount"" = ""PostAnalyticsDailies"".""ChatStartedCount"" + EXCLUDED.""ChatStartedCount"",
                    ""FavoriteCount"" = ""PostAnalyticsDailies"".""FavoriteCount"" + EXCLUDED.""FavoriteCount"",
                    ""AppointmentCount"" = ""PostAnalyticsDailies"".""AppointmentCount"" + EXCLUDED.""AppointmentCount"",
                    ""ApplicationCount"" = ""PostAnalyticsDailies"".""ApplicationCount"" + EXCLUDED.""ApplicationCount"",
                    ""UpdatedAt"" = NOW();";

            await _context.Database.ExecuteSqlRawAsync(sql, 
                normalizedTarget, targetId, date, 
                viewVal, listingImpressionVal, mapImpressionVal, 
                chatVal, favoriteVal, appointmentVal, applicationVal);
        }
        else
        {
            // Fallback cho SQL Server local nếu có chạy thử (sử dụng EF Core cơ bản)
            var dailyStats = await _context.PostAnalyticsDailies
                .FirstOrDefaultAsync(x => x.TargetType == normalizedTarget && x.TargetId == targetId && x.Date == date);

            if (dailyStats is null)
            {
                dailyStats = new PostAnalyticsDaily
                {
                    TargetType = normalizedTarget,
                    TargetId = targetId,
                    Date = date,
                    ViewCount = viewVal,
                    ListingImpressionCount = listingImpressionVal,
                    MapImpressionCount = mapImpressionVal,
                    ChatStartedCount = chatVal,
                    FavoriteCount = favoriteVal,
                    AppointmentCount = appointmentVal,
                    ApplicationCount = applicationVal,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.PostAnalyticsDailies.Add(dailyStats);
            }
            else
            {
                dailyStats.ViewCount += viewVal;
                dailyStats.ListingImpressionCount += listingImpressionVal;
                dailyStats.MapImpressionCount += mapImpressionVal;
                dailyStats.ChatStartedCount += chatVal;
                dailyStats.FavoriteCount += favoriteVal;
                dailyStats.AppointmentCount += appointmentVal;
                dailyStats.ApplicationCount += applicationVal;
                dailyStats.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
        }
    }

    public async Task<PostAnalyticsSummaryDto> GetPostAnalyticsSummaryAsync(
        string targetType, 
        int targetId, 
        int ownerAccountId, 
        DateTime startDate, 
        DateTime endDate)
    {
        var normalizedTarget = targetType.Trim();
        var dailyRecords = await _context.PostAnalyticsDailies
            .AsNoTracking()
            .Where(x => x.TargetType == normalizedTarget && x.TargetId == targetId && x.Date >= startDate && x.Date <= endDate)
            .OrderBy(x => x.Date)
            .ToListAsync();

        var summary = new PostAnalyticsSummaryDto
        {
            TargetId = targetId,
            TargetType = normalizedTarget,
            TotalViews = dailyRecords.Sum(r => r.ViewCount),
            TotalListingImpressions = dailyRecords.Sum(r => r.ListingImpressionCount),
            TotalMapImpressions = dailyRecords.Sum(r => r.MapImpressionCount),
            TotalChats = dailyRecords.Sum(r => r.ChatStartedCount),
            TotalFavorites = await _context.Favorites.CountAsync(f => f.TargetType == normalizedTarget && f.TargetId == targetId),
            TotalAppointments = dailyRecords.Sum(r => r.AppointmentCount),
            TotalApplications = dailyRecords.Sum(r => r.ApplicationCount)
        };

        summary.DailyMetrics = dailyRecords.Select(r => new DailyMetricDto
        {
            Date = r.Date,
            Views = r.ViewCount,
            ListingImpressions = r.ListingImpressionCount,
            MapImpressions = r.MapImpressionCount,
            Chats = r.ChatStartedCount,
            Favorites = r.FavoriteCount,
            Appointments = r.AppointmentCount,
            Applications = r.ApplicationCount
        }).ToList();

        return summary;
    }

    public async Task<OwnerAnalyticsOverviewDto> GetOwnerOverviewAsync(
        int ownerAccountId, 
        string role, 
        DateTime startDate, 
        DateTime endDate)
    {
        var overview = new OwnerAnalyticsOverviewDto
        {
            OwnerAccountId = ownerAccountId
        };

        List<int> targetIds = new();
        string targetType = "";
        Dictionary<int, string> postTitles = new();

        if (role == "Host")
        {
            targetType = "Room";
            var hostProfile = await _context.HostProfiles
                .AsNoTracking()
                .Include(h => h.Rooms)
                .FirstOrDefaultAsync(h => h.AccountId == ownerAccountId);
                
            if (hostProfile != null)
            {
                targetIds = hostProfile.Rooms.Select(r => r.RoomId).ToList();
                postTitles = hostProfile.Rooms.ToDictionary(r => r.RoomId, r => r.Title);
            }
        }
        else if (role == "Employer")
        {
            targetType = "Job";
            var employerProfile = await _context.EmployerProfiles
                .AsNoTracking()
                .Include(e => e.Jobs)
                .FirstOrDefaultAsync(e => e.AccountId == ownerAccountId);

            if (employerProfile != null)
            {
                targetIds = employerProfile.Jobs.Select(j => j.JobId).ToList();
                postTitles = employerProfile.Jobs.ToDictionary(j => j.JobId, j => j.JobTitle);
            }
        }

        overview.TotalPostsCount = targetIds.Count;

        if (targetIds.Count > 0)
        {
            var dailyRecords = await _context.PostAnalyticsDailies
                .AsNoTracking()
                .Where(x => x.TargetType == targetType && targetIds.Contains(x.TargetId) && x.Date >= startDate && x.Date <= endDate)
                .ToListAsync();

            overview.TotalViews = dailyRecords.Sum(r => r.ViewCount);
            overview.TotalChats = dailyRecords.Sum(r => r.ChatStartedCount);
            overview.TotalFavorites = await _context.Favorites.CountAsync(f => f.TargetType == targetType && targetIds.Contains(f.TargetId));
            overview.TotalAppointments = dailyRecords.Sum(r => r.AppointmentCount);
            overview.TotalApplications = dailyRecords.Sum(r => r.ApplicationCount);

            // Xếp hạng bài đăng hiệu quả nhất
            var topPerforming = dailyRecords
                .GroupBy(r => r.TargetId)
                .Select(g => new PostMetricSummaryDto
                {
                    TargetId = g.Key,
                    TargetType = targetType,
                    Title = postTitles.GetValueOrDefault(g.Key, "Bài đăng #" + g.Key),
                    Views = g.Sum(r => r.ViewCount),
                    Chats = g.Sum(r => r.ChatStartedCount),
                    Favorites = 0
                })
                .ToList();

            foreach (var post in topPerforming)
            {
                post.Favorites = await _context.Favorites
                    .CountAsync(f => f.TargetType == targetType && f.TargetId == post.TargetId);
            }

            overview.TopPerformingPosts = topPerforming
                .OrderByDescending(p => p.Views)
                .Take(5)
                .ToList();

            // Thống kê theo ngày (Trend)
            overview.DailyMetrics = dailyRecords
                .GroupBy(r => r.Date)
                .Select(g => new DailyMetricDto
                {
                    Date = g.Key,
                    Views = g.Sum(r => r.ViewCount),
                    Chats = g.Sum(r => r.ChatStartedCount),
                    Favorites = g.Sum(r => r.FavoriteCount),
                    Appointments = g.Sum(r => r.AppointmentCount),
                    Applications = g.Sum(r => r.ApplicationCount)
                })
                .OrderBy(d => d.Date)
                .ToList();
        }

        return overview;
    }
}
