namespace CvManager.CostFloors;

/// <summary>
/// The resume-ingestion half of the Cost Floors (P1T-150). Sibling of <see cref="CostFloors"/>,
/// kept in its own file because it measures a different shape of run and needs a different
/// instrument.
///
/// <para>roster-qa is a READ loop: a handful of tool calls, and the expensive thing is a fat
/// result re-sent on every call after it (<c>manuals/agent-cost-budgets.md</c> §1.4). Ingestion is
/// a WRITE loop, and it inverts almost every term. Its results are acknowledgements — an id, a
/// duplicate warning — and cost nothing. What it re-sends instead is (a) the resume, once per
/// call, from turn one to the last, and (b) its own accumulated tool-call ARGUMENTS: every
/// achievement bullet, every date, every skill id it has already written. Those are the payload,
/// and there is no filter or page that makes them smaller.</para>
///
/// <para>And the run is long by construction, not by thrash. One employee, one skill, one
/// language, one qualification, one role — each is its own MCP write. A faithful ingestion of an
/// ordinary two-role resume is sixteen tool calls before it has done anything wrong. That is what
/// makes an iteration ceiling the load-bearing number here, and what
/// <see cref="ReferenceIngestionPath"/> exists to state as a fact rather than a guess.</para>
/// </summary>
public static class IngestionRunCost
{
    /// <summary>The agent key its Runtime Budget is bound under (<c>AgentBudgets:Agents</c>).</summary>
    public const string AgentKey = "resume-ingestion";

    /// <summary>The agent class the Baseline Prompt Size ceiling is keyed by.</summary>
    public const string AgentClass = "ResumeIngestionAgent";

    /// <summary>
    /// The REFERENCE RESUME the ingestion floors are measured over: the <c>clean-markdown</c>
    /// ingestion-eval fixture (`tests/Agents.Tests/Eval/IngestionEvalFixtures.cs`). Deliberately
    /// the *easy* one — well-structured, every skill already in the catalog, nothing to
    /// self-correct — so the numbers below are a floor under an ingestion, not a worst case. A
    /// messy resume costs more; none costs less.
    /// </summary>
    public const string ReferenceResumeId = "clean-markdown";

    /// <summary>
    /// The REFERENCE INGESTION PATH: the tool calls a faithful ingestion of
    /// <see cref="ReferenceResumeId"/> must make, in order. Not a budget and not an aspiration —
    /// it is the fixture's ground truth (8 skills, 3 languages, 1 qualification, 2 roles) read
    /// against the agent's own procedure, and the write surface gives no way to do it in fewer
    /// calls: each child is its own tool.
    ///
    /// <para>The floor test derives this from <c>GroundTruth</c> rather than trusting the list, so
    /// a fixture edit that changes the shape fails loudly instead of quietly re-baselining.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ReferenceIngestionPath =
    [
        "skill_list",
        "employee_create_draft",
        "language_add", "language_add", "language_add",
        "employee_skill_add", "employee_skill_add", "employee_skill_add", "employee_skill_add",
        "employee_skill_add", "employee_skill_add", "employee_skill_add", "employee_skill_add",
        "qualification_add",
        "experience_add", "experience_add",
    ];

    /// <summary>
    /// Model calls the SERIAL shape takes: one tool call per assistant turn, plus the closing turn
    /// that writes the report. This is the shape the ledger recorded — nothing forces batching,
    /// and a model correcting one child at a time necessarily ends up here.
    /// </summary>
    public static int SerialIterations => ReferenceIngestionPath.Count + 1;

    /// <summary>
    /// Model calls the BATCHED shape takes: <c>skill_list</c>, <c>employee_create_draft</c>, then
    /// one turn per child KIND with its adds issued as parallel tool calls, plus the closing turn.
    /// Same writes, same order, same results — only the turn boundaries move. That it is a third
    /// of <see cref="SerialIterations"/> is the whole finding: on a write loop, iteration count is
    /// the lever, because every iteration re-sends the resume and everything written so far.
    /// </summary>
    public const int BatchedIterations = 7;

    /// <summary>
    /// Ceiling on the SERIAL reference ingestion, in <see cref="TokenEstimate"/> tokens, measured
    /// end to end by <c>IngestionRunCostFloorTests</c> — the real agent, the real function-calling
    /// loop, real tool arguments, every model call's whole input weighed. Not arithmetic: the only
    /// composed terms are the tool schema and instruction sizes, taken from Ratchets the other
    /// floors already hold true.
    ///
    /// <para>Ratcheted at 111,638, today's measured value, and the number that explains the
    /// 157,252-token ledger row: a perfectly ordinary resume, no thrash, no retries, nothing the
    /// agent did wrong. It decomposes as <b>46.1% Baseline Prompt Size</b> (3,025 × 17 calls),
    /// <b>43.9% one unfiltered <c>skill_list</c> result</b> (3,063 fetched on turn 1 and re-sent
    /// sixteen times), 3.5% the resume, and 6.5% everything the agent actually wrote.</para>
    ///
    /// <para>The two findings worth reading twice. First, the catalog dump is the largest single
    /// line item — the same defect that was 42% of the roster-qa run, except here it is not a
    /// mistake the model made: step 1 of the agent's own instructions tells it to load the whole
    /// catalog. P1T-145's <c>nameContains</c> exists and this agent cannot use it as written.
    /// Second, the resume is <b>3.5%</b>. The premise this ticket opened with — that ingestion is
    /// expensive because a pasted resume is genuinely large input — is false at this size; the
    /// bill is the loop, not the document.</para>
    ///
    /// <para>These are ESTIMATED tokens and the ledger row was 155,668 REAL ones; see
    /// <see cref="TokenEstimate"/> on why the two differ, and never quote an estimate as a bill.</para>
    /// </summary>
    public const int SerialRunCeiling = 111_638;

    /// <summary>
    /// Ceiling on the BATCHED reference ingestion, same measurement, same units: 44,001 over
    /// <see cref="BatchedIterations"/> calls. Identical writes, identical results, identical
    /// order — only the turn boundaries move, and the bill falls <b>61%</b>. That gap is what
    /// makes iteration count, not payload size, the lever on a write loop.
    /// </summary>
    public const int BatchedRunCeiling = 44_001;

    /// <summary>
    /// Ceiling on what the resume text itself contributes across the whole serial run — in the
    /// conversation from turn one, so re-sent by every call there is. Isolated because it is the
    /// one term that is NOT waste, and because measuring it is what disproved the assumption that
    /// it was the problem: 3,893 of 111,638.
    /// </summary>
    public const int SerialResumeReSendCeiling = 3_893;
}
