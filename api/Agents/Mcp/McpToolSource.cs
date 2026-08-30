using CvManager.Agents.Auth;
using CvManager.Agents.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace CvManager.Agents.Mcp;

/// <summary>
/// Connects to the MCP server over Streamable HTTP (with a bearer token injected per request)
/// and lists its tools once, caching the result for the app lifetime. Because the agent's token
/// carries only <c>mcp:read</c>, the server advertises read tools only — the agent never sees a
/// write or destructive tool.
///
/// <para>Within that surface the agent's <see cref="AgentToolAllowlist"/> narrows further to the
/// tools it actually uses (P1T-146). Applied here rather than in each agent so the narrowing is
/// one declaration per identity and cannot be widened downstream.</para>
/// </summary>
public sealed class McpToolSource : IMcpToolSource, IAsyncDisposable
{
    private readonly McpServerOptions _server;
    private readonly IAccessTokenProvider _tokens;
    private readonly AgentToolAllowlist _allowlist;
    private readonly ILogger<McpToolSource> _logger;
    private readonly string _agentKey;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HttpClient? _httpClient;
    private McpClient? _client;
    private IReadOnlyList<AITool>? _tools;

    /// <param name="agentKey">The agent's config key. Names the identity in the warning below —
    /// "some agent allowlists a tool that does not exist" is not a diagnosable log line.</param>
    public McpToolSource(
        string agentKey,
        IOptions<McpServerOptions> server,
        IAccessTokenProvider tokens,
        AgentToolAllowlist allowlist,
        ILoggerFactory loggerFactory)
    {
        _server = server.Value;
        _tokens = tokens;
        _allowlist = allowlist;
        _agentKey = agentKey;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpToolSource>();
    }

    /// <summary>The Tool Allowlist this source narrows the advertised surface to. Exposed so the
    /// keyed registration can be asserted without a live MCP server standing behind it.</summary>
    public AgentToolAllowlist Allowlist => _allowlist;

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_tools is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_tools is { } stillCached)
            {
                return stillCached;
            }

            var httpClient = new HttpClient(new BearerTokenHandler(_tokens) { InnerHandler = new SocketsHttpHandler() });
            try
            {
                var transport = new HttpClientTransport(
                    new HttpClientTransportOptions
                    {
                        Endpoint = new Uri(_server.BaseUrl),
                        TransportMode = HttpTransportMode.StreamableHttp,
                    },
                    httpClient,
                    _loggerFactory,
                    ownsHttpClient: false);

                _client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: ct);

                var advertised = (await _client.ListToolsAsync(cancellationToken: ct))
                    .Cast<AITool>().ToList();
                _httpClient = httpClient;
                _tools = _allowlist.Apply(advertised);

                // A name on the list the server never advertised narrows the surface silently —
                // a typo, or a scope that no longer carries the tool. Say so; do not fail the
                // agent over it, since the tools it can still reach are the honest surface.
                if (_allowlist.MissingFrom(advertised) is { Count: > 0 } missing)
                {
                    _logger.LogWarning(
                        "Agent {Agent} allowlists {Count} MCP tool(s) the server did not advertise: {Tools}. " +
                        "Its tool surface is narrower than configured.",
                        _agentKey, missing.Count, string.Join(", ", missing));
                }

                return _tools;
            }
            catch
            {
                // Failed connect (MCP down, auth rejected): release resources so the next call retries clean.
                if (_client is not null)
                {
                    await _client.DisposeAsync();
                    _client = null;
                }

                httpClient.Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _httpClient?.Dispose();
    }
}
