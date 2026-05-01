using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMap360.Constants;
using UniMap360.Models;
using UniMap360.Models.Requests;
using UniMap360.Models.Api;

namespace UniMap360.Controllers.Api;

[Route("api/chat")]
[ApiController]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly UniMap360ProContext _context;

    public ChatController(UniMap360ProContext context)
    {
        _context = context;
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromQuery] int limit = 40, CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Unauthorized.");

        limit = limit <= 0 ? 40 : Math.Min(limit, 100);

        var myParticipants = await _context.ConversationParticipants
            .AsNoTracking()
            .Where(p => p.AccountId == accountId.Value && !p.IsArchived)
            .Select(p => new
            {
                p.ConversationId,
                p.LastReadMessageId
            })
            .ToListAsync(cancellationToken);

        if (myParticipants.Count == 0)
            return this.ApiOk(new { totalUnread = 0, items = Array.Empty<object>() });

        var conversationIds = myParticipants.Select(p => p.ConversationId).Distinct().ToList();

        var conversations = await _context.Conversations
            .AsNoTracking()
            .Where(c => conversationIds.Contains(c.ConversationId))
            .Select(c => new
            {
                c.ConversationId,
                c.Kind,
                c.CreatedAt,
                c.LastMessageAt
            })
            .ToListAsync(cancellationToken);

        var participants = await _context.ConversationParticipants
            .AsNoTracking()
            .Where(p => conversationIds.Contains(p.ConversationId))
            .Select(p => new
            {
                p.ConversationId,
                p.AccountId,
                p.Account.Email,
                p.Account.UserRole
            })
            .ToListAsync(cancellationToken);

        var accountIds = participants.Select(x => x.AccountId).Distinct().ToList();
        var displayNameByAccountId = await BuildDisplayNameMapAsync(accountIds, cancellationToken);

        var lastMessageIds = await _context.Messages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => new
            {
                ConversationId = g.Key,
                MessageId = g.Max(m => m.MessageId)
            })
            .ToListAsync(cancellationToken);

        var messageIdSet = lastMessageIds.Select(x => x.MessageId).Distinct().ToList();
        var lastMessages = await _context.Messages
            .AsNoTracking()
            .Where(m => messageIdSet.Contains(m.MessageId))
            .Select(m => new
            {
                m.MessageId,
                m.ConversationId,
                m.SenderAccountId,
                m.Content,
                m.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var unreadCountByConversation = await (
            from p in _context.ConversationParticipants.AsNoTracking()
            join m in _context.Messages.AsNoTracking()
                on p.ConversationId equals m.ConversationId
            where p.AccountId == accountId.Value
                  && !p.IsArchived
                  && conversationIds.Contains(p.ConversationId)
                  && m.SenderAccountId != accountId.Value
                  && (!p.LastReadMessageId.HasValue || m.MessageId > p.LastReadMessageId.Value)
            group m by p.ConversationId
            into g
            select new
            {
                ConversationId = g.Key,
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, cancellationToken);

        var totalUnread = unreadCountByConversation.Values.Sum();

        var items = conversations
            .Select(conversation =>
            {
                var conversationParticipants = participants
                    .Where(p => p.ConversationId == conversation.ConversationId)
                    .Select(p => new
                    {
                        p.AccountId,
                        p.Email,
                        role = p.UserRole,
                        displayName = displayNameByAccountId.TryGetValue(p.AccountId, out var displayName)
                            ? displayName
                            : p.Email
                    })
                    .ToList();

                var otherParticipants = conversationParticipants
                    .Where(p => p.AccountId != accountId.Value)
                    .ToList();

                var title = otherParticipants.Count == 0
                    ? "Cuoc tro chuyen"
                    : string.Join(", ", otherParticipants.Select(p => p.displayName));

                var lastMessage = lastMessages
                    .FirstOrDefault(m => m.ConversationId == conversation.ConversationId);

                return new
                {
                    conversationId = conversation.ConversationId,
                    kind = conversation.Kind,
                    title,
                    participants = conversationParticipants,
                    unreadCount = unreadCountByConversation.TryGetValue(conversation.ConversationId, out var unread) ? unread : 0,
                    lastMessage = lastMessage is null
                        ? null
                        : new
                        {
                            messageId = lastMessage.MessageId,
                            senderAccountId = lastMessage.SenderAccountId,
                            content = lastMessage.Content,
                            createdAt = lastMessage.CreatedAt
                        },
                    sortAt = lastMessage?.CreatedAt ?? conversation.LastMessageAt ?? conversation.CreatedAt
                };
            })
            .OrderByDescending(x => x.sortAt)
            .Take(limit)
            .Select(x => new
            {
                x.conversationId,
                x.kind,
                x.title,
                x.participants,
                x.unreadCount,
                x.lastMessage
            })
            .ToList();

        return this.ApiOk(new
        {
            totalUnread,
            items
        });
    }

    [HttpPost("conversations/direct")]
    public async Task<IActionResult> CreateOrGetDirectConversation(
        [FromBody] CreateDirectConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Unauthorized.");

        if (request.TargetAccountId.HasValue && request.TargetAccountId.Value == accountId.Value)
            return this.ApiBadRequest("Khong the tu nhan tin voi chinh minh.");

        int? targetAccountId = request.TargetAccountId;
        if (!targetAccountId.HasValue && !string.IsNullOrWhiteSpace(request.TargetEmail))
        {
            var email = request.TargetEmail.Trim();
            targetAccountId = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.Email == email)
                .Select(a => (int?)a.AccountId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (!targetAccountId.HasValue)
            return this.ApiBadRequest("Khong tim thay tai khoan dich.");

        var targetExists = await _context.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.AccountId == targetAccountId.Value, cancellationToken);
        if (!targetExists)
            return this.ApiNotFound("Tai khoan dich khong ton tai.");

        var existingConversationId = await _context.Conversations
            .AsNoTracking()
            .Where(c => c.Kind == "direct")
            .Where(c => _context.ConversationParticipants.Any(p => p.ConversationId == c.ConversationId && p.AccountId == accountId.Value))
            .Where(c => _context.ConversationParticipants.Any(p => p.ConversationId == c.ConversationId && p.AccountId == targetAccountId.Value))
            .Where(c => _context.ConversationParticipants.Count(p => p.ConversationId == c.ConversationId) == 2)
            .Select(c => c.ConversationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingConversationId > 0)
            return this.ApiOk(new { conversationId = existingConversationId, created = false });

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Kind = "direct",
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync(cancellationToken);

        _context.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.ConversationId,
            AccountId = accountId.Value,
            JoinedAt = now,
            IsArchived = false
        });
        _context.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.ConversationId,
            AccountId = targetAccountId.Value,
            JoinedAt = now,
            IsArchived = false
        });
        await _context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return this.ApiOk(new { conversationId = conversation.ConversationId, created = true });
    }

    [HttpGet("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> GetMessages(
        int conversationId,
        [FromQuery] long? beforeMessageId,
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Unauthorized.");

        var isParticipant = await _context.ConversationParticipants
            .AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId && p.AccountId == accountId.Value, cancellationToken);
        if (!isParticipant)
            return Forbid();

        take = take <= 0 ? 30 : Math.Min(take, 100);

        var query = _context.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted);
        if (beforeMessageId.HasValue)
            query = query.Where(m => m.MessageId < beforeMessageId.Value);

        var items = await query
            .OrderByDescending(m => m.MessageId)
            .Take(take)
            .Select(m => new
            {
                m.MessageId,
                m.ConversationId,
                m.SenderAccountId,
                senderEmail = m.SenderAccount.Email,
                m.Content,
                m.CreatedAt
            })
            .ToListAsync(cancellationToken);

        items.Reverse();

        return this.ApiOk(new
        {
            items,
            hasMore = items.Count == take
        });
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> SendMessage(
        int conversationId,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Unauthorized.");

        var participant = await _context.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.AccountId == accountId.Value, cancellationToken);
        if (participant is null)
            return Forbid();

        var content = (request.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
            return this.ApiBadRequest("Noi dung tin nhan khong duoc de trong.");

        if (content.Length > 2000)
            return this.ApiBadRequest("Noi dung tin nhan toi da 2000 ky tu.");

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var message = new Message
        {
            ConversationId = conversationId,
            SenderAccountId = accountId.Value,
            Content = content,
            CreatedAt = now,
            IsDeleted = false
        };

        _context.Messages.Add(message);

        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, cancellationToken);
        if (conversation is not null)
        {
            conversation.UpdatedAt = now;
            conversation.LastMessageAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);

        participant.LastReadMessageId = message.MessageId;

        var recipientAccountIds = await _context.ConversationParticipants
            .Where(p => p.ConversationId == conversationId && p.AccountId != accountId.Value)
            .Select(p => p.AccountId)
            .ToListAsync(cancellationToken);

        var senderEmail = await _context.Accounts
            .Where(a => a.AccountId == accountId.Value)
            .Select(a => a.Email)
            .FirstOrDefaultAsync(cancellationToken);

        foreach (var recipientAccountId in recipientAccountIds)
        {
            _context.Notifications.Add(new Notification
            {
                RecipientAccountId = recipientAccountId,
                Type = "chat_message",
                Title = "Tin nhan moi",
                Message = $"{senderEmail ?? "Nguoi dung"} vua gui tin nhan cho ban.",
                TargetType = ContentTargetTypes.Conversation,
                TargetId = conversationId,
                IsRead = false,
                CreatedAt = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return this.ApiOk(new
        {
            messageId = message.MessageId,
            message = new
            {
                message.MessageId,
                message.ConversationId,
                message.SenderAccountId,
                senderEmail = senderEmail,
                message.Content,
                message.CreatedAt
            }
        });
    }

    [HttpPost("conversations/{conversationId:int}/read")]
    public async Task<IActionResult> MarkRead(
        int conversationId,
        [FromBody] MarkConversationReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Unauthorized.");

        var participant = await _context.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.AccountId == accountId.Value, cancellationToken);
        if (participant is null)
            return Forbid();

        long? targetMessageId = request.LastReadMessageId;
        if (!targetMessageId.HasValue)
        {
            targetMessageId = await _context.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .Select(m => (long?)m.MessageId)
                .OrderByDescending(x => x)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (!targetMessageId.HasValue)
            return this.ApiOk(new { success = true });

        var belongsToConversation = await _context.Messages
            .AsNoTracking()
            .AnyAsync(m => m.ConversationId == conversationId && m.MessageId == targetMessageId.Value, cancellationToken);
        if (!belongsToConversation)
            return this.ApiBadRequest("LastReadMessageId khong hop le.");

        if (!participant.LastReadMessageId.HasValue || participant.LastReadMessageId.Value < targetMessageId.Value)
            participant.LastReadMessageId = targetMessageId.Value;

        await _context.SaveChangesAsync(cancellationToken);
        return this.ApiOk(new { success = true, lastReadMessageId = participant.LastReadMessageId });
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return this.ApiUnauthorized("Unauthorized.");

        var unreadCountByConversation = await (
            from p in _context.ConversationParticipants.AsNoTracking()
            join m in _context.Messages.AsNoTracking()
                on p.ConversationId equals m.ConversationId
            where p.AccountId == accountId.Value
                  && !p.IsArchived
                  && m.SenderAccountId != accountId.Value
                  && (!p.LastReadMessageId.HasValue || m.MessageId > p.LastReadMessageId.Value)
            group m by p.ConversationId
            into g
            select new
            {
                ConversationId = g.Key,
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var totalUnread = unreadCountByConversation.Sum(x => x.Count);

        return this.ApiOk(new { totalUnread });
    }

    private int? GetCurrentAccountId()
    {
        var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
    }

    private async Task<Dictionary<int, string>> BuildDisplayNameMapAsync(
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, string>();
        if (accountIds.Count == 0)
            return result;

        var studentNames = await _context.StudentProfiles
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && !string.IsNullOrWhiteSpace(x.FullName))
            .Select(x => new { x.AccountId, x.FullName })
            .ToListAsync(cancellationToken);
        foreach (var item in studentNames)
            result[item.AccountId] = item.FullName!;

        var hostNames = await _context.HostProfiles
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && !string.IsNullOrWhiteSpace(x.FullName))
            .Select(x => new { x.AccountId, x.FullName })
            .ToListAsync(cancellationToken);
        foreach (var item in hostNames)
            result[item.AccountId] = item.FullName!;

        var employerNames = await _context.EmployerProfiles
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && !string.IsNullOrWhiteSpace(x.CompanyName))
            .Select(x => new { x.AccountId, DisplayName = x.CompanyName })
            .ToListAsync(cancellationToken);
        foreach (var item in employerNames)
            result[item.AccountId] = item.DisplayName!;

        return result;
    }
}
