using System.ComponentModel.DataAnnotations;

namespace ExpertToJob.Agents.Staffing;

/// <summary>Staffing pipeline knobs, bound from the "Staffing" configuration section.</summary>
public sealed class StaffingOptions
{
    public const string Section = "Staffing";

    /// <summary>How many match runs may be in flight at once, across all concurrent staffing
    /// requests (a single shared throttle protects the model endpoint's rate limit).
    ///
    /// <para>No property default since EXP-107: it is coupled to the Azure deployment's TPM, so an
    /// operator moving to a bigger deployment has to be able to raise it without a deploy. The
    /// shipped number lives once, in <c>api/Agents/appsettings.json</c>, and the range below is
    /// enforced with <c>ValidateOnStart</c> — a missing or zero value refuses to boot rather than
    /// handing <see cref="StaffingThrottle"/> a semaphore with no slots, which would deadlock
    /// every staffing run instead of failing one.</para></summary>
    [Range(1, int.MaxValue, ErrorMessage = $"{Section}:{nameof(MaxConcurrentMatches)} must be greater than 0.")]
    public int MaxConcurrentMatches { get; set; }

    /// <summary>How often the SSE response emits a keep-alive comment while no event is ready,
    /// so proxies and idle-timeout middleboxes keep the stream open.</summary>
    public double SseKeepAliveSeconds { get; set; } = 15;
}

/// <summary>The shared match throttle: one process-wide <see cref="SemaphoreSlim"/> sized by
/// <see cref="StaffingOptions.MaxConcurrentMatches"/>. A slot is held for a candidate's whole
/// match attempt (retries included), so backoff never over-admits new model calls.</summary>
public sealed class StaffingThrottle(int maxConcurrentMatches)
{
    private readonly SemaphoreSlim _slots = new(maxConcurrentMatches, maxConcurrentMatches);

    public Task WaitAsync(CancellationToken ct) => _slots.WaitAsync(ct);

    public void Release() => _slots.Release();
}
