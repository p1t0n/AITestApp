namespace ExpertToJob.Application.Compliance;

/// <summary>What erasure does to one store.</summary>
public enum PersonalDataAction
{
    /// <summary>The rows go. Usually by database cascade from <c>Expert</c> or <c>User</c> — which
    /// is deliberate: a cascade cannot be forgotten by a code path that did not know about it.</summary>
    Delete = 1,

    /// <summary>The row survives because something other than the person needs it — a decision a
    /// human made — and its personal fields are nulled in place.</summary>
    Scrub = 2,

    /// <summary>Untouched, and the reason has to be better than "it was hard".</summary>
    Keep = 3
}

/// <summary>
/// One store that holds, or points at, a person (P1T-186).
/// </summary>
/// <param name="Entity">The EF entity type's name. A string rather than a <see cref="Type"/>
/// because <c>ExpertSearchChunk</c> lives in Infrastructure — the Application layer cannot name the
/// type, and a declaration that quietly omitted the store carrying every CV's free text and its
/// embeddings would be worse than useless.</param>
/// <param name="PersonalFields">The properties that are, or contain, personal data. Read by the
/// erasure-completeness test to know what to look for afterwards, and by the Art. 15 access view
/// (P1T-187) to know what to show. Empty when the store only <em>points</em> at a person.</param>
/// <param name="Reason">Why this action and not another, in plain words. This is the field an
/// auditor reads.</param>
public sealed record PersonalDataStore(
    string Entity,
    PersonalDataAction Action,
    IReadOnlyList<string> PersonalFields,
    string Reason);

