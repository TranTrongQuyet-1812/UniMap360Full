using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace UniMap360.Hubs;

[Authorize]
public sealed class RealtimeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var accountId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(accountId))
        {
            var userGroupName = $"user:{accountId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, userGroupName);
            Log.Information("SignalR client connected: User ID {UserId}, Connection ID {ConnectionId} added to group {GroupName}", 
                accountId, Context.ConnectionId, userGroupName);
        }
        else
        {
            Log.Warning("SignalR client connected without NameIdentifier claim. Connection ID: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var accountId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (exception != null)
        {
            Log.Warning(exception, "SignalR client disconnected with error. User ID: {UserId}, Connection ID: {ConnectionId}", 
                accountId ?? "unknown", Context.ConnectionId);
        }
        else
        {
            Log.Information("SignalR client disconnected. User ID: {UserId}, Connection ID: {ConnectionId}", 
                accountId ?? "unknown", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
