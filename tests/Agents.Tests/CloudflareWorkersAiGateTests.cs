using System.ClientModel;
using System.ClientModel.Primitives;
using System.Threading.RateLimiting;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.Agents.RosterScan;
using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using Xunit.Abstractions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// P1T-143's **gate prototype**: does Cloudflare Workers AI hold the real
/// <c>roster_scan_chunk</c> schema? The ticket makes everything downstream conditional on this
/// ("If the gate passes" → widen the provider config), so nothing here touches production wiring —
/// this measures the unknown, it does not commit to the provider.
///
/// <para>The ticket asked for a throwaway script. It is committed as a <c>Category=live</c> probe
/// instead, for the same reason <see cref="CompatEndpointProbeTests"/> (P1T-115) is: this sandbox
/// has neither a Cloudflare token nor a network route to <c>api.cloudflare.com</c>, so a throwaway
/// script would be written, never run, and lost. A skipped test is the project's existing answer to
/// "the measurement needs a key we do not have" (PRD §8) — the gate then runs with one command the
/// day a key exists, rather than being rebuilt from the ticket text.</para>
///
/// <para>It drives the <b>real</b> <see cref="QueuedSyncScoringTransport"/> over <b>real</b>
/// digests — the committed demo roster projected through the production
/// <see cref="ExpertDigestService"/> — so the schema under test is literally the one Roster Scan
/// sends, not a hand-copied replica that could drift from it. Adherence is measured the way the
/// transport itself measures honesty: a candidate is <c>Scored</c> only if the reply parsed as
/// <c>roster_scan_chunk</c> <i>and</i> named that expert id.</para>
///
/// <para>Run the gate:
/// <c>CLOUDFLARE_ACCOUNT_ID=… CLOUDFLARE_API_TOKEN=… dotnet test tests/Agents.Tests --filter "Category=live"</c>.
/// Adding <c>GEMINI_API_KEY</c> also runs the <c>gemini-3.5-flash-lite</c> control on the identical
/// chunk, which is what makes the two printed lines a comparison rather than an isolated number —
/// and what tells you whether a Cloudflare failure is the provider or the chunk.</para>
///
/// <para>Background and the model choice: <c>manuals/cloudflare-workers-ai-provider.md</c>.</para>
/// </summary>
[Trait("Category", "live")]
public class CloudflareWorkersAiGateTests(ITestOutputHelper output)
{
    /// <summary>The one free Workers AI model that carries function calling, JSON mode and a
    /// 128k context; the three LoRA rows the ticket opened with carry none of them. Overridable so
    /// a re-run can price another row without a rebuild.</summary>
    private static string CloudflareModel =>
        Environment.GetEnvironmentVariable("CLOUDFLARE_MODEL") is { Length: > 0 } m
            ? m
            : "@cf/meta/llama-3.1-8b-instruct-fast";

    /// <summary>Account id sits in the path, not a header — that is the whole reason a second
    /// provider needs its own endpoint and not just its own model id.</summary>
    private static string CloudflareEndpoint(string accountId) =>
        $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1";

    /// <summary>The committed demo roster, and therefore the number of chunk calls a full scan
    /// costs at <see cref="RosterScanGateChunk.ChunkSize"/> — the figure that decides whether
    /// Workers AI's daily free grant covers a whole Roster Scan.</summary>
    private const int DemoRosterSize = 500;

    // Neuron rates are per-model and the pricing page is the source of truth. These are the
    // nearest priced 7B row (@cf/mistral/mistral-7b-instruct-v0.1) recorded in
    // manuals/cloudflare-workers-ai-provider.md §1 — so the neuron figures printed below are an
    // ESTIMATE at that reference rate. Read the real row for CloudflareModel off the pricing page
    // when it is reachable and correct these two constants; the free grant is exact.
    private const double NeuronsPerMillionInput = 10_000;
    private const double NeuronsPerMillionOutput = 17_300;
    private const double FreeNeuronsPerDay = 10_000;

    [SkippableFact]
    public async Task Workers_ai_holds_the_roster_scan_chunk_schema()
    {
        var accountId = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
        var token = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        Skip.If(
            string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(token),
            "P1T-143's gate needs CLOUDFLARE_ACCOUNT_ID + CLOUDFLARE_API_TOKEN (and a network route to api.cloudflare.com).");

        var adherence = await MeasureAsync(
            $"cloudflare {CloudflareModel}",
            BuildCloudflareChatClient(accountId!, token!),
            estimateNeurons: true);

        // The ticket's gate, verbatim: "adherence must hold on the real schema. If it does not,
        // stop, record the numbers in the manual, close the ticket." A partial chunk is a failure,
        // not a degradation — the transport already fails every member the reply skipped, so
        // anything under 100% means the provider cannot carry roster-scan as it stands.
        adherence.Should().Be(
            1.0,
            "the gate is schema adherence on the real roster_scan_chunk schema — if this is red, " +
            "record the printed numbers on P1T-143 and close it rather than widening the provider config");
    }