/// <summary>
/// Every store this service holds personal data in, and what erasure does to each (P1T-186).
/// <b>One declaration, two readers</b>: the erasure path scrubs from it, and the Art. 15 access view
/// and Art. 20 export read it too (P1T-187). Two hand-maintained lists would drift, and the drift
/// would be invisible until an audit — so <c>PersonalDataDeclarationTests</c> walks the real EF
/// model and fails the build when a store carrying an <c>ExpertId</c> or a <c>UserId</c> is not
/// declared here.
///
/// <para><b>Scrubbing is pseudonymisation, not anonymisation</b> (EDPB GL 01/2025 §22). Where a row
/// survives with its <c>ExpertId</c> intact, the residue is acknowledged personal data held under
/// Art. 18 restriction — it is not laundered data we may call anonymous, and no code or document
/// here should imply otherwise.</para>
/// </summary>
public static class PersonalDataDeclaration
{
    /// <summary>
    /// The declaration. Ordered as erasure reads it: what the cascade takes, what survives scrubbed,
    /// and what is deliberately left alone.
    /// </summary>
    public static IReadOnlyList<PersonalDataStore> All { get; } =
    [
        // ---- Deleted outright, by cascade from the Expert row -------------------------------
        new("Expert",
            PersonalDataAction.Delete,
            ["FirstName", "LastName", "Title", "Email", "Phone", "Location", "Summary", "PhotoUrl"],
            "The record itself. Hard-deleted with the account as one act — no tombstone, so somebody "
            + "registering again afterwards is simply a new Expert needing no special path."),

        new("SpokenLanguage", PersonalDataAction.Delete, ["Language"], "Cascades with the Expert."),
        new("AvailabilityEntry", PersonalDataAction.Delete, [], "Cascades with the Expert."),
        new("ExpertSkill", PersonalDataAction.Delete, [], "Cascades with the Expert."),
        new("Qualification",
            PersonalDataAction.Delete,
            ["Name", "Institution", "Field", "Issuer", "CredentialId"],
            "Cascades with the Expert. Names an institution and a credential number, so it is "
            + "identifying well beyond the roster."),
        new("Experience",
            PersonalDataAction.Delete,
            ["Company", "Title", "Location", "Summary"],
            "Cascades with the Expert."),
        new("Achievement",
            PersonalDataAction.Delete,
            ["Text"],
            "Cascades through Experience — two hops from the Expert, and the easiest to miss."),
        new("ExperienceSkill", PersonalDataAction.Delete, [], "Cascades through Experience."),

        new("ExpertSearchChunk",
            PersonalDataAction.Delete,
            ["Content", "Embedding"],
            "Cascades with the Expert. The embedding is a vector *of* the CV text, so deleting the "
            + "content and keeping the vector would leave derived personal data behind. This is the "
            + "one store the pause deliberately keeps and erasure deliberately destroys, which is "
            + "why it carries its own regression test."),

        new("ProcessingRecord",
            PersonalDataAction.Delete,
            ["Reason"],
            "Cascades with the Expert, and cannot do anything else: the table carries a BEFORE "
            + "UPDATE trigger, so it is delete-or-nothing. P1T-172's table called this 'keep', but "
            + "P1T-183 built the cascade for this exact act and left DELETE open on purpose — and "
            + "keeping rows about an erased person, to prove we once had a basis for data we no "
            + "longer hold, is not something Art. 17(3) plainly covers."),

        new("PendingClaim",
            PersonalDataAction.Delete,
            ["ClaimantEmail"],
            "Cascades from both sides. Carries its own copy of the email, deliberately (P1T-184), "
            + "so it would survive any scrub that only touched the account."),

        new("ClaimCode",
            PersonalDataAction.Delete,
            [],
            "Cascades with the Expert. Holds only a hash and two account ids."),

        // ---- Deleted outright, by cascade from the account ----------------------------------
        new("User",
            PersonalDataAction.Delete,
            ["Email", "ControlWordHash", "AcknowledgedNoticeVersion"],
            "Deleted with the Expert row as one act. Its absence is also what refuses every live "
            + "session on both hosts."),

        new("PasskeyCredential",
            PersonalDataAction.Delete,
            ["CredentialId", "PublicKey", "AaGuid", "Transports", "Label"],
            "Cascades with the account. Device identifiers tied to one person."),

        new("AgentUsage",
            PersonalDataAction.Delete,
            [],
            "Cascades with the account. No names, but per-call behavioural history against a "
            + "user id is personal data about how somebody worked."),

        // ---- Deleted by the erasure path itself, because no cascade reaches them ------------
        new("ScoringJobCandidate",
            PersonalDataAction.Delete,
            ["Name", "Title", "Digest", "Rationale"],
            "Deleted row and all: a scan is a working artefact, not a decision, so there is nothing "
            + "worth hollowing out. It holds the whole career digest and a model-written rationale, "
            + "and until P1T-186 no cascade reached it: the FK this slice adds is what makes the "
            + "deletion automatic rather than something a future code path has to remember."),

        // ---- Survives, scrubbed, because a human decided something -------------------------
        new("StaffingProposalCandidate",
            PersonalDataAction.Scrub,
            ["Name", "Title", "Rationale"],
            "A human made a decision on this row, and Art. 17(3)(e) covers the fact that they did. "
            + "Name, title and rationale are nulled; ExpertId and the scores stay. Deliberately "
            + "*no* foreign key: the row has to outlive the Expert, so the id is a restricted-"
            + "processing reference and not a link."),

        new("StaffingProposal",
            PersonalDataAction.Scrub,
            ["PackageJson"],
            "The handoff document is the decision's evidence base, so the envelope survives and six "
            + "named fields inside it are nulled. Its own structure is untouched, and the approver "
            + "view still renders."),
    ];

    /// <summary>Everything erasure must leave nothing personal behind in.</summary>
    public static IEnumerable<PersonalDataStore> Erased =>
        All.Where(s => s.Action is PersonalDataAction.Delete or PersonalDataAction.Scrub);

    public static PersonalDataStore For(string entity) =>
        All.SingleOrDefault(s => s.Entity == entity)
        ?? throw new InvalidOperationException(
            $"'{entity}' holds or points at personal data and is not declared in "
            + $"{nameof(PersonalDataDeclaration)}. Declare it and classify it — a store nobody "
            + "declared is a store erasure does not reach.");
}
