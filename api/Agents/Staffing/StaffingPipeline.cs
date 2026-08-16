using System.Text;
using System.Text.Json;
using CvManager.Agents.Agents;
using CvManager.Agents.Usage;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Staffing;

/// <summary>
/// The staffing pipeline: Prepare → Shortlist → Match×N → Aggregate → Narrative → Report, built as
/// a Microsoft Agent Framework workflow over the extracted run services.
///
/// <para><b>Graph shape.</b> The spine is an explicit <see cref="WorkflowBuilder"/> chain of typed
/// executors exchanging typed stage DTOs (not the chat-protocol high-level builders). The match
/// fan-out runs as a bounded-parallel task group <i>inside</i> the Match executor rather than as
/// graph-level AddFanOutEdge/AddFanInBarrierEdge, for two reasons: (1) N is dynamic — the barrier
/// fan-in expects a message from every build-time target, so fewer-than-matchTop candidates would
/// need sentinel work items purely to unblock the barrier; and (2) the real concurrency bound is
/// the shared <see cref="StaffingThrottle"/> (default 2), which makes graph-level fan-out purely
/// cosmetic — a Task.WhenAll behind the same semaphore gives identical, deterministically testable
/// scheduling.</para>
///
/// <para><b>Failure ladder.</b> Everything downstream of a successful shortlist degrades into the
/// report (failed/skipped match statuses, templated rationales, a null recommendation) and never
/// throws; only a failed shortlist — without which there is nothing to report — surfaces as an
/// error outcome for the endpoint to map to 502. Caps are re-checked before the match fan-out and
/// before the narrative call; a trip stops launching steps and adds a cap note. Each step's token
/// usage is metered under its agent name: <c>shortlist</c>, <c>match</c> (per candidate), and
/// <c>staffing</c> for the tool-less narrative call.</para>
/// </summary>
public sealed class StaffingPipeline
{
    private readonly IShortlistRunService _shortlist;
    private readonly IMatchRunService _match;
    private readonly IChatClient _chat;
    private readonly IUsageService _usage;
    private readonly IUsageMeter _meter;
    private readonly StaffingThrottle _throttle;
    private readonly StaffingRetryPolicy _retry;
    private readonly ILogger<StaffingPipeline> _logger;

    public StaffingPipeline(
        IShortlistRunService shortlist,
        IMatchRunService match,
        IChatClient chat,
        IUsageService usage,
        IUsageMeter meter,
        StaffingThrottle throttle,
        StaffingRetryPolicy retry,
        ILogger<StaffingPipeline> logger)
    {
        _shortlist = shortlist;
        _match = match;
        _chat = chat;
        _usage = usage;
        _meter = meter;
        _throttle = throttle;
        _retry = retry;
        _logger = logger;
    }

    private const int DefaultMatchTop = 3;
    private const int MinMatchTop = 1;
    private const int MaxMatchTop = 5;

    /// <summary>The narrative call's metering name (there is no narrative agent — it is a plain
    /// tool-less chat completion owned by the pipeline itself).</summary>
    private const string PipelineAgentName = "staffing";

    /// <summary>Runs the pipeline once for <paramref name="userId"/> (null: unmetered, uncapped —
    /// mirrors the endpoints' treatment of an unidentified principal).</summary>
    public Task<StaffingRunOutcome> RunAsync(
        StaffingPipelineRequest request,
        Guid? userId,
        IProgress<StaffingProgressEvent>? progress = null,
        CancellationToken ct = default) =>
        new Runner(this, userId, progress).RunAsync(request, ct);

    // ----- Stage DTOs (the typed messages flowing along the workflow edges) -------------------

    private sealed record PreparedStage(StaffingPipelineRequest Request, ShortlistAgentRequest Shortlist, int MatchTop);

    private sealed record ShortlistStage(PreparedStage Prepared, ShortlistRunOutcome? Run, string? Fault);

