using System.Text.Json;
using CvManager.Agents.Agents;
using CvManager.Agents.Tests.Eval;
using CvManager.Agents.Tests.Fakes;
using CvManager.Agents.Usage;
using CvManager.CostFloors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace CvManager.Agents.Tests.CostFloors;

/// <summary>
/// The ingestion half of the deterministic Cost Floors (P1T-150): what one resume-ingestion run
/// actually sends, decomposed the way <c>manuals/agent-cost-budgets.md</c> §1.4 decomposes the
/// traced roster-qa run — but measured rather than traced, because no model is involved.
///
/// <para>The real <see cref="ResumeIngestionAgent"/> runs against a scripted
/// <see cref="FakeChatClient"/> that makes exactly the tool calls a faithful ingestion of the
/// reference resume must make, with realistic arguments. The agent's own function-calling loop
/// then builds the conversation, so every model call's input is the genuine article and can simply
/// be weighed. Only the tool SCHEMA half is composed rather than measured, out of the Ratchets
/// <c>Mcp.Tests</c> holds true against real Postgres.</para>
///
/// <para>This is the floor that was missing when a 157,252-token ingestion call went unnoticed.
/// It runs on every push, needs no key and no server, and it prices the two shapes the same run
/// can take — one tool call per turn, or the children batched — because on a write loop the turn
/// count is the lever.</para>
/// </summary>
public class IngestionRunCostFloorTests(ITestOutputHelper output)
{
    private static readonly ResumeFixture Reference =
        Fixtures.All.Single(f => f.Id == IngestionRunCost.ReferenceResumeId);

    private static readonly Guid DraftId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ---- the run under measurement -------------------------------------------------------

    /// <summary>
    /// One model call's input: the whole conversation it was handed, plus the Baseline Prompt Size
    /// handed alongside it — instructions and tool schemas, which travel in <c>ChatOptions</c>
    /// rather than in the message list. Exactly what Turn Amplification multiplies.
    /// </summary>
    /// <param name="Adds">What this call's turn appended to the conversation — the tool call's
    /// arguments and the result that came back. The conversation is append-only, so this is the
    /// delta from the previous call, and turn 1's is the resume itself.</param>
    private sealed record Turn(
        int Index, int ConversationTokens, int BaselineTokens, int Adds, string ToolsCalled)
    {
        public int InputTokens => ConversationTokens + BaselineTokens;
    }

    private sealed record Measured(IReadOnlyList<Turn> Turns)
    {
        public int Total => Turns.Sum(t => t.InputTokens);

        public int Baseline => Turns.Sum(t => t.BaselineTokens);

        /// <summary>What one turn's addition costs across the whole run: itself, plus every call
        /// after it that re-sends it. Turn Amplification, per line item.</summary>
        public int Amplified(Turn turn) => turn.Adds * (Turns.Count - turn.Index + 1);

        /// <summary>The resume is turn 1's addition, so it is re-sent by every call there is.</summary>
        public int ResumeReSend => Amplified(Turns[0]);
    }

