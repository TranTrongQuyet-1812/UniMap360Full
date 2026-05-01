using System;
using System.Collections.Generic;

namespace UniMap360.Models;

public partial class Conversation
{
    public int ConversationId { get; set; }

    public string Kind { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastMessageAt { get; set; }

    public virtual ICollection<ConversationParticipant> ConversationParticipants { get; set; } = new List<ConversationParticipant>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
