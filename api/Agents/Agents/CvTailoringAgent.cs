using EmployeeManager.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace EmployeeManager.Agents.Agents;

/// <summary>
/// Read-only agent that tailors one employee's CV to a target job description. A Microsoft Agent
/// Framework <see cref="ChatClientAgent"/> backed by the configured chat model, given only the
/// <c>cv_get</c> MCP tool. It returns advisory prose — a rewritten summary plus emphasise / drop /
/// reorder guidance — grounded strictly in the employee's real CV; it never fabricates data and
/// writes nothing. Single-turn: each request runs on a fresh, ephemeral session.
/// </summary>
public sealed class CvTailoringAgent : IChatAgent
{
    /// <summary>The one MCP tool this agent uses; <c>cv_get</c> already bundles the full CV.</summary>
    private const string CvTool = "cv_get";

    private const string Instructions =
        """
        You are the CV Tailoring assistant for a CV Manager. You are given an employee id and a
        target job description. Call the cv_get tool to fetch that employee's full CV, then tailor
        it to the job description. Produce:

        1. A ready-to-paste rewritten professional summary (a short paragraph) aimed at the role.
        2. Concrete tailoring guidance: which skills and experiences to emphasise, which to drop or
           de-emphasise, and how to reorder them for this job description.

        Use ONLY facts returned by cv_get — never invent skills, experience, qualifications, or
        achievements the CV does not contain. If cv_get reports the employee was not found, say so
        plainly and stop. You have read-only access and cannot change any data.
        """;

    private readonly IChatClient _chatClient;
    private readonly IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIAgent? _agent;

    public CvTailoringAgent(IChatClient chatClient, IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
    }

    public string Name => "cv-tailoring";

    public async Task<string> AskAsync(string question, CancellationToken ct = default)
    {
        var agent = await GetAgentAsync(ct);
        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync(question, session, null, ct);
        return response.Text;
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

            // Narrow to cv_get only: the model needs nothing else to tailor, and a tighter tool
            // surface keeps it on task. The mcp:read scope already hides write/destructive tools.
            var tools = await _toolSource.GetToolsAsync(ct);
            var cvTools = tools.Where(t => t.Name == CvTool).ToList();

            _agent = new ChatClientAgent(
                _chatClient,
                instructions: Instructions,
                name: "CvTailoring",
                description: "Tailors an employee's CV to a target job description (read-only, advisory).",
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
