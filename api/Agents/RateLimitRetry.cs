using System.ClientModel;
using System.Net;
using Polly;
using Polly.Retry;

namespace ExpertToJob.Agents;

/// <summary>
/// The one place a 429 ladder is built (EXP-87). Both model-calling fan-outs — the staffing match
/// step and the Roster Scan scoring transport — ride out provider rate limiting the same way: only
/// 429-shaped faults are retried, a bounded number of attempts, a backoff the caller names. Any
/// other failure is a real answer and surfaces immediately.
///
/// <para>The ladders are Polly <see cref="ResiliencePipeline"/>s rather than hand-rolled
/// <c>for</c>/<c>catch</c>/<c>Task.Delay</c> loops, so the wait comes from the injected
/// <see cref="TimeProvider"/> — which is what lets a test read a ladder without spending it — and
/// the budget is stated once instead of being re-derived from a loop counter at each site.</para>
/// </summary>
public static class RateLimitRetry
{
    /// <summary>Is this fault a model rate limit (HTTP 429)? Covers both the raw transport shape
    /// and the OpenAI client's <see cref="ClientResultException"/>.</summary>
    public static bool IsRateLimit(Exception exception) => exception
        is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
        or ClientResultException { Status: (int)HttpStatusCode.TooManyRequests };

    /// <summary>Linear ladder: <paramref name="maxAttempts"/> tries in all, the n-th wait being
    /// n × <paramref name="step"/> (5s, 10s, 15s…).</summary>
    public static ResiliencePipeline Linear(int maxAttempts, TimeSpan step, TimeProvider clock) =>
        Build(maxAttempts, step, DelayBackoffType.Linear, clock);

    /// <summary>Exponential ladder: <paramref name="maxAttempts"/> tries in all, the n-th wait
    /// being 2ⁿ⁻¹ × <paramref name="baseDelay"/> (2s, 4s, 8s…).</summary>
    public static ResiliencePipeline Exponential(int maxAttempts, TimeSpan baseDelay, TimeProvider clock) =>
        Build(maxAttempts, baseDelay, DelayBackoffType.Exponential, clock);

    private static ResiliencePipeline Build(
        int maxAttempts, TimeSpan delay, DelayBackoffType backoff, TimeProvider clock) =>
        // A budget of one attempt is a budget of no retries, which Polly declines to model (and
        // Roster Scan can be configured to exactly that), so it is the empty pipeline instead.
        maxAttempts <= 1
            ? ResiliencePipeline.Empty
            : new ResiliencePipelineBuilder { TimeProvider = clock }
                .AddRetry(new RetryStrategyOptions
                {
                    // Polly counts retries; the budget at both call sites is attempts, the first
                    // one included.
                    MaxRetryAttempts = maxAttempts - 1,
                    BackoffType = backoff,
                    Delay = delay,
                    // Frozen ladders: the tests assert the exact sequence, and a jittered one
                    // would only be assertable as a range.
                    UseJitter = false,
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(IsRateLimit),
                })
                .Build();
}
