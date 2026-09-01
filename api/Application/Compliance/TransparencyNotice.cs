using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Application.Compliance;

/// <summary>One version of the transparency notice: the string recorded against a person, and the
/// words they actually saw.</summary>
/// <param name="Version">The identifier written to <c>ProcessingRecord.NoticeVersion</c> and to
/// <c>User.AcknowledgedNoticeVersion</c>. Dated, so a record is legible without a lookup.</param>
/// <param name="Text">The notice itself, as Markdown.</param>
public sealed record TransparencyNoticeDto(string Version, string Text);

/// <summary>
/// The versioned transparency notice shown at registration (P1T-183). Acknowledging it is required
/// to register — and that acknowledgment, not a consent checkbox, is what the person actually does:
/// under Art. 6(1)(b) necessity does the legal work, and offering a consent control where another
/// basis applies is misleading (EDPB GL 05/2020).
///
/// <para><b>Every version ever shipped stays here.</b> Provability is the whole point of recording a
/// version string: if the text a person acknowledged cannot be recovered afterwards, the record
/// proves nothing. Superseded versions are therefore never deleted or edited — only appended to.</para>
///
/// <para>The wording is constrained by Art. 5(1)(a): a notice that creates a false impression is
/// itself a transparency breach. Service Managers keep full write on an Expert's CV and staff-created
/// rows exist that the Expert never authored, so nothing here may imply the Expert controls their
/// data. It says what is true — the company maintains the bench record, the Expert supplies and
/// corrects their own content, and their rights are transparency, erasure and export rather than
/// exclusive authorship.</para>
/// </summary>
public static class TransparencyNotice
{
    /// <summary>The version shown to anyone registering today.</summary>
    public const string CurrentVersion = "2026-09-01";

    private static readonly IReadOnlyDictionary<string, TransparencyNoticeDto> ByVersion =
        new Dictionary<string, TransparencyNoticeDto>(StringComparer.Ordinal)
        {
            [CurrentVersion] = new(CurrentVersion, V20260901),
        };

    /// <summary>The notice as it stands now.</summary>
    public static TransparencyNoticeDto Current => ByVersion[CurrentVersion];

    /// <summary>Every version ever published, newest first.</summary>
    public static IReadOnlyList<TransparencyNoticeDto> All =>
        ByVersion.Values.OrderByDescending(n => n.Version, StringComparer.Ordinal).ToList();

    /// <summary>The exact text of a version somebody acknowledged, or null if no such version was
    /// ever published — which is how an acknowledgment of an invented version is refused.</summary>
    public static TransparencyNoticeDto? Find(string? version) =>
        version is not null && ByVersion.TryGetValue(version, out var notice) ? notice : null;

    /// <summary>Whether this string names a published version.</summary>
    public static bool IsPublished(string? version) => Find(version) is not null;

    /// <summary>
    /// The newer notice to put in front of this account at its next sign-in, or null when there is
    /// nothing to say. This <em>notifies</em>: no data is gated on it, nothing is re-collected, and
    /// no surface is frozen pending a click.
    ///
    /// <para>Experts only. The notice is addressed to the person a bench record is about; a Service
    /// Manager reads a row's basis on the row. A Service Manager who is also on the bench owns an
    /// Expert row and sees it there (P1T-187) — role decides who is <em>told</em>, ownership decides
    /// whose record it is, and the two are independent by design (P1T-182).</para>
    /// </summary>
    public static string? PendingFor(UserRole role, string? acknowledgedVersion) =>
        role == UserRole.Expert && !string.Equals(acknowledgedVersion, CurrentVersion, StringComparison.Ordinal)
            ? CurrentVersion
            : null;

    private const string V20260901 =
        """
        ## What this service does with your data

        **ExpertToJob keeps a bench: a record of the people this company can put forward for
        client work.** Your entry on it is what this notice is about.

        ### Who holds it

        The company holds and maintains the bench record. You supply and correct your own
        content, and our staff (Service Managers) can also write to it — they add people,
        correct entries, and prepare records for client proposals. **The record is not yours
        alone to author, and this notice will not pretend otherwise.** What you have are rights
        over it: to see everything we hold, to take a copy away, and to have it erased.

        ### What we hold

        Your name, professional headline, email, and optionally your phone number and location;
        your professional summary; your spoken languages; your skills and how long you have used
        them; your qualifications; your work history, including the achievements written under
        each role; and your availability.

        ### Who sees it

        Our Service Managers, in full. Clients see the parts of it that go into a staffing
        proposal or a rendered CV. Nobody else — the bench is not published anywhere.

        ### That software scores and ranks you

        **AI agents read your record, score it against a job description, and rank you against
        other people on the bench.** That is how shortlists are produced here. It is software
        making a judgement about your suitability, and you are entitled to know it is happening
        rather than to discover it. Where such a scoring produces a decision about you with no
        human involvement, you can ask for a person to look at it, say why you disagree, and
        have the outcome reconsidered.

        ### Why we are allowed to

        If you registered yourself, we hold your data because you asked us to consider you for
        work — steps taken at your request before a contract (Art. 6(1)(b) GDPR). If a Service
        Manager entered your record before you ever signed in, we hold it on our own legitimate
        interest in maintaining a bench (Art. 6(1)(f)), and you can object to that at any time.
        Which of the two applies to you is recorded against your record, along with the version
        of this notice you acknowledged, and you can read it back.

        ### Please leave sensitive detail out

        Your summary and your achievements are free text, so what goes in them is up to you.
        **Please do not write anything about your health, religion or beliefs, political
        opinions, trade-union membership, ethnicity, sex life or sexual orientation.** We do not
        search or filter on any of it, and we never try to infer any of it about you. Leaving it
        out is the only way to be sure it is not held at all.

        ### How long we keep it

        We keep your record while it is in use. If nothing happens on it for an extended period
        it expires and is removed — and you can have it removed sooner, at any time, by asking.

        ### Your rights

        - **See everything we hold about you**, including the basis above and where your record
          came from.
        - **Take a copy away** in a machine-readable form, if you registered yourself.
        - **Object** to us holding your record, if a Service Manager created it. We honour that
          by deleting it.
        - **Correct** your own content, at any time, yourself.
        - **Stop being offered for work** without deleting anything — a separate control from
          erasure, deliberately, so that pausing is never mistaken for deleting.
        - **Have it erased.** This is permanent and we cannot undo it for you.

        ### One thing we cannot do

        **This service never sends email.** There is no address we can reach you at, which means
        we cannot notify you of anything outside the app. If this notice changes, you will see
        the new version the next time you sign in — nothing is withheld from you in the meantime.

        ### Complaints

        You can complain to your national data protection authority at any time.
        """;
}
