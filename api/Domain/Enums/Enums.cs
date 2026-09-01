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