    private sealed record CandidateMatch(ShortlistCandidateItem Candidate, StaffingMatchDetail Detail);

    private sealed record MatchStage(
        ShortlistStage Shortlist, IReadOnlyList<CandidateMatch> Matches, bool CapTripped, IReadOnlyList<string> Notes);

    private sealed record EvidenceStage(MatchStage Match, string? Evidence);

    private sealed record NarrativeStage(
        MatchStage Match,
        IReadOnlyDictionary<Guid, string> Rationales,
        StaffingRecommendation? Recommendation,
        IReadOnlyList<string> Notes,
        bool Degraded);

    private sealed record ReportResult(StaffingReport? Report, string? ShortlistFault);

    // ----- Per-run state ----------------------------------------------------------------------

    /// <summary>One run of the pipeline: owns the run-scoped state (user, ordered progress events)
    /// and the workflow instance whose executors close over it.</summary>
    private sealed class Runner(StaffingPipeline pipeline, Guid? userId, IProgress<StaffingProgressEvent>? progress)
    {
        private readonly List<StaffingProgressEvent> _events = [];
        private readonly Lock _gate = new();
        private int _sequence;
        private int _matchesFinished;

        public async Task<StaffingRunOutcome> RunAsync(StaffingPipelineRequest request, CancellationToken ct)
        {
            var workflow = BuildWorkflow();

            await using var run = await InProcessExecution.RunAsync(workflow, request, cancellationToken: ct);
            var events = run.NewEvents.ToList();

            var result = events
                .OfType<WorkflowOutputEvent>()
                .Select(e => e.As<ReportResult>())
                .FirstOrDefault(r => r is not null);
            if (result is null)
            {
                // Executors catch their own faults; reaching this means a pipeline bug, so surface
                // whatever the workflow recorded rather than degrading silently.
                var error = events.OfType<WorkflowErrorEvent>().FirstOrDefault();
                throw new InvalidOperationException(
                    $"The staffing workflow completed without a report outcome. {error?.Data}");
            }

            return new StaffingRunOutcome(result.Report, result.ShortlistFault, _events);
        }

        /// <summary>The explicit workflow spine. Executors are per-run instances (they close over
        /// this runner), which keeps every stage strictly request-scoped.</summary>
        private Workflow BuildWorkflow()
        {
            var prepare = new FunctionExecutor<StaffingPipelineRequest, PreparedStage>("prepare", PrepareAsync);
            var shortlistStep = new FunctionExecutor<PreparedStage, ShortlistStage>("shortlist", ShortlistAsync);
            var matchStep = new FunctionExecutor<ShortlistStage, MatchStage>("match", MatchAsync);
            var aggregate = new FunctionExecutor<MatchStage, EvidenceStage>("aggregate", AggregateAsync);
            var narrative = new FunctionExecutor<EvidenceStage, NarrativeStage>("narrative", NarrativeAsync);
            // The sink: composes the report and yields it as the workflow's output explicitly.
            var report = new FunctionExecutor<NarrativeStage>(
                "report", ReportAsync, outputTypes: [typeof(ReportResult)]);

            var builder = new WorkflowBuilder(prepare);
            // Per-executor spans (workflow_invoke, executor.process; P1T-94). No-ops without a
            // subscribed OTel host; sensitive payload capture stays off by default.
            builder.WithOpenTelemetry();
            builder.AddEdge(prepare, shortlistStep);
            builder.AddEdge(shortlistStep, matchStep);
            builder.AddEdge(matchStep, aggregate);
            builder.AddEdge(aggregate, narrative);
            builder.AddEdge(narrative, report);
            builder.WithOutputFrom(report);
            return builder.Build(true);
        }

