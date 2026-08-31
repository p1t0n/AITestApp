namespace CvManager.Agents.Usage;

/// <summary>
/// Runtime Budgets: the per-run ceiling on what one agent run may spend (P1T-147). Design record:
/// <c>manuals/agent-cost-budgets.md</c> §3.1–3.2. A Runtime Budget is not a Cost Floor — the
/// budget bounds the worst case in production, the floor detects drift in CI. Conflating them
/// yields either a cap nobody can hit or a floor that flakes.
/// <para>
/// Bound from <c>AgentBudgets</c>. The values below are the shipped defaults; an agent listed
/// under <c>AgentBudgets:Agents:&lt;agent&gt;</c> in configuration overrides its own row, and
/// <c>AgentBudgets:Default</c> moves the floor for every unlisted agent.
/// </para>
/// </summary>
public sealed class AgentBudgetOptions
{
    public const string Section = "AgentBudgets";

    /// <summary>Applied to any agent without its own row in <see cref="Agents"/>.</summary>
    public AgentBudget Default { get; set; } = new() { MaxInputTokens = 20_000, MaxIterations = 6 };

    /// <summary>Per-agent overrides, keyed by the agent name used at
    /// <c>ResolveAgentChatClient</c> (e.g. <c>roster-qa</c>). Configuration binding merges into
    /// these seeded rows, so a config file may override one number without restating the rest.
    /// <para>resume-ingestion gets more input tokens than the default, and far more iterations:
    /// the write surface has one tool per child, so its run is long by construction. See
    /// <c>IngestionRunCost</c> and <c>manuals/agent-cost-budgets.md</c> §7 (P1T-150).</para></summary>
    public Dictionary<string, AgentBudget> Agents { get; set; } = new()
    {
        ["roster-qa"] = new AgentBudget { MaxInputTokens = 15_000, MaxIterations = 6 },

        // P1T-150 measured a faithful ingestion of the EASIEST eval fixture at 17 model calls,
        // before a single self-correction — 8 was below the agent's own Structural Path Length,
        // so every ordinary resume degraded at call 8 of 17 for a reason that had nothing to do
        // with cost. 24 clears it.
        //
        // P1T-155 then changed the shape underneath these numbers and deliberately left both
        // where they are. The declared run is now 7 model calls (Turn Batching: every call that
        // does not need another's result goes out in one turn), so 24 is no longer the ceiling an
        // ordinary run approaches — it is the backstop for a run that ignores the Batching rule,
        // which is 24 calls exactly. Lowering it toward 7 would truncate a partially-batched run
        // for being long rather than for being expensive, and the token ceiling is the one that
        // should bind: a serial run is 103,865 estimated tokens, a batched one 31,247.
        //
        // MaxInputTokens deliberately stays at 40,000. It is NOT generous: the per-user cap is
        // 50,000 tokens a day (`Usage:DefaultDailyTokens`) and it is enforced before a request,
        // not during one, so this is the only thing bounding a single run — and one resume must
        // not cost a user their day. Whether the declared run now fits inside it is a question
        // about REAL tokens that the estimated floors cannot answer; that is what
        // IngestionConvergenceLiveFloorTests is for.
        ["resume-ingestion"] = new AgentBudget { MaxInputTokens = 40_000, MaxIterations = 24 },
    };

    /// <summary>The budget for an agent: its own row, else <see cref="Default"/>.</summary>
    public AgentBudget For(string agentKey)
        => Agents.TryGetValue(agentKey, out var budget) ? budget : Default;
}

/// <summary>
/// One agent's Runtime Budget. Both ceilings are checked BEFORE each model call, against what the
/// run has already spent, and both degrade the same way: tools are taken off the table and the
/// model is asked to answer from the evidence in hand.
/// </summary>
public sealed class AgentBudget
{
    /// <summary>Input tokens a run may send across all its model calls before tools are withdrawn.
    /// Input is ~99.6% of the spend on the loops this bounds, so output is not metered here.</summary>
    public long MaxInputTokens { get; set; } = 20_000;

    /// <summary>Model calls a run may make before tools are withdrawn. The backstop for what a
    /// token ceiling cannot catch: a long loop of individually tiny calls.</summary>
    public int MaxIterations { get; set; } = 6;
}
