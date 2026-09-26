using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Agents;

/// <summary>
/// What a conversation lookup produced: the id to answer with (fresh when the requested one was
/// unknown or belonged to someone else — the client detects context loss by the id changing), the
/// bounded history to replay into the model, and whether the id names a conversation that does not
/// exist yet.
/// </summary>
/// <param name="IsNew">True when nothing is stored under <paramref name="Id"/>. The first
/// completed turn creates the row; until then a question that never got answered leaves nothing
/// behind. It also tells <see cref="RosterQaConversationStore.AppendAsync"/> the difference
/// between "not written yet" and "the owner deleted it while this run was in flight", which must
/// not be resurrected.</param>
public sealed record ResolvedConversation(Guid Id, IReadOnlyList<ChatMessage> History, bool IsNew);

/// <summary>
/// How a stored turn reads back to its owner (ADR §5). Two of the three are masks over text that
/// is still on disk, and the difference between them is the difference between permanent and
/// reversible — which is why the dock is told which, rather than being handed a blank.
/// </summary>
public enum TurnVisibility
{
    /// <summary>Readable: the turn's own text, as it was shown.</summary>
    Ok,

    /// <summary>The erasure scrub emptied it when an Expert it named was erased. Permanent — the
    /// text is gone from the row, not withheld.</summary>
    Removed,

    /// <summary>An Expert it touched is currently paused. Computed at read time and never stored,
    /// so unpausing restores the turn at no cost.</summary>
    Hidden,
}

/// <summary>One row of the owner's history index: enough to choose a conversation, and no turn
/// text at all.</summary>
public sealed record ConversationSummary(
    Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastActiveAt);

/// <summary>One turn as its owner reads it. <paramref name="Question"/> and
/// <paramref name="Answer"/> are empty whenever <paramref name="State"/> is not
/// <see cref="TurnVisibility.Ok"/> — the mask is applied here, not left to the caller.</summary>
public sealed record ConversationTurnView(
    string Question,
    string Answer,
    string ModelId,
    bool Grounded,
    DateTimeOffset CreatedAt,
    TurnVisibility State);

/// <summary>A conversation and its turns, oldest first.</summary>
public sealed record ConversationDetail(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    IReadOnlyList<ConversationTurnView> Turns);

/// <summary>
/// The durable Roster Q&amp;A conversation store (EXP-36,
/// <c>manuals/adr-roster-qa-conversation-history.md</c> §3–§5), replacing the in-memory
/// <c>RosterQaThreadStore</c> and its 30-minute sliding TTL and 20-thread cap. Those were lifetime
/// rules; lifetime is retention's job now (ADR §6), so resume has no age cutoff at all and a
/// conversation survives a restart and follows its owner between devices.
///
/// <para>Everything here is scoped to the caller's own <c>UserId</c>. An id that names someone
/// else's conversation is treated exactly like an unknown one — a fresh conversation, never a
/// refusal, because a refusal would disclose that the id exists.</para>
/// </summary>
public sealed class RosterQaConversationStore(IAppDbContext db, TimeProvider clock)
{
    /// <summary>The replay window, unchanged from the in-memory store: the last ten Q/A turns as
    /// text. Tool-call intermediates are never replayed, so a prompt stays bounded without a
    /// summarizer.</summary>
    public const int ReplayTurns = 10;

    /// <summary>Where a title is cut. The column holds 80, which leaves room for the ellipsis and
    /// for a word boundary that lands a character or two past this.</summary>
    private const int TitleLength = 60;