    [SkippableFact]
    public async Task Gemini_flash_lite_holds_the_same_chunk_as_the_control()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "The latency/adherence control needs GEMINI_API_KEY.");

        // Same chunk, same schema, same transport — the incumbent's numbers are what Cloudflare's
        // have to be read against, and a red control means the chunk is at fault, not the provider.
        var adherence = await MeasureAsync(
            "gemini-3.5-flash-lite (control)", BuildGeminiChatClient(), estimateNeurons: false);

        adherence.Should().Be(1.0, "the incumbent provider must hold its own production schema");
    }

    /// <summary>Scores one real chunk and prints the ticket's three measurements — schema-adherence
    /// rate, neuron burn, latency — in one comparable line per provider.</summary>
    private async Task<double> MeasureAsync(string label, IChatClient chat, bool estimateNeurons)
    {
        var digests = await RosterScanGateChunk.RealDigestChunkAsync();
        var scored = await Transport(chat).ScoreChunkAsync(
            RosterScanGateChunk.JobDescription, RosterScanGateChunk.Extraction(), digests);

        var adhered = scored.Results.Count(r => r.Status == ScoringCandidateStatus.Scored);
        var rate = (double)adhered / digests.Count;
        var reply = scored.Reply;

        output.WriteLine(
            $"{label}: adherence {adhered}/{digests.Count} ({rate:P0}) · " +
            $"{reply.InputTokens} in / {reply.OutputTokens} out · {reply.LatencyMs}ms · model {reply.ModelId}");

        if (estimateNeurons)
        {
            var neurons = reply.InputTokens / 1_000_000d * NeuronsPerMillionInput
                          + reply.OutputTokens / 1_000_000d * NeuronsPerMillionOutput;
            var fullScan = neurons * (DemoRosterSize / (double)RosterScanGateChunk.ChunkSize);
            output.WriteLine(
                $"  ~{neurons:N1} neurons/chunk at the 7B reference rate → {FreeNeuronsPerDay / neurons:N0} " +
                $"chunk calls/day inside the 10,000-neuron free grant; a {DemoRosterSize}-expert scan " +
                $"costs ~{fullScan:N0} neurons ({fullScan / FreeNeuronsPerDay:N1}× the daily grant)");
        }

        // The rationales are where a schema-shaped-but-useless reply shows itself: a provider can
        // fill every required field with nothing. Print one so a passing gate is still readable.
        var sample = scored.Results.FirstOrDefault(r => r.Status == ScoringCandidateStatus.Scored);
        if (sample is not null)
        {
            output.WriteLine($"  sample: score={sample.Score} band={sample.Band} scorable={sample.Scorable} — {sample.Rationale}");
        }

        foreach (var failed in scored.Results.Where(r => r.Status != ScoringCandidateStatus.Scored))
        {
            output.WriteLine($"  MISS {failed.ExpertId}: {failed.Error}");
        }

        return rate;
    }

    /// <summary>The production transport, unchanged. A real concurrency limiter stands in for the
    /// shared pacing limiter — one call needs no pacing, and a fake would only be a fake.</summary>
    private static QueuedSyncScoringTransport Transport(IChatClient chat) => new(
        chat,
        new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = int.MaxValue,
        }),
        new RosterScanOptions(),
        TimeProvider.System);

    /// <summary>Workers AI over its OpenAI-compatible endpoint. No compat handler and no
    /// thought-signature policy: both exist to paper over Gemini-specific wire quirks, and
    /// applying them here would measure our shims rather than Cloudflare.</summary>
    private static IChatClient BuildCloudflareChatClient(string accountId, string token) =>
        new OpenAIClient(
                new ApiKeyCredential(token),
                new OpenAIClientOptions { Endpoint = new Uri(CloudflareEndpoint(accountId)) })
            .GetChatClient(CloudflareModel)
            .AsIChatClient();

    /// <summary>The incumbent's raw production wiring (endpoint, compat handler, thought-signature
    /// policy, pinned model) without metering/OTel — the control measures the wire, not decorators.</summary>
    private static IChatClient BuildGeminiChatClient()
    {
        var cfg = new GeminiOptions();
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(cfg.Endpoint),
            Transport = new HttpClientPipelineTransport(
                new HttpClient(new GeminiCompatHandler(new HttpClientHandler()))),
        };
        options.AddPolicy(new GeminiThoughtSignaturePolicy(), PipelinePosition.PerCall);
        return new OpenAIClient(
                new ApiKeyCredential(Environment.GetEnvironmentVariable("GEMINI_API_KEY")!), options)
            .GetChatClient(cfg.Model)
            .AsIChatClient();
    }
}

/// <summary>
/// The chunk P1T-143's gate is measured on, shared by every provider it probes so the comparison
/// is over identical input. Kept out of the live class deliberately: everything here is
/// deterministic and credential-free, so <see cref="RosterScanGateChunkTests"/> can hold it green
/// in the default run. A gate harness that is only ever exercised by the run it gates would be
/// unverified on the one day it matters.
/// </summary>
internal static class RosterScanGateChunk
{
    /// <summary>Production's own chunk size (<see cref="RosterScanOptions.ChunkSize"/>), so the
    /// prompt the gate prices is the prompt a real scan sends.</summary>
    public const int ChunkSize = 10;

