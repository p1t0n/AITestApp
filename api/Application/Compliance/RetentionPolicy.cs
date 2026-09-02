namespace ExpertToJob.Application.Compliance;

/// <summary>Which clock a record is on, and why.</summary>
public enum RetentionClock
{
    /// <summary>Somebody's own record: two years from the last thing <em>they</em> did with it.</summary>
    Claimed = 1,

    /// <summary>A staff-created record nobody has claimed: six months from the day it was collected.</summary>
    Unclaimed = 2,

    /// <summary>Fabricated data — a seeded sample or the demo roster. Not a person, so retention
    /// does not apply and the sweep must leave it alone.</summary>
    NotAPerson = 3
}

/// <summary>How long one record is kept and when it goes.</summary>
/// <param name="Clock">Which of the two periods applies, or that this is not a person at all.</param>
/// <param name="AnchoredAt">The moment the clock is measured from.</param>
/// <param name="ExpiresAt">When the sweep will delete it, or null when it never will.</param>
public sealed record RetentionVerdict(RetentionClock Clock, DateTimeOffset? AnchoredAt, DateTimeOffset? ExpiresAt)
{
    /// <summary>Whether the record is past its period at the given moment.</summary>
    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt is { } due && now >= due;

    /// <summary>Whether the person should be warned that their record is about to go.</summary>
    public bool IsInFinalWarningAt(DateTimeOffset now) =>
        ExpiresAt is { } due && now < due && now >= due - RetentionPolicy.FinalWarning;
}

/// <summary>
/// How long this service keeps a person, and from when (P1T-188). Two periods, because two
/// populations are in genuinely different situations — and one exclusion, because a fabricated row
/// is not a person at all.
///
/// <para>Pure and static on purpose: the sweep, the Art. 15 disclosure and the warning banner all
/// read the same function, so "when does this record go" has exactly one answer and it is testable
/// without a database.</para>
/// </summary>
public static class RetentionPolicy
{
    /// <summary>
    /// Two years from the person's last activity. CNIL's number, taken because the transparency
    /// design put this service on the EU/CNIL reading (P1T-171); the ICO gives no number at all,
    /// anchoring instead to claim-limitation periods that do not transfer here.
    ///
    /// <para>Counted in calendar years rather than as a fixed span of days: "two years" spans a leap
    /// day roughly half the time, and a promise that quietly expires somebody a day early is a
    /// promise not kept.</para>
    /// </summary>
    public const int ClaimedYears = 2;

    /// <summary>
    /// Six months from collection for a record nobody has claimed, and the reasoning is the finding
    /// of this slice rather than a softer version of the two years.
    ///
    /// <para>An unclaimed record is held on legitimate interest, so it is <b>never scanned</b>
    /// (P1T-185); its subject was <b>never informed</b>, because this service sends no email and
    /// never will; and they can exercise no right at all, because they do not know we exist. We
    /// would be holding a real person's CV, doing nothing with it, unable to tell them. A short
    /// clock is the only mitigation actually available, and it <b>drains that gap over time</b>
    /// instead of letting it accumulate.</para>
    ///
    /// <para>Consequence, stated rather than discovered: a record a Service Manager enters and
    /// nobody claims disappears in six months. It was invisible to the scan for that whole period
    /// anyway, so nothing that was working is lost.</para>
    ///
    /// <para>Calendar months, for the same reason the claimed period is calendar years.</para>
    /// </summary>
    public const int UnclaimedMonths = 6;

    /// <summary>
    /// How long before expiry the person is warned. There is a neat property here: signing in to
    /// read the warning is itself activity, so <b>the warning cures the thing it warns about</b> —
    /// for anybody who signs in. Somebody who never does never sees it, which is the same gap the
    /// notice has and is recorded rather than papered over.
    /// </summary>
    public static readonly TimeSpan FinalWarning = TimeSpan.FromDays(30);

    /// <summary>
    /// Domains reserved by RFC 2606 and RFC 6761 for documentation and testing. No real person has
    /// an address here, so a record carrying one is fabricated — a seeded sample or the demo roster
    /// — and retention does not apply to it.
    ///
    /// <para>This is the sweep's exclusion, and it is a rule rather than a heuristic: the
    /// alternatives considered were matching on invented surnames (brittle and faintly absurd) and
    /// a database column marking demo rows (a schema change that would then need its own Art. 15
    /// disclosure entry, to describe data that is not about anybody). Without <em>some</em>
    /// exclusion the demo roster silently evaporates and every developer's local environment empties
    /// itself overnight.</para>
    /// </summary>
    private static readonly string[] ReservedDomains =
        ["example.com", "example.net", "example.org", ".example", ".invalid", ".test", ".localhost"];

    /// <summary>Whether this address belongs to a fabricated person rather than a real one.</summary>
    public static bool IsFabricated(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            // No address at all: a draft an agent staged and nobody completed. Not something to
            // delete on a clock — the promote gate is what decides a draft's fate.
            return true;
        }

        var address = email.Trim().ToLowerInvariant();
        return ReservedDomains.Any(d =>
            address.EndsWith("@" + d, StringComparison.Ordinal)
            || address.EndsWith(d, StringComparison.Ordinal));
    }

    /// <summary>
    /// The verdict for one record.
    /// </summary>
    /// <param name="email">Its contact address, which is how a fabricated row is recognised.</param>
    /// <param name="isClaimed">Whether an account owns it.</param>
    /// <param name="collectedAt">When the first <c>ProcessingRecord</c> was written — Art. 5(1)(e)'s
    /// reference point, and the only date an unclaimed row has.</param>
    /// <param name="lastActivityAt">When the person last did something themselves, if ever.</param>
    public static RetentionVerdict For(
        string? email, bool isClaimed, DateTimeOffset collectedAt, DateTimeOffset? lastActivityAt)
    {
        if (IsFabricated(email))
        {
            return new RetentionVerdict(RetentionClock.NotAPerson, null, null);
        }

        if (!isClaimed)
        {
            // Nothing has happened to this data since somebody typed it in, so activity is not even
            // a meaningful question — the clock runs from collection.
            return new RetentionVerdict(
                RetentionClock.Unclaimed, collectedAt, collectedAt.AddMonths(UnclaimedMonths));
        }

        var anchor = lastActivityAt ?? collectedAt;
        return new RetentionVerdict(RetentionClock.Claimed, anchor, anchor.AddYears(ClaimedYears));
    }

    /// <summary>What the person is told the period is, in words, for the Art. 15 view.</summary>
    public static string DescriptionFor(RetentionClock clock) => clock switch
    {
        RetentionClock.Claimed =>
            "Because this record is yours, we keep it for two years from the last time you did "
            + "something with it. Anything you do here — editing your record, pausing, even signing "
            + "in to read this page — starts the two years again.",
        RetentionClock.Unclaimed =>
            "Because a Service Manager created this record and nobody has claimed it, we keep it "
            + "for six months from the day it was entered, and then delete it. We keep it for a "
            + "shorter time precisely because we have no way to reach the person it is about.",
        _ =>
            "This is sample data rather than a record about a person, so no retention period "
            + "applies to it.",
    };
}
