namespace ExpertToJob.Application.Compliance;

/// <summary>One category of people or organisations the data reaches, and why it reaches them.</summary>
public sealed record RecipientCategory(string Recipient, string Why);

/// <summary>
/// The Art. 15(1) information the access view owes, as text (P1T-187). Separate from
/// <see cref="TransparencyNotice"/> and deliberately so: the notice is one versioned artefact
/// somebody acknowledged at a moment in time and every version of it stays readable forever, while
/// this is a description of the service as it stands right now. Versioning "how things are today"
/// would be answering the wrong question.
///
/// <para>The wording is constrained the same way the notice is (Art. 5(1)(a)): it may not create a
/// false impression. Where the honest answer is uncomfortable — that a named third-party model
/// provider reads this person's CV, that software ranks them — it is said plainly rather than
/// softened into a category nobody could act on.</para>
/// </summary>
public static class Art15Disclosure
{
    /// <summary>Art. 15(1)(a) — what the data is used for.</summary>
    public static IReadOnlyList<string> Purposes { get; } =
    [
        "Maintaining a bench record of the people this company can put forward for work.",
        "Assessing your fit against a job description a client has brought in — including "
        + "automatically, by software that scores and ranks you against other people on the bench.",
        "Preparing staffing proposals and rendered CVs to put in front of a client.",
        "Letting you read, correct and take away your own record.",
    ];

    /// <summary>Art. 15(1)(b) — the categories of data held. Deliberately concrete: "profile data"
    /// tells somebody nothing they could check against what they actually see.</summary>
    public static IReadOnlyList<string> DataCategories { get; } =
    [
        "Identity and contact details: your name, professional title, email address, phone number "
        + "and location.",
        "Career history you or a Service Manager entered: roles, employers, dates, what you did, "
        + "and the achievements written under each role.",
        "Skills with a level and years of experience, spoken languages, degrees and certifications.",
        "Your availability over time, as a schedule of capacity percentages.",
        "Account data: your sign-in address, the registered passkeys on your devices, and a hash "
        + "of your control word — never the control word itself.",
        "Data derived about you by software: search embeddings of your career narrative, and the "
        + "scores, bands and written rationales produced when you are assessed against a job.",
        "The record of why we are allowed to hold your data, and which version of the transparency "
        + "notice you acknowledged.",
    ];

    /// <summary>
    /// Art. 15(1)(c) — categories of recipient. <b>Stated as categories, not as a log of who looked
    /// at what.</b> Logging every view by everyone would answer a disclosure duty by manufacturing a
    /// large new store of personal data about access, which would then need its own disclosure,
    /// retention and erasure.
    ///
    /// <para>The second entry is the one that is new information rather than a restatement: the
    /// model provider is named. Until now this service disclosed it to nobody.</para>
    /// </summary>
    public static IReadOnlyList<RecipientCategory> Recipients { get; } =
    [
        new("Service Managers of this organisation",
            "They maintain the bench and decide who is put forward for a job. They see your record "
            + "in full."),
        new("Google (Gemini), as our AI model provider",
            "Your career narrative is sent to Google's Gemini models to be turned into search "
            + "embeddings and to be scored against job descriptions. This is a named third party "
            + "outside this company, and it is how the scoring described above actually happens."),
        new("Clients this company puts you forward to",
            "They see the parts of your record that go into a staffing proposal or a rendered CV — "
            + "not the whole record, and not the scores."),
    ];

    /// <summary>
    /// Art. 15(1)(d) — retention. Criteria rather than a date, because the clock itself is
    /// P1T-188's slice: saying "we keep it while it is in use" is the honest description of what the
    /// service does today, and inventing a number the code does not enforce would be worse than
    /// naming the criterion.
    /// </summary>
    public const string Retention =
        "We keep your record while it is in use. If nothing happens on it for an extended period it "
        + "expires and is removed. You can have it removed sooner, at any time, by deleting it "
        + "yourself — and a record kept only because a Service Manager entered it can be objected "
        + "to, which we honour by deleting it.";

    /// <summary>
    /// Art. 15(1)(h) — "meaningful information about the logic involved". C-203/22 §§59, 61, 76:
    /// the procedure and principles <em>actually applied</em>, in a form the person can understand
    /// and act on. Not the algorithm, not the weights, and not every step.
    ///
    /// <para>We rely on Art. 22(2)(a) — the scoring is necessary to place people on jobs — so this
    /// text concedes the automation rather than claiming a human is meaningfully in the loop. That
    /// concession is what the safeguards below have to earn.</para>
    /// </summary>
    public const string Art22Logic =
        """
        ### How the scoring works, and what it does to you

        When a Service Manager brings in a job description, the software first distils it into a
        list of requirements. Your record is then matched against those requirements in two steps.

        1. **Retrieval.** Your career narrative — your summary and what is written under each role —
           is turned into a numeric representation and compared against each requirement, to find
           the parts of your history that look relevant. The passages it finds are quoted back as
           evidence.
        2. **Assessment.** Those passages, your skills, and your availability are sent to an AI
           model together with the job description. The model returns a score out of 100, a band,
           and a short written rationale explaining the score. Nothing else about you is used: not
           your name's origin, not your location beyond a filter a Service Manager set, and no
           characteristic we could infer about you — we never attempt such inferences.

        The ranking that comes out of this decides who a Service Manager is shown first, and in
        practice that decides who is considered. We do not claim a person meaningfully reviews each
        score before that happens: the assessment is automated, and we rely on it being necessary to
        place people on jobs at all.

        **Two consequences you can act on.** The score and the rationale written about you are shown
        to you in full, below — if we would not be willing to show you what the software wrote, it
        should not have been written. And you can ask for a human to look at any score, say why you
        disagree, and have the outcome reconsidered.

        **What is not scored.** If your record is held only because a Service Manager entered it,
        rather than because you registered, it is excluded from this scoring entirely.
        """;

    /// <summary>Art. 15(1)(e)–(f) — the rights, in the order somebody is likely to want them.</summary>
    public static IReadOnlyList<string> Rights { get; } =
    [
        "See everything held about you — this page.",
        "Take a machine-readable copy away.",
        "Correct your own content yourself, at any time.",
        "Stop being offered for work without deleting anything, and start again later.",
        "Have a human look at a score, say why you disagree, and have it reconsidered.",
        "Object to us holding a record a Service Manager created; we honour that by deleting it.",
        "Have everything erased. This is permanent and cannot be undone.",
    ];

    /// <summary>Art. 15(1)(f) — the right to complain, named as a right rather than buried.</summary>
    public const string ComplaintRight =
        "If you think we are handling your data wrongly you can complain to a data protection "
        + "supervisory authority in the EU country where you live or work, or where you think the "
        + "problem happened. You do not need our permission and you do not need to raise it with us "
        + "first.";

    /// <summary>Art. 15(1)(g) — the source, and only where the data did <em>not</em> come from the
    /// person. A row somebody registered themselves has no source to disclose; a staff-created row
    /// does, and this service can never have told them at the time (there is no email).</summary>
    public static string? SourceFor(Domain.Enums.ProcessingOrigin origin) =>
        origin == Domain.Enums.ProcessingOrigin.StaffCreated
            ? "A Service Manager at this company entered your record. You did not give us this data "
              + "yourself, and because this service sends no email we had no way to tell you at the "
              + "time — you are reading this because you signed in and found it."
            : null;
}
