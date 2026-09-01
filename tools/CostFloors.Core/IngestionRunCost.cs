namespace ExpertToJob.CostFloors;

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
/// <para>And the run is long by construction, not by thrash. One expert, one skill, one
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
    /// <para>P1T-155 made the head of this path LONGER on purpose: one unfiltered
    /// <c>skill_list</c> became one filtered lookup per extracted skill name. Eight calls in place
    /// of one, and the run got cheaper — the dump was 3,063 tokens re-sent on every later call,
    /// the lookups are ~87 each and they all fit in a single turn. Call count was never the cost;
    /// TURN count is (see <see cref="ReferenceIngestionTurns"/>).</para>
    ///
    /// <para>The floor test derives this from <c>GroundTruth</c> rather than trusting the list, so
    /// a fixture edit that changes the shape fails loudly instead of quietly re-baselining.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ReferenceIngestionPath =
    [
        "skill_list", "skill_list", "skill_list", "skill_list",
        "skill_list", "skill_list", "skill_list", "skill_list",
        "expert_create_draft",
        "language_add", "language_add", "language_add",
        "expert_skill_add", "expert_skill_add", "expert_skill_add", "expert_skill_add",
        "expert_skill_add", "expert_skill_add", "expert_skill_add", "expert_skill_add",
        "qualification_add",
        "experience_add", "experience_add",
    ];

    /// <summary>
    /// The REFERENCE INGESTION TURNS: the same path, grouped the way the agent's Batching rule
    /// tells it to issue them — every call that does not need another's result goes out in the
    /// same turn as parallel tool calls. This is the declared shape of a converged ingestion, the
    /// write-loop sibling of roster-qa's Convergent Path (<c>CostFloors.RosterQaConvergentPath</c>).
    ///
    /// <para>Six turns: the skill lookups, the draft, then one turn per child KIND. Only two
    /// boundaries here are real — the children need the draft's id, and the skill adds need the
    /// lookups' ids. The four child kinds are mutually independent and could collapse further; the
    /// declared shape keeps them apart so each turn's arguments stay bounded and a validation error
    /// costs one kind's retry rather than the whole draft's.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ReferenceIngestionTurns =
        ["skill_list", "expert_create_draft", "language_add", "expert_skill_add",
         "qualification_add", "experience_add"];

    /// <summary>
    /// Model calls the SERIAL shape takes: one tool call per assistant turn, plus the closing turn
    /// that writes the report. The shape the ledger recorded, and now the shape the Batching rule
    /// exists to prevent — it is kept measured because nothing structurally forces batching, so a
    /// model that ignores the rule lands back here and the Runtime Budget still has to hold.
    /// </summary>
    public static int SerialIterations => ReferenceIngestionPath.Count + 1;

    /// <summary>
    /// Model calls the BATCHED shape takes: one per entry in <see cref="ReferenceIngestionTurns"/>
    /// plus the closing turn that writes the report. Same writes, same order, same results — only
    /// the turn boundaries move. That it is under a third of <see cref="SerialIterations"/> is the
    /// whole finding: on a write loop, iteration count is the lever, because every iteration
    /// re-sends the resume and everything written so far.
    /// </summary>
    public static int BatchedIterations => ReferenceIngestionTurns.Count + 1;

    /// <summary>
    /// Ceiling on the SERIAL reference ingestion, in <see cref="TokenEstimate"/> tokens, measured
    /// end to end by <c>IngestionRunCostFloorTests</c> — the real agent, the real function-calling
    /// loop, real tool arguments, every model call's whole input weighed. Not arithmetic: the only
    /// composed terms are the tool schema and instruction sizes, taken from Ratchets the other
    /// floors already hold true.
    ///
    /// <para>P1T-150 ratcheted this at 111,638 over 17 calls, decomposing as 46.1% Baseline Prompt
    /// Size, <b>43.9% one unfiltered <c>skill_list</c> result</b> fetched on turn 1 and re-sent
    /// sixteen times, 3.5% the resume, 6.5% everything the agent wrote. P1T-155 ratchets it to
    /// <b>103,865 over 24 calls</b> — and the two numbers are worth reading together, because the
    /// run got 7% cheaper while getting SEVEN CALLS LONGER. The catalog dump became eight filtered
    /// lookups: eight more calls, and the 49,008 they replace collapses to 2,185. What is left is
    /// almost entirely the Baseline Prompt Size, now <b>73.1%</b> of a shape whose only remaining
    /// defect is that it takes a turn per call.</para>
    ///
    /// <para>Which is the point: this ceiling is no longer the interesting number. It prices the
    /// shape the Batching rule exists to prevent, and it is kept measured only because nothing
    /// structurally forces batching. <see cref="BatchedRunCeiling"/> is what an ordinary ingestion
    /// should cost.</para>
    ///
    /// <para>These are ESTIMATED tokens and the ledger row was 155,668 REAL ones; see
    /// <see cref="TokenEstimate"/> on why the two differ, and never quote an estimate as a bill.</para>
    /// </summary>
    public const int SerialRunCeiling = 103_865;

    /// <summary>
    /// Ceiling on the DECLARED reference ingestion — <see cref="ReferenceIngestionTurns"/>, same
    /// measurement, same units. Identical writes, identical results, identical order; only the
    /// turn boundaries move.
    ///
    /// <para>Ratcheted at <b>31,247 over 7 calls</b>, against the 44,001 P1T-150 measured for
    /// batching alone and the 111,638 the serial-and-dumping shape cost — <b>72% off</b>. The two
    /// levers compound rather than merely add: batching cuts the number of times anything is
    /// re-sent, and the filtered lookup cuts what there is to re-send. Neither one was ever going
    /// to get here on its own.</para>
    ///
    /// <para>It buys those tokens with instructions — <c>AgentInstructionCeilings</c> for this
    /// agent was raised 523 → 663 to carry the two rules, and that 140 is paid on all seven calls.
    /// 980 against 80,391 is the trade, and it is the same trade P1T-145 made on <c>skill_list</c>'s
    /// schema: on a re-sent term, the thing that shortens the loop is worth more than its own
    /// weight many times over.</para>
    ///
    /// <para>What this does NOT say: that a run now fits inside the 40,000 Runtime Budget. That
    /// ceiling counts REAL model tokens and this one counts <see cref="TokenEstimate"/> ones —
    /// roughly 2.4× apart on GUID-dense payloads, in the wrong direction. Only
    /// <c>IngestionConvergenceLiveFloorTests</c> can answer that, and it needs a key.</para>
    /// </summary>
    public const int BatchedRunCeiling = 31_247;

    /// <summary>
    /// Ceiling on what the resume text itself contributes across the DECLARED run — it is in the
    /// conversation from turn one, so every call re-sends it. Isolated because it is the one term
    /// that is NOT waste, and because measuring it is what disproved the premise P1T-150 opened
    /// with: that ingestion is expensive because a pasted resume is large input.
    ///
    /// <para>1,603 of 31,247 — <b>5.1%</b>, and P1T-155 ratcheted it down from 3,893 without
    /// touching the resume, by removing calls that were re-sending it. The document was never the
    /// bill; the loop was, and it still is.</para>
    /// </summary>
    public const int ResumeReSendCeiling = 1_603;
}
