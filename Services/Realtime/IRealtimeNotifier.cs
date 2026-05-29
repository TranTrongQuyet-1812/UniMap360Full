using UniMap360.Models;

namespace UniMap360.Services.Realtime;

public interface IRealtimeNotifier
{
    // Notification Events
    Task NotifyNotificationCreatedAsync(int recipientAccountId, Notification notification, CancellationToken ct = default);
    Task NotifyNotificationUnreadChangedAsync(int recipientAccountId, CancellationToken ct = default);

    // Chat Events
    Task NotifyChatMessageCreatedAsync(IReadOnlyCollection<int> participantAccountIds, object payload, CancellationToken ct = default);
    Task NotifyConversationUpdatedAsync(IReadOnlyCollection<int> participantAccountIds, object payload, CancellationToken ct = default);
    Task NotifyChatUnreadChangedAsync(int accountId, CancellationToken ct = default);
}
