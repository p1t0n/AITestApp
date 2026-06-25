using EmployeeManager.Agents.Agents;
using EmployeeManager.Application.Abstractions;
using EmployeeManager.Domain.Entities;

namespace EmployeeManager.Agents.Usage;

public interface IUsageMeter
{
    /// <summary>Persists one usage row for a completed agent call.</summary>
    Task RecordAsync(Guid userId, string agentName, AgentReply reply, CancellationToken ct = default);
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
    public async Task RecordAsync(Guid userId, string agentName, AgentReply reply, CancellationToken ct = default)
    {
        try
        {
            var model = config[$"GitHubModels:Agents:{agentName}"]
                ?? config["GitHubModels:Model"]
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
