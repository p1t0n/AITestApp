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
        "Career history you or an Administrator entered: roles, employers, dates, what you did, "
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
    /// Art. 15(1)(c) — categories of recipient, for the two providers this deployment is actually
    /// running (EXP-21, EXP-66). <b>Stated as categories, not as a log of who looked at what.</b>
    /// Logging every view by everyone would answer a disclosure duty by manufacturing a large new
    /// store of personal data about access, which would then need its own disclosure, retention and
    /// erasure.
    ///
    /// <para>The provider entries are the ones that are new information rather than a restatement —
    /// until this existed the service disclosed it to nobody — and they are the entries that can be
    /// <em>false</em> statements, which is why they are parameters rather than literals. A
    /// hard-coded name survives a provider change silently, and the person reading it has no way to
    /// tell.</para>
    ///
    /// <para><b>Chat and embeddings move independently</b>
    /// (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 16), so this is four present-tense
    /// combinations rather than two. One provider doing both jobs is one entry; two providers are
    /// two, named by the job each does, because naming only one would understate the transfer to
    /// the other. Either parameter being null means configuration named nothing we recognise, and
    /// the honest answer there is to name both companies.</para>
    /// </summary>
    /// <param name="chat">The chat provider, from <see cref="ChatProviderDisclosure"/>.</param>
    /// <param name="embeddings">The embeddings provider, from
    /// <see cref="EmbeddingsProviderDisclosure"/>.</param>
    /// <param name="formerEmbeddings">A provider this deployment has since left, which this
    /// person's narrative reached while it was active — from
    /// <see cref="FormerEmbeddingsRecipientFor"/>. Placed with the other third parties rather than
    /// after the clients row, because that is what it is.</param>
    public static IReadOnlyList<RecipientCategory> RecipientsFor(
        DisclosedChatProvider? chat,
        DisclosedEmbeddingsProvider? embeddings,
        RecipientCategory? formerEmbeddings = null)
    {
        RecipientCategory[] providers = (chat, embeddings) switch
        {
            (DisclosedChatProvider.Gemini, DisclosedEmbeddingsProvider.Gemini) =>
                [GoogleForEverything],
            (DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.AzureFoundry) =>
                [MicrosoftForEverything],
            (DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.Gemini) =>
                [GoogleForEmbeddings, MicrosoftForChat],
            (DisclosedChatProvider.Gemini, DisclosedEmbeddingsProvider.AzureFoundry) =>
                [MicrosoftForEmbeddings, GoogleForChat],

            // Nothing we recognise on one side or both: name every company it might be. Over-telling
            // a data subject is survivable; naming the wrong recipient is not.
            _ => [GoogleForEmbeddings, MicrosoftForChat],
        };

        return
        [
            Administrators,
            .. providers,
            .. formerEmbeddings is null ? Array.Empty<RecipientCategory>() : [formerEmbeddings],
            Clients,
        ];
    }

    /// <summary>
    /// The one recipient entry that is about the past (EXP-62, ADR §2 decision 18): a provider this
    /// deployment has left, whose models this person's career narrative reached while it was
    /// active. Null when there is nothing in the past tense to say.
    ///
    /// <para><b>It states only what this service can verify</b> — that the data was sent, and until
    /// when. It says nothing about what the former provider retained or deleted, because that is
    /// not a fact this service knows, and Art. 5(1)(a) forbids a disclosure that implies otherwise.
    /// The provider's own terms belong in the DPIA, not in a sentence written at a data
    /// subject.</para>
    ///
    /// <para>Whether a period covers somebody is decided by their <em>own record's</em> creation
    /// date, which is why the period table needs no Expert id and holds no personal data.</para>
    /// </summary>
    /// <param name="recordCreatedAt">When this person's record was first held — the first entry in
    /// their lawful-basis history.</param>
    /// <param name="periods">The deployment's embeddings provider history, in any order.</param>
    public static RecipientCategory? FormerEmbeddingsRecipientFor(
        DateTimeOffset recordCreatedAt,
        IReadOnlyList<Domain.Entities.EmbeddingsProviderPeriod> periods)
    {
        // The latest Gemini period that has actually ended and was still running when this record
        // was created. Latest, because an earlier end date would tell somebody Google stopped
        // receiving their data long before it did.
        var endedAt = periods
            .Where(p => string.Equals(p.Provider, nameof(DisclosedEmbeddingsProvider.Gemini),
                StringComparison.OrdinalIgnoreCase))
            .Where(p => p.EndedAt is { } ended && recordCreatedAt < ended)
            .Max(p => p.EndedAt);

        if (endedAt is not { } until)
        {
            return null;
        }

        return new RecipientCategory(
            "Google (Gemini), formerly our embeddings provider",
            $"Until {until:yyyy-MM-dd}, your career narrative was sent to Google's Gemini models to "
            + "be turned into search embeddings. Since then, embeddings are produced by another "
            + "provider named on this page, and Google receives nothing new from us.");
    }

    /// <summary>
    /// The search-index note on the access view, naming the provider that actually produces the
    /// embeddings. A literal here was the second string EXP-55's audit found: it reads as a
    /// contradiction next to a changed recipient list, and it goes silently wrong the day
    /// embeddings move.
    /// </summary>
    public static string SearchIndexNoteFor(DisclosedEmbeddingsProvider? embeddings) =>
        "Your summary and each of your roles are also held as numeric representations (embeddings) "
        + embeddings switch
        {
            DisclosedEmbeddingsProvider.Gemini => "produced by Google's Gemini models",
            DisclosedEmbeddingsProvider.AzureFoundry =>
                "produced by Microsoft's Azure OpenAI service, within the EU",
            _ => "produced by the embeddings provider named among the recipients above",
        }
        + ", so that a search for a capability can find your record. They are derived from the text "
        + "above and hold nothing you have not already read here.";

    private static readonly RecipientCategory Administrators =
        new("Administrators of this organisation",
            "They maintain the bench and decide who is put forward for a job. They see your record "
            + "in full.");

    private static readonly RecipientCategory Clients =
        new("Clients this company puts you forward to",
            "They see the parts of your record that go into a staffing proposal or a rendered CV — "
            + "not the whole record, and not the scores.");

    /// <summary>One provider doing both jobs — the wording this disclosure has always carried, kept
    /// word for word so the Gemini deployment says today exactly what it said yesterday.</summary>
    private static readonly RecipientCategory GoogleForEverything =
        new("Google (Gemini), as our AI model provider",
            "Your career narrative is sent to Google's Gemini models to be turned into search "
            + "embeddings and to be scored against job descriptions. This is a named third party "
            + "outside this company, and it is how the scoring described above actually happens.");

    /// <summary>The same sentence for the other provider, plus the one fact that makes its transfer
    /// different in kind rather than only in name: the deployment is EU-confined (ADR §7).</summary>
    private static readonly RecipientCategory MicrosoftForEverything =
        new("Microsoft (Azure OpenAI), as our AI model provider",
            "Your career narrative is sent to Microsoft's Azure OpenAI service to be turned into "
            + "search embeddings and to be scored against job descriptions. The embeddings are "
            + "processed within the EU. This is a named third party outside this company, and it is "
            + "how the scoring described above actually happens.");

    private static readonly RecipientCategory GoogleForEmbeddings =
        new("Google (Gemini), as our embeddings provider",
            "Your career narrative is sent to Google's Gemini models to be turned into search "
            + "embeddings, so that a search for a capability can find your record. This is a named "
            + "third party outside this company.");

    private static readonly RecipientCategory MicrosoftForEmbeddings =
        new("Microsoft (Azure OpenAI), as our embeddings provider",
            "Your career narrative is sent to Microsoft's Azure OpenAI service to be turned into "
            + "search embeddings, so that a search for a capability can find your record. They are "
            + "processed within the EU. This is a named third party outside this company.");

    private static readonly RecipientCategory MicrosoftForChat =
        new("Microsoft (Azure OpenAI), as our AI model provider",
            "Your career narrative, your skills and your availability are sent to Microsoft's Azure "
            + "OpenAI service to be scored against job descriptions. This is a named third party "
            + "outside this company, and it is how the scoring described above actually happens.");

    private static readonly RecipientCategory GoogleForChat =
        new("Google (Gemini), as our AI model provider",
            "Your career narrative, your skills and your availability are sent to Google's Gemini "
            + "models to be scored against job descriptions. This is a named third party outside "
            + "this company, and it is how the scoring described above actually happens.");

    /// <summary>
    /// Art. 15(1)(d) — retention. Criteria rather than a date, because the clock itself is
    /// P1T-188's slice: saying "we keep it while it is in use" is the honest description of what the
    /// service does today, and inventing a number the code does not enforce would be worse than
    /// naming the criterion.
    /// </summary>
    public const string Retention =
        "We keep your record while it is in use. If nothing happens on it for an extended period it "
        + "expires and is removed. You can have it removed sooner, at any time, by deleting it "
        + "yourself — and a record kept only because an Administrator entered it can be objected "
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

        When an Administrator brings in a job description, the software first distils it into a
        list of requirements. Your record is then matched against those requirements in two steps.

        1. **Retrieval.** Your career narrative — your summary and what is written under each role —
           is turned into a numeric representation and compared against each requirement, to find
           the parts of your history that look relevant. The passages it finds are quoted back as
           evidence.
        2. **Assessment.** Those passages, your skills, and your availability are sent to an AI
           model together with the job description. The model returns a score out of 100, a band,
           and a short written rationale explaining the score. Nothing else about you is used: not
           your name's origin, not your location beyond a filter an Administrator set, and no
           characteristic we could infer about you — we never attempt such inferences.

        The ranking that comes out of this decides who an Administrator is shown first, and in
        practice that decides who is considered. We do not claim a person meaningfully reviews each
        score before that happens: the assessment is automated, and we rely on it being necessary to
        place people on jobs at all.

        **Two consequences you can act on.** The score and the rationale written about you are shown
        to you in full, below — if we would not be willing to show you what the software wrote, it
        should not have been written. And you can ask for a human to look at any score, say why you
        disagree, and have the outcome reconsidered.

        **What is not scored.** If your record is held only because an Administrator entered it,
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
        "Object to us holding a record an Administrator created; we honour that by deleting it.",
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
            ? "An Administrator at this company entered your record. You did not give us this data "
              + "yourself, and because this service sends no email we had no way to tell you at the "
              + "time — you are reading this because you signed in and found it."
            : null;
}