    public const string JobDescription =
        "Senior backend engineer for a payments platform. You will own event-streaming " +
        "infrastructure (Kafka), the .NET services around it, and the cloud footprint they run " +
        "on. Five years or more of backend experience is required. Based in Berlin.";

    /// <summary>Roster Scan passes the JD's extraction alongside the raw text whenever one is
    /// available, so the gate sends both — a prompt without it would under-price the call.</summary>
    public static JdRequirements Extraction() => new(
        [
            new JdRequirement("Kafka / event streaming", RequirementKind.Skill,
                RequirementPriority.MustHave, null, "event-streaming infrastructure (Kafka)", false),
            new JdRequirement(".NET services", RequirementKind.Skill,
                RequirementPriority.MustHave, null, "the .NET services around it", false),
            new JdRequirement("5+ years backend experience", RequirementKind.Experience,
                RequirementPriority.MustHave, 5, "Five years or more of backend experience", false),
        ],
        JdSeniority.Senior,
        "Berlin",
        []);

    /// <summary>
    /// Ten real digests: the committed demo roster seeded into the in-memory provider and projected
    /// by the production <see cref="ExpertDigestService"/>. No Postgres — the gate is about the
    /// model's reply, and a digest is a pure projection over the expert aggregate.
    ///
    /// <para>Built <b>once per process</b>, which is load-bearing rather than an optimisation:
    /// <c>DemoRosterSeeder</c> mints a fresh <c>Guid</c> per expert on every seed and
    /// <c>ExpertDigestService</c> orders by that id, so seeding twice yields the same ten people
    /// in a different order under different ids. Re-seeding per probe would hand Cloudflare and the
    /// Gemini control different chunks and quietly make the comparison meaningless.</para>
    /// </summary>
    public static Task<IReadOnlyList<ExpertDigest>> RealDigestChunkAsync() => Chunk.Value;

    private static readonly Lazy<Task<IReadOnlyList<ExpertDigest>>> Chunk =
        new(BuildAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static async Task<IReadOnlyList<ExpertDigest>> BuildAsync()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"p1t143-gate-{Guid.NewGuid()}")
            .Options);

        // The seeder takes the first ChunkSize experts in dataset file order, so which ten people
        // are in the chunk is fixed by the committed asset; only their ids and ordering are not.
        await DemoRosterSeeder.SeedAsync(db, DemoRosterSeeder.LoadCommittedDataset(), ChunkSize);

        var page = await new ExpertDigestService(db).ListAsync(page: 1, pageSize: ChunkSize);
        return page.Items;
    }
}

/// <summary>
/// Holds P1T-143's gate harness green while the gate itself waits on a Cloudflare token: the model
/// call is what needs credentials, the chunk it is measured on does not. Without this, the first
/// person to run the gate could not tell a provider that fails the schema from a fixture that never
/// produced a usable chunk — which is the same "committed but unmeasured" trap the cost chain's
/// three live floors already sit in.
/// </summary>
public class RosterScanGateChunkTests
{
    [Fact]
    public async Task The_gate_chunk_is_ten_real_distinct_digests()
    {
        var digests = await RosterScanGateChunk.RealDigestChunkAsync();

        digests.Should().HaveCount(RosterScanGateChunk.ChunkSize, "the gate prices a production-sized chunk");
        digests.Select(d => d.ExpertId).Should().OnlyHaveUniqueItems(
            "adherence is counted per expert id, so a duplicate would make the rate a lie");
        digests.Should().AllSatisfy(d =>
        {
            d.Name.Should().NotBeNullOrWhiteSpace();
            // The projection, not the seed row: an empty Digest would send the model nothing to
            // score and every candidate would come back scorable:false through no fault of the
            // provider — a false negative on the gate.
            d.Digest.Should().NotBeNullOrWhiteSpace("an empty digest would fail the gate for the wrong reason");
        });
    }

    [Fact]
    public async Task Every_probe_scores_the_identical_chunk()
    {
        // Cloudflare and the Gemini control are only comparable over identical input. The seeder
        // mints fresh ids per seed and the digest service orders by id, so "same ten people" is NOT
        // enough — the chunk has to be the same object graph, in the same order, for both probes.
        var first = await RosterScanGateChunk.RealDigestChunkAsync();
        var second = await RosterScanGateChunk.RealDigestChunkAsync();

        second.Should().BeSameAs(first, "re-seeding per probe would reorder the chunk under new ids");
    }

    [Fact]
    public async Task The_chunk_holds_the_same_ten_people_the_committed_dataset_names()
    {
        // The half that IS fixed by the committed asset: which people are in the chunk. If the
        // dataset is regenerated, this turns red and the gate's numbers stop being comparable to
        // any run recorded before it.
        var expected = DemoRosterSeeder.LoadCommittedDataset().Experts
            .Take(RosterScanGateChunk.ChunkSize)
            .Select(e => $"{e.FirstName} {e.LastName}");

        var digests = await RosterScanGateChunk.RealDigestChunkAsync();

        digests.Select(d => d.Name).Should().BeEquivalentTo(expected);
    }
}
