using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniMap360.Hubs;
using UniMap360.Models;
using Serilog;

namespace UniMap360.Services.Realtime;

public sealed class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<RealtimeHub> _hubContext;
    private readonly UniMap360ProContext _dbContext;

    public RealtimeNotifier(IHubContext<RealtimeHub> hubContext, UniMap360ProContext dbContext)
    {
        _hubContext = hubContext;
        _dbContext = dbContext;
    }

    public async Task NotifyNotificationCreatedAsync(int recipientAccountId, Notification notification, CancellationToken ct = default)
    {
        try
        {
            var userGroup = $"user:{recipientAccountId}";
            var payload = new
            {
                notificationId = notification.NotificationId,
                type = notification.Type,
                title = notification.Title,
                message = notification.Message,
                targetType = notification.TargetType,
                targetId = notification.TargetId,
                isRead = notification.IsRead,
                createdAt = notification.CreatedAt
            };

            await _hubContext.Clients.Group(userGroup).SendAsync("NotificationCreated", payload, ct);
            Log.Information("SignalR: Pushed NotificationCreated to {GroupName}", userGroup);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SignalR: Failed to push NotificationCreated to user {UserId}", recipientAccountId);
        }
    }

    public async Task NotifyNotificationUnreadChangedAsync(int recipientAccountId, CancellationToken ct = default)
    {
        try
        {
            var userGroup = $"user:{recipientAccountId}";
            var unreadCount = await _dbContext.Notifications
                .AsNoTracking()
                .Where(n => n.RecipientAccountId == recipientAccountId && !n.IsRead)
                .CountAsync(ct);

            await _hubContext.Clients.Group(userGroup).SendAsync("NotificationUnreadChanged", new { unreadCount }, ct);
            Log.Information("SignalR: Pushed NotificationUnreadChanged to {GroupName} (count: {Count})", userGroup, unreadCount);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SignalR: Failed to push NotificationUnreadChanged to user {UserId}", recipientAccountId);
        }
    }

    public async Task NotifyChatMessageCreatedAsync(IReadOnlyCollection<int> participantAccountIds, object payload, CancellationToken ct = default)
    {
        foreach (var id in participantAccountIds)
        {
            try
            {
                var userGroup = $"user:{id}";
                await _hubContext.Clients.Group(userGroup).SendAsync("ReceiveMessage", payload, ct);
                Log.Information("SignalR: Pushed ReceiveMessage to {GroupName}", userGroup);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SignalR: Failed to push ReceiveMessage to user {UserId}", id);
            }
        }
    }

    public async Task NotifyConversationUpdatedAsync(IReadOnlyCollection<int> participantAccountIds, object payload, CancellationToken ct = default)
    {
        foreach (var id in participantAccountIds)
        {
            try
            {
                var userGroup = $"user:{id}";
                await _hubContext.Clients.Group(userGroup).SendAsync("ConversationUpdated", payload, ct);
                Log.Information("SignalR: Pushed ConversationUpdated to {GroupName}", userGroup);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SignalR: Failed to push ConversationUpdated to user {UserId}", id);
            }
        }
    }

    public async Task NotifyChatUnreadChangedAsync(int accountId, CancellationToken ct = default)
    {
        try
        {
            var userGroup = $"user:{accountId}";
            
            var list = await (
                from p in _dbContext.ConversationParticipants.AsNoTracking()
                join m in _dbContext.Messages.AsNoTracking()
                    on p.ConversationId equals m.ConversationId
                where p.AccountId == accountId
                      && !p.IsArchived
                      && m.SenderAccountId != accountId
                      && (!p.LastReadMessageId.HasValue || m.MessageId > p.LastReadMessageId.Value)
                group m by p.ConversationId
                into g
                select g.Count()
            ).ToListAsync(ct);

            var totalUnread = list.Sum();

            await _hubContext.Clients.Group(userGroup).SendAsync("ChatUnreadChanged", new { totalUnread }, ct);
            Log.Information("SignalR: Pushed ChatUnreadChanged to {GroupName} (unread: {Count})", userGroup, totalUnread);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SignalR: Failed to push ChatUnreadChanged to user {UserId}", accountId);
        }
    }
}
