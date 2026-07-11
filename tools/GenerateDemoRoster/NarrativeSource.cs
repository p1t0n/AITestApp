namespace EmployeeManager.Tools.DemoRoster;

/// <summary>
/// Everything a narrative writer needs to know about one experience slot. The generator owns
/// the structure (who/where/when/which skills); the source only writes prose.
/// </summary>
/// <param name="AcronymHeavy">
/// When true, the narrative should lean on product names / protocol acronyms (FIX 4.4, HL7,
/// Unity ECS, ...) — the spec wants ~10-15% of the roster written this way to stress
/// keyword-ish semantic queries.
/// </param>
public sealed record NarrativeContext(
    string Industry,
    string Company,
    string RoleTitle,
    IReadOnlyList<string> Skills,
    bool AcronymHeavy);

public sealed record ExperienceNarrative(string Summary, IReadOnlyList<string> Achievements);

/// <summary>
/// Seam between deterministic roster assembly and narrative text. Production sources: the
/// offline fragment assembler and (optionally) the GitHub Models enricher; tests plug a stub.
/// </summary>
public interface INarrativeSource
{
    string WriteEmployeeSummary(string industry, string title, IReadOnlyList<string> topSkills, DeterministicRandom rng);

    ExperienceNarrative WriteExperience(NarrativeContext context, DeterministicRandom rng);
}
