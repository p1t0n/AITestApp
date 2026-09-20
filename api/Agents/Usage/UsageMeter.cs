using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Entities;

namespace ExpertToJob.Agents.Usage;

public interface IUsageMeter
{
    /// <summary>Persists one usage row for a completed agent call. <paramref name="step"/> tags
    /// pipeline sub-steps (staffing: shortlist / match / narrative); direct calls leave it null.</summary>
    Task RecordAsync(Guid userId, string agentName, AgentReply reply, string? step = null, CancellationToken ct = default);
}

/// <summary>
/// Writes the per-call <see cref="AgentUsage"/> row. Best-effort: a metering failure is logged but
/// never propagates, so a transient DB issue can't fail a user's answer that already succeeded.
/// </summary>
public sealed class UsageMeter(
    IAppDbContext db,
    ChatProvider provider,
    TimeProvider clock,
    ILogger<UsageMeter> logger) : IUsageMeter
{
    public async Task RecordAsync(Guid userId, string agentName, AgentReply reply, string? step = null, CancellationToken ct = default)
    {
        try
        {
            // The model id the response actually reported (captured at the chat seam, P1T-95),
            // and nothing else. There used to be a config fallback here for replies that never
            // reached a model; it mislabelled whenever config and reality drifted, so EXP-19
            // deleted it rather than carrying it into new key names. Empty means "no model
            // answered" — the same fact Iterations = null already records on this row.
            var model = reply.ModelId ?? string.Empty;

            db.AgentUsages.Add(new AgentUsage
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AgentName = agentName,
                Model = model,
                // Which backend served it (EXP-19). Configuration, not a measurement: known even
                // when no model answered.
                Provider = provider.ToString(),
                InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens,
                TotalTokens = reply.TotalTokens,
                LatencyMs = reply.LatencyMs > 0 ? reply.LatencyMs : null,
                // Why the call cost what it did (P1T-144): 0 iterations means the metering seam
                // saw nothing (a reply that never reached a model), which is not "one cheap call".
                Iterations = reply.Iterations > 0 ? reply.Iterations : null,
                ToolSequence = reply.ToolSequence,
                TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
                Step = step,
                Timestamp = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record agent usage for {Agent} / user {User}", agentName, userId);
        }
    }
}
