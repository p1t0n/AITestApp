using EmployeeManager.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace EmployeeManager.Agents.Agents;

/// <summary>
/// Read-only agent that assesses how well one employee fits a target job description. A Microsoft
/// Agent Framework <see cref="ChatClientAgent"/> backed by the configured chat model, given only
/// the <c>cv_get</c> MCP tool. It returns a gap analysis and a fit assessment grounded strictly in
/// the employee's real CV; it never fabricates data and writes nothing. Single-turn: each request
/// runs on a fresh, ephemeral session.
/// </summary>
public sealed class MatchAgent : IChatAgent
{
    /// <summary>The one MCP tool this agent uses; <c>cv_get</c> already bundles the full CV.</summary>
    private const string CvTool = "cv_get";

    private const string Instructions =
        """
        You are the Match assistant for a CV Manager. You are given an employee id and a target job
        description. Call the cv_get tool to fetch that employee's full CV, then assess their fit
        for the role. Produce two sections:

        1. Gap analysis — list each concrete requirement in the job description and mark it Met,
           Partial, or Missing, citing the specific CV evidence (skill, experience, qualification,
           years) for Met/Partial, or noting the absence for Missing.
        2. Fit assessment — an explicit per-requirement rubric (each requirement scored out of an
           equal share of 100), an overall score out of 100 that is the sum of the rubric rows, and
           an overall band: Strong (>=75), Moderate (50-74), Weak (25-49), or Insufficient evidence.
           The number must follow from the rubric — never invent a score.

        Use ONLY facts returned by cv_get — never invent skills, experience, qualifications, or
        achievements the CV does not contain; an unsupported requirement is Missing, not assumed. If
        cv_get reports the employee was not found, say so plainly and stop. You have read-only
        access and cannot change any data.
        """;

    private readonly IChatClient _chatClient;
    private readonly IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIAgent? _agent;

    public MatchAgent(IChatClient chatClient, IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
    }

    public string Name => "match";

    public async Task<AgentReply> AskAsync(string question, CancellationToken ct = default)
    {
        var agent = await GetAgentAsync(ct);
        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync(question, session, null, ct);
        var usage = response.Usage;
        return new AgentReply(
            response.Text,
            usage?.InputTokenCount ?? 0,
            usage?.OutputTokenCount ?? 0,
            usage?.TotalTokenCount ?? 0);
    }

    private async Task<AIAgent> GetAgentAsync(CancellationToken ct)
    {
        if (_agent is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_agent is { } stillCached)
            {
                return stillCached;
            }

            // Narrow to cv_get only: matching needs nothing else, and a tighter tool surface keeps
            // the model on task. The mcp:read scope already hides write/destructive tools.
            var tools = await _toolSource.GetToolsAsync(ct);
            var cvTools = tools.Where(t => t.Name == CvTool).ToList();

            _agent = new ChatClientAgent(
                _chatClient,
                instructions: Instructions,
                name: "Match",
                description: "Assesses an employee's fit for a target job description (read-only, advisory).",
                tools: cvTools,
                loggerFactory: _loggerFactory);
            return _agent;
        }
        finally
        {
            _gate.Release();
        }
    }
}
