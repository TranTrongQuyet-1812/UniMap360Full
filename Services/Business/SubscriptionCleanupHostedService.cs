using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UniMap360.Services.Business;

public class SubscriptionCleanupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionCleanupHostedService> _logger;
    private readonly TimeSpan _period = TimeSpan.FromHours(1); // Chạy mỗi 1 giờ

    public SubscriptionCleanupHostedService(IServiceProvider serviceProvider, ILogger<SubscriptionCleanupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription Cleanup Hosted Service is starting.");

        // Chạy lần đầu tiên ngay lập tức khi khởi động
        await DoCleanupAsync();

        using var timer = new PeriodicTimer(_period);
        while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            await DoCleanupAsync();
        }

        _logger.LogInformation("Subscription Cleanup Hosted Service is stopping.");
    }

    private async Task DoCleanupAsync()
    {
        try
        {
            _logger.LogInformation("Running expired subscriptions cleanup task...");
            using var scope = _serviceProvider.CreateScope();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
            int deactivatedCount = await subscriptionService.CheckAndDeactivateExpiredSubscriptionsAsync();
            if (deactivatedCount > 0)
            {
                _logger.LogInformation("Successfully deactivated {Count} expired subscriptions.", deactivatedCount);
            }
            else
            {
                _logger.LogInformation("No expired subscriptions found.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while cleaning up expired subscriptions.");
        }
    }
}
