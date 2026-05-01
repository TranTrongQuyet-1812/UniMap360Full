using System;

namespace UniMap360.Models;

public partial class Message
{
    public long MessageId { get; set; }

    public int ConversationId { get; set; }

    public int SenderAccountId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Conversation Conversation { get; set; } = null!;

    public virtual Account SenderAccount { get; set; } = null!;
}