    /// <summary>
    /// The caller's conversation and its replay window, or a fresh one. A turn is replayed only if
    /// it is <see cref="RosterQaTurnState.Ok"/> and every Expert it touched is currently on the
    /// bench — the pause mask of ADR §5, read at query time and never written down, so unpausing
    /// restores the turn at no cost.
    /// </summary>
    public async Task<ResolvedConversation> ResolveAsync(
        Guid? userId, string? threadId, CancellationToken ct = default)
    {
        // A caller the token could not name has nowhere to store anything: UserId is the owner and
        // it is not nullable. They get a working, un-persisted id rather than an error.
        if (userId is not { } owner || !Guid.TryParse(threadId, out var id))
        {
            return Fresh();
        }

        var mine = await db.RosterQaConversations.AnyAsync(c => c.Id == id && c.UserId == owner, ct);
        if (!mine)
        {
            return Fresh();
        }

        var window = await db.RosterQaTurns
            .Where(t => t.ConversationId == id && t.State == RosterQaTurnState.Ok)
            // Composed from the visibility seam and nothing else (RosterVisibility.NotHidden), the
            // way the seam's own note prescribes for callers holding a bare ExpertId: an EXISTS
            // that Postgres runs, not a filter over a materialised list. A touched id with no
            // Expert row behind it fails this too — erasure has already marked such a turn
            // Removed, and masking is the safe direction for the case it has not.
            .Where(t => t.TouchedExperts.All(x =>
                db.Experts.Where(RosterVisibility.NotHidden).Any(e => e.Id == x.ExpertId)))
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(ReplayTurns)
            .Select(t => new { t.QuestionText, t.AnswerText })
            .ToListAsync(ct);

        window.Reverse();

        var history = window
            .SelectMany(t => new[]
            {
                new ChatMessage(ChatRole.User, t.QuestionText),
                new ChatMessage(ChatRole.Assistant, t.AnswerText),
            })
            .ToList();

        return new ResolvedConversation(id, history, IsNew: false);
    }

    /// <summary>
    /// Records one completed turn, creating the conversation on the first. Only completed turns
    /// get here: a cap reached, a provider error or a timeout returns before this call and records
    /// nothing, which is why an unanswered question leaves no empty conversation behind.
    /// </summary>
    public async Task AppendAsync(
        Guid? userId,
        ResolvedConversation conversation,
        string question,
        AgentReply reply,
        CancellationToken ct = default)
    {
        if (userId is not { } owner)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var existing = await db.RosterQaConversations
            .FirstOrDefaultAsync(c => c.Id == conversation.Id && c.UserId == owner, ct);

        if (existing is null)
        {
            if (!conversation.IsNew)
            {
                // Deleted (or expired) while this answer was being written. The owner asked for it
                // to be gone; a turn arriving late does not bring it back.
                return;
            }

            existing = new RosterQaConversation
            {
                Id = conversation.Id,
                UserId = owner,
                CreatedAt = now,
                LastActiveAt = now,
                Title = TitleFrom(question),
            };
            db.RosterQaConversations.Add(existing);
        }
        else
        {
            existing.LastActiveAt = now;
            if (string.IsNullOrEmpty(existing.Title))
            {
                existing.Title = TitleFrom(question);
            }
        }

        var turn = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = existing.Id,
            QuestionText = question,
            // Exactly what was shown, the "could not be grounded" note included, so a replay reads
            // as the conversation did.
            AnswerText = reply.Text,
            ModelId = reply.ModelId ?? string.Empty,
            Grounded = reply.Grounded,
            State = RosterQaTurnState.Ok,
            CreatedAt = now,
        };

        foreach (var expertId in reply.TouchedExpertIds.Distinct())
        {
            turn.TouchedExperts.Add(new RosterQaTurnExpert { TurnId = turn.Id, ExpertId = expertId });
        }

        // The navigation is what tells EF to insert the conversation before the turn on the first
        // append. The explicit Add is what stops the second one becoming an UPDATE: a Guid key is
        // store-generated by convention, so an entity reached through a navigation with one
        // already set reads to the change tracker as a row that exists.
        existing.Turns.Add(turn);
        db.RosterQaTurns.Add(turn);

