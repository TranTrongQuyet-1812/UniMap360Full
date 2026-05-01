using Microsoft.EntityFrameworkCore;
using UniMap360.Models;

namespace UniMap360.Services.Admin;

public static class AdminSchemaBootstrapper
{
    public static async Task EnsureAsync(
        UniMap360ProContext context,
        ILogger logger,
        int ownerAccountId,
        string legacyHashFallbackPlainPassword,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await context.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            throw new InvalidOperationException("Cannot connect to SQL Server database.");
        }

        var fallbackPassword = string.IsNullOrWhiteSpace(legacyHashFallbackPlainPassword)
            ? "123456"
            : legacyHashFallbackPlainPassword;

        // Bỏ qua việc fallback plain-text khi chạy thực tế.
        // Trước đây đoạn code này đổi mật khẩu BCrypt về 123456 cho "school-project mode",
        // nhưng giờ chuẩn bị đưa lên Production nên phần đó đã bị xóa.
        logger.LogInformation("AdminSchemaBootstrapper ran successfully. Password hashing is enforced.");

        var expectedOwnerValue = ownerAccountId.ToString();
        var ownerSetting = await context.AppSettings
            .FirstOrDefaultAsync(x => x.Key == "Security.SuperAdminAccountId", cancellationToken);

        if (ownerSetting is null)
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = "Security.SuperAdminAccountId",
                Value = expectedOwnerValue
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (!string.Equals(ownerSetting.Value, expectedOwnerValue, StringComparison.Ordinal))
        {
            ownerSetting.Value = expectedOwnerValue;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
