using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace EmployeeManager.Agents.Staffing;

/// <summary>
/// The pinned SSE contract for POST /agents/staffing (P1T-76). The endpoint's pre-checks
/// (auth 401, blank job description 400, cap 429) answer as plain HTTP <i>before</i> the stream
/// opens; after that the response is <c>text/event-stream</c> carrying, in run order:
///
/// <list type="bullet">
/// <item><c>event: step</c> — <c>{ "stage": "shortlist|match|narrative", "status":
/// "started|completed", "candidate"?: { "employeeId", "name" }, "completedCount"?,
/// "totalCount"? }</c>. Enough for a stepper UI: shortlist started/completed, match
/// started/completed per candidate (name + k/N counters), narrative started/completed.</item>
/// <item><c>event: stepFailed</c> — the same shape with <c>"status": "failed"</c> plus an
/// <c>"error"</c> message; the run continues under the degrade policy (the report ships
/// <c>degraded: true</c>). Stages a cap trip skips emit no step events at all — the report's
/// <c>skipped</c> statuses and cap note are the signal.</item>
/// <item><c>event: report</c> — terminal; data is the full pinned staffing report (P1T-71),
/// serialized exactly as the one-shot endpoint used to return it.</item>
/// <item><c>event: error</c> — terminal; problem-style <c>{ "title", "detail" }</c> for the
/// unrecoverable outcomes: a failed shortlist (nothing to report) or an unexpected fault. A
/// failed shortlist intentionally emits no <c>stepFailed</c> — that event promises the run
/// continues, and this one cannot.</item>
/// </list>
///
/// Payloads are camelCase; optional fields are omitted when absent. While no event is ready the
/// stream carries periodic <c>: ka</c> comment lines as keep-alives. Exactly one terminal event
/// closes the stream. Client disconnect cancels the in-flight pipeline run.
/// </summary>
public static class StaffingSse
{
    public const string ContentType = "text/event-stream";

    public const string StepEvent = "step";
    public const string StepFailedEvent = "stepFailed";
    public const string ReportEvent = "report";
    public const string ErrorEvent = "error";

    private sealed record StepCandidate(Guid EmployeeId, string Name);

    private sealed record StepPayload(
        string Stage,
        string Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StepCandidate? Candidate,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CompletedCount,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalCount,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);

    private sealed record ErrorPayload(string Title, string Detail);

    /// <summary>
    /// Runs the pipeline and streams it as SSE. The pipeline's <see cref="IProgress{T}"/> events
    /// are bridged through an unbounded channel — the progress callback writes from wherever a
    /// step happens to run, this loop reads, maps, and flushes one frame per event as it happens —
    /// then the terminal event comes from the run's outcome.
    /// </summary>
    public static async Task StreamAsync(
        HttpResponse response,
        StaffingPipeline pipeline,
        StaffingPipelineRequest request,
        Guid? userId,
        JsonSerializerOptions json,
        TimeSpan keepAliveInterval,
        ILogger logger,
        CancellationToken ct)
    {
        response.ContentType = ContentType;
        response.Headers.CacheControl = "no-cache";

        var channel = Channel.CreateUnbounded<StaffingProgressEvent>(
            new UnboundedChannelOptions { SingleReader = true });
        var run = RunPipelineAsync();

        try
        {
            // Commit the response before the first event so the client sees the stream open.
            await response.Body.FlushAsync(ct);
            await PumpAsync(response, channel.Reader, json, keepAliveInterval, ct);

            var outcome = await run;
            if (outcome.ShortlistFault is { } fault)
            {
                await WriteFrameAsync(response, ErrorEvent, new ErrorPayload(
                    "Upstream dependency failed (staffing shortlist step).", fault), json, ct);
            }
            else
            {
                await WriteFrameAsync(response, ReportEvent, outcome.Report!, json, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client went away; the token has already cancelled the in-flight pipeline run
            // and there is nobody left to write a terminal event to.
        }
        catch (Exception ex)
        {
            // The response has started, so HTTP status mapping is no longer available: anything
            // that escapes the pipeline's own failure ladder becomes the terminal error event.
            logger.LogError(ex, "The staffing run failed while streaming.");
            if (!ct.IsCancellationRequested)
            {
                await WriteFrameAsync(response, ErrorEvent, new ErrorPayload(
                    ex is HttpRequestException
                        ? "Upstream dependency failed (MCP server, auth, or model)."
                        : "The staffing run failed unexpectedly.",
                    ex.Message), json, ct);
            }
        }

        async Task<StaffingRunOutcome> RunPipelineAsync()
        {
            try
            {
                return await pipeline.RunAsync(request, userId, new ChannelProgress(channel.Writer), ct);
            }
            finally
            {
                // However the run ends, unblock the pump so the terminal event can go out.
                channel.Writer.TryComplete();
            }
        }
    }

    /// <summary>Relays progress events to frames until the run completes the channel, emitting a
    /// keep-alive comment whenever <paramref name="keepAliveInterval"/> passes without one.</summary>
    private static async Task PumpAsync(
        HttpResponse response,
        ChannelReader<StaffingProgressEvent> events,
        JsonSerializerOptions json,
        TimeSpan keepAliveInterval,
        CancellationToken ct)
    {
        while (true)
        {
            var readTask = events.WaitToReadAsync(ct).AsTask();
            if (!readTask.IsCompleted)
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (await Task.WhenAny(readTask, Task.Delay(keepAliveInterval, delayCts.Token)) != readTask)
                {
                    await response.WriteAsync(": ka\n\n", ct);
                    await response.Body.FlushAsync(ct);
                    continue;
                }

                await delayCts.CancelAsync();
            }

            if (!await readTask)
            {
                return;
            }

            while (events.TryRead(out var evt))
            {
                if (TryMap(evt) is { } frame)
                {
                    await WriteFrameAsync(response, frame.EventName, frame.Payload, json, ct);
                }
            }
        }
    }

    /// <summary>Maps one pipeline progress event to its wire event, or null for the events the
    /// contract doesn't carry: message-only diagnostics (prepare/aggregate/report chatter,
    /// cap-trip skips) and the failed-shortlist event, whose signal is the terminal error.</summary>
    private static (string EventName, StepPayload Payload)? TryMap(StaffingProgressEvent evt)
    {
        if (evt.Status is null)
        {
            return null;
        }

        if (evt.Status == StaffingStepStatus.Failed && evt.Stage == "shortlist")
        {
            return null;
        }

        var candidate = evt is { EmployeeId: { } id, CandidateName: { } name }
            ? new StepCandidate(id, name)
            : null;
        var payload = new StepPayload(
            evt.Stage, evt.Status, candidate, evt.CompletedCount, evt.TotalCount, evt.Error);
        return (evt.Status == StaffingStepStatus.Failed ? StepFailedEvent : StepEvent, payload);
    }

    private static async Task WriteFrameAsync<T>(
        HttpResponse response, string eventName, T payload, JsonSerializerOptions json, CancellationToken ct)
    {
        await response.WriteAsync(
            $"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, json)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>The progress bridge: synchronous, lock-free, never blocks a pipeline step (the
    /// channel is unbounded, so TryWrite only fails after completion — i.e. never mid-run).</summary>
    private sealed class ChannelProgress(ChannelWriter<StaffingProgressEvent> writer)
        : IProgress<StaffingProgressEvent>
    {
        public void Report(StaffingProgressEvent value) => writer.TryWrite(value);
    }
}
