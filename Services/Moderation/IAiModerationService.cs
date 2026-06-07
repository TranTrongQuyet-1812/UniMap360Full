using System.Threading;
using System.Threading.Tasks;

namespace UniMap360.Services.Moderation;

public class ModerationResult
{
    public bool IsApproved { get; set; }
    public string? Reason { get; set; }
    public string? FlaggedCategory { get; set; }
}

public interface IAiModerationService
{
    Task<ModerationResult> ModerateContentAsync(string title, string? description, string contentType, CancellationToken cancellationToken = default);
}
