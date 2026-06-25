using EmployeeManager.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace EmployeeManager.Agents.Agents;

/// <summary>
/// Read-only conversational agent over the employee roster. A Microsoft Agent Framework
/// <see cref="ChatClientAgent"/> backed by the configured chat model, given the MCP read tools.
/// It answers natural-language questions ("who knows React?") by calling those tools and never
/// fabricates data. Single-turn: each question runs on a fresh, ephemeral session.
/// </summary>
public sealed class RosterQaAgent : IChatAgent
{
    private const string Instructions =
        """
        You are the Roster Q&A assistant for a CV Manager. Answer questions about the roster of
        employees — their skills, qualifications, experience, spoken languages, and time-based
        availability — using ONLY the provided tools. Never invent employees, skills, or facts.

        When you refer to an employee, give their full name and include their id in parentheses,
        e.g. "Ada Lovelace (a1b2c3d4-...)", so the answer can be linked back to a record.

        If the tools return nothing relevant, say so plainly. You have read-only access; if asked
        to change data, explain that you cannot.
        """;

    private readonly IChatClient _chatClient;
    private readonly IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIAgent? _agent;

    public RosterQaAgent(IChatClient chatClient, IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
    }

    public string Name => "roster-qa";

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

            var tools = await _toolSource.GetToolsAsync(ct);
            _agent = new ChatClientAgent(
                _chatClient,
                instructions: Instructions,
                name: "RosterQa",
                description: "Answers read-only questions about the employee roster.",
                tools: tools.ToList(),
                loggerFactory: _loggerFactory);
            return _agent;
        }
        finally
        {
            _gate.Release();
        }
    }
}
