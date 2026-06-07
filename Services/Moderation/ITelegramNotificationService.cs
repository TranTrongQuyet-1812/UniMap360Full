using System.Threading;
using System.Threading.Tasks;

namespace UniMap360.Services.Moderation;

public interface ITelegramNotificationService
{
    Task SendAlertAsync(string message, CancellationToken cancellationToken = default);
    Task SendAlertWithActionsAsync(string message, int reportId, string contentType, int postId, CancellationToken cancellationToken = default);
}
