using System;

namespace UniMap360.Models;

public partial class ConversationParticipant
{
    public int ConversationId { get; set; }

    public int AccountId { get; set; }

    public DateTime JoinedAt { get; set; }

    public long? LastReadMessageId { get; set; }

    public bool IsArchived { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual Conversation Conversation { get; set; } = null!;
}
