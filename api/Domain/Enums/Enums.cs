namespace ExpertToJob.Domain.Enums;

/// <summary>Draft experts are agent-staged (resume ingestion) and invisible to the roster,
/// search index, and staffing until a human promotes them. Humans hold publication authority.</summary>
public enum ExpertStatus
{
    Draft = 1,
    Active = 2
}

public enum SkillLevel
{
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3,
    Expert = 4
}

public enum LanguageLevel
{
    Basic = 1,
    Conversational = 2,
    Professional = 3,
    Fluent = 4,
    Native = 5
}

public enum QualificationType
{
    Degree = 1,
    Certification = 2
}

public enum UserStatus
{
    Active = 1,
    Deactivated = 2
}

/// <summary>
/// What an account is allowed to be. <see cref="ServiceManager"/> is staff: the roster, the skill
/// catalog, user administration, the agent surfaces. <see cref="Expert"/> is the person the CV is
/// about — they reach their own data and nothing else. Signup creates an Expert; staff are made,
/// not self-declared (bootstrap config or promotion by another Service Manager).
/// </summary>
public enum UserRole
{
    ServiceManager = 1,
    Expert = 2
}

/// <summary>
/// What a <see cref="Entities.ExpertSearchChunk"/> was rendered from: one work
/// <see cref="Entities.Experience"/>, an expert's professional <c>Summary</c>, or a single
/// <see cref="Entities.Achievement"/> bullet.
/// </summary>
public enum SearchChunkSource
{
    Experience = 1,
    Summary = 2,
    Achievement = 3
}

/// <summary>
/// How a roster row came to be — and therefore, through <see cref="Entities.ProcessingRecord"/>,
/// which Art. 6(1) ground it is processed on. Origin is the only input to that decision: there is
/// no global default and no per-installation setting, because a basis chosen once for everybody is
/// exactly the mistake EDPB GL 05/2020 §§120, 123 says cannot be corrected later.
/// </summary>
public enum ProcessingOrigin
{
    /// <summary>The person put themselves here — they registered, acknowledged the transparency
    /// notice, and asked to be considered for Jobs. A pre-contractual measure at their own request
    /// (CNIL's staffing-agency reading of Art. 6(1)(b)).</summary>
    SelfRegistered = 1,

    /// <summary>A Service Manager (or an ingestion agent acting for one) entered the row. Nobody
    /// asked us to hold it on the subject's behalf, so the only ground available is the company's
    /// own legitimate interest in maintaining a bench.</summary>
    StaffCreated = 2
}

/// <summary>
/// The Art. 6(1) ground a <see cref="Entities.ProcessingRecord"/> row states. Derived from
/// <see cref="ProcessingOrigin"/> and never chosen independently — see
/// <see cref="Entities.ProcessingRecord.BasisFor"/>, and the database CHECK constraint that refuses
/// any other pairing.
///
/// <para>No zero member, deliberately: a record built by some path that skipped the factory carries
/// a value the enum does not define, the string mapping writes <c>"0"</c>, and the check constraint
/// rejects the insert. An invalid basis fails loudly instead of defaulting to a lawful-looking one.</para>
/// </summary>
public enum LawfulBasis
{
    /// <summary>Art. 6(1)(b) — necessary for steps taken at the data subject's request prior to
    /// entering a contract. Preserves Art. 20 portability and is one of the three Art. 22(2) routes
    /// to lawful automated decision-making.</summary>
    ContractNecessity = 1,

    /// <summary>Art. 6(1)(f) — the controller's legitimate interests. Carries an Art. 21 objection
    /// right instead of portability, and — the consequence that matters — is <em>not</em> among the
    /// Art. 22(2) exceptions, so a row on this basis has no route to automated decision-making.</summary>
    LegitimateInterest = 2
}
