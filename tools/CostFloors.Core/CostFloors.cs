namespace CvManager.CostFloors;

/// <summary>
/// The committed Cost Floors (P1T-144) — Ratchets, in <see cref="TokenEstimate"/> tokens. THE
/// place these numbers live: the cost-side sibling of <c>tests/Mcp.Tests/Eval/EvalBaselines.cs</c>
/// and <c>tests/Agents.Tests/Eval/AgentEvalBaselines.cs</c>. Shared by both test projects because
/// an agent's Baseline Prompt Size is its own instructions PLUS the schemas of the MCP tools it is
/// shown, and those two halves are only measurable in different assemblies.
///
/// <para>A Ratchet may only ever move DOWN. Every ceiling here is pinned at the value measured on
/// 2026-08-30, each with a comment naming the ticket walking it toward its target — so main stays
/// green through the whole chain and every tightening lands as a visible numeric delta in a diff.
/// Raising one is a deliberate re-baseline that needs a reason on the issue; it is never the fix
/// for a red run. The evidence behind the numbers is in <c>manuals/agent-cost-budgets.md</c>, the
/// values and how to re-measure them in <c>manuals/agent-eval-baselines.md</c>.</para>
/// </summary>
public static class CostFloors
{
    /// <summary>Employees seeded for the result-size floor. Pinned at the roster size the
    /// 2026-08-30 measurement ran on, so the committed ceilings stay comparable to it.</summary>
    public const int DemoRosterEmployees = 45;

    /// <summary>
    /// Per-read-tool RESULT ceilings, measured over the seeded demo roster
    /// (<see cref="DemoRosterEmployees"/> employees, 79 catalog skills) against real Postgres.
    /// Every model-free read tool must appear here — the floor test fails on an unlisted one, so
    /// a new read tool cannot ship unmeasured.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> ReadToolResultCeilings =
        new Dictionary<string, int>
        {
            // 42% of the 160,220-token roster-qa run and the single largest line item: no filter,
            // no paging, the whole catalog. P1T-145 adds nameContains + paging and ratchets this
            // down to roughly the handful of rows a lookup actually wants.
            ["skill_list"] = 3_080,

            // 12.7% of the same run. No paging by design (the roster IS the answer), so the cost
            // is bounded by roster size; the Tool Allowlist (P1T-146) is what keeps it away from
            // agents with no business listing everyone.
            ["employee_list"] = 2_805,

            ["employee_get"] = 2_064,
            ["cv_get"] = 1_643,
            ["availability_list"] = 73,
            ["category_list"] = 270,
            ["category_tree"] = 3_379,

            // Already pages — the P1T-121 reference implementation. This is one default page of 50,
            // which is why a full sweep is a deliberate act and a stray call is not. The only
            // ceiling here carrying slack: digests truncate at 1,500 CHARACTERS, and EF leaves
            // an employee's experience order unspecified, so which non-ASCII characters fall
            // inside the cut — and therefore how many \uXXXX escapes the JSON carries — shifts a
            // few tokens per seed. Measured 18,248-18,253.
            ["roster_digest_list"] = 18_300,
        };

    /// <summary>
    /// Read tools whose result cannot be measured without a model: they embed the query first, so
    /// they have no place in a deterministic floor. Their results are the CHEAP half anyway
    /// (73–1,183 tokens in the traced run) — the structured tools above are the expensive ones.
    /// </summary>
    public static readonly IReadOnlySet<string> ModelBackedReadTools =
        new HashSet<string>
        {
            "roster_semantic_search",
            "roster_shortlist_search",
            "style_exemplar_search",
        };

    /// <summary>
    /// Per-tool SCHEMA ceilings: what one tool costs merely by being offered — its name,
    /// description and input schema, serialized as <see cref="ToolSurface.SchemaText"/>. This is
    /// the half that regressed through P1T-128/129, and it is paid on every iteration.
    ///
    /// <para>The write surface is here too, not out of completeness: resume-ingestion holds
    /// <c>mcp:write</c>, so write descriptions are part of ITS Baseline Prompt Size — and it is
    /// the agent with the worst recorded single call (157,252 tokens, P1T-150).</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> ToolSchemaCeilings =
        new Dictionary<string, int>
        {
            // ---- read surface (mcp:read) ----
            ["availability_list"] = 223,
            ["category_list"] = 165,
            ["category_tree"] = 163,
            ["cv_get"] = 296,
            ["employee_get"] = 357,
            ["employee_list"] = 243,
            ["roster_digest_list"] = 301,
            ["roster_semantic_search"] = 613,
            ["roster_shortlist_search"] = 635,
            ["skill_list"] = 176,
            ["style_exemplar_search"] = 689,

            // ---- write + destructive surface: resume-ingestion holds mcp:write ----
            ["achievement_add"] = 359,
            ["achievement_delete"] = 113,
            ["achievement_update"] = 238,
            ["availability_add"] = 367,
            ["availability_delete"] = 172,
            ["availability_update"] = 274,
            ["category_create"] = 251,
            ["category_delete"] = 158,
            ["category_update"] = 241,
            ["employee_create"] = 421,
            ["employee_create_draft"] = 379,
            ["employee_delete"] = 216,
            ["employee_skill_add"] = 428,
            ["employee_skill_delete"] = 149,
            ["employee_skill_update"] = 351,
            ["employee_update"] = 482,
            ["experience_add"] = 564,
            ["experience_delete"] = 152,
            ["experience_skill_add"] = 275,
            ["experience_skill_delete"] = 146,
            ["experience_update"] = 445,
            ["language_add"] = 314,
            ["language_delete"] = 122,
            ["language_update"] = 248,
            ["qualification_add"] = 509,
            ["qualification_delete"] = 121,
            ["qualification_update"] = 381,
            ["skill_create"] = 310,
            ["skill_delete"] = 155,
            ["skill_update"] = 268,
        };

