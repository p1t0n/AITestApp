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
            // Was 3,080 — the whole 79-skill catalog, 42% of the 160,220-token roster-qa run and
            // its single largest line item. P1T-145 gave it a nameContains filter, so this now
            // measures the call the traced run actually wanted: resolving one skill name. The
            // unfiltered sweep is ratcheted separately (SkillListUnfilteredPageCeiling) — a
            // lookup is the hot path and the only one Turn Amplification multiplied nine times.
            // 87 is nameContains "React" over the seeded catalog: two rows (React, React Native)
            // plus the page envelope. A 35× cut, and the amplified 67,698 becomes ~780.
            ["skill_list"] = 87,

            // 12.7% of the same run. No paging by design (the roster IS the answer), so the cost
            // is bounded by roster size; the Tool Allowlist (P1T-146) is what keeps it away from
            // agents with no business listing everyone — only roster-qa and bench-report see it.
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
    /// Ceiling on <c>skill_list</c> called with NO filter — the whole catalog, one default page.
    /// Split out from <see cref="ReadToolResultCeilings"/> because that entry measures the hot
    /// path (a single-name lookup) and this one has to stay measured too: resume-ingestion still
    /// loads the catalog with one unfiltered call, so nobody may quietly let it grow unbounded.
    /// 3,091 measured over the 79-skill seeded catalog — the 3,080 rows plus the page envelope.
    /// </summary>
    public const int SkillListUnfilteredPageCeiling = 3_100;

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
            // RAISED 176 → 308 by P1T-145, the one deliberate re-baseline in this chain. Three
            // optional parameters and the sentence that teaches the filter cost +132 per
            // iteration; the filter they buy cuts the result from 3,080 to 87, and that result
            // is re-sent on every call after it. On the traced run the trade is +1,320 (132 × 10
            // iterations) against -26,937 (2,993 × 9 re-sends). Down from here, never back up.
            ["skill_list"] = 308,
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
    /// schema text. Since P1T-146 no agent is handed all of it, but the number still guards the
    /// surface itself: it is the pool every Tool Allowlist is drawn from, and the ceiling a new
    /// unmeasured read tool has to fit inside.
    ///
    /// <para>3,861 → 3,993 with P1T-145's <c>skill_list</c> re-baseline above: the schema half is
    /// paid per iteration. P1T-146 takes what any one agent pays down by removing tools from its
    /// allowlist, not by shortening the surface.</para>
    /// </summary>
    public const int ReadToolSurfaceCeiling = 3_993;

    /// <summary>
    /// Each agent's <b>Tool Allowlist</b> (P1T-146), keyed by the agent's <c>McpAuth:&lt;agent&gt;</c>
    /// config key. THE declaration of what each agent may see: <c>Agents.Tests</c> offers an agent
    /// exactly this surface when measuring its Baseline Prompt Size, and asserts the shipped
    /// <c>appsettings.json</c> matches it — so the config and the measured cost cannot drift apart.
    ///
    /// <para>Every agent is listed, including the ones that already narrowed themselves in code
    /// (Match/Interview Kit to <c>cv_get</c>, Tailoring to <c>cv_get</c> +
    /// <c>style_exemplar_search</c>): the allowlist is the outer bound on the identity, the
    /// in-agent filter is which of those tools that turn offers. roster-qa is the one that moves
    /// — 11 tools down to the 4 the traced run provably called.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AgentToolAllowlists =
        new Dictionary<string, IReadOnlySet<string>>
        {
            // The 26% line item. roster_shortlist_search, roster_digest_list, category_list,
            // category_tree, availability_list and employee_get were paid for ten times and never
            // called; availability facts come off cv_get, which carries them.
            ["roster-qa"] = new HashSet<string>
                { "roster_semantic_search", "skill_list", "employee_list", "cv_get" },
            ["cv-tailoring"] = new HashSet<string> { "cv_get", "style_exemplar_search" },
            ["match"] = new HashSet<string> { "cv_get" },
            ["shortlist"] = new HashSet<string> { "roster_shortlist_search" },
            ["interview-kit"] = new HashSet<string> { "cv_get" },
            ["bench-report"] = new HashSet<string> { "employee_list" },

            // The one mcp:write identity. Not skill_create (it proposes catalog additions, humans
            // approve them) and no Availability tools — resumes do not state capacity.
            ["resume-ingestion"] = new HashSet<string>
            {
                "skill_list", "employee_create_draft", "language_add", "employee_skill_add",
                "qualification_add", "experience_add",
            },
            ["roster-scan"] = new HashSet<string> { "roster_digest_list" },
        };

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
    /// <para>Only agents that are handed tools appear here, measured against their
    /// <see cref="AgentToolAllowlists"/> surface. Most already narrowed themselves in code as well
    /// (Match and Interview Kit to <c>cv_get</c>, Tailoring to <c>cv_get</c> +
    /// <c>style_exemplar_search</c>), so P1T-146 left their numbers where they were; roster-qa was
    /// the outlier taking all 11, and it is the one that moved.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> BaselinePromptSizeCeilings =
        new Dictionary<string, int>
        {
            // Was 4,409: 416 instructions + all 11 read tool schemas, 3,993 of it schema the agent
            // never used. P1T-146's allowlist shows it 4 tools instead. At the traced run's 10
            // iterations that is 44,090 → 18,760 re-sent tokens. P1T-148 moves it again by
            // rewriting the instructions for Convergence.
            ["RosterQaAgent"] = 1_876,
            ["CvTailoringAgent"] = 1_187,
            // +132 for P1T-145's skill_list schema, which this agent is also shown.
            ["ResumeIngestionAgent"] = 3_025,
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
