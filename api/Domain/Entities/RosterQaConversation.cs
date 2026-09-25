using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// One Roster Q&amp;A conversation, owned by the account that asked it (EXP-32,
/// <c>manuals/adr-roster-qa-conversation-history.md</c> §3). This is the durable replacement for
/// the in-memory <c>RosterQaThreadStore</c>: a conversation survives a restart, resumes from any
/// device, and is readable by nobody but its owner — Administrators included.
///
/// <para>Every turn under it is personal data from the moment it is written, which is why this
/// store is declared, scrubbed on erasure, and disclosed by existence in the Access View of every
/// Expert it touched before anything is allowed to write to it.</para>
/// </summary>
public class RosterQaConversation
{
    public Guid Id { get; set; }

    /// <summary>The owner. Cascades with the account, exactly as <c>AgentUsage</c> does.</summary>
    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The latest turn's time. What retention counts six months from (ADR §6).</summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>The first question, trimmed at a word boundary. No model-written title and no
    /// rename — so the only text here is text the owner typed.</summary>
    public string Title { get; set; } = string.Empty;

    public List<RosterQaTurn> Turns { get; set; } = [];
}

/// <summary>
/// One question and the answer it got, appended once and never edited (ADR §3). Only completed
/// turns are stored: a model error, a cap or a timeout records nothing.
/// </summary>
public class RosterQaTurn
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string QuestionText { get; set; } = string.Empty;

    /// <summary>Exactly what was shown, the "could not be grounded" note included, so a replay
    /// reads as the conversation did.</summary>
    public string AnswerText { get; set; } = string.Empty;

    /// <summary>The model the provider reported, not configuration. Empty when it reported
    /// none.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Whether the answer rested on captured tool results after the Capture-Verify
    /// retry. Stored so the dock never has to parse the note out of the text.</summary>
    public bool Grounded { get; set; }

    public RosterQaTurnState State { get; set; } = RosterQaTurnState.Ok;

    public DateTimeOffset CreatedAt { get; set; }

    public List<RosterQaTurnExpert> TouchedExperts { get; set; } = [];
}

/// <summary>
/// One Expert whose id appeared in <em>any</em> tool result during a turn — a superset of the ones
/// the answer names, extracted in code and never parsed from model text (ADR §3).
///
/// <para><b>Deliberately no foreign key to <c>Expert</c>.</b> The reference has to survive that
/// Expert's erasure long enough for the scrub to use it, in the same transaction — a cascade would
/// delete the row before the scrub could read it and a restrict would block the erasure
/// outright.</para>
/// </summary>
public class RosterQaTurnExpert
{
    public Guid TurnId { get; set; }

    public Guid ExpertId { get; set; }
}