        await db.SaveChangesAsync(ct);
    }

    // ---- history (EXP-33, ADR §5 "Owner delete" and §7) ---------------------------------------
    //
    // Every method below takes the caller's own UserId and no other, so there is no argument a
    // caller could pass to reach somebody else's rows. A conversation that is not theirs is simply
    // absent — the endpoints turn that into 404, which is also what a guessed id gets.

    /// <summary>
    /// The caller's conversations, most recently active first. Titles and timestamps only: a list
    /// is for choosing, and turn text is what the drill-in is for.
    /// </summary>
    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(
        Guid userId, CancellationToken ct = default) =>
        await db.RosterQaConversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastActiveAt).ThenByDescending(c => c.Id)
            .Select(c => new ConversationSummary(c.Id, c.Title, c.CreatedAt, c.LastActiveAt))
            .ToListAsync(ct);

    /// <summary>
    /// One of the caller's conversations with its turns, oldest first, or null when the id names
    /// nothing of theirs. The pause mask is computed here, by the same
    /// <see cref="RosterVisibility.NotHidden"/> the replay window uses and nothing else, so a
    /// review and a resume agree about which turns exist.
    /// </summary>
    public async Task<ConversationDetail?> GetAsync(
        Guid userId, Guid id, CancellationToken ct = default)
    {
        var conversation = await db.RosterQaConversations
            .Where(c => c.Id == id && c.UserId == userId)
            .Select(c => new ConversationSummary(c.Id, c.Title, c.CreatedAt, c.LastActiveAt))
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
        {
            return null;
        }

        var turns = await db.RosterQaTurns
            .Where(t => t.ConversationId == id)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.QuestionText,
                t.AnswerText,
                t.ModelId,
                t.Grounded,
                t.CreatedAt,
                t.State,
                // The same correlated EXISTS the replay window runs, composed from the visibility
                // seam and nothing else, so a review and a resume can never disagree about which
                // turns exist. A touched id with no Expert row behind it at all masks too:
                // erasure has already marked such a turn Removed, and masking is the safe
                // direction for the case it has not.
                Visible = t.TouchedExperts.All(x =>
                    db.Experts.Where(RosterVisibility.NotHidden).Any(e => e.Id == x.ExpertId)),
            })
            .ToListAsync(ct);

        return new ConversationDetail(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.LastActiveAt,
            turns.Select(t =>
            {
                var state = t.State == RosterQaTurnState.Removed ? TurnVisibility.Removed
                    : t.Visible ? TurnVisibility.Ok
                    : TurnVisibility.Hidden;
                var masked = state != TurnVisibility.Ok;
                return new ConversationTurnView(
                    masked ? string.Empty : t.QuestionText,
                    masked ? string.Empty : t.AnswerText,
                    // Not personal data and not masked: which model answered and whether it was
                    // grounded stay readable, so a masked turn still says what kind of turn it was.
                    t.ModelId,
                    t.Grounded,
                    t.CreatedAt,
                    state);
            }).ToList());
    }

    /// <summary>
    /// Hard-deletes one of the caller's conversations, or reports that there was nothing of theirs
    /// under that id. Immediate and without a control word: the data is the caller's own and the
    /// act is low-stakes (ADR §5).
    /// </summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var conversation = await db.RosterQaConversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

        if (conversation is null)
        {
            return false;
        }

        // Loaded and removed rather than ExecuteDelete, exactly as the retention sweep does: the
        // turns and their touched rows go by the configured ON DELETE CASCADE, and ExecuteDelete
        // would bypass the change tracker without taking them.
        db.RosterQaConversations.Remove(conversation);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Hard-deletes every conversation the caller owns. Deleting nothing is a success:
    /// the caller asked for their history to be gone, and it is.</summary>
    public async Task<int> DeleteAllAsync(Guid userId, CancellationToken ct = default)
    {
        var mine = await db.RosterQaConversations.Where(c => c.UserId == userId).ToListAsync(ct);
        if (mine.Count == 0)
        {
            return 0;
        }

        db.RosterQaConversations.RemoveRange(mine);
        await db.SaveChangesAsync(ct);
        return mine.Count;
    }

    /// <summary>
    /// The first question, cut at the last word boundary inside <see cref="TitleLength"/> with an
    /// ellipsis saying it was cut. No model writes this and nobody renames it, so the only text in
    /// a title is text its owner typed.
    /// </summary>
    internal static string TitleFrom(string question)
    {
        var text = question.Trim();
        if (text.Length <= TitleLength)
        {
            return text;
        }

        var cut = text[..TitleLength];
        var boundary = cut.LastIndexOf(' ');
        if (boundary > 0)
        {
            cut = cut[..boundary];
        }

        return cut.TrimEnd() + "…";
    }

    private static ResolvedConversation Fresh() => new(Guid.NewGuid(), [], IsNew: true);
}