    private async Task<Measured> MeasureAsync(IReadOnlyList<IReadOnlyList<string>> turns)
    {
        // Each scripted response is one assistant turn: the tools it calls, with the arguments a
        // faithful ingestion would carry. The closing turn is the schema-constrained report.
        var call = 0;
        var responses = turns
            .Select<IReadOnlyList<string>, Func<ChatResponse>>(tools => () => new ChatResponse(
                new ChatMessage(ChatRole.Assistant,
                    tools.Select(t => (AIContent)new FunctionCallContent(
                        $"c{++call}", t, ArgumentsFor(t, call))).ToList())))
            .Append(() => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":[],"aborted":false,"abortReason":null}""")))
            .ToArray();

        var chat = new FakeChatClient(responses);
        await new ResumeIngestionAgent(chat, new FakeToolSource(Tools()), NullLoggerFactory.Instance)
            .IngestAsync(Reference.Text);

        var conversation = chat.ReceivedMessages.Select(m => TokenEstimate.Of(Wire(m))).ToList();
        return new Measured(conversation
            .Select((tokens, i) => new Turn(
                i + 1,
                tokens,
                CvManager.CostFloors.CostFloors.BaselinePromptSize(
                    CvManager.CostFloors.CostFloors.AgentInstructionCeilings[IngestionRunCost.AgentClass],
                    chat.ReceivedOptions[i]?.Tools?.Select(t => t.Name) ?? []),
                tokens - (i == 0 ? 0 : conversation[i - 1]),
                i < turns.Count ? string.Join(" + ", Distinct(turns[i])) : "— (closing report)"))
            .ToList());
    }

    /// <summary>Serial: one tool call per assistant turn — the shape the ledger recorded.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> SerialTurns() =>
        IngestionRunCost.ReferenceIngestionPath.Select(t => (IReadOnlyList<string>)[t]).ToList();

    /// <summary>Batched: the same calls in the same order, but every add of one kind issued as
    /// parallel tool calls in a single turn. Only the turn boundaries move.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> BatchedTurns()
    {
        var path = IngestionRunCost.ReferenceIngestionPath;
        return
        [
            ["skill_list"],
            ["employee_create_draft"],
            .. new[] { "language_add", "employee_skill_add", "qualification_add", "experience_add" }
                .Select(kind => (IReadOnlyList<string>)path.Where(t => t == kind).ToList()),
        ];
    }

    // ---- the floors ----------------------------------------------------------------------

    [Fact]
    public void The_reference_ingestion_path_is_what_the_fixtures_ground_truth_requires()
    {
        // The path is a FACT about the resume, not a target: the write surface has one tool per
        // child, so a faithful ingestion cannot be shorter. Deriving it here means a fixture edit
        // fails loudly rather than quietly re-baselining every ceiling below it.
        var truth = Reference.Truth;
        IReadOnlyList<string> required =
        [
            "skill_list",
            "employee_create_draft",
            .. truth.Languages.Select(_ => "language_add"),
            .. truth.Skills.Where(s => s.InCatalog).Select(_ => "employee_skill_add"),
            .. truth.Qualifications.Select(_ => "qualification_add"),
            .. truth.Experiences.Select(_ => "experience_add"),
        ];

        IngestionRunCost.ReferenceIngestionPath.Should().Equal(required);
        IngestionRunCost.BatchedIterations.Should().Be(
            BatchedTurns().Count + 1, "the batched shape is one turn per child kind plus the closing turn");
    }

    [Fact]
    public void The_harness_never_prices_the_catalog_above_what_the_real_tool_costs()
    {
        // Everything else here is the real agent; skill_list's result is the one payload this
        // harness has to synthesize, since measuring it needs Postgres. Holding it under the
        // Ratchet Mcp.Tests measures there means the run price below can only be conservative.
        TokenEstimate.Of(CatalogPage).Should().BeLessThanOrEqualTo(
            CvManager.CostFloors.CostFloors.SkillListUnfilteredPageCeiling,
            "the synthetic catalog page stands in for the real unfiltered one");
    }

    [Fact]
    public async Task The_serial_ingestion_stays_under_its_ratcheted_ceiling()
    {
        var measured = await MeasureAsync(SerialTurns());
        Report("SERIAL — one tool call per turn", measured);

        using var _ = new AssertionScope();
        measured.Turns.Should().HaveCount(
            IngestionRunCost.SerialIterations, "a tool call per turn plus the closing report");

        // The decomposition is arithmetic on an append-only conversation, so it closes exactly —
        // the same property that makes §1.4's roster-qa table trustworthy. If it ever stops
        // closing, something is being re-sent that this instrument is not accounting for.
        (measured.Baseline + measured.Turns.Sum(measured.Amplified)).Should().Be(
            measured.Total, "every token is either the Baseline Prompt Size or a re-sent turn");

        measured.Total.Should().BeLessThanOrEqualTo(
            IngestionRunCost.SerialRunCeiling,
            "the serial shape is what the 157,252-token call was, and this is its model-free price");
        measured.ResumeReSend.Should().BeLessThanOrEqualTo(
            IngestionRunCost.SerialResumeReSendCeiling,
            "the resume is in the conversation from turn one, so every call re-sends it");
    }

    [Fact]
    public async Task Batching_the_children_prices_the_same_writes_far_lower()
    {
        var measured = await MeasureAsync(BatchedTurns());
        Report("BATCHED — one turn per child kind", measured);

        using var _ = new AssertionScope();
        measured.Turns.Should().HaveCount(IngestionRunCost.BatchedIterations);
        measured.Total.Should().BeLessThanOrEqualTo(IngestionRunCost.BatchedRunCeiling);

        // The point of measuring both: identical writes, identical results, a third of the turns.
        var serial = await MeasureAsync(SerialTurns());
        measured.Total.Should().BeLessThan(
            serial.Total / 2, "turn boundaries alone are worth more than half the bill on a write loop");
    }

    [Fact]
    public async Task The_runtime_budget_admits_a_faithful_ingestion_of_the_reference_resume()
    {
        // The load-bearing check, and the one that needs no unit conversion at all: a Runtime
        // Budget that cannot fit an ORDINARY resume does not bound waste, it truncates work — and
        // on the one agent holding mcp:write, a truncated run leaves a half-populated DRAFT
        // persisted behind it. The ceiling has to clear the shape the agent actually has.
        var budget = new AgentBudgetOptions().For(IngestionRunCost.AgentKey);
        var measured = await MeasureAsync(SerialTurns());

        using var _ = new AssertionScope();
        budget.MaxIterations.Should().BeGreaterThanOrEqualTo(
            IngestionRunCost.SerialIterations,
            $"a faithful ingestion of the {IngestionRunCost.ReferenceResumeId} fixture is " +
            $"{IngestionRunCost.SerialIterations} model calls, and it is the EASY fixture");

        // The other half of the decision, and the reason the token ceiling was NOT raised to
        // match: the per-user daily cap is enforced before a request rather than during one, so a
        // single run is bounded by this and nothing else. One resume may not cost a user a day.
        budget.MaxInputTokens.Should().BeLessThan(
            new UsageOptions().DefaultDailyTokens,
            "a Runtime Budget above the daily cap would let one run spend a user's whole day");

        output.WriteLine(
            $"budget: {budget.MaxIterations} iterations / {budget.MaxInputTokens} input tokens; " +
            $"reference serial run: {measured.Turns.Count} iterations / {measured.Total} estimated tokens");
    }

    [Fact]
    public async Task Every_tool_the_agent_is_shown_is_one_the_reference_ingestion_calls()
    {
        // Scope item 3 of P1T-150, answered from the trace instead of guessed: the Tool Allowlist
        // for this agent is already exact. It narrows itself to six tools and the reference path
        // uses all six, so there is nothing here to remove — its 3,025-token Baseline Prompt Size
        // is the floor for the work, not slack. When P1T-146's server-side seam lands,
        // McpAuth:resume-ingestion:Tools should mirror this set so the narrowing is enforced by
        // the agent's identity rather than by client-side code it could stop applying.
        var chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")));
        await new ResumeIngestionAgent(chat, new FakeToolSource(Tools()), NullLoggerFactory.Instance)
            .IngestAsync(Reference.Text);

        chat.ReceivedOptions[0]!.Tools!.Select(t => t.Name)
            .Should().BeEquivalentTo(IngestionRunCost.ReferenceIngestionPath.Distinct());
    }

    // ---- the §1.4-style table ------------------------------------------------------------

    private void Report(string title, Measured measured)
    {
        output.WriteLine($"{title} — {measured.Turns.Count} model calls, {measured.Total} estimated input tokens");
        output.WriteLine(
            $"{"#",3} {"input",7} {"= convo",8} {"+ baseline",10} {"adds",6} {"x re-sends",10} " +
            $"{"= total",8}  tool called after this call");
        foreach (var t in measured.Turns)
        {
            output.WriteLine(
                $"{t.Index,3} {t.InputTokens,7} {t.ConversationTokens,8} {t.BaselineTokens,10} " +
                $"{t.Adds,6} {measured.Turns.Count - t.Index + 1,10} {measured.Amplified(t),8}  {t.ToolsCalled}");
        }

        var conversation = measured.Turns.Sum(measured.Amplified);
        output.WriteLine(
            $"= Baseline Prompt Size {measured.Baseline} ({Share(measured.Baseline, measured)}) " +
            $"+ conversation {conversation} ({Share(conversation, measured)}), of which the resume " +
            $"itself is {measured.ResumeReSend} ({Share(measured.ResumeReSend, measured)}) and the " +
            $"unfiltered skill_list catalog is {measured.Amplified(measured.Turns[1])} " +
            $"({Share(measured.Amplified(measured.Turns[1]), measured)}).");
    }

    private static string Share(int part, Measured measured) => $"{100.0 * part / measured.Total:0.0}%";

    // ---- the fake MCP surface ------------------------------------------------------------

    /// <summary>The six tools the agent narrows itself to, each returning what the MCP layer
    /// really returns: an acknowledgement for the writes, one unfiltered page for the catalog.</summary>
    private static AITool[] Tools() =>
    [
        AIFunctionFactory.Create(() => CatalogPage, "skill_list"),
        AIFunctionFactory.Create(
            () => $$"""{"employee":{"id":"{{DraftId}}","firstName":"Torvald","lastName":"Emberwright"},"duplicateWarning":null}""",
            "employee_create_draft"),
        .. new[] { "language_add", "employee_skill_add", "qualification_add", "experience_add" }
            .Select(name => (AITool)AIFunctionFactory.Create(
                () => $$"""{"id":"{{Guid.NewGuid()}}"}""", name)),
    ];

    /// <summary>
    /// One unfiltered <c>skill_list</c> page in the real <c>SkillPage</c>/<c>SkillDto</c> shape,
    /// over a catalog the size of the seeded one. Sized to sit under
    /// <see cref="CvManager.CostFloors.CostFloors.SkillListUnfilteredPageCeiling"/> — the Ratchet
    /// <c>Mcp.Tests</c> measures against real Postgres — so this harness can never price the run
    /// higher than the tool really costs.
    /// </summary>
    private static readonly string CatalogPage = BuildCatalogPage();

    private static string BuildCatalogPage()
    {
        var names = Fixtures.Catalog.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var items = Enumerable.Range(0, 79)
            .Select(i => new
            {
                id = Deterministic(i, 0xA1),
                name = names[i % names.Count],
                categoryId = Deterministic(i % 9, 0xC2),
                categoryName = $"Category {i % 9}",
                rank = i % 5,
            });
        return JsonSerializer.Serialize(new { page = 1, pageSize = 100, total = 79, items });
    }

    private static Guid Deterministic(int seed, byte salt)
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes, seed);
        Array.Fill(bytes, salt, 4, 12);
        return new Guid(bytes);
    }

    // ---- realistic tool-call arguments ---------------------------------------------------

    /// <summary>
    /// What the model emits when it calls each tool — the term that dominates a write loop, since
    /// every argument stays in the conversation and is re-sent on every call after it. Drawn from
    /// the reference fixture's ground truth so the bullets, dates and ids are the real weight and
    /// not a placeholder.
    /// </summary>
    private static Dictionary<string, object?> ArgumentsFor(string tool, int nth) => tool switch
    {
        "skill_list" => [],
        "employee_create_draft" => new()
        {
            ["dto"] = new
            {
                firstName = "Torvald",
                lastName = "Emberwright",
                title = "Senior .NET Engineer",
                email = "torvald.emberwright@postbox.example",
                phone = "+371 2000 1111",
                location = "Riga",
                summary = "Backend engineer with 11 years building transactional systems on .NET. " +
                          "Comfortable owning services end to end, from EF Core data models to Kubernetes deploys.",
            },
        },
        "language_add" => new()
        {
            ["employeeId"] = DraftId,
            ["dto"] = new { name = "Latvian", level = "Native" },
        },
        "employee_skill_add" => new()
        {
            ["employeeId"] = DraftId,
            ["dto"] = new
            {
                skillId = Deterministic(nth, 0xA1),
                level = "Advanced",
                yearsExperience = 11,
            },
        },
        "qualification_add" => new()
        {
            ["employeeId"] = DraftId,
            ["dto"] = new
            {
                type = "Degree",
                title = "BSc Computer Science",
                institution = "University of Latvia",
                startDate = "2010-09-01",
                endDate = "2014-06-01",
            },
        },
        "experience_add" => new()
        {
            ["employeeId"] = DraftId,
            ["dto"] = new
            {
                company = "Brasswater Ledger",
                title = "Lead Backend Engineer",
                startDate = "2019-03-01",
                endDate = (string?)null,
                summary = "Owns the settlement platform end to end.",
                achievements = new[]
                {
                    "Cut settlement batch runtime from 4h to 35min by reworking EF Core change tracking.",
                    "Introduced outbox-based messaging that removed 100% of dual-write incidents.",
                },
                skillIds = Enumerable.Range(0, 5).Select(i => Deterministic(i, 0xA1)).ToArray(),
            },
        },
        _ => [],
    };

    // ---- weighing the wire ---------------------------------------------------------------

    /// <summary>The conversation as the model is handed it: prose, the tool calls the assistant
    /// made with their arguments, and the results that came back. Weighed with the same
    /// <see cref="TokenEstimate"/> yardstick every other Cost Floor is denominated in.</summary>
    private static string Wire(IEnumerable<ChatMessage> messages) =>
        string.Join("\n", messages.SelectMany(m => m.Contents).Select(Wire));

    private static string Wire(AIContent content) => content switch
    {
        TextContent t => t.Text,
        FunctionCallContent c => c.Name + JsonSerializer.Serialize(c.Arguments),
        // A tool result that is already JSON text is weighed as that text, NOT re-serialized:
        // the loop hands results back as a JSON string, and escaping it a second time would
        // inflate every payload past the ceilings Mcp.Tests measures on the raw result. This
        // floor has to stay denominated the same way as the rest of them.
        FunctionResultContent r => Unwrap(r.Result),
        _ => content.ToString() ?? string.Empty,
    };

    private static string Unwrap(object? result) => result switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? string.Empty,
        _ => JsonSerializer.Serialize(result),
    };

    private static IEnumerable<string> Distinct(IReadOnlyList<string> tools) =>
        tools.GroupBy(t => t).Select(g => g.Count() == 1 ? g.Key : $"{g.Count()}x {g.Key}");
}
