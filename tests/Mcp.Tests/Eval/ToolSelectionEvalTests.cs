using System.ClientModel;
using System.ClientModel.Primitives;
using ExpertToJob.ToolSelectionEval;
using FluentAssertions;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using Xunit;
using Xunit.Abstractions;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>Deterministic scoring tests for the tool-selection eval (P1T-127).</summary>
public class ToolSelectionScoringTests
{
    private static readonly GoldenPrompt Strict = new("p1", "c1", "text", "cv_get");
    private static readonly GoldenPrompt Lenient = new("p2", "c2", "text", "expert_list",
        AlsoAcceptable: ["roster_digest_list"]);

    [Fact]
    public void First_tool_credit_covers_expected_and_also_acceptable_only()
    {
        new PromptResult(Strict, "cv_get", ["cv_get"]).FirstToolCorrect.Should().BeTrue();
        new PromptResult(Strict, "expert_get", ["expert_get"]).FirstToolCorrect.Should().BeFalse();
        new PromptResult(Lenient, "roster_digest_list", ["roster_digest_list"]).FirstToolCorrect.Should().BeTrue();
        new PromptResult(Strict, null, []).FirstToolCorrect.Should().BeFalse("no call is a miss");
        new PromptResult(Strict, null, [], "boom").FirstToolCorrect.Should().BeFalse("an error is a miss");
    }

    [Fact]
    public void Any_call_credit_looks_across_the_whole_response()
    {
        new PromptResult(Strict, "expert_get", ["expert_get", "cv_get"]).AnyCallCorrect.Should().BeTrue();
        new PromptResult(Strict, "expert_get", ["expert_get"]).AnyCallCorrect.Should().BeFalse();
    }

    [Fact]
    public void Aggregate_reports_per_cluster_accuracy_and_error_count()
    {
        var aggregate = SelectionAggregate.From(
        [
            new PromptResult(Strict, "cv_get", ["cv_get"]),
            new PromptResult(Strict with { Id = "p3" }, "expert_get", ["expert_get"]),
            new PromptResult(Lenient, null, [], "transport down"),
        ]);

        aggregate.FirstToolAccuracy.Should().BeApproximately(1.0 / 3, 0.001);
        aggregate.FirstToolByCluster["c1"].Should().Be(0.5);
        aggregate.FirstToolByCluster["c2"].Should().Be(0);
        aggregate.Errors.Should().Be(1);
    }

    [Fact]
    public void Gate_flags_each_floor_independently()
    {
        var bad = SelectionAggregate.From(
            Enumerable.Range(0, 10)
                .Select(i => new PromptResult(Strict with { Id = $"p{i}" }, null, [], "err"))
                .ToList());

        var violations = ToolSelectionReport.GateViolations(bad);

        violations.Should().HaveCount(3, "accuracy floors and the error ceiling all trip");
    }

    [Fact]
    public void Cluster_floors_gate_only_the_clusters_that_ran()
    {
        var capability = new GoldenPrompt("c1", GoldenPromptSet.Capability, "text", "roster_semantic_search");

        // capability measured below its 100% floor; the other gated clusters simply did not run.
        var violations = ToolSelectionReport.GateViolations(SelectionAggregate.From(
        [
            new PromptResult(capability, "roster_semantic_search", ["roster_semantic_search"]),
            new PromptResult(capability with { Id = "c2" }, "expert_list", ["expert_list"]),
        ]));

        violations.Should().ContainSingle(v => v.Contains($"cluster '{GoldenPromptSet.Capability}'"));
        violations.Should().NotContain(v => v.Contains(GoldenPromptSet.Catalog));
    }

    [Fact]
    public void A_cluster_at_its_floor_is_not_a_violation()
    {
        var catalog = new GoldenPrompt("cat1", GoldenPromptSet.Catalog, "text", "category_list");

        var violations = ToolSelectionReport.GateViolations(SelectionAggregate.From(
            [new PromptResult(catalog, "category_list", ["category_list"])]));

        violations.Should().NotContain(v => v.Contains("cluster"));
    }

