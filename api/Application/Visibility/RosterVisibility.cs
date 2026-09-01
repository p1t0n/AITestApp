using System.Linq.Expressions;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Application.Visibility;

/// <summary>
/// What a roster row permits, as opposed to who is asking about it (P1T-185). The ownership scope
/// (P1T-182) answers the second question; this answers the first, and the two are deliberately
/// separate — an agent is <c>Unrestricted</c> on ownership and still bound by everything here,
/// which is what makes a hidden Expert vanish from the MCP tools without agents needing an
/// identity.
///
/// <para><b>One seam, two predicates, and that is the whole design.</b> Three filters bolted onto
/// three query paths would be correct on the day they were written and wrong by the next consumer;
/// the blast-radius list in <c>manuals/expert-visibility.md</c> stays true only because there is a
/// single place to change. <c>HiddenAt</c> appears in exactly one query expression in this codebase
/// — this file — and <c>VisibilitySeamTests</c> fails the build if a second one appears.</para>
///
/// <para>Both predicates are <see cref="Expression"/>s over <see cref="Expert"/> so EF translates
/// them into the caller's SQL rather than filtering a materialised list — a retrieval path that
/// pulls rows back and drops them in memory has already paid for them, and paging over a filtered
/// list silently returns short pages.</para>
/// </summary>
public static class RosterVisibility
{
    /// <summary>
    /// The Expert has not paused themselves. A pause is reversible and free: the row, its search
    /// chunks and their embeddings all stay exactly where they are and are filtered at query time,
    /// because deleting the chunks would spend the 100/day embedding quota to undo a pause.
    /// </summary>
    public static Expression<Func<Expert, bool>> NotHidden { get; } = e => e.HiddenAt == null;

    /// <summary>
    /// The row's <em>current</em> lawful basis carries an Art. 22(2) route — i.e. it is on
    /// 6(1)(b), reached by self-registration or an approved claim (P1T-183, P1T-184). Legitimate
    /// interest is not among the three Art. 22(2) exceptions, so a row on LI has no route to
    /// automated decision-making at all and is never enumerated for scoring.
    ///
    /// <para>"Current" is the highest <c>Sequence</c>, not the latest timestamp: records are
    /// append-only and two written in the same tick would tie, and "which basis applies right now"
    /// has to have exactly one answer.</para>
    ///
    /// <para>The consequence is a product behaviour, stated rather than discovered: <b>an unclaimed
    /// bench member is not scanned, and therefore not considered.</b></para>
    /// </summary>
    public static Expression<Func<Expert, bool>> HasArt22Route { get; } = e =>
        e.ProcessingRecords
            .OrderByDescending(r => r.Sequence)
            .Select(r => (LawfulBasis?)r.Basis)
            .FirstOrDefault() == LawfulBasis.ContractNecessity;

    /// <summary>
    /// Everyone available for work: published, and not paused by the person themselves. What
    /// search, matching, the Command Palette and every MCP read tool see.
    ///
    /// <para>Drafts are excluded here too, so a caller cannot accidentally get the "hidden filtered
    /// but drafts included" combination — the two exclusions have always travelled together, and
    /// the one caller that legitimately wants drafts (the staff review surface) asks for them
    /// explicitly through <see cref="OnTheBench(IQueryable{Expert}, bool)"/>.</para>
    /// </summary>
    public static IQueryable<Expert> OnTheBench(this IQueryable<Expert> experts) =>
        experts.OnTheBench(includeDrafts: false);

    /// <inheritdoc cref="OnTheBench(IQueryable{Expert})"/>
    public static IQueryable<Expert> OnTheBench(this IQueryable<Expert> experts, bool includeDrafts) =>
        experts
            .Where(e => includeDrafts || e.Status == ExpertStatus.Active)
            .Where(NotHidden);

    /// <summary>
    /// What one audience may see (<see cref="RosterAudience"/>). The whole branch lives here rather
    /// than at each call site, so "who sees a paused Expert" is one edit and not a hunt.
    /// </summary>
    public static IQueryable<Expert> ForAudience(
        this IQueryable<Expert> experts, RosterAudience audience, bool includeDrafts = false) =>
        audience == RosterAudience.Bench
            ? experts.OnTheBench(includeDrafts)
            : experts.Where(e => includeDrafts || e.Status == ExpertStatus.Active);

    /// <summary>
    /// The same branch for a read that already names one row by id. Drafts stay reachable — an
    /// ingestion agent reads back the draft it just staged — because publication is a different
    /// question from the pause, and conflating them here would break the promote flow.
    /// </summary>
    public static IQueryable<Expert> ReachableBy(this IQueryable<Expert> experts, RosterAudience audience) =>
        audience == RosterAudience.Bench ? experts.Where(NotHidden) : experts;

    /// <summary>
    /// Everyone the Roster Scan may enumerate: on the bench, <em>and</em> with an Art. 22(2) route.
    /// Scoring-without-persisting was considered and rejected — the model call itself is the
    /// processing, and "we did not write the row" is not a defence.
    /// </summary>
    public static IQueryable<Expert> Scannable(this IQueryable<Expert> experts) =>
        experts.OnTheBench().Where(HasArt22Route);

    // Chunk-shaped callers — the search index rows live in Infrastructure and carry a bare
    // ExpertId with no navigation — compose an EXISTS from the same seam instead:
    //
    //     chunks.Where(c => db.Experts.OnTheBench().Any(e => e.Id == c.ExpertId))
    //
    // which EF translates to a subquery, so hidden rows drop out in SQL while their chunks and
    // their embeddings stay in the table untouched. The predicate is still written only here.

    /// <summary>In-memory counterpart, for a row already loaded — the badge on a Service Manager's
    /// screen, not a filter. Staff see paused people and see that they are paused: a bench that
    /// silently loses somebody is a bench nobody can explain.</summary>
    public static bool IsOnTheBench(this Expert expert) =>
        expert.Status == ExpertStatus.Active && expert.HiddenAt is null;
}