        /// <summary>Records one ordered progress event. Step-transition events carry a
        /// <paramref name="status"/> for the SSE stepper; <paramref name="countsMatchRun"/> events
        /// advance the k/N fan-out counter (under the same gate as the sequence, so the counters
        /// are monotonic in event order even while match runs race).</summary>
        private void Emit(
            string stage,
            string message,
            Guid? employeeId = null,
            string? status = null,
            string? candidateName = null,
            int? totalCount = null,
            string? error = null,
            bool countsMatchRun = false)
        {
            StaffingProgressEvent evt;
            lock (_gate)
            {
                var completedCount = countsMatchRun ? ++_matchesFinished : (int?)null;
                evt = new StaffingProgressEvent(
                    ++_sequence, stage, message, employeeId, status, candidateName,
                    completedCount, totalCount, error);
                _events.Add(evt);
            }

            progress?.Report(evt);
        }

        private Task<WindowUsage?> FindExceededAsync(CancellationToken ct) =>
            userId is { } uid ? pipeline._usage.FindExceededAsync(uid, ct) : Task.FromResult<WindowUsage?>(null);

        private async Task MeterAsync(string agentName, AgentReply reply, string step, CancellationToken ct)
        {
            if (userId is { } uid)
            {
                await pipeline._meter.RecordAsync(uid, agentName, reply, step, ct);
            }
        }

        // ----- Prepare -------------------------------------------------------------------------

        private ValueTask<PreparedStage> PrepareAsync(
            StaffingPipelineRequest request, IWorkflowContext context, CancellationToken ct)
        {
            var matchTop = Math.Clamp(request.MatchTop ?? DefaultMatchTop, MinMatchTop, MaxMatchTop);
            Emit("prepare", $"Prepared staffing request (matchTop={matchTop}).");

            // The shortlist step retrieves exactly the candidates the fan-out will assess.
            var shortlistRequest = new ShortlistAgentRequest(
                request.JobDescription, request.AvailableOn, request.SkillIds,
                request.Location, request.MinYears, TopK: matchTop);
            return ValueTask.FromResult(new PreparedStage(request, shortlistRequest, matchTop));
        }

        // ----- Shortlist -----------------------------------------------------------------------

