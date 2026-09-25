using System.Globalization;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Compliance;

/// <summary>What erasure removed, for the caller to report honestly.</summary>
/// <param name="ExpertId">The roster row that went, or null when the account owned none.</param>
/// <param name="ScoringRowsDeleted">Scan candidate rows removed outright.</param>
/// <param name="ProposalRowsScrubbed">Proposal candidate rows hollowed out but kept.</param>
/// <param name="PackagesRewritten">Handoff documents whose report was rewritten.</param>
public sealed record ErasureResult(
    Guid? ExpertId, int ScoringRowsDeleted, int ProposalRowsScrubbed, int PackagesRewritten);

/// <summary>
/// The single erasure path (P1T-186), reading <see cref="PersonalDataDeclaration"/> for what it has
/// to reach. Self-service, synchronous, irreversible, and gated by the control word — the only
/// proof-of-person this service has, because there is no email, so no confirmation link and no way
/// to tell anybody afterwards that it happened.
///
/// <para>The account and the roster row go <b>together, hard</b>. No tombstone: the residue left in
/// the decision ledger is acknowledged pseudonymous data under Art. 18 restriction rather than
/// something we pretend is anonymous, and somebody registering again afterwards is simply a new
/// Expert needing no special path at all.</para>
///
/// <para>Like the pause control it lives beside, no signature takes an expert id — the row is
/// always the acting account's own, resolved through <c>OwnerUserId</c>. An API that cannot express
/// "erase somebody else" cannot be talked into it.</para>
///
/// <para>The retention sweep needs the same act without an account or a control word, and it gets
/// it through a <em>separate</em> interface (<see cref="IRetentionErasure"/>) that the Web host
/// never routes to — the two share a private core rather than a signature, so this one still cannot
/// name anybody but the caller.</para>
/// </summary>
public interface IErasureService
{
    /// <summary>
    /// Erases the acting account and everything the declaration says goes with it. Refuses on a
    /// wrong control word without touching anything.
    /// </summary>
    Task<ErasureResult> EraseMineAsync(
        Guid actingUserId, string controlWord, CancellationToken ct = default);
}

/// <summary>
/// The same erasure, triggered by a clock instead of a person (P1T-188). Deliberately a second
/// interface rather than a second method on <see cref="IErasureService"/>: that one's whole
/// guarantee is that no signature can name somebody else's record, and adding an id-taking method
/// beside it would quietly retire the guarantee. Nothing in the Web API routes to this.
///
/// <para>Retention is a <b>trigger</b>, not a second mechanism. Two implementations of "delete a
/// person" diverge; it is only a question of when. <c>ErasureTests</c> asserts the two produce
/// identical database state.</para>
/// </summary>
public interface IRetentionErasure
{
    /// <summary>
    /// Erases one record because its period ran out. Takes no control word — there is nobody to ask
    /// — and works on an unclaimed record, which has no account behind it at all.
    /// </summary>
    Task<ErasureResult> EraseExpiredAsync(Guid expertId, CancellationToken ct = default);
}