    /// <summary>
    /// The tools an <c>mcp:read</c> token is shown — the surface every read-only agent picks from.
    /// Declared here so <c>Agents.Tests</c> can offer an agent the surface its real token would
    /// carry; <c>Mcp.Tests</c> asserts this matches what the server actually advertises, so the
    /// declaration cannot drift away from the scope policy.
    /// </summary>
    public static readonly IReadOnlySet<string> ReadScopeTools =
        new HashSet<string>
        {
            "availability_list", "category_list", "category_tree", "cv_get", "employee_get",
            "employee_list", "roster_digest_list", "roster_semantic_search",
            "roster_shortlist_search", "skill_list", "style_exemplar_search",
        };

    /// <summary>
    /// The tools an <c>mcp:read mcp:write</c> token is shown: the read surface plus every
    /// non-destructive write. Destructive tools need <c>mcp:admin</c> and no agent holds it.
    /// resume-ingestion is the one agent on this surface, and it pays for all of it.
    /// </summary>
    public static readonly IReadOnlySet<string> WriteScopeTools =
        new HashSet<string>(ReadScopeTools)
        {
            "achievement_add", "achievement_update", "availability_add", "availability_update",
            "category_create", "category_update", "employee_create", "employee_create_draft",
            "employee_skill_add", "employee_skill_update", "employee_update", "experience_add",
            "experience_skill_add", "experience_update", "language_add", "language_update",
            "qualification_add", "qualification_update", "skill_create", "skill_update",
        };

    /// <summary>
    /// Ceiling on the WHOLE read surface an <c>mcp:read</c> token is shown — every read tool's
    /// schema text. roster-qa is the only agent still handed all of it; P1T-146 cuts it to 4 of 11.
    /// </summary>
    public const int ReadToolSurfaceCeiling = 3_861;

    /// <summary>
    /// Per-agent INSTRUCTION ceilings — the prompt an agent brings itself, before any tool schema
    /// or tool result. Keyed by agent class name. P1T-148 rewrites roster-qa's for Convergence,
    /// which is what will move that one.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> AgentInstructionCeilings =
        new Dictionary<string, int>
        {
            ["RosterQaAgent"] = 416,
            ["CvTailoringAgent"] = 498,
            ["ResumeIngestionAgent"] = 523,
            ["MatchAgent"] = 328,
            ["ShortlistAgent"] = 121,
            ["InterviewKitAgent"] = 371,
            ["BenchReportService"] = 199,
            ["JdRequirementExtractor"] = 237,
            ["QueuedSyncScoringTransport"] = 199,
        };

    /// <summary>
    /// Per-agent BASELINE PROMPT SIZE ceilings: instructions plus the schemas of the tools that
    /// agent is actually shown, which is what one model call costs it before a single tool result
    /// comes back — and what Turn Amplification multiplies on every iteration.
    ///
    /// <para>Only agents that are handed tools appear here. Most already narrow the surface
    /// themselves (Match and Interview Kit to <c>cv_get</c>, Tailoring to <c>cv_get</c> +
    /// <c>style_exemplar_search</c>); roster-qa is the outlier that takes all 11, which is the
    /// 26% line item in the traced run and P1T-146's target.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> BaselinePromptSizeCeilings =
        new Dictionary<string, int>
        {
            // 416 instructions + all 11 read tool schemas — 3,861 of it is schema the agent
            // never uses. P1T-146 shows it 4 tools instead, which should land near 2,300.
            ["RosterQaAgent"] = 4_277,
            ["CvTailoringAgent"] = 1_187,
            ["ResumeIngestionAgent"] = 2_893,
            ["MatchAgent"] = 624,
            ["InterviewKitAgent"] = 667,
        };

    /// <summary>Composes the number: instructions the agent sent, plus the pinned schema ceiling of
    /// every tool it handed the model. The schema half comes from the Mcp.Tests floor rather than
    /// from a second measurement — that floor is what holds those values true.</summary>
    public static int BaselinePromptSize(int instructionTokens, IEnumerable<string> toolNames) =>
        instructionTokens + toolNames.Sum(t => ToolSchemaCeilings.GetValueOrDefault(t));
}

/// <summary>The canonical serialization of a tool as the model is offered it — the unit every
/// schema-side Cost Floor is measured in. Name, description and input schema, nothing else.</summary>
public static class ToolSurface
{
    public static string SchemaText(string name, string? description, string schemaJson) =>
        $"{name}\n{description}\n{schemaJson}";
}
