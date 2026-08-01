using CvManager.Agents.Agents;
using CvManager.Application.Abstractions;
using CvManager.Domain.Entities;

namespace CvManager.Agents.Usage;

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
    IConfiguration config,
    TimeProvider clock,
    ILogger<UsageMeter> logger) : IUsageMeter
{
    public async Task RecordAsync(Guid userId, string agentName, AgentReply reply, string? step = null, CancellationToken ct = default)
    {
        try
        {
            // Prefer the model id the response actually reported (captured at the chat seam,
            // P1T-95); the config lookup is only the fallback for replies that never reached a
            // model (and mislabels whenever config and reality drift).
            var model = reply.ModelId
                ?? config[$"Gemini:Agents:{agentName}"]
                ?? config["Gemini:Model"]
                ?? string.Empty;

            db.AgentUsages.Add(new AgentUsage
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AgentName = agentName,
                Model = model,
                InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens,
                TotalTokens = reply.TotalTokens,
                LatencyMs = reply.LatencyMs > 0 ? reply.LatencyMs : null,
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