public class ErasureService(IAppDbContext db, IControlWordHasher controlWords)
    : IErasureService, IRetentionErasure
{
    public async Task<ErasureResult> EraseMineAsync(
        Guid actingUserId, string controlWord, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == actingUserId, ct)
            ?? throw new NotFoundException(nameof(User), actingUserId);

        // Before anything is touched. A bootstrapped account carries an empty hash and can never
        // verify, which is correct: it has no control word, so it has no self-service erasure.
        if (string.IsNullOrEmpty(user.ControlWordHash)
            || !controlWords.Verify(controlWord ?? string.Empty, user.ControlWordHash))
        {
            throw new ConflictException(
                "That control word is not right, so nothing was deleted. Erasure cannot be undone, "
                + "and there is no email on this service to recover an account with — so it asks "
                + "for the control word every time.");
        }

        var expert = await db.Experts.FirstOrDefaultAsync(e => e.OwnerUserId == actingUserId, ct);
        return await EraseAsync(expert, user, ct);
    }

    public async Task<ErasureResult> EraseExpiredAsync(Guid expertId, CancellationToken ct = default)
    {
        var expert = await db.Experts.FirstOrDefaultAsync(e => e.Id == expertId, ct)
            ?? throw new NotFoundException(nameof(Expert), expertId);

        // The account goes with the record, exactly as it does when somebody deletes themselves —
        // and an unclaimed record has none, which is the case self-service erasure cannot express.
        var user = expert.OwnerUserId is { } ownerId
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == ownerId, ct)
            : null;

        return await EraseAsync(expert, user, ct);
    }

    /// <summary>
    /// The act itself, with the gate already passed and both rows resolved. <b>One implementation,
    /// two triggers</b> — a person asking, and a period running out. Everything below is keyed on
    /// the record; who asked for it is settled before we get here.
    /// </summary>
    private async Task<ErasureResult> EraseAsync(Expert? expert, User? user, CancellationToken ct)
    {
        var scoringRows = 0;
        var proposalRows = 0;
        var packages = 0;

        if (expert is not null)
        {
            // A scan is a working artefact, not a decision, so the rows go whole — including the
            // captured career digest, which is the fullest copy of the person outside their own row.
            // The FK added in P1T-186 makes the database do this too; doing it here as well means
            // erasure does not depend on a cascade a future migration could drop.
            var scoring = await db.ScoringJobCandidates
                .Where(c => c.ExpertId == expert.Id).ToListAsync(ct);
            db.ScoringJobCandidates.RemoveRange(scoring);
            scoringRows = scoring.Count;

            // A human decided on these, so the rows stay and the person comes out of them. ExpertId
            // and the scores remain: that is pseudonymisation, not anonymisation, and it is held
            // under Art. 18 restriction rather than relabelled as anonymous.
            var candidates = await db.StaffingProposalCandidates
                .Where(c => c.ExpertId == expert.Id).ToListAsync(ct);
            foreach (var candidate in candidates)
            {
                candidate.Name = string.Empty;
                candidate.Title = string.Empty;
                candidate.Rationale = string.Empty;
            }

            proposalRows = candidates.Count;

            // The report inside the package is the decision's evidence base, so the envelope
            // survives and what it says about this person does not.
            var proposalIds = candidates.Select(c => c.ProposalId).Distinct().ToList();
            var proposals = await db.StaffingProposals
                .Where(p => proposalIds.Contains(p.Id) || p.RecommendedExpertId == expert.Id)
                .ToListAsync(ct);

            foreach (var proposal in proposals)
            {
                var rewritten = HandoffPackageScrub.Remove(proposal.PackageJson, expert.Id);
                if (!string.Equals(rewritten, proposal.PackageJson, StringComparison.Ordinal))
                {
                    proposal.PackageJson = rewritten;
                    packages++;
                }
            }

            // Roster Q&A transcripts (EXP-32, ADR §5). Two reaches, deliberately: by the ids the
            // capture wrapper recorded, and by the person's full name in the text — because an
            // answer can name somebody no tool result ever returned, and a question is free text
            // the owner typed. The turn row survives hollowed out: when the owner asked, how many
            // times and on which model is the owner's own data, not this Expert's.
            await ScrubRosterQaAsync(expert, ct);

            // The row itself, and by cascade: the six child collections, the search chunks and their
            // embeddings, the lawful-basis history, any open claim and any unspent claim code.
            db.Experts.Remove(expert);
        }

        // And the account, in the same transaction — its absence is what refuses every live session
        // on both hosts, so there is no window in which a deleted person's token still works. Null
        // only for an unclaimed record, which never had one.
        if (user is not null)
        {
            db.Users.Remove(user);
        }

        await db.SaveChangesAsync(ct);

        return new ErasureResult(expert?.Id, scoringRows, proposalRows, packages);
    }

    /// <summary>
    /// Empties every Roster Q&A turn this person appears in, and retitles the conversations that
    /// named them (EXP-32; <c>manuals/adr-roster-qa-conversation-history.md</c> §5). Runs inside
    /// <see cref="EraseAsync"/>'s single <c>SaveChanges</c>, before the Expert row goes — the
    /// touched-id rows have no foreign key precisely so they are still here to be read.
    ///
    /// <para>A turn is reached two ways. By <c>RosterQaTurnExpert</c>, which the capture wrapper
    /// writes for every Expert id any tool result returned — a superset, so a turn that mentioned
    /// somebody only in passing is scrubbed rather than missed. And by full name in either text,
    /// which is what covers the question the owner typed and the answer's own prose. Nicknames and
    /// misspellings remain a residual risk, recorded in the DPIA (R6/R13).</para>
    /// </summary>
    private async Task ScrubRosterQaAsync(Expert expert, CancellationToken ct)
    {
        var fullName = $"{expert.FirstName} {expert.LastName}".Trim();
        // Case-insensitively, through lower() on both sides rather than ILIKE: this runs in the
        // Application layer, which does not reference the Npgsql provider, and a name match that
        // only worked on one provider is a scrub that silently misses on the other.
        var needle = $"%{fullName.ToLowerInvariant()}%";

        var touched = await db.RosterQaTurnExperts
            .Where(t => t.ExpertId == expert.Id).ToListAsync(ct);
        var touchedTurnIds = touched.Select(t => t.TurnId).ToHashSet();

        // An Expert with no name would match every row, so a blank one is never searched for.
        var named = fullName.Length == 0
            ? []
            : await db.RosterQaTurns
                .Where(t => EF.Functions.Like(t.QuestionText.ToLower(), needle)
                            || EF.Functions.Like(t.AnswerText.ToLower(), needle))
                .ToListAsync(ct);

        var byId = await db.RosterQaTurns
            .Where(t => touchedTurnIds.Contains(t.Id)).ToListAsync(ct);

        var turns = byId.Concat(named).DistinctBy(t => t.Id).ToList();

        foreach (var turn in turns)
        {
            turn.QuestionText = string.Empty;
            turn.AnswerText = string.Empty;
            turn.State = RosterQaTurnState.Removed;
        }

        // The ids themselves go: they are a reference to somebody we no longer hold, and the scrub
        // that needed them has just run.
        db.RosterQaTurnExperts.RemoveRange(touched);

        // A conversation is retitled when its title names the person, or when the turn it was
        // titled from — the first one — was just emptied. Titles are the owner's first question
        // verbatim, so both cases are real.
        var scrubbedIds = turns.Select(t => t.Id).ToHashSet();
        var conversationIds = turns.Select(t => t.ConversationId).Distinct().ToList();

        var conversations = await db.RosterQaConversations
            .Where(c => conversationIds.Contains(c.Id)
                        || (fullName.Length > 0 && EF.Functions.Like(c.Title.ToLower(), needle)))
            .ToListAsync(ct);

        foreach (var conversation in conversations)
        {
            var namesThem = fullName.Length > 0
                && conversation.Title.Contains(fullName, StringComparison.OrdinalIgnoreCase);

            var firstTurnId = await db.RosterQaTurns
                .Where(t => t.ConversationId == conversation.Id)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);

            if (namesThem || (firstTurnId is { } first && scrubbedIds.Contains(first)))
            {
                // Invariant, and off the UTC instant: a custom yyyy-MM-dd format still renders
                // through the ambient culture's *calendar*, so on a Hijri or Buddhist default this
                // would quietly write a different year into somebody's conversation list.
                conversation.Title = "Conversation from "
                    + conversation.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
    }
}
