using System.ClientModel;
using System.Net;

namespace CvManager.Agents.Staffing;

/// <summary>
/// How a match run rides out model rate limiting (mirrors the retrieval eval's QueryRetryPolicy
/// shape, kept local so the service doesn't reference the tools tree): up to
/// <paramref name="MaxAttempts"/> tries per candidate, waiting <paramref name="Delay"/> (keyed by
/// the 1-based count of failures so far) between them. Only 429-shaped faults are retried — any
/// other failure is a real answer and fails the candidate immediately.
/// </summary>
public sealed record StaffingRetryPolicy(int MaxAttempts, Func<int, TimeSpan> Delay)
{
    /// <summary>Production default: three attempts, linear backoff (5s, then 10s) — enough to ride
    /// out Gemini's free-tier per-minute limits without stalling the report for long.</summary>
    public static StaffingRetryPolicy Default { get; } = new(
        MaxAttempts: 3,
        Delay: failures => TimeSpan.FromSeconds(5 * failures));

    /// <summary>Is this fault a model rate limit (HTTP 429)? Covers both the raw transport shape
    /// and the OpenAI client's <see cref="ClientResultException"/>.</summary>
    public static bool IsRateLimit(Exception exception) => exception
        is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
        or ClientResultException { Status: (int)HttpStatusCode.TooManyRequests };
}