    [Fact]
    public void A_failed_call_records_the_status_and_the_service_message()
    {
        // The OpenAI-compat client reports every HTTP fault as "Service request failed.", which
        // makes a quota 429 indistinguishable from a real collapse in the report (P1T-137).
        var quota = new ClientResultException(new FakeResponse(
            429, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED","message":"quota exceeded"}}"""));

        var described = ToolSelectionRunner.DescribeFault(quota);

        described.Should().StartWith("HTTP 429");
        described.Should().Contain("RESOURCE_EXHAUSTED");
    }

    [Fact]
    public void A_non_transport_fault_keeps_its_own_message()
    {
        ToolSelectionRunner.DescribeFault(new InvalidOperationException("boom")).Should().Be("boom");
    }

    [Fact]
    public void A_run_past_the_error_ceiling_is_reported_as_unusable()
    {
        // Every cluster reads 0% when the transport dies mid-run; the report must not let that be
        // mistaken for a selection regression.
        var dead = SelectionAggregate.From(
            Enumerable.Range(0, 10)
                .Select(i => new PromptResult(Strict with { Id = $"p{i}" }, null, [], "HTTP 429"))
                .ToList());

        var report = ToolSelectionReport.Render(dead, "model", new DateOnly(2026, 8, 29));

        report.Should().Contain("Not a usable measurement");
    }

    [Fact]
    public void A_clean_run_is_not_flagged_unusable()
    {
        var clean = SelectionAggregate.From([new PromptResult(Strict, "cv_get", ["cv_get"])]);

        ToolSelectionReport.Render(clean, "model", new DateOnly(2026, 8, 29))
            .Should().NotContain("Not a usable measurement");
    }

    /// <summary>Minimal <see cref="PipelineResponse"/> so a fault can be built without a live call.</summary>
    private sealed class FakeResponse(int status, string body) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "";
        public override BinaryData Content => BinaryData.FromString(body);
        public override Stream? ContentStream { get => null; set => throw new NotSupportedException(); }
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken ct = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(Content);
        public override void Dispose() { }
    }

    [Fact]
    public void Every_gated_cluster_name_exists_in_the_golden_set()
    {
        var clusters = GoldenPromptSet.Load().Select(p => p.Cluster).Distinct().ToList();

        ToolSelectionBaselines.ClusterFirstToolFloors.Keys.Should().BeSubsetOf(clusters);
    }

    [Fact]
    public void The_golden_set_is_well_formed()
    {
        var prompts = GoldenPromptSet.Load();
        prompts.Should().HaveCountGreaterThanOrEqualTo(30);
        prompts.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        prompts.Select(p => p.Cluster).Distinct().Should().HaveCountGreaterThanOrEqualTo(6);
    }
}

/// <summary>
/// Live tool-selection regression gate (P1T-127): the REAL in-process MCP listing (real
/// descriptions, real schemas) presented to the REAL model, one golden prompt per call. This is
/// the before/after instrument for the description pass — floors sit below the pre-pass baseline
/// and gate regressions. Run: <c>GEMINI_API_KEY=&lt;key&gt; dotnet test --filter "Category=eval"</c>.
/// </summary>
[Trait("Category", "eval")]
[Trait("Category", "live")]
public class ToolSelectionEvalTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Tool_selection_does_not_regress_below_the_committed_baseline()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live tool-selection eval needs a Gemini API key in GEMINI_API_KEY.");

        using var factory = McpTestHost.CreateFactory(nameof(Tool_selection_does_not_regress_below_the_committed_baseline));
        await using var client = await McpTestHost.ConnectAsync(factory);
        var tools = (await client.ListToolsAsync()).Cast<AIFunction>().ToList();
        tools.Should().HaveCountGreaterThanOrEqualTo(40, "the listing is the real tool surface");

        var chat = new OpenAIClient(
                new ApiKeyCredential(Environment.GetEnvironmentVariable("GEMINI_API_KEY")!),
                new OpenAIClientOptions { Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai") })
            .GetChatClient("gemini-3.5-flash-lite")
            .AsIChatClient();

        var aggregate = await ToolSelectionRunner.RunAsync(
            chat, tools, GoldenPromptSet.Load(), TimeSpan.FromSeconds(4), output.WriteLine);

        output.WriteLine("");
        output.WriteLine(ToolSelectionReport.Render(
            aggregate, "gemini-3.5-flash-lite", DateOnly.FromDateTime(DateTime.UtcNow)));

        ToolSelectionReport.GateViolations(aggregate).Should().BeEmpty();
    }
}