        private async ValueTask<ShortlistStage> ShortlistAsync(
            PreparedStage prepared, IWorkflowContext context, CancellationToken ct)
        {
            Emit("shortlist", "Shortlisting candidates against the job description.",
                status: StaffingStepStatus.Started);
            try
            {
                var run = await pipeline._shortlist.RunAsync(prepared.Shortlist, ct);

                // Meter first: tokens were spent even when the run degrades to a fault below.
                // The extraction call (P1T-117) rides inside the shortlist run and meters under
                // its own agent name, so the Usage tab's per-agent breakdown stays truthful.
                if (run.ExtractionReply is { } extractionReply)
                {
                    await MeterAsync(Agents.JdRequirementExtractor.AgentName, extractionReply, "jd-extraction", ct);
                }

                await MeterAsync(run.AgentName, run.Reply, "shortlist", ct);

                if (run.Response is null)
                {
                    var fault = run.FaultDetail ?? "The shortlist step produced no result.";
                    Emit("shortlist", "Shortlist step failed (upstream retrieval fault).",
                        status: StaffingStepStatus.Failed, error: fault);
                    return new ShortlistStage(prepared, run, fault);
                }

                Emit("shortlist", $"Shortlisted {run.Response.Candidates.Count} candidate(s).",
                    status: StaffingStepStatus.Completed);
                return new ShortlistStage(prepared, run, Fault: null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // MCP server unreachable, Keycloak token failure, or model endpoint error. Without
                // a shortlist there is nothing to report, so this is the pipeline's one error
                // outcome — surfaced as data for the endpoint to map, never thrown.
                pipeline._logger.LogError(ex, "Staffing shortlist step failed.");
                Emit("shortlist", "Shortlist step failed (upstream dependency).",
                    status: StaffingStepStatus.Failed, error: ex.Message);
                return new ShortlistStage(prepared, Run: null, ex.Message);
            }
        }

        // ----- Match fan-out ---------------------------------------------------------------------

        private async ValueTask<MatchStage> MatchAsync(
            ShortlistStage stage, IWorkflowContext context, CancellationToken ct)
        {
            if (stage.Fault is not null || stage.Run?.Response is null)
            {
                return new MatchStage(stage, [], CapTripped: false, []);
            }

            var candidates = stage.Run.Response.Candidates.Take(stage.Prepared.MatchTop).ToList();
            if (candidates.Count == 0)
            {
                Emit("match", "No candidates to match.");
                return new MatchStage(stage, [], CapTripped: false, ["The shortlist returned no candidates."]);
            }

            // Cap re-check before launching the fan-out: the shortlist step just spent tokens.
            if (await FindExceededAsync(ct) is { } window)
            {
                Emit("match", $"Token cap reached ({window.Window}); skipping match runs.");
                var skipped = candidates
                    .Select(c => new CandidateMatch(c, new StaffingMatchDetail(
                        StaffingMatchStatus.Skipped, null, null, null, $"Skipped: the {window.Window} token cap was reached.")))
                    .ToList();
                return new MatchStage(stage, skipped, CapTripped: true,
                    [$"The {window.Window} token cap was reached after the shortlist step; match runs and the narrative were skipped."]);
            }

            Emit("match", $"Assessing the top {candidates.Count} candidate(s) in parallel.");
            var jobDescription = stage.Prepared.Request.JobDescription;
            var extraction = stage.Run.Response.Extraction;
            var results = await Task.WhenAll(
                candidates.Select(c => RunOneMatchAsync(c, jobDescription, extraction, candidates.Count, ct)));

            // Meter sequentially after the fan-out: the meter (an EF-backed scoped service) is not
            // safe for concurrent use.
            foreach (var (_, reply) in results)
            {
                if (reply is not null)
                {
                    await MeterAsync("match", reply, "match", ct);
                }
            }

            var notes = results
                .Select(r => r.Match)
                .Where(m => m.Detail.Status == StaffingMatchStatus.Failed)
                .Select(m => $"Match failed for {m.Candidate.Name}: {m.Detail.Error}")
                .ToList();
            return new MatchStage(stage, results.Select(r => r.Match).ToList(), CapTripped: false, notes);
        }

        /// <summary>One candidate's match run: a shared-throttle slot held for the whole attempt,
        /// 429-aware retries inside it, and any terminal fault mapped to a failed status — a failed
        /// candidate never fails the report.</summary>
        private async Task<(CandidateMatch Match, AgentReply? Reply)> RunOneMatchAsync(
            ShortlistCandidateItem candidate, string jobDescription, Agents.JdRequirements? extraction,
            int totalCount, CancellationToken ct)
        {
            await pipeline._throttle.WaitAsync(ct);
            try
            {
                // "Started" only once a throttle slot is held: the event marks real work, not a
                // queued task, so the SSE stepper's per-candidate ticks reflect actual progress.
                Emit("match", $"Match started for {candidate.Name}.", candidate.EmployeeId,
                    status: StaffingStepStatus.Started, candidateName: candidate.Name, totalCount: totalCount);
                var run = await RunWithRateLimitRetryAsync(candidate.EmployeeId, jobDescription, extraction, ct);
                Emit("match", $"Match completed for {candidate.Name}.", candidate.EmployeeId,
                    status: StaffingStepStatus.Completed, candidateName: candidate.Name,
                    totalCount: totalCount, countsMatchRun: true);
                return (new CandidateMatch(candidate, new StaffingMatchDetail(
                    StaffingMatchStatus.Completed, run.Score, run.Band, run.Answer, Error: null)), run.Reply);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pipeline._logger.LogError(ex, "Staffing match step failed for {EmployeeId}.", candidate.EmployeeId);
                Emit("match", $"Match failed for {candidate.Name}.", candidate.EmployeeId,
                    status: StaffingStepStatus.Failed, candidateName: candidate.Name,
                    totalCount: totalCount, error: ex.Message, countsMatchRun: true);
                return (new CandidateMatch(candidate, new StaffingMatchDetail(
                    StaffingMatchStatus.Failed, null, null, null, ex.Message)), null);
            }
            finally
            {
                pipeline._throttle.Release();
            }
        }

        private async Task<MatchRunOutcome> RunWithRateLimitRetryAsync(
            Guid employeeId, string jobDescription, Agents.JdRequirements? extraction, CancellationToken ct)
        {
            for (var failures = 1; ; failures++)
            {
                try
                {
                    return await pipeline._match.RunAsync(employeeId, jobDescription, extraction, ct);
                }
                catch (Exception ex) when (
                    StaffingRetryPolicy.IsRateLimit(ex) && failures < pipeline._retry.MaxAttempts)
                {
                    await Task.Delay(pipeline._retry.Delay(failures), ct);
                }
            }
        }

        // ----- Aggregate -------------------------------------------------------------------------

        private ValueTask<EvidenceStage> AggregateAsync(
            MatchStage stage, IWorkflowContext context, CancellationToken ct)
        {
            if (stage.Shortlist.Fault is not null || stage.Matches.Count == 0)
            {
                return ValueTask.FromResult(new EvidenceStage(stage, Evidence: null));
            }

            Emit("aggregate", "Assembling per-candidate evidence for the narrative.");
            var evidence = new StringBuilder();
            evidence.AppendLine("Job description:");
            evidence.AppendLine(stage.Shortlist.Prepared.Request.JobDescription);
            evidence.AppendLine();
            evidence.AppendLine("Candidates:");
            foreach (var (candidate, detail) in stage.Matches)
            {
                var matched = candidate.Requirements.Where(r => r.Matched).Select(r => r.Text).ToList();
                var missing = candidate.Requirements.Where(r => !r.Matched).Select(r => r.Text).ToList();

                evidence.AppendLine();
                evidence.AppendLine($"## {candidate.Name} — {candidate.Title} (employeeId: {candidate.EmployeeId})");
                evidence.AppendLine(
                    $"- Shortlist: score {candidate.Score:0.##}, matched {candidate.Coverage.Matched}/{candidate.Coverage.Total} requirements."
                    + $" Matched: {Join(matched)}. Missing: {Join(missing)}.");
                evidence.AppendLine(detail.Status == StaffingMatchStatus.Completed
                    ? $"- Match assessment ({ScoreSummary(detail)}):\n{Truncate(detail.Answer ?? "", 1500)}"
                    : $"- Match assessment: {detail.Status} — no assessment available.");
            }

            return ValueTask.FromResult(new EvidenceStage(stage, evidence.ToString()));

            static string Join(IReadOnlyList<string> items) => items.Count > 0 ? string.Join(", ", items) : "none";
            static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
            static string ScoreSummary(StaffingMatchDetail detail) =>
                detail.Score is { } score ? $"score {score}/100{(detail.Band is { } b ? $", {b}" : "")}" : "no parsed score";
        }

        // ----- Narrative -------------------------------------------------------------------------

        private const string NarrativeInstructions =
            """
            You write the closing narrative for a staffing report. You are given a job description
            and per-candidate evidence: shortlist requirement coverage and (when available) a match
            assessment. Reply with the structured object: one rationale (one or two sentences) per
            candidate, using exactly the employeeId values given, and a recommendation (two to four
            sentences) that picks exactly one of the given candidates. Ground every statement
            strictly in the evidence provided — never invent skills, experience, or facts.
            """;

        private async ValueTask<NarrativeStage> NarrativeAsync(
            EvidenceStage stage, IWorkflowContext context, CancellationToken ct)
        {
            var match = stage.Match;
            var empty = new Dictionary<Guid, string>();
            if (match.Shortlist.Fault is not null || match.Matches.Count == 0)
            {
                return new NarrativeStage(match, empty, null, [], Degraded: false);
            }

            if (match.CapTripped)
            {
                // The match-stage cap note already covers the narrative; don't add a second one.
                Emit("narrative", "Narrative skipped: token cap reached.");
                return new NarrativeStage(match, empty, null, [], Degraded: true);
            }

            // Cap re-check before the narrative call: the match fan-out just spent tokens.
            if (await FindExceededAsync(ct) is { } window)
            {
                Emit("narrative", $"Token cap reached ({window.Window}); skipping the narrative.");
                return new NarrativeStage(match, empty, null,
                    [$"The {window.Window} token cap was reached after the match runs; the narrative was skipped."],
                    Degraded: true);
            }

            Emit("narrative", "Generating rationales and a recommendation.",
                status: StaffingStepStatus.Started);
            try
            {
                // Tool-less completion on the default chat client: the narrative needs no agent
                // identity or MCP access — all its facts arrive pre-assembled in the prompt.
                var narrativeClock = System.Diagnostics.Stopwatch.StartNew();
                // Schema-constrained since P1T-118; TryParse below stays as the fallback parser.
                var narrativeOptions = new ChatOptions
                {
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(
                        AIJsonUtilities.CreateJsonSchema(typeof(NarrativePayload)), "staffing_narrative"),
                };
                var response = await pipeline._chat.GetResponseAsync(
                    [new ChatMessage(ChatRole.System, NarrativeInstructions), new ChatMessage(ChatRole.User, stage.Evidence)],
                    narrativeOptions,
                    ct);
                await MeterAsync(PipelineAgentName, new AgentReply(
                    response.Text,
                    response.Usage?.InputTokenCount ?? 0,
                    response.Usage?.OutputTokenCount ?? 0,
                    response.Usage?.TotalTokenCount ?? 0,
                    response.ModelId,
                    narrativeClock.ElapsedMilliseconds), "narrative", ct);

                return ComposeNarrative(match, response.Text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pipeline._logger.LogError(ex, "Staffing narrative step failed.");
                Emit("narrative", "Narrative step failed; falling back to templated rationales.",
                    status: StaffingStepStatus.Failed, error: ex.Message);
                return new NarrativeStage(match, empty, null,
                    ["The narrative step failed; rationales are templated from shortlist and match evidence."],
                    Degraded: true);
            }
        }

        /// <summary>Applies the corruption guards to the model's narrative JSON: rationales for
        /// unknown ids are dropped (the template covers those candidates), and the recommendation
        /// must name one of the report's candidates or it degrades to none.</summary>
        private NarrativeStage ComposeNarrative(MatchStage match, string modelText)
        {
            var parsed = NarrativePayload.TryParse(modelText);
            if (parsed is null)
            {
                Emit("narrative", "Narrative output was unparseable; falling back to templated rationales.",
                    status: StaffingStepStatus.Failed, error: "The narrative output was unparseable.");
                return new NarrativeStage(match, new Dictionary<Guid, string>(), null,
                    ["The narrative output was unparseable; rationales are templated from shortlist and match evidence."],
                    Degraded: true);
            }

            var knownIds = match.Matches.Select(m => m.Candidate.EmployeeId).ToHashSet();

            var rationales = new Dictionary<Guid, string>();
            foreach (var entry in parsed.Rationales ?? [])
            {
                if (entry?.EmployeeId is { } idText
                    && Guid.TryParse(idText, out var id)
                    && knownIds.Contains(id)
                    && !string.IsNullOrWhiteSpace(entry.Rationale))
                {
                    rationales[id] = entry.Rationale.Trim();
                }
            }

            if (parsed.Recommendation?.EmployeeId is { } recIdText
                && Guid.TryParse(recIdText, out var recId)
                && knownIds.Contains(recId)
                && !string.IsNullOrWhiteSpace(parsed.Recommendation.Narrative))
            {
                Emit("narrative", "Narrative completed.", status: StaffingStepStatus.Completed);
                return new NarrativeStage(match, rationales,
                    new StaffingRecommendation(recId, parsed.Recommendation.Narrative.Trim()), [], Degraded: false);
            }

            // The step still completed — it produced rationales; the dropped recommendation is a
            // report-level degrade (note + degraded:true), not a step failure.
            Emit("narrative", "Narrative recommendation was missing or named an unknown candidate; dropped.",
                status: StaffingStepStatus.Completed);
            return new NarrativeStage(match, rationales, null,
                ["The narrative recommendation was missing or named an unknown candidate; no recommendation is included."],
                Degraded: true);
        }

        // ----- Report ----------------------------------------------------------------------------

        private async ValueTask ReportAsync(
            NarrativeStage stage, IWorkflowContext context, CancellationToken ct)
        {
            var match = stage.Match;
            if (match.Shortlist.Fault is { } fault)
            {
                Emit("report", "No report: the shortlist step failed.");
                await context.YieldOutputAsync(new ReportResult(Report: null, fault), ct);
                return;
            }

            var candidates = match.Matches
                .Select(m => new StaffingCandidate(
                    m.Candidate.EmployeeId,
                    m.Candidate.Name,
                    m.Candidate.Title,
                    new StaffingShortlistDetail(m.Candidate.Score, m.Candidate.Coverage, m.Candidate.Requirements),
                    m.Detail,
                    stage.Rationales.TryGetValue(m.Candidate.EmployeeId, out var rationale)
                        ? rationale
                        : TemplatedRationale(m)))
                .ToList();

            var degraded = stage.Degraded
                || match.Matches.Any(m => m.Detail.Status != StaffingMatchStatus.Completed);

            Emit("report", "Staffing report composed.");
            var report = new StaffingReport(
                match.Shortlist.Run!.Response!.Requirements,
                candidates,
                stage.Recommendation,
                degraded,
                [.. match.Notes, .. stage.Notes],
                Extraction: match.Shortlist.Run.Response.Extraction);
            await context.YieldOutputAsync(new ReportResult(report, ShortlistFault: null), ct);
        }

        /// <summary>The deterministic rationale used whenever the narrative can't supply one:
        /// templated purely from the shortlist coverage and the parsed match score.</summary>
        private static string TemplatedRationale(CandidateMatch match)
        {
            var coverage = match.Candidate.Coverage;
            var text = $"Matched {coverage.Matched}/{coverage.Total} shortlist requirements";
            if (match.Detail.Score is { } score)
            {
                text += $"; match score {score}/100";
                if (match.Detail.Band is { } band)
                {
                    text += $" ({band})";
                }
            }

            return text + ".";
        }
    }

    // ----- Narrative JSON shape -----------------------------------------------------------------

    /// <summary>The narrative model's minimal JSON, parsed leniently: a direct parse first, then a
    /// retry on the outermost {...} span (the model wrapped it in prose or a fence), null when
    /// nothing parseable remains.</summary>
    private sealed record NarrativePayload(
        List<NarrativeRationale?>? Rationales, NarrativeRecommendation? Recommendation)
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

        public static NarrativePayload? TryParse(string modelText)
        {
            if (TryDeserialize(modelText) is { } direct)
            {
                return direct;
            }

            var start = modelText.IndexOf('{');
            var end = modelText.LastIndexOf('}');
            return start >= 0 && end > start ? TryDeserialize(modelText[start..(end + 1)]) : null;
        }

        private static NarrativePayload? TryDeserialize(string text)
        {
            try
            {
                return JsonSerializer.Deserialize<NarrativePayload>(text, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private sealed record NarrativeRationale(string? EmployeeId, string? Rationale);

    private sealed record NarrativeRecommendation(string? EmployeeId, string? Narrative);
}
